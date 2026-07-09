using System.Reflection;

namespace ClaudeUsageTray;

public static class AppInfo
{
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";

    public const string RepositoryUrl = "https://github.com/bluepoke/ClaudeUsage";
}
