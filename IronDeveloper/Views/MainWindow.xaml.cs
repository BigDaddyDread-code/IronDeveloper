using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using IronDev.Agent.ViewModels.Shell;
using IronDev.Agent.Views.Workspaces;
using Serilog;

namespace IronDev.Agent.Views;

public partial class MainWindow : Window
{
    private const int TestingHotkeyId = 0x4944;
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkM = 0x4D;

    private TestingSidekickWindow? _testingSidekickWindow;
    private HwndSource? _hwndSource;

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Loaded += (_, _) =>
        {
            RegisterTestingHotkey();
            TraceShellState("Loaded");
        };
        Closed += (_, _) => UnregisterTestingHotkey();
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

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.M ||
            Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift) ||
            DataContext is not ShellViewModel shell)
        {
            return;
        }

        e.Handled = true;
        await HandleTestingHotkeyAsync();
    }

    private async Task HandleTestingHotkeyAsync()
    {
        if (DataContext is not ShellViewModel shell)
            return;

        if (!shell.CanUseTestingCompanion)
        {
            Log.Information("Testing hotkey ignored: companion unavailable. ShellMode={ShellMode}, HasActiveProject={HasActiveProject}",
                shell.CurrentShellMode,
                shell.HasActiveProject);
            return;
        }

        Log.Information("Testing hotkey received. ShellMode={ShellMode}, Workspace={Workspace}, HasActiveProject={HasActiveProject}, HasActiveRun={HasActiveRun}",
            shell.CurrentShellMode,
            shell.CurrentWorkspace,
            shell.HasActiveProject,
            shell.TestingCompanion.HasActiveRun);

        if (!shell.TestingCompanion.HasActiveRun)
        {
            await shell.EnsureTestingSessionStartedAsync();
            ShowTestingSidekick(shell);
            return;
        }

        await shell.MarkTestingMomentAsync();
        ShowTestingSidekick(shell);
    }

    private void RegisterTestingHotkey()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);

        if (!RegisterHotKey(handle, TestingHotkeyId, ModControl | ModShift, VkM))
        {
            Log.Warning("Failed to register Ctrl+Shift+M testing hotkey. Error={Error}", Marshal.GetLastWin32Error());
        }
        else
        {
            Log.Information("Registered Ctrl+Shift+M testing hotkey. Handle={Handle}", handle);
        }
    }

    private void UnregisterTestingHotkey()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            UnregisterHotKey(handle, TestingHotkeyId);

        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == TestingHotkeyId)
        {
            handled = true;
            Log.Information("WM_HOTKEY received for testing companion.");
            _ = HandleTestingHotkeyAsync();
        }

        return IntPtr.Zero;
    }

    private void ShowTestingSidekick(ShellViewModel shell)
    {
        if (_testingSidekickWindow == null)
        {
            _testingSidekickWindow = new TestingSidekickWindow(
                shell.TestingCompanion,
                shell.MarkTestingMomentAsync)
            {
                Owner = this
            };
            _testingSidekickWindow.Closing += (_, e) =>
            {
                e.Cancel = true;
                _testingSidekickWindow.Hide();
            };
        }

        _testingSidekickWindow.PositionNear(this);
        _testingSidekickWindow.Show();
        _testingSidekickWindow.Activate();
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
