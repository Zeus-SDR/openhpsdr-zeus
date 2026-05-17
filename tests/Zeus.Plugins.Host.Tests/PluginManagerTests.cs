using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Zeus.Plugins.Host;

namespace Zeus.Plugins.Host.Tests;

public class PluginManagerTests : IDisposable
{
    private readonly string _root;
    private readonly string _dbPath;
    private readonly PluginSettingsStore _store;
    private readonly PluginManager _manager;

    private const string PluginSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.Logging;
        using Zeus.Plugins.Contracts;

        namespace Fixture;

        public sealed class P : IZeusPlugin
        {
            public Task InitializeAsync(IPluginContext context, CancellationToken ct)
            {
                context.Logger.LogInformation("init");
                return Task.CompletedTask;
            }
            public Task ShutdownAsync(CancellationToken ct) => Task.CompletedTask;
        }
        """;

    public PluginManagerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "zeus-plugin-mgr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _dbPath = Path.Combine(_root, "settings.db");
        _store = new PluginSettingsStore(_dbPath);

        var loader = new PluginLoader(NullLogger<PluginLoader>.Instance);
        _manager = new PluginManager(
            loader: loader,
            settings: _store,
            services: new ServiceCollection().BuildServiceProvider(),
            logFactory: NullLoggerFactory.Instance,
            options: new PluginManagerOptions { PluginRoot = _root });
    }

    public void Dispose()
    {
        _manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private RoslynFixture WriteFixturePluginToRoot(string id, string asmName)
    {
        var fixture = RoslynFixture.Create(
            asmName,
            PluginSource,
            $$"""
            {
              "schemaVersion": 1,
              "id": "{{id}}",
              "name": "P",
              "version": "1.0.0",
              "sdk": { "abi": 1, "minVersion": "1.0.0" },
              "entrypoint": { "assembly": "{{asmName}}.dll" }
            }
            """);

        // Copy fixture contents into a sub-dir of our plugin root.
        var dest = Path.Combine(_root, id);
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.EnumerateFiles(fixture.PluginDir))
            File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), overwrite: true);
        return fixture;
    }

    [Fact]
    public async Task StartAsync_LoadsAllPluginDirs()
    {
        using var a = WriteFixturePluginToRoot("com.example.a", "A");
        using var b = WriteFixturePluginToRoot("com.example.b", "B");

        await _manager.StartAsync(default);

        Assert.Equal(2, _manager.Active.Count);
        Assert.NotNull(_manager.Find("com.example.a"));
        Assert.NotNull(_manager.Find("com.example.b"));
    }

    [Fact]
    public async Task ActivateAsync_TwiceWithSameId_RestartsPlugin()
    {
        using var a = WriteFixturePluginToRoot("com.example.a", "A");
        var p1 = await _manager.ActivateAsync(Path.Combine(_root, "com.example.a"), default);
        var p2 = await _manager.ActivateAsync(Path.Combine(_root, "com.example.a"), default);

        // Plugin re-activation produces a fresh instance; old one is gone.
        Assert.NotSame(p1.Loaded.Plugin, p2.Loaded.Plugin);
        Assert.Single(_manager.Active);
    }

    [Fact]
    public async Task StopAsync_DeactivatesAll()
    {
        using var a = WriteFixturePluginToRoot("com.example.a", "A");
        await _manager.StartAsync(default);
        Assert.Single(_manager.Active);

        await _manager.StopAsync(default);
        Assert.Empty(_manager.Active);
    }

    [Fact]
    public async Task SafeMode_SkipsAllPlugins()
    {
        using var a = WriteFixturePluginToRoot("com.example.a", "A");

        var safe = new PluginManager(
            loader: new PluginLoader(NullLogger<PluginLoader>.Instance),
            settings: _store,
            services: new ServiceCollection().BuildServiceProvider(),
            logFactory: NullLoggerFactory.Instance,
            options: new PluginManagerOptions { SafeMode = true, PluginRoot = _root });

        await safe.StartAsync(default);
        Assert.Empty(safe.Active);
        await safe.StopAsync(default);
    }
}
