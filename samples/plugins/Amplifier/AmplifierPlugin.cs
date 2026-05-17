using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Zeus.Plugins.Contracts;
using Zeus.Plugins.Contracts.Extensions;

namespace Openhpsdr.Zeus.Samples.Amplifier;

/// <summary>
/// Sample amplifier plugin — the canonical example of a plugin that
/// contributes both backend HTTP endpoints and a frontend panel.
///
/// State is intentionally in-memory and synthetic: there's no real
/// hardware behind it. Operators wiring up a real amplifier should
/// replace this with their device-specific code (CAT, GPIO, RS-485,
/// network, etc.) — the contract with Zeus stays identical.
/// </summary>
public sealed class AmplifierPlugin : IZeusPlugin, IBackendPlugin
{
    private readonly object _lock = new();
    private AmplifierState _state = new(PowerWatts: 0, SwrTenths: 11, Fault: null);
    private IPluginContext? _ctx;

    public Task InitializeAsync(IPluginContext context, CancellationToken ct)
    {
        _ctx = context;
        context.Logger.LogInformation(
            "Amplifier sample plugin online; state = {PowerWatts} W, SWR {Swr:F1}",
            _state.PowerWatts, _state.SwrTenths / 10.0);
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken ct) => Task.CompletedTask;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("status", GetStatus);
        endpoints.MapPost("power", SetPower);
        endpoints.MapPost("reset", ResetAmp);
    }

    private IResult GetStatus()
    {
        lock (_lock) return Results.Ok(ToDto(_state));
    }

    private IResult SetPower(SetPowerRequest req)
    {
        if (req.Watts is < 0 or > 2000)
            return Results.BadRequest(new { error = "watts must be in 0..2000" });

        lock (_lock)
        {
            _state = _state with { PowerWatts = req.Watts };
            _ctx?.Logger.LogInformation("Amp power set to {Watts} W", req.Watts);
            return Results.Ok(ToDto(_state));
        }
    }

    private IResult ResetAmp()
    {
        lock (_lock)
        {
            _state = _state with { Fault = null };
            _ctx?.Logger.LogInformation("Amp fault cleared");
            return Results.Ok(ToDto(_state));
        }
    }

    private static AmplifierStatusDto ToDto(AmplifierState s) => new()
    {
        PowerWatts = s.PowerWatts,
        Swr = s.SwrTenths / 10.0,
        Fault = s.Fault,
    };

    private sealed record AmplifierState(int PowerWatts, int SwrTenths, string? Fault);

    private sealed record SetPowerRequest(int Watts);

    public sealed record AmplifierStatusDto
    {
        [JsonPropertyName("powerWatts")] public int PowerWatts { get; init; }
        [JsonPropertyName("swr")]        public double Swr { get; init; }
        [JsonPropertyName("fault")]      public string? Fault { get; init; }
    }
}
