using Microsoft.Extensions.Logging;
using Zeus.Plugins.Contracts;
using Zeus.Plugins.Contracts.Audio;
using Zeus.Plugins.Contracts.Extensions;

namespace Zeus.Plugins.Host.Audio;

/// <summary>
/// <see cref="IAudioPlugin"/> implementation that hosts a single VST3
/// effect via <see cref="IVstBridgeNative"/>. Synthesised by the host
/// when a plugin's manifest declares <c>audio.vst3Path</c> — plugin
/// authors don't write this themselves.
/// </summary>
public sealed class VstHostAudioPlugin : IAudioPlugin, IAsyncDisposable
{
    private readonly IVstBridgeNative _bridge;
    private readonly string _vst3Path;
    private readonly string _pluginRootPath;
    private readonly ILogger? _log;
    private nint _handle;

    public VstHostAudioPlugin(
        IVstBridgeNative bridge,
        AudioBlock manifestAudio,
        string pluginRootPath,
        string displayName,
        ILogger? log = null)
    {
        _bridge = bridge;
        _pluginRootPath = pluginRootPath;
        _log = log;
        DisplayName = displayName;
        Requirements = new AudioPluginRequirements(
            SampleRate: manifestAudio.SampleRate,
            Channels: manifestAudio.Channels,
            BlockSize: 256);
        _vst3Path = manifestAudio.Vst3Path
            ?? throw new ArgumentException("audio.vst3Path is required for VstHostAudioPlugin");
    }

    public string DisplayName { get; }
    public AudioPluginRequirements Requirements { get; }

    public Task InitializeAudioAsync(IAudioHost host, CancellationToken ct)
    {
        // Bridge init is idempotent — the native side ref-counts.
        var initStatus = _bridge.Init(VstBridgeAbi.Current);
        if (initStatus != VstBridgeStatus.Ok)
            throw new PluginLoadException(
                $"VST bridge init failed (status={initStatus}); is zeus-vst-bridge installed?");

        var absPath = Path.IsPathRooted(_vst3Path)
            ? _vst3Path
            : Path.Combine(_pluginRootPath, _vst3Path);

        if (!File.Exists(absPath) && !Directory.Exists(absPath))
            throw new PluginLoadException($"VST3 path not found: {absPath}");

        var status = _bridge.LoadVst3(
            absPath,
            Requirements.Channels,
            Requirements.SampleRate,
            Requirements.BlockSize,
            out _handle);

        if (status != VstBridgeStatus.Ok || _handle == 0)
            throw new PluginLoadException(
                $"VST3 load failed for {absPath} (status={status})");

        _log?.LogInformation(
            "VST host loaded {Path} (channels={Channels} sr={SampleRate} block={Block})",
            absPath, Requirements.Channels, Requirements.SampleRate, Requirements.BlockSize);
        return Task.CompletedTask;
    }

    public void Process(ReadOnlySpan<float> input, Span<float> output, AudioBlockContext ctx)
    {
        if (_handle == 0)
        {
            input.CopyTo(output); // safety: pass through if not initialised
            return;
        }

        var status = _bridge.Process(_handle, input, output, ctx.Frames);
        if (status != VstBridgeStatus.Ok)
        {
            // Realtime path: NEVER throw, NEVER log here (allocation).
            // Pass through on bridge failure — the operator will see a
            // status surface up via the next non-realtime poll.
            input.CopyTo(output);
        }
    }

    public Task ShutdownAudioAsync(CancellationToken ct)
    {
        if (_handle != 0)
        {
            _bridge.Unload(_handle);
            _handle = 0;
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_handle != 0)
        {
            try { _bridge.Unload(_handle); } catch { /* swallow */ }
            _handle = 0;
        }
        return ValueTask.CompletedTask;
    }
}
