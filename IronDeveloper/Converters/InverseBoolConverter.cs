using System;
using System.Globalization;
using System.Windows.Data;

namespace IronDev.Agent.Converters;

/// <summary>
/// Inverts a bool. Used to disable buttons while IsBusy=true.
/// </summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
