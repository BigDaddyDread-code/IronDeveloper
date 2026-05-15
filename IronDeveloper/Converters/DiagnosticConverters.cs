using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace IronDev.Agent.Converters;

public sealed class BoolToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && parameter is string p)
        {
            var parts = p.Split('|');
            if (parts.Length == 2)
            {
                return b ? parts[0] : parts[1];
            }
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && parameter is string p)
        {
            var parts = p.Split('|');
            if (parts.Length == 2)
            {
                var hex = b ? parts[0] : parts[1];
                try
                {
                    return (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
                }
                catch { }
            }
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ValueToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasValue = value != null;
        if (value is string s) hasValue = !string.IsNullOrWhiteSpace(s);

        bool inverse = parameter?.ToString() == "Inverse";
        bool visible = inverse ? !hasValue : hasValue;

        return visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
