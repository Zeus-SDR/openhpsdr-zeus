// SPDX-License-Identifier: GPL-2.0-or-later
//
// CapabilitiesService — single source of truth for the /api/capabilities
// endpoint. Captures host-mode, platform / architecture, and per-feature
// availability once at construction and serves the same snapshot for the
// lifetime of the process.
//
// Probe-once-at-startup is deliberate. The frontend caches the response
// anyway. Feature-gate fields will be reintroduced as the new plugin
// system lands; the FeatureMatrix is kept as an empty record so callers
// can rely on a stable JSON shape.

using System.Reflection;
using System.Runtime.InteropServices;

namespace Zeus.Server;

public sealed class CapabilitiesService
{
    private readonly CapabilitiesSnapshot _snapshot;

    public CapabilitiesService(ZeusHostOptions options)
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

        var platform = DetectPlatform();
        var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

        _snapshot = new CapabilitiesSnapshot(
            Host: options.HostMode == ZeusHostMode.Desktop ? "desktop" : "server",
            Platform: platform,
            Architecture: architecture,
            Version: version,
            Features: new FeatureMatrix());
    }

    public CapabilitiesSnapshot Snapshot() => _snapshot;

    private static string DetectPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "darwin";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
        return "unknown";
    }
}

// JSON shape returned by /api/capabilities. Property names land lower-case
// on the wire via the default minimal-API camel-case policy, matching the
// rest of the Zeus REST surface.

public sealed record CapabilitiesSnapshot(
    string Host,
    string Platform,
    string Architecture,
    string Version,
    FeatureMatrix Features);

public sealed record FeatureMatrix();
