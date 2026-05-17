namespace Zeus.Plugins.Contracts.Audio;

/// <summary>
/// Metadata passed to <see cref="Extensions.IAudioPlugin.Process"/>
/// alongside each audio block.
/// </summary>
public readonly ref struct AudioBlockContext
{
    public AudioBlockContext(int sampleRate, int channels, int frames, long sampleTime, bool mox)
    {
        SampleRate = sampleRate;
        Channels = channels;
        Frames = frames;
        SampleTime = sampleTime;
        Mox = mox;
    }

    public int SampleRate { get; }
    public int Channels { get; }
    public int Frames { get; }

    /// <summary>Monotonic sample-frame counter from session start.</summary>
    public long SampleTime { get; }

    /// <summary>True if the radio is currently transmitting.</summary>
    public bool Mox { get; }
}

/// <summary>Sample-rate / channel / block-size negotiation values.</summary>
public sealed record AudioPluginRequirements(
    int SampleRate,
    int Channels,
    int BlockSize);

/// <summary>What an <see cref="Extensions.IAudioPlugin"/> sees of its host.</summary>
public interface IAudioHost
{
    int CurrentSampleRate { get; }
    int CurrentChannels { get; }
    int CurrentBlockSize { get; }

    /// <summary>
    /// Where in the chain this plugin sits, copied from the manifest's
    /// <c>audio.slot</c>. Useful for plugins that branch on TX vs RX.
    /// </summary>
    string Slot { get; }
}
