// SPDX-License-Identifier: GPL-2.0-or-later
//
// AudioPluginBridge — wires PluginManager's audio-bearing plugins into
// WdspDspEngine's realtime TX seam via Zeus.Plugins.Host.AudioChain.
//
// The chain itself (AudioChain) is realtime-safe and tested in
// isolation under Zeus.Plugins.Host.Tests. This file is the
// integration glue: it subscribes to PluginManager activation events,
// adopts each audio-bearing plugin into a free slot, and re-installs
// the WDSP delegate whenever DspPipelineService swaps engines.
//
// The whole bridge is a no-op when no plugins implement IAudioPlugin
// or declare audio.vst3Path in their manifest.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zeus.Dsp;
using Zeus.Dsp.Wdsp;
using Zeus.Plugins.Contracts;
using Zeus.Plugins.Contracts.Audio;
using Zeus.Plugins.Contracts.Extensions;
using Zeus.Plugins.Host;
using Zeus.Plugins.Host.Audio;

namespace Zeus.Server;

public sealed class AudioPluginBridge : IHostedService, IAsyncDisposable
{
    private readonly PluginManager _manager;
    private readonly DspPipelineService _pipeline;
    private readonly IVstBridgeNative _vstBridge;
    private readonly ILogger<AudioPluginBridge> _log;
    private readonly AudioChain _chain = new();
    private readonly Dictionary<string, int> _idToSlot = new();
    private readonly object _lock = new();

    public AudioPluginBridge(
        PluginManager manager,
        DspPipelineService pipeline,
        ILogger<AudioPluginBridge> log)
        : this(manager, pipeline, new VstBridgeNative(), log) { }

    // Testable ctor — lets unit tests inject a fake IVstBridgeNative.
    internal AudioPluginBridge(
        PluginManager manager,
        DspPipelineService pipeline,
        IVstBridgeNative vstBridge,
        ILogger<AudioPluginBridge> log)
    {
        _manager = manager;
        _pipeline = pipeline;
        _vstBridge = vstBridge;
        _log = log;
    }

    /// <summary>Current chain (exposed for diagnostics / tests).</summary>
    internal AudioChain Chain => _chain;

    public Task StartAsync(CancellationToken ct)
    {
        _manager.PluginActivated   += OnPluginActivated;
        _manager.PluginDeactivated += OnPluginDeactivated;
        _pipeline.EngineChanged    += OnEngineChanged;

        // Adopt any plugins already active (PluginManager might have
        // finished startup before us depending on hosted-service ordering).
        foreach (var p in _manager.Active) OnPluginActivated(p);

        // Install the handler on whatever engine is currently live.
        if (_pipeline.CurrentEngine is { } engine) AttachToEngine(engine);

        _log.LogInformation("AudioPluginBridge online.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _manager.PluginActivated   -= OnPluginActivated;
        _manager.PluginDeactivated -= OnPluginDeactivated;
        _pipeline.EngineChanged    -= OnEngineChanged;

        if (_pipeline.CurrentEngine is WdspDspEngine wdsp)
            wdsp.SetTxAudioPluginHandler(null);

        return Task.CompletedTask;
    }

    private void OnEngineChanged(IDspEngine engine) => AttachToEngine(engine);

    private void AttachToEngine(IDspEngine engine)
    {
        // The realtime seam is WdspDspEngine-only; SyntheticDspEngine has
        // no TX block to intercept. Skip the install and let the chain
        // sit idle until the next engine swap.
        if (engine is not WdspDspEngine wdsp)
        {
            _log.LogDebug("Engine {Type} not WdspDspEngine; audio plugin bridge idle", engine.GetType().Name);
            return;
        }
        wdsp.SetTxAudioPluginHandler(Process);
        _log.LogInformation("Audio plugin handler installed on WdspDspEngine.");
    }

    /// <summary>Realtime entry point — never allocates, never logs.</summary>
    private void Process(
        ReadOnlySpan<float> input,
        Span<float> output,
        int frames,
        int channels,
        int sampleRate)
    {
        var ctx = new AudioBlockContext(sampleRate, channels, frames, sampleTime: 0, mox: true);
        _chain.Process(input, output, ctx);
    }

    // -- Plugin lifecycle ------------------------------------------------

    private void OnPluginActivated(ActivatedPlugin p)
    {
        var audioPlugin = ResolveAudioPlugin(p);
        if (audioPlugin is null) return;

        int slot;
        lock (_lock)
        {
            slot = FindFreeSlot();
            if (slot < 0)
            {
                _log.LogWarning(
                    "Audio chain full (8 slots); ignoring plugin {Id}",
                    p.Loaded.Manifest.Id);
                return;
            }
            _idToSlot[p.Loaded.Manifest.Id] = slot;
            _chain.SetSlot(slot, audioPlugin);
        }

        // Realtime-safe init off-thread before the chain dispatches. The
        // chain itself doesn't call Initialize; we do it here so plugins
        // get a chance to allocate / open resources before their first
        // Process() call.
        try
        {
            audioPlugin.InitializeAudioAsync(
                new AudioHost(slotName: p.Loaded.Manifest.Audio?.Slot ?? "tx.post-leveler"),
                CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Audio plugin {Id} InitializeAudioAsync threw; clearing slot {Slot}",
                p.Loaded.Manifest.Id, slot);
            lock (_lock)
            {
                _chain.ClearSlot(slot);
                _idToSlot.Remove(p.Loaded.Manifest.Id);
            }
            return;
        }

        _chain.MasterEnabled = true;
        _log.LogInformation(
            "Audio plugin {Id} attached to slot {Slot}",
            p.Loaded.Manifest.Id, slot);
    }

    private void OnPluginDeactivated(ActivatedPlugin p)
    {
        IAudioPlugin? attached = null;
        int slot;
        lock (_lock)
        {
            if (!_idToSlot.TryGetValue(p.Loaded.Manifest.Id, out slot)) return;
            _idToSlot.Remove(p.Loaded.Manifest.Id);
            attached = _chain.GetSlot(slot);
            _chain.ClearSlot(slot);
            if (_idToSlot.Count == 0) _chain.MasterEnabled = false;
        }

        if (attached is null) return;
        try
        {
            attached.ShutdownAudioAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Audio plugin {Id} ShutdownAudioAsync threw", p.Loaded.Manifest.Id);
        }
        if (attached is IAsyncDisposable ad)
        {
            try { ad.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch { /* best effort */ }
        }
    }

    /// <summary>Returns the plugin's IAudioPlugin, or synthesises a
    /// <see cref="VstHostAudioPlugin"/> if the manifest declares audio.vst3Path,
    /// or null if the plugin contributes no audio.</summary>
    private IAudioPlugin? ResolveAudioPlugin(ActivatedPlugin p)
    {
        if (p.Loaded.Plugin is IAudioPlugin direct) return direct;

        var audio = p.Loaded.Manifest.Audio;
        if (audio is { Vst3Path: { Length: > 0 } })
        {
            return new VstHostAudioPlugin(
                bridge: _vstBridge,
                manifestAudio: audio,
                pluginRootPath: p.Loaded.PluginDir,
                displayName: p.Loaded.Manifest.Name,
                log: _log);
        }
        return null;
    }

    private int FindFreeSlot()
    {
        for (int i = 0; i < _chain.SlotCount; i++)
            if (_chain.GetSlot(i) is null) return i;
        return -1;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        await _chain.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class AudioHost : IAudioHost
    {
        public AudioHost(string slotName) { Slot = slotName; }
        public int CurrentSampleRate => 48000;
        public int CurrentChannels => 1;
        public int CurrentBlockSize => 256;
        public string Slot { get; }
    }
}
