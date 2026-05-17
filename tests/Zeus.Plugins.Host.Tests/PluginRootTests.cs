using Zeus.Plugins.Host;

namespace Zeus.Plugins.Host.Tests;

public class PluginRootTests
{
    [Fact]
    public void EnvVar_Overrides_Default()
    {
        var prev = Environment.GetEnvironmentVariable(PluginRoot.EnvVar);
        try
        {
            var custom = Path.Combine(Path.GetTempPath(), "zeus-plugins-test-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(PluginRoot.EnvVar, custom);
            Assert.Equal(custom, PluginRoot.Get());
        }
        finally
        {
            Environment.SetEnvironmentVariable(PluginRoot.EnvVar, prev);
        }
    }

    [Fact]
    public void Default_IsNonEmpty()
    {
        var prev = Environment.GetEnvironmentVariable(PluginRoot.EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(PluginRoot.EnvVar, null);
            var p = PluginRoot.DefaultPath();
            Assert.False(string.IsNullOrWhiteSpace(p));
            Assert.Contains("plugins", p, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(PluginRoot.EnvVar, prev);
        }
    }

    [Fact]
    public void EnsureExists_CreatesTheDirectory()
    {
        var prev = Environment.GetEnvironmentVariable(PluginRoot.EnvVar);
        try
        {
            var custom = Path.Combine(Path.GetTempPath(), "zeus-plugins-test-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(PluginRoot.EnvVar, custom);
            Assert.False(Directory.Exists(custom));
            PluginRoot.EnsureExists();
            Assert.True(Directory.Exists(custom));
            Directory.Delete(custom);
        }
        finally
        {
            Environment.SetEnvironmentVariable(PluginRoot.EnvVar, prev);
        }
    }
}
