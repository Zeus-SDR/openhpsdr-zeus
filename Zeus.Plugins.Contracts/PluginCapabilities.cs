namespace Zeus.Plugins.Contracts;

/// <summary>
/// Capability flags a plugin declares in its manifest. The host grants
/// each capability at first load (user prompt) and stores the decision in
/// scoped persistence. Ungranted capabilities surface as null services on
/// <see cref="IPluginContext"/> — the plugin SHOULD null-check rather
/// than reflect over the context.
/// </summary>
[Flags]
public enum PluginCapabilities
{
    None             = 0,

    /// <summary>Subscribe to frequency / mode / band / MOX events.</summary>
    ReadRadioState   = 1 << 0,

    /// <summary>Call into RadioController to change VFO / mode / MOX.</summary>
    ControlRadio     = 1 << 1,

    /// <summary>Process RX or TX audio blocks (requires IAudioPlugin).</summary>
    AudioStream      = 1 << 2,

    /// <summary>Open outbound network sockets / HTTP clients.</summary>
    NetworkAccess    = 1 << 3,

    /// <summary>Read arbitrary files outside the plugin's own directory.</summary>
    FileSystemRead   = 1 << 4,

    /// <summary>Write arbitrary files outside the plugin's own directory.</summary>
    FileSystemWrite  = 1 << 5,

    /// <summary>
    /// Persist plugin-scoped settings via IPluginContext.Settings.
    /// Granted by default to every plugin; declared in manifest only
    /// for documentation symmetry.
    /// </summary>
    PersistSettings  = 1 << 6,
}
