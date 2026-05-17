using Microsoft.Extensions.Logging.Abstractions;
using Zeus.Plugins.Contracts;
using Zeus.Plugins.Host;

namespace Zeus.Plugins.Host.Tests;

public class PluginLoaderTests
{
    private readonly PluginLoader _loader = new(NullLogger<PluginLoader>.Instance);

    private const string ValidPluginSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Zeus.Plugins.Contracts;

        namespace Fixture;

        public sealed class HelloPlugin : IZeusPlugin
        {
            public Task InitializeAsync(IPluginContext context, CancellationToken ct)
                => Task.CompletedTask;
            public Task ShutdownAsync(CancellationToken ct)
                => Task.CompletedTask;
        }
        """;

    private static string Manifest(string id, string assemblyName, string? typeName = null)
        => $$"""
        {
          "schemaVersion": 1,
          "id": "{{id}}",
          "name": "Hello",
          "version": "1.0.0",
          "sdk": { "abi": 1, "minVersion": "1.0.0" },
          "entrypoint": { "assembly": "{{assemblyName}}.dll"{{(typeName is null ? "" : $", \"type\": \"{typeName}\"")}} }
        }
        """;

    [Fact]
    public void Load_HappyPath_ReturnsActivatedInstance()
    {
        using var fixture = RoslynFixture.Create(
            "HelloFixture1",
            ValidPluginSource,
            Manifest("com.example.hello1", "HelloFixture1", "Fixture.HelloPlugin"));

        var loaded = _loader.Load(fixture.PluginDir);

        Assert.Equal("com.example.hello1", loaded.Manifest.Id);
        Assert.NotNull(loaded.Plugin);
        Assert.IsAssignableFrom<IZeusPlugin>(loaded.Plugin);

        loaded.LoadContext.Unload();
    }

    [Fact]
    public void Load_PicksFirstIZeusPlugin_WhenTypeNotSpecified()
    {
        using var fixture = RoslynFixture.Create(
            "HelloFixture2",
            ValidPluginSource,
            Manifest("com.example.hello2", "HelloFixture2"));

        var loaded = _loader.Load(fixture.PluginDir);
        Assert.Equal("Fixture.HelloPlugin", loaded.Plugin.GetType().FullName);
        loaded.LoadContext.Unload();
    }

    [Fact]
    public void Load_MissingPluginJson_Throws()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ex = Assert.Throws<PluginLoadException>(() => _loader.Load(dir));
            Assert.Contains("plugin.json", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_BrokenJson_Throws()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "plugin.json"), "{ not json");
            var ex = Assert.Throws<PluginLoadException>(() => _loader.Load(dir));
            Assert.Contains("parse error", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_AbiMismatch_Throws()
    {
        using var fixture = RoslynFixture.Create(
            "HelloFixture3",
            ValidPluginSource,
            // sdkAbi 99 doesn't match the host's AbiVersion.Current=1
            """
            {
              "schemaVersion": 1,
              "id": "com.example.hello3",
              "name": "Hello",
              "version": "1.0.0",
              "sdk": { "abi": 99, "minVersion": "1.0.0" },
              "entrypoint": { "assembly": "HelloFixture3.dll" }
            }
            """);
        var ex = Assert.Throws<PluginLoadException>(() => _loader.Load(fixture.PluginDir));
        Assert.Contains("abi", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_NoIZeusPluginInAssembly_Throws()
    {
        using var fixture = RoslynFixture.Create(
            "Empty1",
            "namespace Empty { public sealed class Nothing { } }",
            Manifest("com.example.empty1", "Empty1"));

        var ex = Assert.Throws<PluginLoadException>(() => _loader.Load(fixture.PluginDir));
        Assert.Contains("no public IZeusPlugin", ex.Message);
    }

    [Fact]
    public void Load_NamedTypeNotFound_Throws()
    {
        using var fixture = RoslynFixture.Create(
            "Hello4",
            ValidPluginSource,
            Manifest("com.example.hello4", "Hello4", "Fixture.DoesNotExist"));

        var ex = Assert.Throws<PluginLoadException>(() => _loader.Load(fixture.PluginDir));
        Assert.Contains("DoesNotExist", ex.Message);
    }

    [Fact]
    public void Load_AssemblyMissing_Throws()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "plugin.json"),
                Manifest("com.example.x", "Missing"));
            var ex = Assert.Throws<PluginLoadException>(() => _loader.Load(dir));
            Assert.Contains("entrypoint assembly not found", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
