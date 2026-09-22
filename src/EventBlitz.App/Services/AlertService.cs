            using var asset = Avalonia.Platform.AssetLoader.Open(new Uri($"avares://EventBlitz/Assets/Sounds/{name}.wav"));
            using var buffer = new MemoryStream();
            asset.CopyTo(buffer);
            var bytes = buffer.ToArray();
            // Re-extract when the shipped file changed (a new build with another sound); the files are tiny, so compare bytes.
            if (!File.Exists(target) || !File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes))
                File.WriteAllBytes(target, bytes);
            return target;using System.Collections.ObjectModel;
using Avalonia.Threading;
using EventBlitz.Core.Models;
using EventBlitz.Core.Services;
using Serilog;

namespace EventBlitz.App.Services;

/// <summary>One alert that fired: the rule and the event, newest first in <see cref="AlertService.History"/>.</summary>
public sealed record AlertHit(AlertRule Rule, EventItem Event, DateTime Time);

/// <summary>
/// Runs a watcher per enabled rule, independent of what the window shows, and turns matches into sound, taskbar
/// flash and toasts. Everything observable is touched on the UI thread; the watcher callbacks arrive on the pool.
/// </summary>
public sealed class AlertService : IDisposable
{
    private const int HistorySize = 200;

    private static readonly ILogger Log = AppLog.For("alerts");

    private readonly IEventLogSource _source;
    private readonly Dictionary<Guid, IDisposable> _watchers = new();
    private readonly Dictionary<Guid, DateTime> _lastSound = new();

    public AlertService(IEventLogSource source)
    {
        _source = source;
    }

    /// <summary>Fired hits, newest first.</summary>
    public ObservableCollection<AlertHit> History { get; } = new();

    /// <summary>Raised on the UI thread for every hit; the window shows the toast and flashes.</summary>
    public event Action<AlertHit>? Triggered;

    /// <summary>When set, hits still show up but stay silent.</summary>
    public bool IsMuted { get; set; }

    /// <summary>The same rule fires its sound at most this often, so an event storm does not become a siren.</summary>
    public TimeSpan SoundCooldown { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>(Re)subscribes so the watchers match the rules exactly; called after any edit.</summary>
    public void Apply(IEnumerable<AlertRule> rules)
    {
        var wanted = rules.Where(r => r.IsEnabled && r.Channels.Count > 0).ToList();

        foreach (var id in _watchers.Keys.ToList())
        {
            if (wanted.All(r => r.Id != id))
            {
                _watchers[id].Dispose();
                _watchers.Remove(id);
            }
        }

        foreach (var rule in wanted)
        {
            // A rule that already runs is restarted so an edited filter takes effect.
            if (_watchers.Remove(rule.Id, out var old))
                old.Dispose();

            var snapshot = rule.Clone();
            try
            {
                _watchers[rule.Id] = _source.Watch(snapshot.Channels, snapshot.ToFilter(),
                    item => Dispatcher.UIThread.Post(() => Raise(snapshot, item)),
                    (channel, ex) => Log.Warning(ex, "Alert {Rule}: watching {Channel} failed", snapshot.Name, channel));
                Log.Information("Alert {Rule} armed on {Channels}", snapshot.Name, string.Join(",", snapshot.Channels));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Alert {Rule} could not be armed", snapshot.Name);
            }
        }
    }

    /// <summary>Fires the rule by hand so the sound and toast can be checked without waiting for a real event.</summary>
    public void Test(AlertRule rule)
    {
        var sample = new EventItem
        {
            Channel = rule.Channels.FirstOrDefault() ?? "Application",
            RecordId = 0,
            TimeCreated = DateTime.Now,
            Level = rule.Levels.HasFlag(LevelMask.Critical) ? EventLevel.Critical : rule.Levels.HasFlag(LevelMask.Error) ? EventLevel.Error : EventLevel.Information,
            EventId = 0,
            Provider = "EventBlitz",
            Message = $"Test of alert \"{rule.Name}\": this is what a hit looks like.",
        };
        Raise(rule, sample, ignoreCooldown: true);
    }

    private void Raise(AlertRule rule, EventItem item, bool ignoreCooldown = false)
    {
        var hit = new AlertHit(rule, item, DateTime.Now);
        History.Insert(0, hit);
        while (History.Count > HistorySize)
            History.RemoveAt(History.Count - 1);
        Log.Information("Alert {Rule}: {Channel} {Provider} {EventId} ({Level})", rule.Name, item.Channel, item.Provider, item.EventId, item.LevelName);

        if (!IsMuted && rule.Sound != AlertSound.None)
        {
            var now = DateTime.UtcNow;
            if (ignoreCooldown || !_lastSound.TryGetValue(rule.Id, out var last) || now - last >= SoundCooldown)
            {
                _lastSound[rule.Id] = now;
                Play(rule.Sound, rule.SoundPath);
            }
        }

        Triggered?.Invoke(hit);
    }

    /// <summary>
    /// The built-in sounds are EventBlitz's own (synthesised by tools/Make-Sounds.ps1, shipped as assets); they are
    /// copied to the local app data folder and played through winmm asynchronously. A custom WAV goes the same way.
    /// </summary>
    public static void Play(AlertSound sound, string? path)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var file = sound switch
            {
                AlertSound.Notify => BuiltIn("notify"),
                AlertSound.Warning => BuiltIn("warning"),
                AlertSound.Critical => BuiltIn("critical"),
                AlertSound.Custom when !string.IsNullOrWhiteSpace(path) && File.Exists(path) => path,
                AlertSound.Custom => BuiltIn("notify"),
                _ => null,
            };
            if (file is null) return;
            if (!PlaySound(file, IntPtr.Zero, SndFilename | SndAsync | SndNoDefault))
                MessageBeep(MbIconAsterisk);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Playing the alert sound failed");
        }
    }

    /// <summary>Extracts an asset WAV to %LOCALAPPDATA%\EventBlitz\sounds (once per build) and returns its path.</summary>
    private static string? BuiltIn(string name)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name, "sounds");
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, $"{name}.wav");
            using var asset = Avalonia.Platform.AssetLoader.Open(new Uri($"avares://EventBlitz/Assets/Sounds/{name}.wav"));
            // Re-extract when the shipped file changed (a new build with another sound), otherwise reuse the copy.
            if (!File.Exists(target) || new FileInfo(target).Length != asset.Length)
            {
                using var output = File.Create(target);
                asset.CopyTo(output);
            }
            return target;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Built-in sound {Name} is not available", name);
            return null;
        }
    }

    private const uint MbIconAsterisk = 0x40;
    private const uint SndAsync = 0x1;
    private const uint SndNoDefault = 0x2;
    private const uint SndFilename = 0x20000;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool MessageBeep(uint type);

    [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool PlaySound(string? sound, IntPtr module, uint flags);

    public void Dispose()
    {
        foreach (var watcher in _watchers.Values)
            watcher.Dispose();
        _watchers.Clear();
    }
}
