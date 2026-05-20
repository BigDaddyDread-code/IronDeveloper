using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Agent.Services.Testing;
using IronDev.Core.Testing;
using IronDev.Data.Models;
using Microsoft.Win32;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class TestingCompanionViewModel : ObservableObject
{
    private readonly ITestingCompanionAgent _agent;
    private int _projectId;
    private string _projectName = "";
    private string? _projectPath;

    [ObservableProperty] private TestRun? _currentRun;
    [ObservableProperty] private TestMomentItemViewModel? _selectedMoment;
    [ObservableProperty] private string _statusText = "No active test session";
    [ObservableProperty] private string _targetName = "IronDev";
    [ObservableProperty] private TestTargetType _targetType = TestTargetType.IronDev;
    [ObservableProperty] private string _launchCommand = "";
    [ObservableProperty] private string _workingDirectory = "";
    [ObservableProperty] private string _logPath = "";
    [ObservableProperty] private string _reportPath = "";
    [ObservableProperty] private string _llmDebugPrompt = "";
    [ObservableProperty] private string _returnWorkspaceName = "";
    [ObservableProperty] private bool _autoReturnAfterStart = true;

    public ObservableCollection<TestMomentItemViewModel> Moments { get; } = [];
    public IReadOnlyList<TestTargetType> TargetTypes { get; } = Enum.GetValues<TestTargetType>();
    public Action? OnRequestReturnToWork { get; set; }

    public bool HasActiveRun => CurrentRun?.Status == TestRunStatus.Running;
    public bool HasMoments => Moments.Count > 0;
    public bool CanReturnToWork => !string.IsNullOrWhiteSpace(ReturnWorkspaceName);
    public string RunStatusText => CurrentRun == null
        ? "Idle"
        : $"{CurrentRun.Status} | {CurrentRun.Id.ToString("N")[..8]}";

    public TestingCompanionViewModel(ITestingCompanionAgent agent)
    {
        _agent = agent;
        Moments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMoments));
    }

    public async Task LoadAsync(Project project)
    {
        _projectId = project.Id;
        _projectName = project.Name;
        _projectPath = project.LocalPath;
        TargetName = string.IsNullOrWhiteSpace(project.Name) ? "IronDev" : project.Name;
        TargetType = TestTargetType.IronDev;
        WorkingDirectory = string.IsNullOrWhiteSpace(project.LocalPath) ? ResolveLikelyRepoRoot() : project.LocalPath;
        LogPath = ResolveDefaultIronDevLogPath();
        StatusText = "Ready to start a testing session.";
        await LoadPersistedMomentsAsync();
    }

    [RelayCommand]
    private void ReturnToWork()
    {
        OnRequestReturnToWork?.Invoke();
    }

    [RelayCommand]
    private async Task StartSessionAsync()
    {
        var target = new TestTarget
        {
            Name = string.IsNullOrWhiteSpace(TargetName)
                ? (string.IsNullOrWhiteSpace(_projectName) ? "IronDev" : _projectName)
                : TargetName.Trim(),
            TargetType = TargetType,
            LaunchCommand = string.IsNullOrWhiteSpace(LaunchCommand) ? null : LaunchCommand.Trim(),
            WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectory) ? null : WorkingDirectory.Trim(),
            LogPath = string.IsNullOrWhiteSpace(LogPath) ? null : LogPath.Trim()
        };

        CurrentRun = await _agent.StartSessionAsync(new StartTestRunRequest
        {
            ProjectId = _projectId,
            ProjectName = string.IsNullOrWhiteSpace(_projectName) ? "IronDev" : _projectName,
            ProjectPath = string.IsNullOrWhiteSpace(_projectPath) ? WorkingDirectory : _projectPath,
            Target = target
        });

        Moments.Clear();
        ReportPath = "";
        LlmDebugPrompt = "";
        StatusText = "Testing session running. Use Ctrl+Shift+M to mark a moment.";
        RefreshState();

        if (AutoReturnAfterStart && CanReturnToWork)
            OnRequestReturnToWork?.Invoke();
    }

    public async Task EnsureSessionStartedAsync()
    {
        if (HasActiveRun)
            return;

        await StartSessionAsync();
    }

    public void SetReturnWorkspace(string workspaceName)
    {
        ReturnWorkspaceName = string.IsNullOrWhiteSpace(workspaceName) || workspaceName == "Testing"
            ? ""
            : workspaceName;
        OnPropertyChanged(nameof(CanReturnToWork));
    }

    [RelayCommand]
    private void BrowseExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select application executable",
            Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            LaunchCommand = dialog.FileName;
            WorkingDirectory = Path.GetDirectoryName(dialog.FileName) ?? "";
            if (string.IsNullOrWhiteSpace(TargetName))
                TargetName = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    [RelayCommand]
    private void LaunchTarget()
    {
        if (string.IsNullOrWhiteSpace(LaunchCommand))
        {
            StatusText = "Choose an executable before launching a target app.";
            return;
        }

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = LaunchCommand,
            WorkingDirectory = Directory.Exists(WorkingDirectory) ? WorkingDirectory : Path.GetDirectoryName(LaunchCommand),
            UseShellExecute = true
        });

        if (CurrentRun != null && process?.Id > 0)
            _ = _agent.AttachToProcessAsync(CurrentRun.Id, process.Id);

        StatusText = process == null
            ? "Launch command was sent."
            : $"Launched target process #{process.Id}.";
    }

    [RelayCommand]
    public async Task MarkMomentAsync()
        => await MarkMomentForWorkspaceAsync("Testing");

    public async Task MarkMomentForWorkspaceAsync(string activeWorkspace)
    {
        if (!HasActiveRun || CurrentRun == null)
        {
            StatusText = "Start a testing session before marking a moment.";
            return;
        }

        var draft = await _agent.BeginMarkMomentAsync(CurrentRun.Id, activeWorkspace, CancellationToken.None);
        var vm = new MarkMomentViewModel(draft);
        var window = new global::IronDev.Agent.Views.Workspaces.MarkMomentWindow(vm)
        {
            Owner = App.Current.MainWindow
        };

        if (window.ShowDialog() != true)
        {
            StatusText = "Moment capture cancelled.";
            return;
        }

        var moment = await _agent.SaveMarkedMomentAsync(CurrentRun.Id, vm.ToSaveRequest());
        var item = new TestMomentItemViewModel(moment);
        Moments.Insert(0, item);
        SelectedMoment = item;
        LlmDebugPrompt = BuildCopyPrompt(moment);
        StatusText = $"Saved moment at {moment.MarkedAt:HH:mm:ss}.";
        RefreshState();
    }

    [RelayCommand]
    private async Task LoadPersistedMomentsAsync()
    {
        var loaded = await _agent.LoadPersistedMomentsAsync(
            string.IsNullOrWhiteSpace(_projectPath) ? WorkingDirectory : _projectPath);

        Moments.Clear();
        foreach (var moment in loaded)
            Moments.Add(new TestMomentItemViewModel(moment));

        StatusText = loaded.Count == 0
            ? "Ready to start a testing session."
            : $"Loaded {loaded.Count} persisted captured moment(s).";
        RefreshState();
    }

    [RelayCommand]
    private async Task CopyCombinedPromptAsync()
    {
        var moments = Moments
            .Select(item => item.Moment)
            .OrderByDescending(moment => moment.MarkedAt)
            .Take(12)
            .ToList();

        if (moments.Count == 0)
        {
            StatusText = "No captured moments are available to bundle yet.";
            return;
        }

        LlmDebugPrompt = await _agent.BuildCombinedPromptAsync(
            string.IsNullOrWhiteSpace(_projectPath) ? WorkingDirectory : _projectPath,
            moments);
        System.Windows.Clipboard.SetText(LlmDebugPrompt);
        StatusText = $"Copied combined prompt for {moments.Count} captured moment(s).";
    }

    public Task CopyLastPromptAsync()
    {
        var latest = Moments
            .Select(item => item.Moment)
            .OrderByDescending(moment => moment.MarkedAt)
            .FirstOrDefault();

        if (latest == null)
        {
            StatusText = "No captured moment is available to copy yet.";
            return Task.CompletedTask;
        }

        LlmDebugPrompt = BuildCopyPrompt(latest);
        System.Windows.Clipboard.SetText(LlmDebugPrompt);
        StatusText = "Copied latest captured moment prompt.";
        return Task.CompletedTask;
    }

    public async Task CopyBundlePromptAsync()
    {
        await CopyCombinedPromptAsync();
    }

    public async Task FinishSessionAsync()
    {
        await EndSessionAsync();
    }

    public void OpenLatestReport()
    {
        OpenReport();
    }

    [RelayCommand]
    private async Task EndSessionAsync()
    {
        if (CurrentRun == null)
        {
            StatusText = "No test session is active.";
            return;
        }

        var report = await _agent.EndSessionAndGenerateReportAsync(CurrentRun.Id);
        ReportPath = report.ReportPath ?? "";
        StatusText = $"Report generated: {ReportPath}";
        RefreshState();
    }

    [RelayCommand]
    private void OpenReport()
    {
        if (string.IsNullOrWhiteSpace(ReportPath) || !File.Exists(ReportPath))
        {
            StatusText = "Generate a report first.";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = ReportPath,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void CopyLlmPrompt()
    {
        if (string.IsNullOrWhiteSpace(LlmDebugPrompt))
        {
            StatusText = "Mark a moment first, then copy its LLM debug prompt.";
            return;
        }

        System.Windows.Clipboard.SetText(LlmDebugPrompt);
        StatusText = "Copied LLM debug prompt.";
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(HasActiveRun));
        OnPropertyChanged(nameof(HasMoments));
        OnPropertyChanged(nameof(RunStatusText));
        StartSessionCommand.NotifyCanExecuteChanged();
        EndSessionCommand.NotifyCanExecuteChanged();
        MarkMomentCommand.NotifyCanExecuteChanged();
    }

    private static string ResolveDefaultIronDevLogPath()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IronDev",
            "logs");

        if (!Directory.Exists(logDir))
            return "";

        return Directory
            .EnumerateFiles(logDir, "irondev-*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault() ?? "";
    }

    private static string ResolveLikelyRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return "";
    }

    private string BuildCopyPrompt(TestMoment moment)
    {
        return $"""
        I found this issue while testing with IronDev Testing Companion.

        Project: {_projectName}
        Target: {CurrentRun?.TargetName} ({CurrentRun?.TargetType})
        Branch: {CurrentRun?.GitBranch ?? "Unknown"}
        Commit: {CurrentRun?.GitCommit ?? "Unknown"}
        Moment type: {moment.MomentType}
        Time: {moment.MarkedAt:yyyy-MM-dd HH:mm:ss}

        Tester note:
        {moment.UserTextNote}

        Expected:
        {moment.ExpectedBehavior}

        Actual:
        {moment.ActualBehavior}

        Suspected area:
        {moment.SuspectedArea}

        Screenshot: {moment.ScreenshotPath}
        Annotated screenshot: {moment.AnnotatedScreenshotPath}

        Please identify likely causes, suggest files/components to inspect, and propose a fix plan.
        """;
    }
}

public sealed partial class MarkMomentViewModel : ObservableObject
{
    public BrokenMomentCaptureDraft Draft { get; }

    [ObservableProperty] private TestMomentType _momentType = TestMomentType.Bug;
    [ObservableProperty] private string _userTextNote = "";
    [ObservableProperty] private string _expectedBehavior = "";
    [ObservableProperty] private string _actualBehavior = "";
    [ObservableProperty] private string _severity = "Medium";
    [ObservableProperty] private string _suspectedArea = "";
    [ObservableProperty] private int _markedAreaX;
    [ObservableProperty] private int _markedAreaY;
    [ObservableProperty] private int _markedAreaWidth;
    [ObservableProperty] private int _markedAreaHeight;

    public MarkMomentViewModel(BrokenMomentCaptureDraft draft)
    {
        Draft = draft;
    }

    public string ScreenshotPath => Draft.ScreenshotPath;
    public string CapturedText => $"Captured {Draft.CapturedAt:HH:mm:ss}";
    public IReadOnlyList<TestMomentType> MomentTypes { get; } = Enum.GetValues<TestMomentType>();
    public IReadOnlyList<string> Severities { get; } = ["Low", "Medium", "High", "Blocker"];

    public SaveMarkedMomentRequest ToSaveRequest()
    {
        return new SaveMarkedMomentRequest
        {
            Draft = Draft,
            MomentType = MomentType,
            UserTextNote = UserTextNote,
            ExpectedBehavior = ExpectedBehavior,
            ActualBehavior = ActualBehavior,
            Severity = Severity,
            SuspectedArea = SuspectedArea,
            MarkedAreaX = MarkedAreaX,
            MarkedAreaY = MarkedAreaY,
            MarkedAreaWidth = MarkedAreaWidth,
            MarkedAreaHeight = MarkedAreaHeight
        };
    }
}

public sealed class TestMomentItemViewModel
{
    public TestMomentItemViewModel(TestMoment moment)
    {
        Moment = moment;
    }

    public TestMoment Moment { get; }
    public string TimeText => Moment.MarkedAt.ToString("HH:mm:ss");
    public string TypeText => Moment.MomentType.ToString();
    public string Title => FirstLine(Moment.ActualBehavior ?? Moment.UserTextNote ?? "Captured moment");
    public string Detail => Moment.ExpectedBehavior ?? Moment.SuspectedArea ?? "";
    public string ScreenshotPath => Moment.AnnotatedScreenshotPath ?? Moment.ScreenshotPath ?? "";

    private static string FirstLine(string value)
        => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "Captured moment";
}
