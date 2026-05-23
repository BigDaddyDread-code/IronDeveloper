using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Core.RunReports;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class RunReportsViewModel : ObservableObject
{
    private readonly IRunReportService _runReportService;
    private readonly IRunEvidenceService _runEvidenceService;

    public ObservableCollection<RunReportSummary> Runs { get; } = [];
    public ObservableCollection<RunStageStatus> Stages { get; } = [];
    public ObservableCollection<RunAttemptSummary> Attempts { get; } = [];
    public ObservableCollection<RunRepairSummary> Repairs { get; } = [];
    public ObservableCollection<RunEvidenceItem> Evidence { get; } = [];

    [ObservableProperty]
    private RunReportSummary? _selectedRun;

    [ObservableProperty]
    private RunReportDetail? _selectedRunDetail;

    [ObservableProperty]
    private RunEvidenceItem? _selectedEvidence;

    [ObservableProperty]
    private string _selectedEvidenceText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Run reports are read-only.";

    public string RunTitle => SelectedRunDetail?.Title ?? "No run selected";
    public string Project => SelectedRunDetail?.Project ?? "";
    public string Status => SelectedRunDetail?.Status ?? "";
    public string Recommendation => SelectedRunDetail?.Recommendation ?? "";
    public string Summary => SelectedRunDetail?.Summary ?? "";
    public string Boundary => SelectedRunDetail?.Boundary ?? "Read-only run report viewer. No CLI process is started.";
    public int RealRepoMutationCount => SelectedRunDetail?.RealRepoMutationCount ?? 0;
    public int DisposableFilesChanged => SelectedRunDetail?.DisposableFilesChanged ?? 0;
    public string WorkspacePath => SelectedRunDetail?.WorkspacePath ?? "";

    public RunReportsViewModel(IRunReportService runReportService, IRunEvidenceService runEvidenceService)
    {
        _runReportService = runReportService;
        _runEvidenceService = runEvidenceService;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            Runs.Clear();
            var runs = await _runReportService.GetRecentRunsAsync();
            foreach (var run in runs)
                Runs.Add(run);

            StatusMessage = runs.Count == 0
                ? "No run reports found."
                : $"Loaded {runs.Count} run report summaries.";

            if (SelectedRun is null && Runs.Count > 0)
                SelectedRun = Runs[0];
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load run reports: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var selectedRunId = SelectedRun?.RunId;
        await LoadAsync();
        if (!string.IsNullOrWhiteSpace(selectedRunId))
            SelectedRun = Runs.FirstOrDefault(run => string.Equals(run.RunId, selectedRunId, StringComparison.OrdinalIgnoreCase)) ?? Runs.FirstOrDefault();
    }

    partial void OnSelectedRunChanged(RunReportSummary? value)
    {
        _ = LoadSelectedRunAsync(value?.RunId);
    }

    partial void OnSelectedEvidenceChanged(RunEvidenceItem? value)
    {
        _ = LoadEvidenceTextAsync(value);
    }

    private async Task LoadSelectedRunAsync(string? runId)
    {
        Stages.Clear();
        Attempts.Clear();
        Repairs.Clear();
        Evidence.Clear();
        SelectedEvidenceText = string.Empty;
        SelectedRunDetail = null;

        if (string.IsNullOrWhiteSpace(runId))
            return;

        IsLoading = true;
        try
        {
            var detail = await _runReportService.GetRunAsync(runId);
            SelectedRunDetail = detail;

            if (detail is null)
            {
                StatusMessage = $"Run {runId} was not found.";
                return;
            }

            foreach (var stage in detail.Stages)
                Stages.Add(stage);
            foreach (var attempt in detail.Attempts)
                Attempts.Add(attempt);
            foreach (var repair in detail.Repairs)
                Repairs.Add(repair);
            foreach (var evidence in detail.Evidence)
                Evidence.Add(evidence);

            StatusMessage = detail.Warnings.Count > 0
                ? string.Join(" ", detail.Warnings)
                : $"Loaded run {detail.RunId}.";

            SelectedEvidence = Evidence.FirstOrDefault();
            RaiseSelectedRunProperties();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load run {runId}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadEvidenceTextAsync(RunEvidenceItem? item)
    {
        SelectedEvidenceText = string.Empty;
        if (SelectedRunDetail is null || item is null)
            return;

        try
        {
            SelectedEvidenceText = await _runEvidenceService.ReadEvidenceTextAsync(SelectedRunDetail.RunId, item.Path) ?? "";
        }
        catch (Exception ex)
        {
            SelectedEvidenceText = $"Unable to read evidence: {ex.Message}";
        }
    }

    private void RaiseSelectedRunProperties()
    {
        OnPropertyChanged(nameof(RunTitle));
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Recommendation));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Boundary));
        OnPropertyChanged(nameof(RealRepoMutationCount));
        OnPropertyChanged(nameof(DisposableFilesChanged));
        OnPropertyChanged(nameof(WorkspacePath));
    }
}
