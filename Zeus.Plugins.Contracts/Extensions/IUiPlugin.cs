namespace Zeus.Plugins.Contracts.Extensions;

/// <summary>
/// Optional extension for plugins that contribute frontend modules.
/// Most plugins do not implement this in C#: declaring an
/// <see cref="UiBlock"/> in <c>plugin.json</c> is sufficient for the
/// host to serve the modules at
/// <c>/plugins/{id}/ui/{module}</c>. Implement this only if the plugin
/// needs to gate UI visibility based on runtime state.
/// </summary>
public interface IUiPlugin
{
    /// <summary>
    /// Return true if the plugin's UI contributions should be loaded
    /// for the current Zeus session. Called once after init.
    /// </summary>
    bool ShouldLoadUi();
}
