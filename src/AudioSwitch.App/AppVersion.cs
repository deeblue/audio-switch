using System.Reflection;
using AudioSwitch.Core;

namespace AudioSwitch.App;

/// <summary>Version shown to the user, so different builds of the same version can be told apart.</summary>
public static class AppVersion
{
    private static readonly Assembly Assembly = typeof(AppVersion).Assembly;

    public static string Version { get; } =
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    public static string BuildText => FormatBuild(Metadata("BuildCommit"), Metadata("BuildHasLocalChanges") == "true", Metadata("BuildTime"));

    public static string DisplayText => string.IsNullOrEmpty(BuildText) ? Loc.F("版本 {0}", Version) : Loc.F("版本 {0}（{1}）", Version, BuildText);

    internal static string FormatBuild(string? commit, bool hasLocalChanges, string? buildTime)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(commit)) parts.Add(hasLocalChanges ? Loc.F("{0} 含未提交修改", commit) : commit);
        if (!string.IsNullOrWhiteSpace(buildTime)) parts.Add(Loc.F("建置於 {0}", buildTime));
        return string.Join(Loc.T("，"), parts);
    }

    private static string? Metadata(string key) =>
        Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == key)?.Value;
}
