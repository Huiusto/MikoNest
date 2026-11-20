using System.Reflection;

namespace MiNest.Avalonia;

public static class AppInfo
{
    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.13";
}
