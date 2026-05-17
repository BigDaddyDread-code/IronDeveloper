using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace IronDev.Agent.Converters;

public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(status))
            return Application.Current.TryFindResource("StatusWarningBrush") as SolidColorBrush ?? Brushes.Orange;

        if (status.Equals("Ready", StringComparison.OrdinalIgnoreCase))
            return Application.Current.TryFindResource("StatusReadyBrush") as SolidColorBrush ?? Brushes.SpringGreen;

        if (status.Equals("Needs Index", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Stale Index", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("Needs Index", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Indexing...", StringComparison.OrdinalIgnoreCase))
            return Application.Current.TryFindResource("StatusWarningBrush") as SolidColorBrush ?? Brushes.Orange;

        if (status.StartsWith("Err:", StringComparison.OrdinalIgnoreCase) || 
            status.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("failed", StringComparison.OrdinalIgnoreCase))
            return Application.Current.TryFindResource("StatusErrorBrush") as SolidColorBrush ?? Brushes.Crimson;

        if (status.Equals("Checking...", StringComparison.OrdinalIgnoreCase) || 
            status.Equals("Offline", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return Application.Current.TryFindResource("MutedBrush") as SolidColorBrush ?? Brushes.Gray;

        // Default
        return Application.Current.TryFindResource("StatusReadyBrush") as SolidColorBrush ?? Brushes.SpringGreen;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
