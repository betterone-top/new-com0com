using System.Reflection;

namespace Com0com.Core;

/// <summary>Build information shown by the front-ends.</summary>
public static class BuildInfo
{
    /// <summary>
    /// Version stamped at build time: UTC year.month.day.HHmm without leading zeros,
    /// e.g. "26.11.20.1526" or "26.1.5.930".
    /// </summary>
    public static string Version { get; } =
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(BuildInfo).Assembly.GetName().Version?.ToString()
        ?? "?";
}
