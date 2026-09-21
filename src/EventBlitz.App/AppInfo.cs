using System.Reflection;
using System.Security.Principal;

namespace EventBlitz.App;

/// <summary>Facts about the running build and process that several places need.</summary>
public static class AppInfo
{
    public const string Name = "EventBlitz";

    /// <summary>"0.1.0" or "0.1.0-nightly.abc1234": the informational version without the "+commit" metadata.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>The version as the header shows it.</summary>
    public static string VersionLabel => "v" + Version;

    /// <summary>
    /// Settings and everything else the app writes: <c>EVENTBLITZ_DATA_DIR</c> (fixtures for scripted UI checks)
    /// or <c>%APPDATA%\EventBlitz</c>.
    /// </summary>
    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static bool IsAdministrator { get; } = CheckAdministrator();

    private static string ReadVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        var informational = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        var version = assembly?.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string ResolveDataDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable("EVENTBLITZ_DATA_DIR");
        var directory = string.IsNullOrWhiteSpace(overridden)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name)
            : Path.GetFullPath(overridden);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static bool CheckAdministrator()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
