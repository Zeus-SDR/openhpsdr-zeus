using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zeus.Plugins.Contracts;
using Zeus.Plugins.Contracts.Extensions;
using Zeus.Plugins.Host.Registry;

namespace Zeus.Plugins.Host;

/// <summary>
/// REST endpoints for the plugin system. Mounts under <c>/api/plugins</c>.
/// Plugin-owned endpoints (from <see cref="IBackendPlugin"/>) land under
/// <c>/api/plugins/{id}/...</c> and are mapped during activation by
/// <see cref="MapAll"/> — call once at app start.
/// </summary>
public static class PluginEndpoints
{
    public static void MapAll(IEndpointRouteBuilder app, PluginManager manager)
    {
        app.MapGet("/api/plugins", () =>
        {
            var items = manager.Active.Select(p => ToDto(p)).ToArray();
            return Results.Ok(new PluginListResponse
            {
                SdkAbi = AbiVersion.Current,
                SdkVersion = AbiVersion.SdkVersion,
                Plugins = items,
            });
        });

        app.MapGet("/api/plugins/{id}", (string id) =>
        {
            var p = manager.Find(id);
            return p is null ? Results.NotFound() : Results.Ok(ToDto(p));
        });

        app.MapGet("/api/plugins/registry", async (
            IRegistryClient registry, CancellationToken ct) =>
        {
            try
            {
                var catalog = await registry.FetchAsync(ct);
                return Results.Ok(new RegistryResponse { SourceUrl = registry.SourceUrl, Catalog = catalog });
            }
            catch (RegistryFetchException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "registry-fetch-failed",
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });

        app.MapPost("/api/plugins/install", async (
            InstallRequest req, PluginInstaller installer, CancellationToken ct) =>
        {
            try
            {
                InstalledPlugin installed = req.Source switch
                {
                    "url"      => await installer.InstallFromUrlAsync(req.Url ?? "", req.Sha256, ct),
                    "file"     => await installer.InstallFromZipFileAsync(req.FilePath ?? "", ct),
                    "registry" => await installer.InstallFromRegistryAsync(req.Id ?? "", req.Version ?? "", ct),
                    _          => throw new PluginInstallException($"unknown source '{req.Source}'"),
                };
                return Results.Ok(ToDto(installed.Activated));
            }
            catch (PluginInstallException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapDelete("/api/plugins/{id}", async (
            string id, PluginInstaller installer, CancellationToken ct) =>
        {
            try
            {
                await installer.UninstallAsync(id, ct);
                return Results.NoContent();
            }
            catch (PluginInstallException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "plugin-uninstall-deferred",
                    statusCode: StatusCodes.Status202Accepted);
            }
        });

        // Per-plugin endpoints from IBackendPlugin
        foreach (var p in manager.Active)
        {
            MapBackendEndpointsFor(app, p);
        }
    }

    /// <summary>
    /// Re-maps the backend endpoints for plugins activated after app
    /// startup (e.g. after BYOP install). Idempotent per plugin id;
    /// existing mappings are NOT removed because ASP.NET routing is
    /// immutable post-build. Restart Zeus to fully unmap a plugin's
    /// endpoints.
    /// </summary>
    public static void MapBackendEndpointsFor(IEndpointRouteBuilder app, ActivatedPlugin p)
    {
        if (p.Loaded.Plugin is not IBackendPlugin backend) return;
        var group = app.MapGroup($"/api/plugins/{p.Loaded.Manifest.Id}");
        try
        {
            backend.MapEndpoints(group);
        }
        catch (Exception ex)
        {
            // Logged but not rethrown — a bad plugin endpoint mapping
            // shouldn't take down server startup.
            Console.Error.WriteLine(
                $"[plugins] {p.Loaded.Manifest.Id}: MapEndpoints threw: {ex.Message}");
        }
    }

    internal static PluginDto ToDto(ActivatedPlugin p) => new()
    {
        Id = p.Loaded.Manifest.Id,
        Name = p.Loaded.Manifest.Name,
        Version = p.Loaded.Manifest.Version,
        Author = p.Loaded.Manifest.Author,
        Description = p.Loaded.Manifest.Description,
        Homepage = p.Loaded.Manifest.Homepage,
        License = p.Loaded.Manifest.License,
        Capabilities = p.Context.GrantedCapabilities.ToString().Split(", "),
        Ui = p.Loaded.Manifest.Ui is null ? null : new PluginUiDto
        {
            Modules = p.Loaded.Manifest.Ui.Modules,
            Panels = p.Loaded.Manifest.Ui.Panels.Select(panel => new PluginPanelDto
            {
                Id = panel.Id,
                Title = panel.Title,
                Icon = panel.Icon,
                Slot = panel.Slot,
            }).ToArray(),
        },
        Audio = p.Loaded.Manifest.Audio is { } a ? new PluginAudioDto
        {
            Vst3Path = a.Vst3Path,
            Slot = a.Slot,
            Channels = a.Channels,
            SampleRate = a.SampleRate,
        } : null,
    };
}

public sealed record PluginListResponse
{
    public int SdkAbi { get; init; }
    public string SdkVersion { get; init; } = "";
    public IReadOnlyList<PluginDto> Plugins { get; init; } = Array.Empty<PluginDto>();
}

public sealed record PluginDto
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Homepage { get; init; }
    public string License { get; init; } = "";
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();
    public PluginUiDto? Ui { get; init; }
    public PluginAudioDto? Audio { get; init; }
}

public sealed record PluginUiDto
{
    public IReadOnlyList<string> Modules { get; init; } = Array.Empty<string>();
    public IReadOnlyList<PluginPanelDto> Panels { get; init; } = Array.Empty<PluginPanelDto>();
}

public sealed record PluginPanelDto
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Icon { get; init; } = "";
    public string Slot { get; init; } = "";
}

public sealed record PluginAudioDto
{
    public string? Vst3Path { get; init; }
    public string Slot { get; init; } = "";
    public int Channels { get; init; }
    public int SampleRate { get; init; }
}

public sealed record RegistryResponse
{
    public string SourceUrl { get; init; } = "";
    public Zeus.Plugins.Contracts.Registry.RegistryCatalog Catalog { get; init; }
        = new Zeus.Plugins.Contracts.Registry.RegistryCatalog();
}

public sealed record InstallRequest
{
    /// <summary>One of: "url", "file", "registry".</summary>
    public string Source { get; init; } = "url";

    /// <summary>HTTPS download URL. Used when Source = "url".</summary>
    public string? Url { get; init; }

    /// <summary>Absolute path to a .zip on disk. Used when Source = "file".</summary>
    public string? FilePath { get; init; }

    /// <summary>Optional hex SHA-256 of the zip, verified before extraction.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Plugin id. Used when Source = "registry".</summary>
    public string? Id { get; init; }

    /// <summary>Plugin version. Used when Source = "registry".</summary>
    public string? Version { get; init; }
}
