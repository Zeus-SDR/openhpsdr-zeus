// SPDX-License-Identifier: GPL-2.0-or-later
//
// RxAudioTapBridge — fans the host's demodulated RX audio stream out to any
// plugin implementing IRxAudioTapPlugin (a read-only, non-destructive tap;
// e.g. a recorder or decoder). Independent of the rx.post-demod insert chain:
// taps observe audio but never alter it, and never occupy a chain slot.
//
// The seam is DspPipelineService.RxAudioAvailable, which already exists and is
// raised once per Tick with the 48 kHz mono RX block — so this bridge adds NO
// code to the audio hot path itself. The fan-out reads a copy-on-write array
// of taps with no lock and no allocation.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zeus.Plugins.Contracts.Audio;
using Zeus.Plugins.Contracts.Extensions;
using Zeus.Plugins.Host;

namespace Zeus.Server;

public sealed class RxAudioTapBridge : IHostedService
{
    private readonly PluginManager _manager;
    private readonly DspPipelineService _pipeline;
    private readonly ILogger<RxAudioTapBridge> _log;

    private readonly object _lock = new();
    // id -> tap, the mutable source of truth (control thread only).
    private readonly Dictionary<string, IRxAudioTapPlugin> _byId = new();
    // Copy-on-write snapshot read lock-free on the RX audio thread.
    private volatile IRxAudioTapPlugin[] _taps = Array.Empty<IRxAudioTapPlugin>();

    public RxAudioTapBridge(
        PluginManager manager,
        DspPipelineService pipeline,
        ILogger<RxAudioTapBridge> log)
    {
        _manager = manager;
        _pipeline = pipeline;
        _log = log;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _manager.PluginActivated   += OnPluginActivated;
        _manager.PluginDeactivated += OnPluginDeactivated;
        _pipeline.RxAudioAvailable += OnRxAudio;

        foreach (var p in _manager.Active) OnPluginActivated(p);

        _log.LogInformation("RxAudioTapBridge online.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _pipeline.RxAudioAvailable -= OnRxAudio;
        _manager.PluginActivated   -= OnPluginActivated;
        _manager.PluginDeactivated -= OnPluginDeactivated;

        IRxAudioTapPlugin[] taps;
        lock (_lock)
        {
            taps = _taps;
            _byId.Clear();
            _taps = Array.Empty<IRxAudioTapPlugin>();
        }
        foreach (var tap in taps) ShutdownTap(tap);
        return Task.CompletedTask;
    }

    /// <summary>RX audio thread — never allocates, never logs, never throws.</summary>
    private void OnRxAudio(int receiver, int sampleRate, ReadOnlyMemory<float> block)
    {
        var taps = _taps;            // single volatile read of the snapshot
        if (taps.Length == 0) return;

        var span = block.Span;
        var ctx = new AudioBlockContext(sampleRate, channels: 1, frames: span.Length, sampleTime: 0, mox: false);
        for (int i = 0; i < taps.Length; i++)
        {
            try { taps[i].OnRxAudio(span, ctx); }
            catch { /* a misbehaving tap must never break the audio pipeline */ }
        }
    }

    private void OnPluginActivated(ActivatedPlugin p)
    {
        if (p.Loaded.Plugin is not IRxAudioTapPlugin tap) return;
        var id = p.Loaded.Manifest.Id;

        try
        {
            tap.InitializeTapAsync(
                new TapHost(),
                CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "RX audio tap {Id} InitializeTapAsync threw; not subscribing", id);
            return;
        }

        lock (_lock)
        {
            _byId[id] = tap;
            _taps = _byId.Values.ToArray();
        }
        _log.LogInformation("RX audio tap {Id} subscribed", id);
    }

    private void OnPluginDeactivated(ActivatedPlugin p)
    {
        var id = p.Loaded.Manifest.Id;
        IRxAudioTapPlugin? tap;
        lock (_lock)
        {
            if (!_byId.Remove(id, out tap)) return;
            _taps = _byId.Values.ToArray();
        }
        ShutdownTap(tap);
        _log.LogInformation("RX audio tap {Id} unsubscribed", id);
    }

    private void ShutdownTap(IRxAudioTapPlugin tap)
    {
        try { tap.ShutdownTapAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch (Exception ex) { _log.LogWarning(ex, "RX audio tap ShutdownTapAsync threw"); }
    }

    // The host RX stream is fixed mono 48 kHz; taps don't negotiate, but we
    // reuse IAudioHost so the contract surface matches IAudioPlugin.
    private sealed class TapHost : IAudioHost
    {
        public int CurrentSampleRate => DspPipelineService.AudioOutputRateHz;
        public int CurrentChannels => 1;
        public int CurrentBlockSize => 2048;
        public string Slot => "rx.tap";
    }
}
