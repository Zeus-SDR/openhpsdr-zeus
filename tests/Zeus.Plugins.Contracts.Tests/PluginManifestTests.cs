using System.Text.Json;
using Zeus.Plugins.Contracts;

namespace Zeus.Plugins.Contracts.Tests;

public class PluginManifestTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    [Fact]
    public void Deserialises_MinimalManifest()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "id": "com.example.test",
          "name": "Test",
          "version": "1.0.0",
          "sdk": { "abi": 1, "minVersion": "1.0.0" },
          "entrypoint": { "assembly": "Test.dll" }
        }
        """;

        var m = JsonSerializer.Deserialize<PluginManifest>(json, JsonOpts);

        Assert.NotNull(m);
        Assert.Equal("com.example.test", m!.Id);
        Assert.Equal("Test", m.Name);
        Assert.Equal("1.0.0", m.Version);
        Assert.Equal(1, m.Sdk.Abi);
        Assert.Equal("Test.dll", m.Entrypoint.Assembly);
        Assert.Null(m.Entrypoint.Type);
        Assert.Null(m.Ui);
        Assert.Null(m.Audio);
    }

    [Fact]
    public void Deserialises_FullManifest_WithUiAndAudio()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "id": "com.example.amp",
          "name": "Amp",
          "version": "1.0.0",
          "author": "Jane",
          "description": "Amp control",
          "homepage": "https://example.com",
          "license": "GPL-2.0-or-later",
          "sdk": { "abi": 1, "minVersion": "1.0.0" },
          "entrypoint": { "assembly": "Amp.dll", "type": "Amp.AmpPlugin" },
          "capabilities": ["ReadRadioState", "ControlRadio"],
          "permissions": { "network": true, "fileSystemRead": false, "fileSystemWrite": false },
          "ui": {
            "modules": ["ui/amp.es.js"],
            "panels": [
              { "id": "amp.main", "title": "Amp", "icon": "Zap", "slot": "workspace.amplifier" }
            ]
          },
          "audio": {
            "vst3Path": "vst3/MyEffect.vst3",
            "slot": "tx.post-leveler",
            "channels": 1,
            "sampleRate": 48000
          }
        }
        """;

        var m = JsonSerializer.Deserialize<PluginManifest>(json, JsonOpts);

        Assert.NotNull(m);
        Assert.Equal("Amp.AmpPlugin", m!.Entrypoint.Type);
        Assert.Equal(2, m.CapabilitiesRaw.Count);
        Assert.True(m.Permissions.Network);
        Assert.NotNull(m.Ui);
        Assert.Single(m.Ui!.Panels);
        Assert.Equal("amp.main", m.Ui.Panels[0].Id);
        Assert.NotNull(m.Audio);
        Assert.Equal("vst3/MyEffect.vst3", m.Audio!.Vst3Path);
        Assert.Equal("tx.post-leveler", m.Audio.Slot);
    }

    [Fact]
    public void ParseCapabilities_AlwaysIncludesPersistSettings()
    {
        var m = new PluginManifest
        {
            Id = "x",
            Name = "x",
            Version = "1.0.0",
            Sdk = new() { Abi = 1, MinVersion = "1.0.0" },
            Entrypoint = new() { Assembly = "x.dll" },
        };

        var caps = m.ParseCapabilities();
        Assert.True(caps.HasFlag(PluginCapabilities.PersistSettings));
    }

    [Fact]
    public void ParseCapabilities_UnknownEntriesAreIgnored()
    {
        var m = new PluginManifest
        {
            Id = "x",
            Name = "x",
            Version = "1.0.0",
            Sdk = new() { Abi = 1, MinVersion = "1.0.0" },
            Entrypoint = new() { Assembly = "x.dll" },
            CapabilitiesRaw = new[] { "ReadRadioState", "FutureCapability42" },
        };

        var caps = m.ParseCapabilities();
        Assert.True(caps.HasFlag(PluginCapabilities.ReadRadioState));
        // FutureCapability42 silently dropped — forward compat.
    }

    [Fact]
    public void ParseCapabilities_RejectsCaseMismatch()
    {
        // ignoreCase: false in ParseCapabilities — manifest authors MUST use exact PascalCase.
        var m = new PluginManifest
        {
            Id = "x",
            Name = "x",
            Version = "1.0.0",
            Sdk = new() { Abi = 1, MinVersion = "1.0.0" },
            Entrypoint = new() { Assembly = "x.dll" },
            CapabilitiesRaw = new[] { "readradiostate" },
        };

        var caps = m.ParseCapabilities();
        Assert.False(caps.HasFlag(PluginCapabilities.ReadRadioState));
    }
}
