using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace EventBlitz.App.Controls;

/// <summary>
/// Highlights the structure of a plain-text event message: "Key: value" labels, numbers and hex codes, file
/// paths, GUIDs, URLs and IP addresses. Colours come from the theme tokens so both variants read well.
/// </summary>
public sealed partial class MessageColorizer : DocumentColorizingTransformer
{
    [GeneratedRegex(@"^\s*([A-Za-z][A-Za-z0-9 _\-/().]{1,60}):(?=\s|$)", RegexOptions.Compiled)]
    private static partial Regex LabelPattern();

    [GeneratedRegex(@"\b0x[0-9A-Fa-f]+\b|\b\d+(?:[.,]\d+)*\b", RegexOptions.Compiled)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\b[A-Za-z]:\\[^\s""'<>|]+|\\\\[^\s""'<>|]+|\b(?:https?|file)://[^\s""'<>]+", RegexOptions.Compiled)]
    private static partial Regex PathPattern();

    [GeneratedRegex(@"\{?[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}?|\bS-1-\d+(?:-\d+)+\b", RegexOptions.Compiled)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"\b(?:error|failed|failure|exception|denied|crash(?:ed|ing)?|fault(?:ing)?|timeout|unexpected(?:ly)?|cannot|could not|terminated)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ProblemPattern();

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.Length == 0) return;
        var text = CurrentContext.Document.GetText(line);
        var start = line.Offset;

        var label = LabelPattern().Match(text);
        if (label.Success)
            Apply(start + label.Groups[1].Index, label.Groups[1].Length, "TextMutedBrush", null);

        foreach (Match m in PathPattern().Matches(text))
            Apply(start + m.Index, m.Length, "AccentBrush", null);

        foreach (Match m in IdPattern().Matches(text))
            Apply(start + m.Index, m.Length, "TextSecondaryBrush", null);

        foreach (Match m in NumberPattern().Matches(text))
            Apply(start + m.Index, m.Length, "InfoBrush", null);

        foreach (Match m in ProblemPattern().Matches(text))
            Apply(start + m.Index, m.Length, "ErrorBrush", FontWeight.SemiBold);
    }

    private void Apply(int offset, int length, string brushKey, FontWeight? weight)
    {
        var brush = Application.Current?.TryGetResource(brushKey, Application.Current.ActualThemeVariant, out var value) == true ? value as IBrush : null;
        ChangeLinePart(offset, offset + length, element =>
        {
            if (brush is not null) element.TextRunProperties.SetForegroundBrush(brush);
            if (weight is { } w)
            {
                var typeface = element.TextRunProperties.Typeface;
                element.TextRunProperties.SetTypeface(new Typeface(typeface.FontFamily, typeface.Style, w));
            }
        });
    }
}
