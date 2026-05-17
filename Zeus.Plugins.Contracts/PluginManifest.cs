using System.Text.Json.Serialization;

namespace Zeus.Plugins.Contracts;

/// <summary>
/// JSON-deserialised <c>plugin.json</c>. Schema version 1 is the only
/// version recognised by ABI 1.
/// </summary>
public sealed record PluginManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("author")]
    public string Author { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }

    [JsonPropertyName("license")]
    public string License { get; init; } = "";

    [JsonPropertyName("sdk")]
    public required SdkRequirement Sdk { get; init; }

    [JsonPropertyName("entrypoint")]
    public required EntryPoint Entrypoint { get; init; }

    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string> CapabilitiesRaw { get; init; } = Array.Empty<string>();

    [JsonPropertyName("permissions")]
    public PermissionsBlock Permissions { get; init; } = new();

    [JsonPropertyName("ui")]
    public UiBlock? Ui { get; init; }

    [JsonPropertyName("audio")]
    public AudioBlock? Audio { get; init; }

    /// <summary>
    /// Parses <see cref="CapabilitiesRaw"/> into a typed flags value.
    /// Unknown capability names are ignored (forward-compat).
    /// </summary>
    public PluginCapabilities ParseCapabilities()
    {
        var flags = PluginCapabilities.PersistSettings;
        foreach (var raw in CapabilitiesRaw)
        {
            if (Enum.TryParse<PluginCapabilities>(raw, ignoreCase: false, out var c))
                flags |= c;
        }
        return flags;
    }
}

public sealed record SdkRequirement
{
    [JsonPropertyName("abi")]
    public int Abi { get; init; }

    [JsonPropertyName("minVersion")]
    public required string MinVersion { get; init; }
}

public sealed record EntryPoint
{
    [JsonPropertyName("assembly")]
    public required string Assembly { get; init; }

    /// <summary>
    /// Optional fully-qualified type name. If omitted the loader
    /// scans the assembly for the first public <see cref="IZeusPlugin"/>.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

public sealed record PermissionsBlock
{
    [JsonPropertyName("network")]
    public bool Network { get; init; }

    [JsonPropertyName("fileSystemRead")]
    public bool FileSystemRead { get; init; }

    [JsonPropertyName("fileSystemWrite")]
    public bool FileSystemWrite { get; init; }
}

public sealed record UiBlock
{
    [JsonPropertyName("modules")]
    public IReadOnlyList<string> Modules { get; init; } = Array.Empty<string>();

    [JsonPropertyName("panels")]
    public IReadOnlyList<PanelContribution> Panels { get; init; } = Array.Empty<PanelContribution>();
}

public sealed record PanelContribution
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "Box";

    /// <summary>
    /// Named slot in the Zeus shell the panel renders into. Known slots:
    /// <c>workspace.amplifier</c>, <c>settings.plugins</c>,
    /// <c>topbar.right</c>. Unknown slots are ignored.
    /// </summary>
    [JsonPropertyName("slot")]
    public required string Slot { get; init; }
}

public sealed record AudioBlock
{
    /// <summary>Relative path inside the plugin dir to a VST3 file.</summary>
    [JsonPropertyName("vst3Path")]
    public string? Vst3Path { get; init; }

    /// <summary>
    /// Where in the TX/RX path this audio plugin sits. Known values:
    /// <c>tx.post-leveler</c>, <c>tx.pre-cfc</c>, <c>rx.post-demod</c>.
    /// </summary>
    [JsonPropertyName("slot")]
    public string Slot { get; init; } = "tx.post-leveler";

    [JsonPropertyName("channels")]
    public int Channels { get; init; } = 1;

    [JsonPropertyName("sampleRate")]
    public int SampleRate { get; init; } = 48000;
}
