using System.Reflection;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// The LIM release version (Directory.Build.props stamps every assembly with it), shown in each
/// tool's title bar so a screenshot says which version it came from.
/// </summary>
public static class LimVersion
{
    public static string Text { get; } = $"v{(typeof(LimVersion).Assembly.GetName().Version ?? new Version(0, 0, 0)).ToString(3)}";
}
