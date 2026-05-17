using System.Text.RegularExpressions;
using Zeus.Plugins.Contracts;

namespace Zeus.Plugins.Host;

/// <summary>
/// Runtime validation of a parsed <see cref="PluginManifest"/>. Returns
/// a non-empty failure list if any rule is broken; an empty list means
/// the manifest is loadable.
/// </summary>
public static partial class ManifestValidator
{
    [GeneratedRegex(@"^[a-z][a-z0-9.]*[a-z0-9]$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^[0-9]+\.[0-9]+\.[0-9]+([+-][0-9A-Za-z.-]+)?$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^[0-9]+\.[0-9]+\.[0-9]+$")]
    private static partial Regex StrictSemVerPattern();

    public static IReadOnlyList<string> Validate(PluginManifest m)
    {
        var errors = new List<string>();

        if (m.SchemaVersion != 1)
            errors.Add($"unsupported schemaVersion: {m.SchemaVersion} (this host accepts 1)");

        if (string.IsNullOrWhiteSpace(m.Id) || !IdPattern().IsMatch(m.Id))
            errors.Add($"invalid id '{m.Id}': must match {IdPattern()}");

        if (string.IsNullOrWhiteSpace(m.Name))
            errors.Add("name is required");

        if (string.IsNullOrWhiteSpace(m.Version) || !VersionPattern().IsMatch(m.Version))
            errors.Add($"invalid version '{m.Version}'");

        if (m.Sdk is null)
        {
            errors.Add("sdk block missing");
        }
        else
        {
            if (m.Sdk.Abi <= 0)
                errors.Add($"invalid sdk.abi: {m.Sdk.Abi}");
            if (string.IsNullOrWhiteSpace(m.Sdk.MinVersion) || !StrictSemVerPattern().IsMatch(m.Sdk.MinVersion))
                errors.Add($"invalid sdk.minVersion '{m.Sdk.MinVersion}'");
        }

        if (m.Entrypoint is null || string.IsNullOrWhiteSpace(m.Entrypoint.Assembly))
            errors.Add("entrypoint.assembly missing");
        else if (!m.Entrypoint.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            errors.Add($"entrypoint.assembly must end in .dll, got '{m.Entrypoint.Assembly}'");
        else if (Path.IsPathRooted(m.Entrypoint.Assembly) ||
                 m.Entrypoint.Assembly.Contains("..", StringComparison.Ordinal))
            errors.Add("entrypoint.assembly must be a plain filename relative to the plugin root");

        if (m.Ui is not null)
        {
            foreach (var p in m.Ui.Panels)
            {
                if (string.IsNullOrWhiteSpace(p.Id))
                    errors.Add("ui.panels[].id is required");
                if (string.IsNullOrWhiteSpace(p.Slot))
                    errors.Add("ui.panels[].slot is required");
            }
        }

        if (m.Audio is { Vst3Path: { Length: > 0 } vst3 })
        {
            if (Path.IsPathRooted(vst3) || vst3.Contains("..", StringComparison.Ordinal))
                errors.Add("audio.vst3Path must be relative to the plugin root");
        }

        return errors;
    }

    /// <summary>
    /// Returns true iff the manifest is binary-compatible with the host's
    /// SDK. Caller should also run <see cref="Validate"/> for structural
    /// issues.
    /// </summary>
    public static bool IsAbiCompatible(PluginManifest m, int hostAbi, string hostSdkVersion)
    {
        if (m.Sdk is null) return false;
        if (m.Sdk.Abi != hostAbi) return false;

        if (!Version.TryParse(m.Sdk.MinVersion, out var min)) return false;
        if (!Version.TryParse(hostSdkVersion, out var host)) return false;

        if (min.Major != host.Major) return false;
        return min <= host;
    }
}
