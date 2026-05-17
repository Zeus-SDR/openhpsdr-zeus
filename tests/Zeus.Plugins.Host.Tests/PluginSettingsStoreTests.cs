using Zeus.Plugins.Host;

namespace Zeus.Plugins.Host.Tests;

public class PluginSettingsStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PluginSettingsStore _store;

    public PluginSettingsStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(),
            "zeus-plugins-store-" + Guid.NewGuid().ToString("N") + ".db");
        _store = new PluginSettingsStore(_dbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { File.Delete(_dbPath); } catch { /* ignore */ }
    }

    [Fact]
    public async Task SetThenGet_RoundTrips_PrimitiveString()
    {
        var s = _store.ForPlugin("com.example.a");
        await s.SetAsync("name", "alice");
        Assert.Equal("alice", await s.GetAsync<string>("name"));
    }

    [Fact]
    public async Task SetThenGet_RoundTrips_Record()
    {
        var s = _store.ForPlugin("com.example.a");
        var sample = new Sample(7, "x");
        await s.SetAsync("k", sample);
        Assert.Equal(sample, await s.GetAsync<Sample>("k"));
    }

    [Fact]
    public async Task Get_MissingKey_ReturnsDefault()
    {
        var s = _store.ForPlugin("com.example.a");
        Assert.Null(await s.GetAsync<string>("absent"));
        Assert.Equal(0, await s.GetAsync<int>("absent"));
    }

    [Fact]
    public async Task ScopedPerPluginId_NoCrossTalk()
    {
        var a = _store.ForPlugin("com.example.a");
        var b = _store.ForPlugin("com.example.b");

        await a.SetAsync("shared-key", "from-a");
        await b.SetAsync("shared-key", "from-b");

        Assert.Equal("from-a", await a.GetAsync<string>("shared-key"));
        Assert.Equal("from-b", await b.GetAsync<string>("shared-key"));
    }

    [Fact]
    public async Task Delete_RemovesKey()
    {
        var s = _store.ForPlugin("com.example.a");
        await s.SetAsync("k", 42);
        Assert.Equal(42, await s.GetAsync<int>("k"));
        await s.DeleteAsync("k");
        Assert.Equal(0, await s.GetAsync<int>("k"));
    }

    [Fact]
    public async Task SurvivesReopen()
    {
        var s = _store.ForPlugin("com.example.a");
        await s.SetAsync("persist", "value-1");

        _store.Dispose();
        using var reopened = new PluginSettingsStore(_dbPath);
        var s2 = reopened.ForPlugin("com.example.a");
        Assert.Equal("value-1", await s2.GetAsync<string>("persist"));
    }

    [Fact]
    public void EmptyPluginId_Throws()
    {
        Assert.Throws<ArgumentException>(() => _store.ForPlugin(""));
    }

    private sealed record Sample(int N, string S);
}
