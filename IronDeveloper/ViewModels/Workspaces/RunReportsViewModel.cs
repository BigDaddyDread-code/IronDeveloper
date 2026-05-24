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
    public ObservableCollection<RunPromotionFile> PromotableFiles { get; } = [];
    public ObservableCollection<RunPromotionFile> BlockedFiles { get; } = [];
    public ObservableCollection<RunPromotionRisk> PromotionRisks { get; } = [];
    public ObservableCollection<string> PromotionChecklist { get; } = [];
    public ObservableCollection<string> HardInvariants { get; } = [];
    public ObservableCollection<string> ConfigurablePolicy { get; } = [];
    public ObservableCollection<RunDoubtFinding> DoubtFindings { get; } = [];
    public ObservableCollection<RunMemoryProposal> MemoryProposals { get; } = [];

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
    public string PromotionPackageId => SelectedRunDetail?.PromotionReview?.PackageId ?? "";
    public string ProposedChangeId => SelectedRunDetail?.PromotionReview?.ProposedChangeId ?? "";
    public string ApprovalState => SelectedRunDetail?.PromotionReview?.ApprovalState ?? "";
    public string RuntimeProfile => SelectedRunDetail?.PromotionReview?.RuntimeProfileId ?? "";
    public string TargetLanguage => SelectedRunDetail?.PromotionReview?.TargetLanguage ?? "";
    public string TargetStack => SelectedRunDetail?.PromotionReview?.TargetStack ?? "";
    public int BlockedFileCount => SelectedRunDetail?.PromotionReview?.BlockedFileCount ?? 0;
    public bool HasPromotionReview => SelectedRunDetail?.PromotionReview is not null;
    public int DoubtFindingCount => SelectedRunDetail?.AdversarialReview?.FindingCount ?? 0;
    public int DoubtHighCriticalCount => SelectedRunDetail?.AdversarialReview?.HighCriticalCount ?? 0;
    public bool KilljoyAddressedDoubt => SelectedRunDetail?.AdversarialReview?.KilljoyAddressedHighCritical ?? false;
    public int MemoryProposalCount => SelectedRunDetail?.MemoryImprovement?.ProposalCount ?? 0;
    public string MemoryHealthScore => SelectedRunDetail?.MemoryImprovement?.MemoryHealthScore ?? "";
    public bool MemoryKeyReady => SelectedRunDetail?.MemoryImprovement?.ReadyForAcceptedMemoryKey ?? false;

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
        PromotableFiles.Clear();
        BlockedFiles.Clear();
        PromotionRisks.Clear();
        PromotionChecklist.Clear();
        HardInvariants.Clear();
        ConfigurablePolicy.Clear();
        DoubtFindings.Clear();
        MemoryProposals.Clear();
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
            if (detail.PromotionReview is not null)
            {
                foreach (var file in detail.PromotionReview.PromotableFiles.Take(40))
                    PromotableFiles.Add(file);
                foreach (var file in detail.PromotionReview.BlockedFiles.Take(40))
                    BlockedFiles.Add(file);
                foreach (var risk in detail.PromotionReview.Risks)
                    PromotionRisks.Add(risk);
                foreach (var item in detail.PromotionReview.RequiredChecks.Concat(detail.PromotionReview.ExplicitApprovalsNeeded))
                    PromotionChecklist.Add(item);
            }
            foreach (var invariant in detail.Policy.HardInvariants)
                HardInvariants.Add(invariant);
            foreach (var setting in detail.Policy.ConfigurableSettings)
                ConfigurablePolicy.Add(setting);
            if (detail.AdversarialReview is not null)
            {
                foreach (var finding in detail.AdversarialReview.Findings.Take(10))
                    DoubtFindings.Add(finding);
            }
            if (detail.MemoryImprovement is not null)
            {
                foreach (var proposal in detail.MemoryImprovement.Proposals.Take(10))
                    MemoryProposals.Add(proposal);
            }

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
        OnPropertyChanged(nameof(PromotionPackageId));
        OnPropertyChanged(nameof(ProposedChangeId));
        OnPropertyChanged(nameof(ApprovalState));
        OnPropertyChanged(nameof(RuntimeProfile));
        OnPropertyChanged(nameof(TargetLanguage));
        OnPropertyChanged(nameof(TargetStack));
        OnPropertyChanged(nameof(BlockedFileCount));
        OnPropertyChanged(nameof(HasPromotionReview));
        OnPropertyChanged(nameof(DoubtFindingCount));
        OnPropertyChanged(nameof(DoubtHighCriticalCount));
        OnPropertyChanged(nameof(KilljoyAddressedDoubt));
        OnPropertyChanged(nameof(MemoryProposalCount));
        OnPropertyChanged(nameof(MemoryHealthScore));
        OnPropertyChanged(nameof(MemoryKeyReady));
    }
}
