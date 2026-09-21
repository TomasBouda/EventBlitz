using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using EventBlitz.Core.Models;
using Serilog.Events;

namespace EventBlitz.App.Converters;

/// <summary>Maps an event level (or an app log level) to the matching semantic brush from Tokens.axaml.</summary>
public sealed class LevelToBrushConverter : IValueConverter
{
    public static readonly LevelToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            EventLevel.Critical => "CriticalBrush",
            EventLevel.Error => "ErrorBrush",
            EventLevel.Warning => "WarnBrush",
            EventLevel.Verbose => "TextMutedBrush",
            EventLevel => "InfoBrush",
            LevelMask.Critical => "CriticalBrush",
            LevelMask.Error => "ErrorBrush",
            LevelMask.Warning => "WarnBrush",
            LevelMask.Information => "InfoBrush",
            LevelMask => "TextMutedBrush",
            LogEventLevel.Fatal or LogEventLevel.Error => "ErrorBrush",
            LogEventLevel.Warning => "WarnBrush",
            LogEventLevel.Information => "InfoBrush",
            LogEventLevel => "TextMutedBrush",
            _ => "TextMutedBrush",
        };

        return Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var brush) == true
            ? brush as IBrush
            : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>"12.3 MB" for a byte count; empty for null.</summary>
public sealed class BytesConverter : IValueConverter
{
    public static readonly BytesConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes) return string.Empty;
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>"1 234" style thousands separator with a thin space; empty for null.</summary>
public sealed class CountConverter : IValueConverter
{
    public static readonly CountConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        long l => l.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' '),
        int i => i.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' '),
        _ => string.Empty,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Short channel name for the list ("Kernel-Power / Thermal-Operational" for the full channel path).</summary>
public sealed class ChannelDisplayConverter : IValueConverter
{
    public static readonly ChannelDisplayConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name ? ChannelInfo.Describe(name).DisplayName : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
