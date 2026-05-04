using System;
using System.Globalization;
using System.Windows.Data;

namespace IronDev.Agent.Converters;

/// <summary>
/// A generic converter that compares an enum value to a parameter string.
/// Returns true if they match.
/// Supports TwoWay binding: if value is true, it returns the enum value parsed from parameter.
/// </summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;

        string? checkValue = value.ToString();
        string? targetValue = parameter.ToString();

        if (checkValue == null || targetValue == null) return false;

        return checkValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is string paramStr)
        {
            try
            {
                return Enum.Parse(targetType, paramStr);
            }
            catch
            {
                return Binding.DoNothing;
            }
        }

        return Binding.DoNothing;
    }
}
