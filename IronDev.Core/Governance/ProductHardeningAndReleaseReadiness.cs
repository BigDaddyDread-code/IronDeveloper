using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public enum ProductHardeningAuditOutcome
{
    Pass = 0,
    NeedsMoreEvidence,
    NotReady
}

public enum ProductReleaseReadinessOutcome
{
    ReadyForDecision = 0,
    NotReady,
    NeedsMoreEvidence
}

public enum ProductHardeningStepStatus
{
    NotStarted = 0,
    Completed,
    Failed
}

public sealed record ProductHardeningBoundary
{
    public bool EvidenceOnly { get; init; } = true;
    public bool CanApprove { get; init; }
    public bool CanExecute { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanMutateWorkspace { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanCreatePullRequest { get; init; }
    public bool CanMerge { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanPromoteMemory { get; init; }
}

public static class ProductHardeningBoundaryText
{
    public const string Boundary = """
        Product hardening is evidence.
        Release readiness is evidence.
        Release readiness decision is bounded.
        No merge/release/deploy authority is added.
        No workflow continuation authority is added.
        No source/workspace mutation authority is added.
        """;

    public const string ReadinessDecisionBoundary = """
        This decision record does not release.
        This decision record does not deploy.
        This decision record does not merge.
        This decision record does not continue workflow.
        This decision record does not approve source mutation.
        """;
}

public sealed record ProductDogfoodRunRequest
{
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required string TaskSummary { get; init; }
    public string[] ExistingArtifacts { get; init; } = [];
    public string[] MissingArtifacts { get; init; } = [];
    public bool SimulateFailure { get; init; }
    public DateTimeOffset? RequestedAtUtc { get; init; }
}

public sealed record ProductDogfoodRun
{
    public required string DogfoodRunId { get; init; }
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required string TaskSummary { get; init; }
    public required ProductHardeningAuditOutcome Outcome { get; init; }
    public ProductDogfoodStep[] Steps { get; init; } = [];
    public string[] ArtifactsWritten { get; init; } = [];
    public string[] MissingArtifacts { get; init; } = [];
    public string[] KnownRisks { get; init; } = [];
    public bool SourceMutated { get; init; }
    public bool CommitCreated { get; init; }
    public bool PushPerformed { get; init; }
    public bool PullRequestCreated { get; init; }
    public bool MergePerformed { get; init; }
    public bool ReleasePerformed { get; init; }
    public bool DeployPerformed { get; init; }
    public bool WorkflowContinued { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public ProductHardeningBoundary Boundary { get; init; } = new();
}

public sealed record ProductDogfoodStep
{
    public required string StepId { get; init; }
    public required string Name { get; init; }
    public required ProductHardeningStepStatus Status { get; init; }
    public string[] ArtifactRefs { get; init; } = [];
}

public sealed record ProductDogfoodArtifactChecklist
{
    public required string DogfoodArtifactChecklistId { get; init; }
    public required string RunId { get; init; }
    public ProductDogfoodArtifactChecklistItem[] Items { get; init; } = [];
    public ProductHardeningBoundary Boundary { get; init; } = new();
}

public sealed record ProductDogfoodArtifactChecklistItem
{
    public required string ArtifactName { get; init; }
    public required bool Required { get; init; }
    public required bool Exists { get; init; }
    public string? ContentHash { get; init; }
}

public static class ProductHardeningDogfood
{
    public static readonly string[] ProductPathSteps =
    [
        "manual patch proposal",
        "patch loop usability",
        "governed action spine",
        "governed workspace tools",
        "AI patch assistance",
        "safe memory from runs",
        "controlled source apply evidence",
        "AJ governed action kernel",
        "AK memory-informed planning"
    ];

    public static readonly string[] DogfoodArtifacts =
    [
        "dogfood-run.json",
        "dogfood-run.md",
        "dogfood-artifact-checklist.json",
        "dogfood-artifact-checklist.md",
        "dogfood-known-risks.md"
    ];

    public static ProductDogfoodRun CreateRun(ProductDogfoodRunRequest request, DateTimeOffset? now = null)
    {
        var missing = request.MissingArtifacts
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var outcome = request.SimulateFailure || missing.Length > 0
            ? ProductHardeningAuditOutcome.NeedsMoreEvidence
            : ProductHardeningAuditOutcome.Pass;

        return new ProductDogfoodRun
        {
            DogfoodRunId = $"dogfood_{ShortHash($"{request.RunId}|{request.ProjectId}|{request.TaskSummary}")}",
            RunId = Safe(request.RunId),
            ProjectId = Safe(request.ProjectId),
            TaskSummary = Safe(request.TaskSummary),
            Outcome = outcome,
            Steps = ProductPathSteps.Select((step, index) => new ProductDogfoodStep
            {
                StepId = $"ai1_step_{index + 1:D2}",
                Name = step,
                Status = outcome == ProductHardeningAuditOutcome.Pass ? ProductHardeningStepStatus.Completed : index == ProductPathSteps.Length - 1 ? ProductHardeningStepStatus.Failed : ProductHardeningStepStatus.Completed,
                ArtifactRefs = index == ProductPathSteps.Length - 1 ? ["memory-informed-plan.json", "killjoy-plan-review.json"] : []
            }).ToArray(),
            ArtifactsWritten = DogfoodArtifacts,
            MissingArtifacts = missing,
            KnownRisks = missing.Length == 0
                ? ["Dogfood evidence is product-readiness evidence only."]
                : [$"Missing artifacts: {string.Join(", ", missing)}", "Manual recovery is required before relying on this dogfood run."],
            CreatedAtUtc = now ?? request.RequestedAtUtc ?? DateTimeOffset.UtcNow,
            Boundary = new()
        };
    }

    public static ProductDogfoodArtifactChecklist CreateChecklist(string runId, IReadOnlyDictionary<string, string> artifactContents)
    {
        return new ProductDogfoodArtifactChecklist
        {
            DogfoodArtifactChecklistId = $"dogfood_checklist_{ShortHash(runId)}",
            RunId = Safe(runId),
            Items = DogfoodArtifacts.Select(artifact => new ProductDogfoodArtifactChecklistItem
            {
                ArtifactName = artifact,
                Required = true,
                Exists = artifactContents.ContainsKey(artifact),
                ContentHash = artifactContents.TryGetValue(artifact, out var content) ? Hash(content) : null
            }).ToArray(),
            Boundary = new()
        };
    }

    private static string ShortHash(string value) => Hash(value)[..16];
    internal static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
    internal static string Safe(string? value) => (value ?? string.Empty).Trim();
}

public sealed record ProductArtifactConsistencyAuditRequest
{
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public string[] RequiredArtifacts { get; init; } = [];
    public ProductArtifactDescriptor[] Artifacts { get; init; } = [];
    public DateTimeOffset? AuditedAtUtc { get; init; }
}

public sealed record ProductArtifactDescriptor
{
    public required string ArtifactName { get; init; }
    public bool Exists { get; init; } = true;
    public string? RunId { get; init; }
    public string? ProjectId { get; init; }
    public string? PatchHash { get; init; }
    public string? BaseCommit { get; init; }
    public string? SourceRepoIdentity { get; init; }
    public string[] ChangedFiles { get; init; } = [];
    public string[] MemoryCitationHashes { get; init; } = [];
    public string[] GovernanceEventRefs { get; init; } = [];
    public string[] ThoughtLedgerRefs { get; init; } = [];
    public string[] ConscienceRefs { get; init; } = [];
}

public sealed record ProductArtifactConsistencyIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string ArtifactName { get; init; }
    public required string Message { get; init; }
}

public sealed record ProductArtifactConsistencyReport
{
    public required string ArtifactConsistencyReportId { get; init; }
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required ProductHardeningAuditOutcome Outcome { get; init; }
    public ProductArtifactConsistencyIssue[] Issues { get; init; } = [];
    public bool CanApprove { get; init; }
    public bool CanExecute { get; init; }
    public bool CanRepairArtifacts { get; init; }
    public required DateTimeOffset AuditedAtUtc { get; init; }
    public ProductHardeningBoundary Boundary { get; init; } = new();
}

public static class ProductArtifactConsistencyAuditor
{
    public static ProductArtifactConsistencyReport Audit(ProductArtifactConsistencyAuditRequest request)
    {
        var issues = new List<ProductArtifactConsistencyIssue>();
        var artifacts = request.Artifacts.ToDictionary(item => item.ArtifactName, StringComparer.OrdinalIgnoreCase);

        foreach (var required in request.RequiredArtifacts.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            if (!artifacts.TryGetValue(required.Trim(), out var artifact) || !artifact.Exists)
                Add(issues, "MissingArtifact", "Blocking", required.Trim(), "Required artifact is missing.");
        }

        CheckScalar(issues, "RunIdMismatch", "Run id mismatch across artifacts.", request.Artifacts.Select(item => item.RunId), request.RunId);
        CheckScalar(issues, "ProjectIdMismatch", "Project id mismatch across artifacts.", request.Artifacts.Select(item => item.ProjectId), request.ProjectId);
        CheckScalar(issues, "PatchHashMismatch", "Patch hash mismatch across artifacts.", request.Artifacts.Select(item => item.PatchHash));
        CheckScalar(issues, "BaseCommitMismatch", "Base commit mismatch across artifacts.", request.Artifacts.Select(item => item.BaseCommit));
        CheckScalar(issues, "SourceRepoIdentityMismatch", "Source repo identity mismatch across artifacts.", request.Artifacts.Select(item => item.SourceRepoIdentity));
        CheckSet(issues, "ChangedFilesMismatch", "Changed files mismatch across artifacts.", request.Artifacts.Select(item => item.ChangedFiles));
        CheckSet(issues, "MemoryCitationHashMismatch", "Memory citation hash mismatch across artifacts.", request.Artifacts.Select(item => item.MemoryCitationHashes));

        if (!request.Artifacts.Any(item => item.GovernanceEventRefs.Length > 0))
            Add(issues, "GovernanceEventReferencesMissing", "Warning", "governance-events", "No governance event references were found.");
        if (!request.Artifacts.Any(item => item.ThoughtLedgerRefs.Length > 0))
            Add(issues, "ThoughtLedgerReferencesMissing", "Warning", "thought-ledger", "No ThoughtLedger references were found.");
        if (!request.Artifacts.Any(item => item.ConscienceRefs.Length > 0))
            Add(issues, "ConscienceReferencesMissing", "Warning", "conscience", "No Conscience references were found.");

        var blocking = issues.Any(item => item.Severity == "Blocking");
        return new ProductArtifactConsistencyReport
        {
            ArtifactConsistencyReportId = $"artifact_consistency_{ProductHardeningDogfood.Hash($"{request.RunId}|{request.ProjectId}")[..16]}",
            RunId = ProductHardeningDogfood.Safe(request.RunId),
            ProjectId = ProductHardeningDogfood.Safe(request.ProjectId),
            Outcome = blocking ? ProductHardeningAuditOutcome.NotReady : ProductHardeningAuditOutcome.Pass,
            Issues = issues.ToArray(),
            AuditedAtUtc = request.AuditedAtUtc ?? DateTimeOffset.UtcNow,
            Boundary = new()
        };
    }

    private static void CheckScalar(List<ProductArtifactConsistencyIssue> issues, string code, string message, IEnumerable<string?> values, string? expected = null)
    {
        var distinct = values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!string.IsNullOrWhiteSpace(expected) && distinct.Any(value => !string.Equals(value, expected.Trim(), StringComparison.OrdinalIgnoreCase)))
            Add(issues, code, "Blocking", "artifact-set", message);
        else if (distinct.Length > 1)
            Add(issues, code, "Blocking", "artifact-set", message);
    }

    private static void CheckSet(List<ProductArtifactConsistencyIssue> issues, string code, string message, IEnumerable<string[]> values)
    {
        var distinct = values
            .Select(items => string.Join("|", items.Select(item => item.Trim()).Where(item => item.Length > 0).OrderBy(item => item, StringComparer.OrdinalIgnoreCase)))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinct.Length > 1)
            Add(issues, code, "Blocking", "artifact-set", message);
    }

    private static void Add(List<ProductArtifactConsistencyIssue> issues, string code, string severity, string artifact, string message) =>
        issues.Add(new ProductArtifactConsistencyIssue { Code = code, Severity = severity, ArtifactName = artifact, Message = message });
}

public sealed record UnsafeMaterialScanRequest
{
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required IReadOnlyDictionary<string, string> ArtifactText { get; init; }
    public DateTimeOffset? ScannedAtUtc { get; init; }
}

public sealed record UnsafeMaterialFinding
{
    public required string FindingId { get; init; }
    public required string ArtifactName { get; init; }
    public required string Kind { get; init; }
    public required string Severity { get; init; }
    public required string RedactedPreview { get; init; }
}

public sealed record UnsafeMaterialReport
{
    public required string UnsafeMaterialReportId { get; init; }
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required ProductHardeningAuditOutcome Outcome { get; init; }
    public UnsafeMaterialFinding[] Findings { get; init; } = [];
    public bool CanApprove { get; init; }
    public bool CanExecute { get; init; }
    public bool MutatesArtifacts { get; init; }
    public required DateTimeOffset ScannedAtUtc { get; init; }
    public ProductHardeningBoundary Boundary { get; init; } = new();
}

public static class UnsafeMaterialScanner
{
    private static readonly (string Kind, Regex Pattern)[] Patterns =
    [
        ("SecretShape", new Regex(@"(?i)(api[_-]?key|token|password)\s*[:=]\s*['""]?[^'""\s]+", RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        ("BearerToken", new Regex(@"(?i)bearer\s+[a-z0-9._\-]{10,}", RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        ("ConnectionString", new Regex(@"(?i)(server|data source)\s*=.+;(user id|uid|password|pwd)\s*=", RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        ("PrivateKey", new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        ("HiddenReasoning", new Regex(@"(?i)(chain-of-thought|chain of thought|hidden reasoning|private reasoning|scratchpad|raw prompt|raw completion|raw tool output)", RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        ("AuthorityClaim", new Regex(@"(?i)(approval granted|release approved|deployment approved|merge approved|execute now|safe to deploy|safe to merge|deployable|shipped)", RegexOptions.Compiled | RegexOptions.CultureInvariant))
    ];

    public static UnsafeMaterialReport Scan(UnsafeMaterialScanRequest request)
    {
        var findings = new List<UnsafeMaterialFinding>();
        foreach (var (artifact, text) in request.ArtifactText)
        {
            foreach (var (kind, pattern) in Patterns)
            {
                foreach (Match match in pattern.Matches(text ?? string.Empty))
                {
                    findings.Add(new UnsafeMaterialFinding
                    {
                        FindingId = $"unsafe_{ProductHardeningDogfood.Hash($"{artifact}|{kind}|{match.Index}")[..16]}",
                        ArtifactName = artifact,
                        Kind = kind,
                        Severity = "Blocking",
                        RedactedPreview = Redact(match.Value)
                    });
                }
            }
        }

        return new UnsafeMaterialReport
        {
            UnsafeMaterialReportId = $"unsafe_report_{ProductHardeningDogfood.Hash($"{request.RunId}|{request.ProjectId}")[..16]}",
            RunId = ProductHardeningDogfood.Safe(request.RunId),
            ProjectId = ProductHardeningDogfood.Safe(request.ProjectId),
            Outcome = findings.Count == 0 ? ProductHardeningAuditOutcome.Pass : ProductHardeningAuditOutcome.NotReady,
            Findings = findings.ToArray(),
            ScannedAtUtc = request.ScannedAtUtc ?? DateTimeOffset.UtcNow,
            Boundary = new()
        };
    }

    private static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "[REDACTED]";
        var label = value.Contains('=') ? value.Split('=')[0].Trim() : value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "value";
        return $"{label}=[REDACTED]";
    }
}

public sealed record ProductResumeReportRequest
{
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public ProductDogfoodStep[] Steps { get; init; } = [];
    public string[] ArtifactRefs { get; init; } = [];
    public string[] MissingArtifacts { get; init; } = [];
    public string? FailedArtifact { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record ProductResumeReport
{
    public required string ResumeReportId { get; init; }
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required string LastCompletedProductStep { get; init; }
    public required string LastSafeArtifact { get; init; }
    public string[] MissingArtifacts { get; init; } = [];
    public string? FailedArtifact { get; init; }
    public required string SuggestedManualNextCommand { get; init; }
    public string[] UnsafeContinuationWarnings { get; init; } = [];
    public bool ContinuesWorkflow { get; init; }
    public bool ExecutesCommands { get; init; }
    public bool MutatesSource { get; init; }
    public bool PromotesMemory { get; init; }
    public bool MarksReleaseReady { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public ProductHardeningBoundary Boundary { get; init; } = new();
}

public sealed record ProductFailureSummary
{
    public required string FailureSummaryId { get; init; }
    public required string RunId { get; init; }
    public string[] MissingArtifacts { get; init; } = [];
    public string? FailedArtifact { get; init; }
    public required string Summary { get; init; }
    public ProductHardeningBoundary Boundary { get; init; } = new();
}

public static class ProductResumeReportBuilder
{
    public static ProductResumeReport Build(ProductResumeReportRequest request)
    {
        var lastCompleted = request.Steps.LastOrDefault(step => step.Status == ProductHardeningStepStatus.Completed)?.Name ?? "none";
        var lastSafeArtifact = request.ArtifactRefs.LastOrDefault(artifact => !string.Equals(artifact, request.FailedArtifact, StringComparison.OrdinalIgnoreCase)) ?? "none";
        return new ProductResumeReport
        {
            ResumeReportId = $"resume_{ProductHardeningDogfood.Hash($"{request.RunId}|{request.ProjectId}")[..16]}",
            RunId = ProductHardeningDogfood.Safe(request.RunId),
            ProjectId = ProductHardeningDogfood.Safe(request.ProjectId),
            LastCompletedProductStep = lastCompleted,
            LastSafeArtifact = lastSafeArtifact,
            MissingArtifacts = request.MissingArtifacts,
            FailedArtifact = request.FailedArtifact,
            SuggestedManualNextCommand = $"Inspect run {request.RunId} and rerun the failed product-hardening command manually after fixing missing prerequisites.",
            UnsafeContinuationWarnings =
            [
                "Resume guidance is not workflow continuation.",
                "Manual review is required before any source mutation, memory promotion, release, merge, or deploy."
            ],
            CreatedAtUtc = request.CreatedAtUtc ?? DateTimeOffset.UtcNow,
            Boundary = new()
        };
    }

    public static ProductFailureSummary BuildFailureSummary(ProductResumeReport report) => new()
    {
        FailureSummaryId = $"failure_{ProductHardeningDogfood.Hash(report.ResumeReportId)[..16]}",
        RunId = report.RunId,
        MissingArtifacts = report.MissingArtifacts,
        FailedArtifact = report.FailedArtifact,
        Summary = report.MissingArtifacts.Length == 0 && string.IsNullOrWhiteSpace(report.FailedArtifact)
            ? "No failed artifact was recorded."
            : $"Failed artifact: {report.FailedArtifact ?? "none"}. Missing artifacts: {string.Join(", ", report.MissingArtifacts)}.",
        Boundary = new()
    };
}

public sealed record ProductReleaseReadinessEvaluationRequest
{
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public bool FocusedBlockValidationRecorded { get; init; }
    public bool StableBandValidationRecorded { get; init; }
    public bool BuildResultRecorded { get; init; }
    public bool DiffCheckRecorded { get; init; }
    public bool ArtifactConsistencyReportExists { get; init; }
    public bool ArtifactConsistencyPassed { get; init; }
    public bool UnsafeMaterialReportExists { get; init; }
    public bool UnsafeMaterialPassed { get; init; }
    public bool ResumeReportBehaviorExists { get; init; }
    public bool KnownRisksDocumented { get; init; }
    public bool AuthorityBoundaryDocumented { get; init; }
    public bool NoUnsupportedMutationSurfacesPresent { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public DateTimeOffset? EvaluatedAtUtc { get; init; }
}

public sealed record ProductReleaseReadinessChecklistItem
{
    public required string Check { get; init; }
    public required bool Passed { get; init; }
    public required string EvidenceRef { get; init; }
}

public sealed record ProductReleaseReadinessReport
{
    public required string ReleaseReadinessReportId { get; init; }
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required ProductReleaseReadinessOutcome Outcome { get; init; }
    public ProductReleaseReadinessChecklistItem[] Checklist { get; init; } = [];
    public string[] BlockingIssues { get; init; } = [];
    public string[] EvidenceRefs { get; init; } = [];
    public string[] KnownRisks { get; init; } = [];
    public bool CanMerge { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public required string ReadinessReportHash { get; init; }
    public ProductHardeningBoundary Boundary { get; init; } = new();
}

public static class ProductReleaseReadinessEvaluator
{
    public static ProductReleaseReadinessReport Evaluate(ProductReleaseReadinessEvaluationRequest request)
    {
        var checklist = new[]
        {
            Item("focused block validation recorded", request.FocusedBlockValidationRecorded),
            Item("stable band validation recorded", request.StableBandValidationRecorded),
            Item("build result recorded", request.BuildResultRecorded),
            Item("diff check recorded", request.DiffCheckRecorded),
            Item("artifact consistency report exists", request.ArtifactConsistencyReportExists),
            Item("artifact consistency passed", request.ArtifactConsistencyPassed),
            Item("unsafe material report exists", request.UnsafeMaterialReportExists),
            Item("unsafe material passed", request.UnsafeMaterialPassed),
            Item("resume report behavior exists", request.ResumeReportBehaviorExists),
            Item("known risks documented", request.KnownRisksDocumented),
            Item("authority boundary documented", request.AuthorityBoundaryDocumented),
            Item("no unsupported mutation surfaces present", request.NoUnsupportedMutationSurfacesPresent)
        };
        var missing = checklist.Where(item => !item.Passed).Select(item => item.Check).ToArray();
        var notReady = (request.ArtifactConsistencyReportExists && !request.ArtifactConsistencyPassed) ||
            (request.UnsafeMaterialReportExists && !request.UnsafeMaterialPassed) ||
            !request.NoUnsupportedMutationSurfacesPresent;
        var outcome = notReady
            ? ProductReleaseReadinessOutcome.NotReady
            : missing.Length > 0
                ? ProductReleaseReadinessOutcome.NeedsMoreEvidence
                : ProductReleaseReadinessOutcome.ReadyForDecision;
        var report = new ProductReleaseReadinessReport
        {
            ReleaseReadinessReportId = $"readiness_{ProductHardeningDogfood.Hash($"{request.RunId}|{request.ProjectId}")[..16]}",
            RunId = ProductHardeningDogfood.Safe(request.RunId),
            ProjectId = ProductHardeningDogfood.Safe(request.ProjectId),
            Outcome = outcome,
            Checklist = checklist,
            BlockingIssues = missing,
            EvidenceRefs = request.EvidenceRefs,
            KnownRisks = ["Readiness evidence still requires a later human/governed release decision."],
            EvaluatedAtUtc = request.EvaluatedAtUtc ?? DateTimeOffset.UtcNow,
            ReadinessReportHash = "pending",
            Boundary = new()
        };
        return report with { ReadinessReportHash = ProductHardeningDogfood.Hash($"{report.ReleaseReadinessReportId}|{report.Outcome}|{string.Join("|", report.BlockingIssues)}|{report.EvaluatedAtUtc:O}") };
    }

    private static ProductReleaseReadinessChecklistItem Item(string check, bool passed) => new()
    {
        Check = check,
        Passed = passed,
        EvidenceRef = passed ? $"evidence:{check.Replace(' ', '-')}" : "missing"
    };
}

public sealed record ProductReleaseReadinessDecisionRequest
{
    public ProductReleaseReadinessReport? Report { get; init; }
    public required ProductReleaseReadinessOutcome Decision { get; init; }
    public string[] Reasons { get; init; } = [];
    public string[] EvidenceRefs { get; init; } = [];
    public required string ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAtUtc { get; init; }
}

public sealed record ProductReleaseReadinessDecisionRecord
{
    public required string ReleaseReadinessDecisionId { get; init; }
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required string ReadinessReportId { get; init; }
    public required ProductReleaseReadinessOutcome Decision { get; init; }
    public string[] Reasons { get; init; } = [];
    public string[] EvidenceRefs { get; init; } = [];
    public required string ReviewedBy { get; init; }
    public required DateTimeOffset ReviewedAtUtc { get; init; }
    public required string DecisionHash { get; init; }
    public string Boundary { get; init; } = ProductHardeningBoundaryText.ReadinessDecisionBoundary;
    public bool Releases { get; init; }
    public bool Deploys { get; init; }
    public bool Merges { get; init; }
    public bool ContinuesWorkflow { get; init; }
    public bool ApprovesSourceMutation { get; init; }
}

public sealed record ProductReleaseReadinessDecisionResult
{
    public ProductReleaseReadinessDecisionRecord? Record { get; init; }
    public string[] Issues { get; init; } = [];
    public bool IsValid => Issues.Length == 0 && Record is not null;
}

public static class ProductReleaseReadinessDecisionRecorder
{
    public static ProductReleaseReadinessDecisionResult Create(ProductReleaseReadinessDecisionRequest request)
    {
        var issues = new List<string>();
        if (request.Report is null)
            issues.Add("MissingReadinessReport");
        else if (string.IsNullOrWhiteSpace(request.Report.ReadinessReportHash))
            issues.Add("MissingReadinessReportHash");
        if (string.IsNullOrWhiteSpace(request.ReviewedBy))
            issues.Add("MissingReviewer");
        if (issues.Count > 0)
            return new ProductReleaseReadinessDecisionResult { Issues = issues.ToArray() };

        var reviewedAt = request.ReviewedAtUtc ?? DateTimeOffset.UtcNow;
        var id = $"readiness_decision_{ProductHardeningDogfood.Hash($"{request.Report!.ReleaseReadinessReportId}|{request.ReviewedBy}|{reviewedAt:O}")[..16]}";
        var hash = ProductHardeningDogfood.Hash($"{id}|{request.Report.RunId}|{request.Report.ProjectId}|{request.Report.ReleaseReadinessReportId}|{request.Decision}|{string.Join("|", request.Reasons)}|{string.Join("|", request.EvidenceRefs)}|{request.ReviewedBy}|{reviewedAt:O}");
        return new ProductReleaseReadinessDecisionResult
        {
            Record = new ProductReleaseReadinessDecisionRecord
            {
                ReleaseReadinessDecisionId = id,
                RunId = request.Report.RunId,
                ProjectId = request.Report.ProjectId,
                ReadinessReportId = request.Report.ReleaseReadinessReportId,
                Decision = request.Decision,
                Reasons = request.Reasons.Length == 0 ? [$"Recorded readiness status: {request.Decision}."] : request.Reasons,
                EvidenceRefs = request.EvidenceRefs.Length == 0 ? request.Report.EvidenceRefs : request.EvidenceRefs,
                ReviewedBy = request.ReviewedBy.Trim(),
                ReviewedAtUtc = reviewedAt,
                DecisionHash = hash,
                Boundary = ProductHardeningBoundaryText.ReadinessDecisionBoundary
            },
            Issues = []
        };
    }
}

public sealed record ProductHardeningBypassReport
{
    public required string ProductHardeningBypassReportId { get; init; }
    public required string RunId { get; init; }
    public string[] EvidenceSubjects { get; init; } = [];
    public bool MergeAuthorized { get; init; }
    public bool ReleaseAuthorized { get; init; }
    public bool DeployAuthorized { get; init; }
    public bool SourceMutationAuthorized { get; init; }
    public bool WorkflowContinuationAuthorized { get; init; }
    public ProductHardeningBoundary Boundary { get; init; } = new();
}

public static class ProductHardeningBypassEvaluator
{
    public static ProductHardeningBypassReport Evaluate(string runId, IEnumerable<string> evidenceSubjects) => new()
    {
        ProductHardeningBypassReportId = $"product_hardening_bypass_{ProductHardeningDogfood.Hash(runId)[..16]}",
        RunId = ProductHardeningDogfood.Safe(runId),
        EvidenceSubjects = evidenceSubjects.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).ToArray(),
        Boundary = new()
    };

    public static bool CanAuthorizeShipping(object? evidence) => false;
}
