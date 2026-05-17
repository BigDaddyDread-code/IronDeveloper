using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using IronDev.Agent.ViewModels.Shell;
using Serilog;

namespace IronDev.Agent.Views;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => TraceShellState("Loaded");
        DataContextChanged += (_, e) =>
        {
            Trace.WriteLine($"[MainWindow] DataContext changed: {Describe(e.OldValue)} -> {Describe(e.NewValue)}");
            TraceShellState("DataContextChanged");
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            this.WindowState = this.WindowState == WindowState.Maximized 
                ? WindowState.Normal 
                : WindowState.Maximized;
        }
        else
        {
            this.DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        this.WindowState = this.WindowState == WindowState.Maximized 
            ? WindowState.Normal 
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void TraceShellState(string reason)
    {
        if (DataContext is not ShellViewModel shell)
        {
            Trace.WriteLine($"[MainWindow] {reason}: DataContext={Describe(DataContext)}");
            return;
        }

        var message =
            $"[MainWindow] {reason}: " +
            $"Project='{shell.ActiveProjectName}', " +
            $"Path='{shell.ActiveProjectPath}', " +
            $"Model='{shell.ActiveModel}', " +
            $"Status='{shell.ActiveStatus}', " +
            $"Workspace='{shell.CurrentWorkspace}', " +
            $"CurrentView={Describe(shell.CurrentView)}, " +
            $"ShowHeader={shell.ShowHeader}, " +
            $"HasActiveProject={shell.HasActiveProject}";

        Trace.WriteLine(message);
        Log.Information(message);
    }

    private static string Describe(object? value)
    {
        if (value is null)
            return "<null>";

        if (value is string text)
            return $"\"{text}\"";

        return value.GetType().FullName ?? value.GetType().Name;
    }
}
