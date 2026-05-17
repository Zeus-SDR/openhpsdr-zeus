using Zeus.Plugins.Contracts;

namespace Zeus.Plugins.Contracts.Tests;

public class PluginPermissionExceptionTests
{
    [Fact]
    public void CapturesPluginIdAndCapability()
    {
        var ex = new PluginPermissionException("com.example.test", PluginCapabilities.NetworkAccess);
        Assert.Equal("com.example.test", ex.PluginId);
        Assert.Equal(PluginCapabilities.NetworkAccess, ex.RequiredCapability);
        Assert.Contains("NetworkAccess", ex.Message);
        Assert.Contains("com.example.test", ex.Message);
    }
}
