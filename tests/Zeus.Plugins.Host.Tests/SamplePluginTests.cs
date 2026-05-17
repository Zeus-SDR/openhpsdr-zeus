using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Zeus.Plugins.Host;
using DotnetHost = Microsoft.Extensions.Hosting.Host;

namespace Zeus.Plugins.Host.Tests;

/// <summary>
/// End-to-end integration: builds the canonical samples, hands them
/// to PluginManager, exercises the resulting REST endpoints. Verifies
/// that a plugin author can ship a class library + plugin.json and
/// have it appear under <c>/api/plugins</c> with a working backend.
/// </summary>
public class SamplePluginTests : IDisposable
{
    private readonly string _root;

    public SamplePluginTests()
    {
        _root = Path.Combine(Path.GetTempPath(),
            "zeus-sample-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private string SampleSourceDir(string name)
        => Path.Combine(AppContext.BaseDirectory, "sample-plugins", name);

    private string CopySampleIntoRoot(string name, string installDirName)
    {
        var src = SampleSourceDir(name);
        var dst = Path.Combine(_root, installDirName);
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src))
        {
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
        }
        return dst;
    }

    [Fact]
    public async Task HelloWorld_Loads_And_Reports_Manifest()
    {
        var dir = CopySampleIntoRoot("HelloWorld", "helloworld");

        var loader = new PluginLoader(NullLogger<PluginLoader>.Instance);
        using var store = new PluginSettingsStore(Path.Combine(_root, "settings.db"));
        var manager = new PluginManager(loader, store, new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance);

        var activated = await manager.ActivateAsync(dir, default);

        Assert.Equal("com.openhpsdr.zeus.samples.helloworld", activated.Loaded.Manifest.Id);
        Assert.Equal("1.0.0", activated.Loaded.Manifest.Version);
        Assert.Equal("Hello World", activated.Loaded.Manifest.Name);

        await manager.StopAsync(default);
        store.Dispose();
    }

    [Fact]
    public async Task Amplifier_Plugin_Status_Endpoint_Returns_Expected_Json()
    {
        var dir = CopySampleIntoRoot("Amplifier", "amplifier");

        var loader = new PluginLoader(NullLogger<PluginLoader>.Instance);
        using var store = new PluginSettingsStore(Path.Combine(_root, "settings.db"));
        var manager = new PluginManager(loader, store, new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance);
        await manager.ActivateAsync(dir, default);

        // Spin up an in-process test server with PluginEndpoints mounted.
        using var host = await BuildTestServerAsync(manager);
        var client = host.GetTestClient();

        var status = await client.GetFromJsonAsync<AmpStatus>(
            "/api/plugins/com.openhpsdr.zeus.samples.amplifier/status");
        Assert.NotNull(status);
        Assert.Equal(0, status!.PowerWatts);
        Assert.InRange(status.Swr, 1.0, 2.0);
        Assert.Null(status.Fault);

        // Mutate
        var set = await client.PostAsJsonAsync(
            "/api/plugins/com.openhpsdr.zeus.samples.amplifier/power",
            new { watts = 750 });
        Assert.True(set.IsSuccessStatusCode);

        status = await client.GetFromJsonAsync<AmpStatus>(
            "/api/plugins/com.openhpsdr.zeus.samples.amplifier/status");
        Assert.Equal(750, status!.PowerWatts);

        await manager.StopAsync(default);
        store.Dispose();
    }

    [Fact]
    public async Task GetApiPlugins_Lists_All_Activated()
    {
        var hwDir = CopySampleIntoRoot("HelloWorld", "helloworld");
        var ampDir = CopySampleIntoRoot("Amplifier", "amplifier");

        var loader = new PluginLoader(NullLogger<PluginLoader>.Instance);
        using var store = new PluginSettingsStore(Path.Combine(_root, "settings.db"));
        var manager = new PluginManager(loader, store, new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance);
        await manager.ActivateAsync(hwDir, default);
        await manager.ActivateAsync(ampDir, default);

        using var host = await BuildTestServerAsync(manager);
        var client = host.GetTestClient();

        var resp = await client.GetFromJsonAsync<PluginListResponse>("/api/plugins");
        Assert.NotNull(resp);
        Assert.Equal(Zeus.Plugins.Contracts.AbiVersion.Current, resp!.SdkAbi);
        Assert.Equal(2, resp.Plugins.Count);

        await manager.StopAsync(default);
        store.Dispose();
    }

    private static async Task<IHost> BuildTestServerAsync(PluginManager manager)
    {
        var builder = DotnetHost.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(s => s.AddRouting());
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        PluginEndpoints.MapAll(endpoints, manager);
                    });
                });
            });

        var host = await builder.StartAsync();
        return host;
    }

    private sealed record AmpStatus
    {
        public int PowerWatts { get; init; }
        public double Swr { get; init; }
        public string? Fault { get; init; }
    }
}
