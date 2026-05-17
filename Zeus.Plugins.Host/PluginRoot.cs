using System.Runtime.InteropServices;

namespace Zeus.Plugins.Host;

/// <summary>
/// Resolves the directory the host scans for plugins. Override via
/// <c>ZEUS_PLUGINS_PATH</c> env var (useful in tests and CI). Default
/// is platform-conventional under the user profile.
/// </summary>
public static class PluginRoot
{
    public const string EnvVar = "ZEUS_PLUGINS_PATH";

    public static string Get() =>
        Environment.GetEnvironmentVariable(EnvVar)
        ?? DefaultPath();

    public static string DefaultPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.Create);
            return Path.Combine(appData, "Zeus", "plugins");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "Zeus", "plugins");
        }

        // Linux + others — XDG base dir spec
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(xdg))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            xdg = Path.Combine(home, ".local", "share");
        }
        return Path.Combine(xdg, "zeus", "plugins");
    }

    /// <summary>Ensure the plugin root exists. Idempotent.</summary>
    public static string EnsureExists()
    {
        var path = Get();
        Directory.CreateDirectory(path);
        return path;
    }
}
