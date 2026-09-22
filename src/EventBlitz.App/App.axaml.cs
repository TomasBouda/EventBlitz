using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using EventBlitz.App.Services;
using EventBlitz.App.ViewModels;
using EventBlitz.App.Views;
using EventBlitz.Core.Services;
using TomLabs.AutoUpdate;
using TomLabs.UiAutomation;

namespace EventBlitz.App;

public partial class App : Application
{
    private UserSettings _settings = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _settings = UserSettings.Load();
        AppLog.Initialize(_settings);
        ApplySavedTheme();
    }

    /// <summary>Restores the theme the user picked last time; without a saved choice the OS preference applies.</summary>
    private void ApplySavedTheme()
    {
        RequestedThemeVariant = _settings.Theme switch
        {
            "Dark" => ThemeVariant.Dark,
            "Light" => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // An exception in a command or event handler should not take the whole viewer down.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Program.WriteCrashLog(e.Exception);
            e.Handled = true;
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            StartUpdater(desktop);

            var viewModel = new MainWindowViewModel(CreateSource(), _settings);
            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            var automation = StartAutomation(desktop, viewModel);
            desktop.Exit += (_, _) =>
            {
                automation?.Dispose();
                viewModel.Shutdown();
                AppLog.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The live Event Log, or the JSON fixture when the data folder carries one (scripted UI checks).</summary>
    private static IEventLogSource CreateSource()
    {
        var fixture = Path.Combine(AppInfo.DataDirectory, FixtureEventLogSource.FileName);
        if (File.Exists(fixture))
        {
            try
            {
                AppLog.For("app").Information("Using the fixture event source {Path}", fixture);
                return FixtureEventLogSource.Load(fixture);
            }
            catch (Exception ex)
            {
                AppLog.For("app").Error(ex, "The fixture {Path} cannot be read; falling back to the live Event Log", fixture);
            }
        }
        return new WindowsEventLogSource();
    }

    /// <summary>
    /// Scripted UI channel (EVENTBLITZ_AUTOMATION=&lt;port&gt;, see CLAUDE.md) with EventBlitz's own shortcuts on top of the
    /// generic endpoints: <c>/do/channel?arg=Application,System</c> selects channels, <c>/do/page?arg=events|log</c> switches pages.
    /// </summary>
    private static UiAutomationServer? StartAutomation(IClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel viewModel)
    {
        var server = UiAutomationServer.StartIfEnabled(desktop, AppInfo.Name, AppInfo.Version, message => AppLog.For("automation").Information("{Message}", message));
        if (server is null)
            return null;

        server.Actions["channel"] = (_, arg) =>
        {
            var names = (arg ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            viewModel.SelectChannels(names);
            return new { selected = viewModel.SelectedChannelNames };
        };
        server.Actions["range"] = (_, arg) =>
        {
            viewModel.SelectedTimeRange = TimeRangeOption.ByKey(arg);
            return new { range = viewModel.SelectedTimeRange.Key };
        };
        server.Actions["page"] = (_, arg) =>
        {
            viewModel.ShowAppLog = string.Equals(arg, "log", StringComparison.OrdinalIgnoreCase);
            viewModel.ShowAlerts = string.Equals(arg, "alerts", StringComparison.OrdinalIgnoreCase);
            return new { page = viewModel.ShowAppLog ? "log" : viewModel.ShowAlerts ? "alerts" : "events" };
        };
        return server;
    }

    /// <summary>Self-update from GitHub Releases: nightly builds follow the rolling "nightly" pre-release, releases follow stable.</summary>
    private void StartUpdater(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // EVENTBLITZ_UPDATE_MANIFEST=https://host/latest.json points the updater at a test manifest instead of GitHub.
        var manifestOverride = Environment.GetEnvironmentVariable("EVENTBLITZ_UPDATE_MANIFEST");
        IUpdateSource source = Uri.TryCreate(manifestOverride, UriKind.Absolute, out var manifestUri)
            ? new ManifestSource(manifestUri)
            : new GitHubReleasesSource("TomasBouda", AppInfo.Name);

        Updater.Start(new UpdateOptions(AppInfo.Name, source)
        {
            Channel = Enum.TryParse<UpdateChannel>(_settings.UpdateChannel, out var channel) ? channel : null,
            ChannelChanged = ch =>
            {
                _settings.UpdateChannel = ch.ToString();
                _settings.Save();
            },
            ExitApplication = () => desktop.Shutdown(),
            // Manifests must be signed by the CI key (UPDATE_SIGNING_KEY secret); this is the matching public key.
            PublicKeyPem = """
                -----BEGIN PUBLIC KEY-----
                MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAExv4j51ZsiQGKA1YV99myQyQkw4jd
                A6dln5ketjqnce0ML/VPNU+ONoEVoeGZrA/bpMD4gX7wS1mmqMriAbY92A==
                -----END PUBLIC KEY-----
                """,
            Log = message => AppLog.For("update").Information("{Message}", message),
        });
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove = BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();
        foreach (var plugin in dataValidationPluginsToRemove)
            BindingPlugins.DataValidators.Remove(plugin);
    }
}
