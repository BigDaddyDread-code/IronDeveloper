using System;
using System.Globalization;
using System.Windows.Data;

namespace IronDev.Agent.Converters;

/// <summary>
/// Converts chat role (user/assistant) to a display name.
/// </summary>
public sealed class RoleToDisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string role) return string.Empty;

        return role.ToLowerInvariant() switch
        {
            "user" => "YOU",
            "assistant" => "IRONDEV ARCHITECT",
            _ => role.ToUpperInvariant()
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>
/// Converts chat role to an initial (U/A).
/// </summary>
public sealed class RoleToInitialConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string role) return "?";

        return role.ToLowerInvariant() switch
        {
            "user" => "U",
            "assistant" => "A",
            _ => role.Substring(0, 1).ToUpperInvariant()
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>
/// Converts a full name to initials (e.g. "Bob Developer" -> "BD")
/// </summary>
public sealed class StringToInitialsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string name || string.IsNullOrWhiteSpace(name)) return "??";

        var parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();

        return (parts[0][0].ToString() + parts[parts.Length - 1][0].ToString()).ToUpperInvariant();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
