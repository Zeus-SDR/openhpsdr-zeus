namespace Zeus.Plugins.Contracts;

/// <summary>
/// Thrown by the host when a plugin tries to use a capability that
/// either was not declared in its manifest or was not granted by the
/// user. Plugin authors SHOULD null-check the IPluginContext slot
/// rather than rely on this exception.
/// </summary>
public sealed class PluginPermissionException : Exception
{
    public PluginPermissionException(string pluginId, PluginCapabilities required)
        : base($"Plugin '{pluginId}' attempted to use capability '{required}' which is not granted.")
    {
        PluginId = pluginId;
        RequiredCapability = required;
    }

    public string PluginId { get; }
    public PluginCapabilities RequiredCapability { get; }
}
