using System.Text.Json;
using Avalonia;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.FontAwesome;
using TomLabs.UiAutomation;

namespace EventBlitz.App;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // A desktop application has no HTTP surface, so the health document is available on the command line
        // instead: `EventBlitz --health` prints the same JSON a web application would serve on GET /health.
        if (Array.Exists(args, a => a.Equals("--health", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(JsonSerializer.Serialize(new { status = "Healthy", version = AppInfo.Version, name = AppInfo.Name }));
            return 0;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashLog(e.ExceptionObject as Exception);

        try
        {
            return BuildAvaloniaApp(args).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(Array.Empty<string>());

    /// <summary>
    /// `--headless` renders into memory instead of a real window, so a script can drive and screenshot the UI through
    /// the automation channel (EVENTBLITZ_AUTOMATION=&lt;port&gt;, TomLabs.UiAutomation) without touching the user's desktop.
    /// </summary>
    private static AppBuilder BuildAvaloniaApp(string[] args)
    {
        IconProvider.Current.Register<FontAwesomeIconProvider>();

        return AppBuilder.Configure<App>()
            .UsePlatformDetectOrHeadless(args)
            .LogToTrace();
    }

    /// <summary>A GUI app has no console to read; unhandled exceptions land in the data folder's crash.log as well as the log.</summary>
    internal static void WriteCrashLog(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            File.AppendAllText(Path.Combine(AppInfo.DataDirectory, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
            Services.AppLog.For("crash").Fatal(ex, "Unhandled exception");
        }
        catch
        {
            // Nothing left to do if even the crash log cannot be written.
        }
    }
}
