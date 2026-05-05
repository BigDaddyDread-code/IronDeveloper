using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace IronDev.Agent.Converters;

/// <summary>
/// Strips common markdown syntax from a string and returns a plain-text
/// preview suitable for a single list row. Also trims leading whitespace,
/// blank lines, and list-markers that would otherwise dominate the preview.
/// </summary>
[ValueConversion(typeof(string), typeof(string))]
public class MarkdownPreviewConverter : IValueConverter
{
    /// <summary>Maximum characters in the returned preview. Default 120.</summary>
    public int MaxLength { get; set; } = 120;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string raw || string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        // 1. Collapse all line endings to spaces
        var text = raw.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

        // 2. Remove markdown heading markers:  ### **Heading**
        text = Regex.Replace(text, @"#{1,6}\s*\*{0,2}", string.Empty);

        // 3. Remove bold/italic markers
        text = Regex.Replace(text, @"\*{1,3}(.*?)\*{1,3}", "$1");
        text = Regex.Replace(text, @"_{1,3}(.*?)_{1,3}", "$1");

        // 4. Remove inline code ticks
        text = Regex.Replace(text, @"`{1,3}(.*?)`{1,3}", "$1");

        // 5. Remove block-level markers at start of fragment: - * 1. 2. PLAN DETAILS: etc.
        text = Regex.Replace(text, @"^\s*[-*•]\s+", string.Empty);
        text = Regex.Replace(text, @"^\s*\d+\.\s+", string.Empty);

        // 6. Collapse multiple spaces
        text = Regex.Replace(text, @"\s{2,}", " ").Trim();

        // 7. Truncate
        if (text.Length > MaxLength)
            text = text[..MaxLength].TrimEnd() + "…";

        return text;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
