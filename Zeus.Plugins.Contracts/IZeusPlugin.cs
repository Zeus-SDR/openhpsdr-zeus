namespace Zeus.Plugins.Contracts;

/// <summary>
/// Required interface for every Openhpsdr-Zeus plugin. The host discovers
/// implementations by reflection in the assembly named by
/// <see cref="PluginManifest.Entrypoint"/>.
///
/// Implementations MUST be public, instantiable via the default constructor,
/// and stateless until <see cref="InitializeAsync"/> is called. The host
/// catches every exception thrown by these methods; a throw during
/// <c>InitializeAsync</c> aborts loading, a throw during
/// <c>ShutdownAsync</c> is logged and discarded.
/// </summary>
public interface IZeusPlugin
{
    /// <summary>
    /// Called once after the plugin is loaded and before any extension
    /// interfaces (<c>IBackendPlugin</c>, <c>IUiPlugin</c>,
    /// <c>IAudioPlugin</c>) are invoked. Honour <paramref name="ct"/>;
    /// the host applies a 10-second timeout.
    /// </summary>
    Task InitializeAsync(IPluginContext context, CancellationToken ct);

    /// <summary>
    /// Called once before the plugin's <see cref="System.Runtime.Loader.AssemblyLoadContext"/>
    /// is unloaded. The host applies a 5-second timeout; if it expires
    /// the plugin is force-unloaded and any leaked threads remain a
    /// debugging problem for the plugin author.
    /// </summary>
    Task ShutdownAsync(CancellationToken ct);
}
