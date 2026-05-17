using Microsoft.AspNetCore.Routing;

namespace Zeus.Plugins.Contracts.Extensions;

/// <summary>
/// Optional extension a plugin can implement alongside <see cref="IZeusPlugin"/>
/// to contribute HTTP endpoints under <c>/api/plugins/{plugin-id}/...</c>
/// </summary>
public interface IBackendPlugin
{
    /// <summary>
    /// Called once during plugin activation, after
    /// <see cref="IZeusPlugin.InitializeAsync"/>. The endpoint route
    /// builder is already scoped to the plugin's URL prefix; mapping
    /// <c>"status"</c> exposes it at <c>/api/plugins/{id}/status</c>.
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
