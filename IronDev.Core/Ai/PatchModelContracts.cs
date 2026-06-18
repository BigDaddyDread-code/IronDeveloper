using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IronDev.Core.Ai;

public enum PatchModelRequestKind
{
    Unknown = 0,
    PatchSuggestion,
    TestFailureAnalysis,
    PatchRefinement,
    PatchReview
}

public enum PatchEditOperation
{
    Unknown = 0,
    ReplaceText,
    AppendText,
    CreateFile,
    DeleteFile,
    RenameFile,
    BinaryEdit,
    Chmod
}

public enum AiPatchReviewVerdict
{
    Unknown = 0,
    LooksReviewable,
    NeedsHumanReview,
    TestsFailing,
    BoundaryConcern,
    NotEnoughEvidence
}

public sealed record PatchAiBoundary
{
    public bool SourceRepoMutated { get; init; }
    public bool SourceApplied { get; init; }
    public bool GitCommitCreated { get; init; }
    public bool GitPushPerformed { get; init; }
    public bool PullRequestCreated { get; init; }
    public bool ApprovalGranted { get; init; }
    public bool PolicySatisfied { get; init; }
    public bool ReleaseApproved { get; init; }
    public bool DeploymentApproved { get; init; }
    public bool MergeApproved { get; init; }
    public bool WorkflowContinued { get; init; }
    public bool MemoryPromoted { get; init; }
    public bool AcceptedMemoryMutated { get; init; }
    public bool AgentDispatched { get; init; }
    public bool ModelToToolDirectExecution { get; init; }
    public bool HiddenChainOfThoughtStored { get; init; }
    public bool RequiresHumanReview { get; init; } = true;

    public static PatchAiBoundary None { get; } = new();
}

public sealed record PatchFileSnapshot
{
    public required string RelativePath { get; init; }
    public required string ContentPreview { get; init; }
    public required int OriginalByteCount { get; init; }
    public required bool Truncated { get; init; }
    public required string Sha256 { get; init; }
}

public sealed record PatchTaskContextBundle
{
    public required string ContextBundleId { get; init; }
    public required string RunId { get; init; }
    public required string TaskPath { get; init; }
    public required string TaskText { get; init; }
    public required string SourceRepoPath { get; init; }
    public required string WorkspacePath { get; init; }
    public required string BaseBranch { get; init; }
    public required string BaseCommit { get; init; }
    public string[] ChangedFilesBeforeAssist { get; init; } = [];
    public string[] RelevantFiles { get; init; } = [];
    public PatchFileSnapshot[] FileSnapshots { get; init; } = [];
    public string? TestProfileName { get; init; }
    public required string TestCommand { get; init; }
    public string? PriorTestSummaryPath { get; init; }
    public string? KnownRisksPath { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public required bool FileSnapshotLimitHit { get; init; }
    public required bool ByteLimitHit { get; init; }
    public PatchAiBoundary Boundary { get; init; } = PatchAiBoundary.None;
}

public sealed record PatchTaskContextBuildOptions
{
    public int MaxFiles { get; init; } = 8;
    public int MaxBytesPerFile { get; init; } = 4096;
    public int MaxTotalBytes { get; init; } = 16384;
}

public static class PatchTaskContextBundleBuilder
{
    public static PatchTaskContextBundle Build(
        string runId,
        string taskPath,
        string sourceRepoPath,
        string workspacePath,
        string baseBranch,
        string baseCommit,
        string testCommand,
        string? testProfileName = null,
        string? priorTestSummaryPath = null,
        string? knownRisksPath = null,
        IEnumerable<string>? evidenceRefs = null,
        PatchTaskContextBuildOptions? options = null)
    {
        options ??= new PatchTaskContextBuildOptions();
        var taskText = File.Exists(taskPath) ? File.ReadAllText(taskPath) : string.Empty;
        var snapshots = new List<PatchFileSnapshot>();
        var totalBytes = 0;
        var byteLimitHit = false;
        var snapshotLimitHit = false;

        if (Directory.Exists(workspacePath))
        {
            foreach (var file in Directory.EnumerateFiles(workspacePath, "*", SearchOption.AllDirectories)
                         .Where(path => IsSnapshotCandidate(path, workspacePath))
                         .OrderBy(path => RelativePath(path, workspacePath), StringComparer.OrdinalIgnoreCase))
            {
                if (snapshots.Count >= options.MaxFiles)
                {
                    snapshotLimitHit = true;
                    break;
                }

                var bytes = File.ReadAllBytes(file);
                if (totalBytes >= options.MaxTotalBytes)
                {
                    byteLimitHit = true;
                    break;
                }

                var allowed = Math.Min(bytes.Length, options.MaxBytesPerFile);
                allowed = Math.Min(allowed, Math.Max(0, options.MaxTotalBytes - totalBytes));
                var preview = Encoding.UTF8.GetString(bytes, 0, allowed);
                var truncated = allowed < bytes.Length;
                byteLimitHit |= truncated || totalBytes + bytes.Length > options.MaxTotalBytes;
                totalBytes += allowed;

                snapshots.Add(new PatchFileSnapshot
                {
                    RelativePath = RelativePath(file, workspacePath),
                    ContentPreview = preview,
                    OriginalByteCount = bytes.Length,
                    Truncated = truncated,
                    Sha256 = Sha256Hex(bytes)
                });
            }
        }

        return new PatchTaskContextBundle
        {
            ContextBundleId = $"ctx_{Guid.NewGuid():N}",
            RunId = Trim(runId),
            TaskPath = Trim(taskPath),
            TaskText = taskText,
            SourceRepoPath = Trim(sourceRepoPath),
            WorkspacePath = Trim(workspacePath),
            BaseBranch = Trim(baseBranch),
            BaseCommit = Trim(baseCommit),
            ChangedFilesBeforeAssist = [],
            RelevantFiles = snapshots.Select(item => item.RelativePath).ToArray(),
            FileSnapshots = snapshots.ToArray(),
            TestProfileName = string.IsNullOrWhiteSpace(testProfileName) ? null : testProfileName.Trim(),
            TestCommand = Trim(testCommand),
            PriorTestSummaryPath = string.IsNullOrWhiteSpace(priorTestSummaryPath) ? null : priorTestSummaryPath.Trim(),
            KnownRisksPath = string.IsNullOrWhiteSpace(knownRisksPath) ? null : knownRisksPath.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            EvidenceRefs = (evidenceRefs ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            FileSnapshotLimitHit = snapshotLimitHit,
            ByteLimitHit = byteLimitHit
        };
    }

    private static bool IsSnapshotCandidate(string path, string workspacePath)
    {
        var relative = RelativePath(path, workspacePath).Replace('\\', '/');
        if (relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase))
            return false;
        if (relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase))
            return false;
        if (relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static string RelativePath(string path, string root) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

public sealed record ModelRequestEnvelope
{
    public required string ModelRequestId { get; init; }
    public required string RunId { get; init; }
    public required PatchModelRequestKind RequestKind { get; init; }
    public required string ProviderName { get; init; }
    public required string ModelName { get; init; }
    public required string PromptVersion { get; init; }
    public required string SystemInstruction { get; init; }
    public required string UserInstruction { get; init; }
    public string[] ContextRefs { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public PatchAiBoundary Boundary { get; init; } = PatchAiBoundary.None;
    public bool HiddenChainOfThoughtStored { get; init; }
}

public sealed record ModelResponseEnvelope
{
    public required string ModelResponseId { get; init; }
    public required string ModelRequestId { get; init; }
    public required string RunId { get; init; }
    public required string ProviderName { get; init; }
    public required string ModelName { get; init; }
    public required PatchModelRequestKind ResponseKind { get; init; }
    public string? ResponseText { get; init; }
    public string? StructuredPayloadJson { get; init; }
    public string? ResponseTextPath { get; init; }
    public string? StructuredPayloadPath { get; init; }
    public required string FinishReason { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public PatchAiBoundary Boundary { get; init; } = PatchAiBoundary.None;
    public bool HiddenChainOfThoughtStored { get; init; }
}

public sealed record PatchSuggestion
{
    public required string PatchSuggestionId { get; init; }
    public required string RunId { get; init; }
    public required string ModelResponseId { get; init; }
    public required string Summary { get; init; }
    public string[] Assumptions { get; init; } = [];
    public string[] ProposedFiles { get; init; } = [];
    public required string EditPlanPath { get; init; }
    public required string Confidence { get; init; }
    public bool RequiresHumanReview { get; init; } = true;
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public PatchAiBoundary Boundary { get; init; } = PatchAiBoundary.None;
}

public sealed record PatchEdit
{
    public required string Path { get; init; }
    public required PatchEditOperation Operation { get; init; }
    public string? FindText { get; init; }
    public string? ReplaceText { get; init; }
    public string? NewContent { get; init; }
    public required string Rationale { get; init; }
    public required string Risk { get; init; }
}

public sealed record PatchEditPlan
{
    public required string PatchEditPlanId { get; init; }
    public required string RunId { get; init; }
    public required string ModelResponseId { get; init; }
    public PatchEdit[] Edits { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public bool AppliesOnlyToWorkspace { get; init; } = true;
    public PatchAiBoundary Boundary { get; init; } = PatchAiBoundary.None;
}

public sealed record WorkspacePatchEditResult
{
    public required string Path { get; init; }
    public required PatchEditOperation Operation { get; init; }
    public required bool Applied { get; init; }
    public required string Status { get; init; }
    public string[] Reasons { get; init; } = [];
}

public sealed record WorkspacePatchEditApplicationResult
{
    public WorkspacePatchEditResult[] Results { get; init; } = [];
    public bool AnyApplied => Results.Any(item => item.Applied);
    public bool AnyBlocked => Results.Any(item => !item.Applied);
}

public static class WorkspacePatchEditor
{
    public static WorkspacePatchEditApplicationResult Apply(string workspacePath, string sourceRepoPath, PatchEditPlan plan)
    {
        var results = new List<WorkspacePatchEditResult>();
        foreach (var edit in plan.Edits)
            results.Add(ApplyOne(workspacePath, sourceRepoPath, edit));

        return new WorkspacePatchEditApplicationResult { Results = results.ToArray() };
    }

    private static WorkspacePatchEditResult ApplyOne(string workspacePath, string sourceRepoPath, PatchEdit edit)
    {
        var reasons = ValidateTarget(workspacePath, sourceRepoPath, edit.Path).ToList();
        if (!IsAllowedOperation(edit.Operation))
            reasons.Add("UnsupportedEditOperation");
        if (PatchAiTextSafety.ContainsUnsafeText(edit.NewContent) || PatchAiTextSafety.ContainsUnsafeText(edit.ReplaceText) || PatchAiTextSafety.ContainsUnsafeText(edit.Rationale))
            reasons.Add("UnsafeEditText");

        var targetPath = ResolveTargetPath(workspacePath, edit.Path);
        if (reasons.Count > 0)
            return Blocked(edit, reasons.ToArray());

        try
        {
            switch (edit.Operation)
            {
                case PatchEditOperation.ReplaceText:
                    if (!File.Exists(targetPath))
                        return Blocked(edit, ["TargetFileMissing"]);
                    if (string.IsNullOrEmpty(edit.FindText))
                        return Blocked(edit, ["MissingFindText"]);
                    var existing = File.ReadAllText(targetPath);
                    if (!existing.Contains(edit.FindText, StringComparison.Ordinal))
                        return Blocked(edit, ["FindTextNotFound"]);
                    File.WriteAllText(targetPath, existing.Replace(edit.FindText, edit.ReplaceText ?? string.Empty, StringComparison.Ordinal));
                    break;
                case PatchEditOperation.AppendText:
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? workspacePath);
                    File.AppendAllText(targetPath, edit.NewContent ?? string.Empty);
                    break;
                case PatchEditOperation.CreateFile:
                    if (File.Exists(targetPath))
                        return Blocked(edit, ["TargetFileAlreadyExists"]);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? workspacePath);
                    File.WriteAllText(targetPath, edit.NewContent ?? string.Empty);
                    break;
            }
        }
        catch (IOException ex)
        {
            return Blocked(edit, ["IoError", ex.GetType().Name]);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Blocked(edit, ["AccessDenied", ex.GetType().Name]);
        }

        return new WorkspacePatchEditResult
        {
            Path = edit.Path,
            Operation = edit.Operation,
            Applied = true,
            Status = "AppliedInsideDisposableWorkspace"
        };
    }

    private static bool IsAllowedOperation(PatchEditOperation operation) =>
        operation is PatchEditOperation.ReplaceText or PatchEditOperation.AppendText or PatchEditOperation.CreateFile;

    private static IEnumerable<string> ValidateTarget(string workspacePath, string sourceRepoPath, string editPath)
    {
        if (string.IsNullOrWhiteSpace(editPath))
        {
            yield return "MissingPath";
            yield break;
        }

        var target = ResolveTargetPath(workspacePath, editPath);
        if (!IsSameOrUnderPath(target, workspacePath))
            yield return "PathOutsideWorkspace";

        if (IsSameOrUnderPath(target, Path.Combine(workspacePath, ".git")))
            yield return "PathUnderGitDirectory";

        if (!string.IsNullOrWhiteSpace(sourceRepoPath) && IsSameOrUnderPath(target, sourceRepoPath))
            yield return "PathUnderSourceRepository";
    }

    private static WorkspacePatchEditResult Blocked(PatchEdit edit, string[] reasons) =>
        new()
        {
            Path = edit.Path,
            Operation = edit.Operation,
            Applied = false,
            Status = "BlockedBeforeMutation",
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };

    private static string ResolveTargetPath(string workspacePath, string editPath) =>
        Path.GetFullPath(Path.IsPathRooted(editPath) ? editPath : Path.Combine(workspacePath, editPath));

    private static bool IsSameOrUnderPath(string candidate, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedCandidate = NormalizePath(candidate);
        var normalizedRoot = NormalizePath(root);
        return string.Equals(normalizedCandidate, normalizedRoot, comparison) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
}

public sealed record TestFailureAnalysis
{
    public required string TestFailureAnalysisId { get; init; }
    public required string RunId { get; init; }
    public required string ModelResponseId { get; init; }
    public string[] TestResultRefs { get; init; } = [];
    public required string Summary { get; init; }
    public string[] LikelyCauses { get; init; } = [];
    public string[] SuggestedNextEdits { get; init; } = [];
    public required string Confidence { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public PatchAiBoundary Boundary { get; init; } = PatchAiBoundary.None;
}

public sealed record RefinementIterationRecord
{
    public required string RefinementIterationId { get; init; }
    public required string RunId { get; init; }
    public required int IterationNumber { get; init; }
    public required string ModelRequestId { get; init; }
    public required string ModelResponseId { get; init; }
    public required bool SafeEditsApplied { get; init; }
    public required bool UnsafeEditsBlocked { get; init; }
    public required bool TestCommandExecutedThroughToolGate { get; init; }
    public required bool TestsPassed { get; init; }
    public required string StopReason { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public PatchAiBoundary Boundary { get; init; } = PatchAiBoundary.None;
}

public sealed record AiPatchReviewFinding
{
    public required string Severity { get; init; }
    public required string Summary { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
}

public sealed record AiPatchReview
{
    public required string AiPatchReviewId { get; init; }
    public required string RunId { get; init; }
    public required string ModelResponseId { get; init; }
    public required string PatchHash { get; init; }
    public string[] ChangedFiles { get; init; } = [];
    public AiPatchReviewFinding[] Findings { get; init; } = [];
    public string[] Risks { get; init; } = [];
    public string[] MissingTests { get; init; } = [];
    public string[] BoundaryConcerns { get; init; } = [];
    public required AiPatchReviewVerdict Verdict { get; init; }
    public required bool RequiresHumanReview { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public PatchAiBoundary Boundary { get; init; } = PatchAiBoundary.None;
}

public static class AiPatchReviewValidator
{
    public static bool IsAllowedVerdict(AiPatchReviewVerdict verdict) =>
        verdict is AiPatchReviewVerdict.LooksReviewable or
            AiPatchReviewVerdict.NeedsHumanReview or
            AiPatchReviewVerdict.TestsFailing or
            AiPatchReviewVerdict.BoundaryConcern or
            AiPatchReviewVerdict.NotEnoughEvidence;
}

public interface IPatchModelProvider
{
    string ProviderName { get; }
    string ModelName { get; }
    Task<ModelResponseEnvelope> CompleteAsync(ModelRequestEnvelope request, CancellationToken cancellationToken);
}

public sealed class DisabledPatchModelProvider : IPatchModelProvider
{
    public string ProviderName => "Disabled";
    public string ModelName => "disabled";

    public Task<ModelResponseEnvelope> CompleteAsync(ModelRequestEnvelope request, CancellationToken cancellationToken) =>
        Task.FromResult(new ModelResponseEnvelope
        {
            ModelResponseId = $"model_resp_{Guid.NewGuid():N}",
            ModelRequestId = request.ModelRequestId,
            RunId = request.RunId,
            ProviderName = ProviderName,
            ModelName = ModelName,
            ResponseKind = request.RequestKind,
            ResponseText = "Model provider is disabled.",
            StructuredPayloadJson = null,
            FinishReason = "Disabled",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
}

public sealed class DeterministicPatchModelProvider : IPatchModelProvider
{
    private static readonly JsonSerializerOptions ProviderJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string ProviderName => "Deterministic";
    public string ModelName => "deterministic-patch-assistant-v1";

    public Task<ModelResponseEnvelope> CompleteAsync(ModelRequestEnvelope request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = request.RequestKind is PatchModelRequestKind.PatchSuggestion or PatchModelRequestKind.PatchRefinement
            ? CreateEditPlan(request)
            : null;
        var text = request.RequestKind switch
        {
            PatchModelRequestKind.PatchSuggestion => "Patch suggestion: append a small deterministic review line inside the disposable workspace. Human review remains required.",
            PatchModelRequestKind.TestFailureAnalysis => "Test failure analysis: inspect the latest test summary, keep edits bounded, and rerun tests through the workspace tool gate.",
            PatchModelRequestKind.PatchRefinement => "Patch refinement: make one bounded workspace edit, then rerun tests through the workspace tool gate.",
            PatchModelRequestKind.PatchReview => "AI patch review: review evidence only. Human review remains required before any source apply.",
            _ => "No model action."
        };

        return Task.FromResult(new ModelResponseEnvelope
        {
            ModelResponseId = $"model_resp_{Guid.NewGuid():N}",
            ModelRequestId = request.ModelRequestId,
            RunId = request.RunId,
            ProviderName = ProviderName,
            ModelName = ModelName,
            ResponseKind = request.RequestKind,
            ResponseText = text,
            StructuredPayloadJson = plan is null ? null : JsonSerializer.Serialize(plan, ProviderJsonOptions),
            FinishReason = "Stop",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static PatchEditPlan CreateEditPlan(ModelRequestEnvelope request) =>
        new()
        {
            PatchEditPlanId = $"edit_plan_{Guid.NewGuid():N}",
            RunId = request.RunId,
            ModelResponseId = "pending",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Edits =
            [
                new PatchEdit
                {
                    Path = "README.md",
                    Operation = PatchEditOperation.AppendText,
                    NewContent = Environment.NewLine + "AI assistance suggestion from deterministic provider." + Environment.NewLine,
                    Rationale = "Bounded deterministic provider proves the patch assistance pipeline without network access.",
                    Risk = "Human review remains required."
                }
            ]
        };
}

public static class PatchAiTextSafety
{
    private static readonly string[] UnsafeMarkers =
    [
        "hidden chain-of-thought",
        "chain of thought",
        "chain-of-thought",
        "private scratchpad",
        "scratchpad",
        "private reasoning",
        "raw prompt",
        "raw completion",
        "raw tool output",
        "approved",
        "release ready",
        "safe to merge",
        "safe to deploy",
        "apply automatically",
        "policy satisfied",
        "approval granted"
    ];

    public static bool ContainsUnsafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.ToLowerInvariant();
        return UnsafeMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }
}