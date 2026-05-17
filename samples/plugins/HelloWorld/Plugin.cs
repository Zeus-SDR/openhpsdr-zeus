using Microsoft.Extensions.Logging;
using Zeus.Plugins.Contracts;

namespace Openhpsdr.Zeus.Samples.HelloWorld;

/// <summary>
/// Smallest possible Zeus plugin — logs "Hello from Zeus plugin" on
/// activation and "Goodbye" on shutdown. Sample for plugin authors;
/// also exercised by the host's integration tests.
/// </summary>
public sealed class HelloWorldPlugin : IZeusPlugin
{
    private IPluginContext? _ctx;

    public Task InitializeAsync(IPluginContext context, CancellationToken ct)
    {
        _ctx = context;
        context.Logger.LogInformation(
            "Hello from Zeus plugin {Id} v{Version}",
            context.PluginId,
            context.Manifest.Version);
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken ct)
    {
        _ctx?.Logger.LogInformation("Goodbye from Zeus plugin {Id}", _ctx.PluginId);
        return Task.CompletedTask;
    }
}
