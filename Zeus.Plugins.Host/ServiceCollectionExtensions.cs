using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zeus.Plugins.Host.Registry;

namespace Zeus.Plugins.Host;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register the plugin system. The settings store uses the path
    /// returned by <paramref name="prefsDbPathProvider"/> — typically a
    /// thin wrapper around Zeus.Server.PrefsDbPath.Get(). Callers MUST
    /// also call <see cref="PluginEndpoints.MapAll"/> on their endpoint
    /// route builder.
    /// </summary>
    public static IServiceCollection AddZeusPlugins(
        this IServiceCollection services,
        Func<string> prefsDbPathProvider,
        PluginManagerOptions? options = null,
        RegistryClientOptions? registryOptions = null)
    {
        services.AddSingleton<PluginLoader>();
        services.AddSingleton(sp => new PluginSettingsStore(
            prefsDbPathProvider(),
            sp.GetService<ILogger<PluginSettingsStore>>()));
        services.AddSingleton(sp => new PluginManager(
            loader: sp.GetRequiredService<PluginLoader>(),
            settings: sp.GetRequiredService<PluginSettingsStore>(),
            services: sp,
            logFactory: sp.GetRequiredService<ILoggerFactory>(),
            options: options));
        services.AddHostedService(sp => sp.GetRequiredService<PluginManager>());

        // Registry + installer — uses the typed HttpClient pattern so
        // operators can replace the default user-agent / timeouts via
        // IHttpClientFactory configuration if needed.
        services.AddHttpClient<HttpRegistryClient>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Openhpsdr-Zeus/1.0 (plugins-registry-client)");
        });
        services.AddSingleton<IRegistryClient>(sp => sp.GetRequiredService<HttpRegistryClient>());
        if (registryOptions is not null)
            services.AddSingleton(registryOptions);

        services.AddHttpClient<PluginInstaller>(c =>
        {
            c.Timeout = TimeSpan.FromMinutes(2);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Openhpsdr-Zeus/1.0 (plugins-installer)");
        });
        services.AddSingleton(sp => new PluginInstaller(
            http: sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PluginInstaller)),
            registry: sp.GetRequiredService<IRegistryClient>(),
            manager: sp.GetRequiredService<PluginManager>(),
            pluginRoot: options?.PluginRoot ?? PluginRoot.Get(),
            log: sp.GetService<ILogger<PluginInstaller>>()));

        return services;
    }
}
