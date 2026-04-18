using System;
using System.Globalization;
using System.Windows.Data;
using IronDev.Agent.Models;

namespace IronDev.Agent.Converters;

/// <summary>
/// Returns <c>"Active"</c> when the bound <see cref="ProjectWorkspace"/> value matches
/// the ConverterParameter string, otherwise returns <c>""</c>.
/// 
/// Usage in XAML (on a sidebar IronSidebarButton):
///   Tag="{Binding ShellVm.CurrentWorkspace,
///         Converter={StaticResource WorkspaceActiveConverter},
///         ConverterParameter='Chat'}"
///
/// The IronSidebarButton template shows the orange accent bar when Tag="Active".
/// </summary>
[ValueConversion(typeof(ProjectWorkspace), typeof(string))]
public sealed class WorkspaceActiveConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ProjectWorkspace workspace && parameter is string paramStr)
            return Enum.TryParse<ProjectWorkspace>(paramStr, out var target) && workspace == target
                ? "Active"
                : string.Empty;

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
