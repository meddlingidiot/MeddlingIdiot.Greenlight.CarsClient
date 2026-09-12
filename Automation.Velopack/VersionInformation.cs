using System.Reflection;

namespace Automation.Velopack;

public static class VersionInformation
{
    public static string FullVersion
    {
        get
        {
            var fullVersion = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            // Might fix version bug where Velopack version is shown.
            fullVersion ??= Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown";
            
            // Left as extra fallback, but this might be what causes the Velopack version to be shown in some cases
            fullVersion ??= Assembly.GetCallingAssembly().GetName().Version?.ToString() ?? "Unknown";

            return fullVersion;
        }
    }

    public static string Version
    {
        get
        {
            var full = FullVersion;
            var plusIndex = full.IndexOf('+');
            return plusIndex > 0 ? full.Substring(0, plusIndex) : full;
        }
    }
}
