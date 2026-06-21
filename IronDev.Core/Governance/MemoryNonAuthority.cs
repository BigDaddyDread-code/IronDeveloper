using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronDev.Core.Governance;

public sealed record MemoryAuthorityUseAttempt
{
    public required string AttemptId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required string SourceKind { get; init; }
    public required string SourceId { get; init; }

    public required string MemoryScope { get; init; }
    public required string MemoryKind { get; init; }

    public required string CurrentProjectId { get; init; }
    public required string CurrentRepository { get; init; }

    public required string MemoryProjectId { get; init; }
    public required string MemoryRepository { get; init; }

    public required string RequestedActionType { get; init; }
    public required string RequiredAuthorityType { get; init; }
    public required string SuppliedAuthorityType { get; init; }

    public required string ClaimedAuthorityPhrase { get; init; }
    public required string ClaimHash { get; init; }

    public required bool MemoryUsedAsContext { get; init; }
    public required bool MemoryPresentedAsAuthority { get; init; }

    public required bool CrossProject { get; init; }
    public required bool CrossRepository { get; init; }

    public required bool ContainsApprovalLanguage { get; init; }
    public required bool ContainsPolicySatisfactionLanguage { get; init; }
    public required bool ContainsExecutionLanguage { get; init; }
    public required bool ContainsMutationLanguage { get; init; }
    public required bool ContainsWorkflowContinuationLanguage { get; init; }
    public required bool ContainsMemoryPromotionLanguage { get; init; }
}

public sealed record MemoryNonAuthorityDecision
{
    public required string DecisionId { get; init; }
    public required string AttemptId { get; init; }

    public required string Verdict { get; init; }
    public required string BlockReason { get; init; }
    public required string RequiredAuthorityType { get; init; }
    public required string SuppliedAuthorityType { get; init; }

    public required bool MemoryAllowedAsContext { get; init; }
    public required bool MemoryAcceptedAsAuthority { get; init; }

    public required bool ApprovalSatisfied { get; init; }
    public required bool PolicySatisfied { get; init; }
    public required bool ExecutionAuthorized { get; init; }
    public required bool MutationAuthorized { get; init; }
    public required bool WorkflowContinuationAuthorized { get; init; }
    public required bool MemoryPromotionAuthorized { get; init; }

    public required bool StaleAuthorityRefreshed { get; init; }
    public required bool CrossProjectAuthorityAccepted { get; init; }
    public required bool CrossRepositoryAuthorityAccepted { get; init; }

    public required string HumanSummary { get; init; }
    public required string SafeNextStep { get; init; }

    public required bool DecisionGrantedAuthority { get; init; }
    public required bool DecisionMutatedState { get; init; }

    public string[] RedFlags { get; init; } = [];
    public string[] AmberFlags { get; init; } = [];
    public string[] Notes { get; init; } = [];
}

public sealed record MemoryNonAuthoritySummary
{
    public required string ReportId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required int TotalAttempts { get; init; }
    public required int AllowedAsContextCount { get; init; }
    public required int BlockedAsAuthorityCount { get; init; }

    public required int MemoryAcceptedAsAuthorityCount { get; init; }
    public required int ApprovalSatisfiedByMemoryCount { get; init; }
    public required int PolicySatisfiedByMemoryCount { get; init; }
    public required int ExecutionAuthorizedByMemoryCount { get; init; }
    public required int MutationAuthorizedByMemoryCount { get; init; }
    public required int WorkflowContinuationAuthorizedByMemoryCount { get; init; }
    public required int MemoryPromotionAuthorizedByMemoryCount { get; init; }

    public required int StaleAuthorityRefreshCount { get; init; }
    public required int CrossProjectAuthorityAcceptedCount { get; init; }
    public required int CrossRepositoryAuthorityAcceptedCount { get; init; }

    public required int MissingSafeNextStepCount { get; init; }
    public required int GenericBlockReasonCount { get; init; }

    public required bool ReportPassed { get; init; }

    public string[] RedFindings { get; init; } = [];
    public string[] AmberFindings { get; init; } = [];
    public string[] GreenFindings { get; init; } = [];
}

public sealed record MemoryNonAuthorityBoundary
{
    public bool EvidenceOnly { get; init; } = true;
    public bool ContextOnly { get; init; } = true;

    public bool CanApprove { get; init; }
    public bool CanSatisfyPolicy { get; init; }
    public bool CanExecute { get; init; }
    public bool CanRetry { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanRollback { get; init; }
    public bool CanMerge { get; init; }
    public bool CanSourceApply { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanPublishPackages { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanDispatchPipeline { get; init; }
    public bool CanMutate { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanMutateEnvironment { get; init; }

    public static MemoryNonAuthorityBoundary Context { get; } = new();
    public static MemoryNonAuthorityBoundary ReadOnly { get; } = new();
}

public sealed record MemoryNonAuthorityFinding
{
    public required string DecisionId { get; init; }
    public required string AttemptId { get; init; }
    public required string Severity { get; init; }
    public required string Flag { get; init; }
    public required string Message { get; init; }
}

public sealed record MemoryNonAuthorityArtifacts
{
    public required MemoryNonAuthorityDecision[] Decisions { get; init; }
    public required MemoryNonAuthoritySummary Summary { get; init; }
    public required MemoryNonAuthorityFinding[] RedFindings { get; init; }
    public required MemoryNonAuthorityFinding[] AmberFindings { get; init; }
    public required string MarkdownReport { get; init; }
    public MemoryNonAuthorityBoundary Boundary { get; init; } = MemoryNonAuthorityBoundary.Context;
}

public static class MemoryNonAuthorityReportBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions JsonLineOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] KnownSourceKinds =
    [
        "AcceptedMemoryClaim",
        "ThoughtLedgerText",
        "PriorRunSummary",
        "PriorReceipt",
        "WorkflowHistory",
        "PortableEngineeringMemory"
    ];

    public static MemoryNonAuthorityArtifacts EvaluateScenarioSet(
        string scenarioSet,
        string reportId,
        DateTimeOffset? createdAtUtc = null)
    {
        var attempts = MemoryNonAuthorityScenarioCatalog.Get(scenarioSet);
        return EvaluateAttempts(reportId, createdAtUtc ?? DateTimeOffset.UtcNow, attempts);
    }

    public static MemoryNonAuthorityArtifacts EvaluateAttemptsFromJsonl(
        string attemptsPath,
        string reportId,
        DateTimeOffset? createdAtUtc = null) =>
        EvaluateAttempts(reportId, createdAtUtc ?? DateTimeOffset.UtcNow, ReadAttempts(attemptsPath));

    public static MemoryNonAuthorityArtifacts EvaluateAttempts(
        string reportId,
        DateTimeOffset createdAtUtc,
        IEnumerable<MemoryAuthorityUseAttempt> attempts)
    {
        var decisions = attempts.Select(EvaluateAttempt).ToArray();
        var redFindings = BuildFindings(decisions, "Red").ToArray();
        var amberFindings = BuildFindings(decisions, "Amber").ToArray();
        var summary = Summarize(reportId, createdAtUtc, decisions, redFindings, amberFindings);

        return new MemoryNonAuthorityArtifacts
        {
            Decisions = decisions,
            Summary = summary,
            RedFindings = redFindings,
            AmberFindings = amberFindings,
            MarkdownReport = RenderReport(summary, decisions, redFindings, amberFindings),
            Boundary = MemoryNonAuthorityBoundary.Context
        };
    }

    public static MemoryNonAuthorityDecision EvaluateAttempt(MemoryAuthorityUseAttempt attempt)
    {
        var verdict = ResolveVerdict(attempt);
        var blockReason = ResolveBlockReason(attempt);
        var notes = new List<string>
        {
            "Memory may explain context.",
            "Memory must not authorize action.",
            "Claim payload is represented by sanitized phrase and hash only."
        };
        if (attempt.ClaimedAuthorityPhrase.Contains("RAW_MEMORY_PAYLOAD", StringComparison.OrdinalIgnoreCase))
            notes.Add("RawMemoryPayloadOmitted");

        var decision = new MemoryNonAuthorityDecision
        {
            DecisionId = $"memory-non-authority-{attempt.AttemptId}",
            AttemptId = attempt.AttemptId,
            Verdict = verdict,
            BlockReason = blockReason,
            RequiredAuthorityType = attempt.RequiredAuthorityType,
            SuppliedAuthorityType = attempt.SuppliedAuthorityType,
            MemoryAllowedAsContext = attempt.MemoryUsedAsContext,
            MemoryAcceptedAsAuthority = false,
            ApprovalSatisfied = false,
            PolicySatisfied = false,
            ExecutionAuthorized = false,
            MutationAuthorized = false,
            WorkflowContinuationAuthorized = false,
            MemoryPromotionAuthorized = false,
            StaleAuthorityRefreshed = false,
            CrossProjectAuthorityAccepted = false,
            CrossRepositoryAuthorityAccepted = false,
            HumanSummary = string.Empty,
            SafeNextStep = SafeNextStepFor(attempt, blockReason),
            DecisionGrantedAuthority = false,
            DecisionMutatedState = false,
            Notes = notes.ToArray()
        };

        return RecalculateFlags(decision with
        {
            HumanSummary = BuildHumanSummary(attempt, decision)
        });
    }

    public static MemoryNonAuthorityDecision RecalculateFlags(MemoryNonAuthorityDecision decision) =>
        decision with
        {
            RedFlags = BuildRedFlags(decision).ToArray(),
            AmberFlags = BuildAmberFlags(decision).ToArray()
        };

    public static MemoryNonAuthoritySummary Summarize(
        string reportId,
        DateTimeOffset createdAtUtc,
        IReadOnlyCollection<MemoryNonAuthorityDecision> decisions,
        IReadOnlyCollection<MemoryNonAuthorityFinding>? redFindings = null,
        IReadOnlyCollection<MemoryNonAuthorityFinding>? amberFindings = null)
    {
        var red = redFindings ?? BuildFindings(decisions, "Red").ToArray();
        var amber = amberFindings ?? BuildFindings(decisions, "Amber").ToArray();
        return new MemoryNonAuthoritySummary
        {
            ReportId = reportId,
            CreatedAtUtc = createdAtUtc,
            TotalAttempts = decisions.Count,
            AllowedAsContextCount = decisions.Count(item => string.Equals(item.Verdict, "AllowedAsContext", StringComparison.OrdinalIgnoreCase)),
            BlockedAsAuthorityCount = decisions.Count(item => !string.Equals(item.Verdict, "AllowedAsContext", StringComparison.OrdinalIgnoreCase)),
            MemoryAcceptedAsAuthorityCount = decisions.Count(item => item.MemoryAcceptedAsAuthority),
            ApprovalSatisfiedByMemoryCount = decisions.Count(item => item.ApprovalSatisfied),
            PolicySatisfiedByMemoryCount = decisions.Count(item => item.PolicySatisfied),
            ExecutionAuthorizedByMemoryCount = decisions.Count(item => item.ExecutionAuthorized),
            MutationAuthorizedByMemoryCount = decisions.Count(item => item.MutationAuthorized),
            WorkflowContinuationAuthorizedByMemoryCount = decisions.Count(item => item.WorkflowContinuationAuthorized),
            MemoryPromotionAuthorizedByMemoryCount = decisions.Count(item => item.MemoryPromotionAuthorized),
            StaleAuthorityRefreshCount = decisions.Count(item => item.StaleAuthorityRefreshed),
            CrossProjectAuthorityAcceptedCount = decisions.Count(item => item.CrossProjectAuthorityAccepted),
            CrossRepositoryAuthorityAcceptedCount = decisions.Count(item => item.CrossRepositoryAuthorityAccepted),
            MissingSafeNextStepCount = decisions.Count(item => string.IsNullOrWhiteSpace(item.SafeNextStep)),
            GenericBlockReasonCount = decisions.Count(item => IsGenericReason(item.BlockReason)),
            ReportPassed = red.Count == 0,
            RedFindings = red.Select(item => item.Message).ToArray(),
            AmberFindings = amber.Select(item => item.Message).ToArray(),
            GreenFindings = decisions
                .Where(item => item.RedFlags.Length == 0)
                .Select(item => $"{item.AttemptId}: memory stayed context-only and granted no authority.")
                .ToArray()
        };
    }

    public static string ToDecisionJsonl(IEnumerable<MemoryNonAuthorityDecision> decisions) =>
        ToJsonl(decisions);

    public static string ToSummaryJson(MemoryNonAuthoritySummary summary) =>
        JsonSerializer.Serialize(summary, JsonOptions);

    public static string ToRedFindingsJsonl(IEnumerable<MemoryNonAuthorityFinding> findings) =>
        ToJsonl(findings);

    public static string ToAmberFindingsJsonl(IEnumerable<MemoryNonAuthorityFinding> findings) =>
        ToJsonl(findings);

    public static MemoryAuthorityUseAttempt[] ReadAttempts(string attemptsPath)
    {
        var path = Path.GetFullPath(attemptsPath);
        if (!File.Exists(path))
            throw new FileNotFoundException("memory authority attempts JSONL was not found.", path);

        var attempts = new List<MemoryAuthorityUseAttempt>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var attempt = JsonSerializer.Deserialize<MemoryAuthorityUseAttempt>(line, JsonLineOptions);
            if (attempt is null)
                throw new InvalidDataException("A memory authority attempt row could not be read.");
            attempts.Add(attempt);
        }

        return attempts.ToArray();
    }

    private static string ResolveVerdict(MemoryAuthorityUseAttempt attempt)
    {
        if (!KnownSourceKinds.Contains(attempt.SourceKind, StringComparer.OrdinalIgnoreCase))
            return "BlockedUnsupportedMemorySource";
        if (!attempt.MemoryPresentedAsAuthority)
            return "AllowedAsContext";
        if (IsStaleRefresh(attempt))
            return "BlockedStaleAuthorityRefresh";
        if (attempt.CrossProject)
            return "BlockedCrossProjectAuthority";
        if (attempt.CrossRepository)
            return "BlockedCrossRepositoryAuthority";
        if (attempt.SourceKind.Contains("ThoughtLedger", StringComparison.OrdinalIgnoreCase))
            return "BlockedMemoryAsApproval";
        if (attempt.ContainsApprovalLanguage || ContainsAny(attempt.RequiredAuthorityType, "approval", "conscience", "decision record"))
            return "BlockedMemoryAsApproval";
        if (attempt.ContainsPolicySatisfactionLanguage || ContainsAny(attempt.RequiredAuthorityType, "policy"))
            return "BlockedMemoryAsPolicySatisfaction";
        if (attempt.ContainsWorkflowContinuationLanguage || ContainsAny(attempt.RequestedActionType, "workflow", "continue"))
            return "BlockedMemoryAsWorkflowContinuation";
        if (attempt.ContainsMemoryPromotionLanguage || ContainsAny(attempt.RequestedActionType, "memory promotion", "promote memory"))
            return "BlockedMemoryAsPromotionAuthority";
        if (attempt.ContainsMutationLanguage || ContainsAny(attempt.RequestedActionType, "source apply", "mutation", "mutate"))
            return "BlockedMemoryAsMutationAuthority";

        return "BlockedMemoryAsExecutionRequest";
    }

    private static string ResolveBlockReason(MemoryAuthorityUseAttempt attempt)
    {
        if (!KnownSourceKinds.Contains(attempt.SourceKind, StringComparer.OrdinalIgnoreCase))
            return "UnsupportedMemorySource";
        if (!attempt.MemoryPresentedAsAuthority)
            return "MemoryAllowedAsPlanningContextOnly";
        if (IsStaleRefresh(attempt))
            return "MemoryCannotRefreshStaleAuthority";
        if (attempt.CrossProject)
            return "CrossProjectMemoryCannotAuthorizeCurrentProject";
        if (attempt.CrossRepository)
            return "CrossRepositoryMemoryCannotAuthorizeCurrentRepository";
        if (attempt.SourceKind.Contains("ThoughtLedger", StringComparison.OrdinalIgnoreCase))
            return "ThoughtLedgerTextIsNotApproval";
        if (attempt.SourceKind.Contains("PriorRunSummary", StringComparison.OrdinalIgnoreCase))
            return "PriorRunSummaryIsNotAuthority";
        if (attempt.ContainsApprovalLanguage || ContainsAny(attempt.RequiredAuthorityType, "approval", "conscience", "decision record"))
            return "MemoryIsContextNotApproval";
        if (attempt.ContainsPolicySatisfactionLanguage || ContainsAny(attempt.RequiredAuthorityType, "policy"))
            return "MemoryIsContextNotPolicySatisfaction";
        if (ContainsAny(attempt.RequestedActionType, "release"))
            return "MemoryIsContextNotReleaseAuthority";
        if (ContainsAny(attempt.RequestedActionType, "deployment", "deploy"))
            return "MemoryIsContextNotDeploymentAuthority";
        if (ContainsAny(attempt.RequestedActionType, "rollback"))
            return "MemoryIsContextNotRollbackAuthority";
        if (attempt.ContainsWorkflowContinuationLanguage || ContainsAny(attempt.RequestedActionType, "workflow", "continue"))
            return "MemoryIsContextNotWorkflowContinuation";
        if (attempt.ContainsMemoryPromotionLanguage || ContainsAny(attempt.RequestedActionType, "memory promotion", "promote memory"))
            return "MemoryIsContextNotPromotionAuthority";
        if (attempt.ContainsMutationLanguage || ContainsAny(attempt.RequestedActionType, "source apply", "mutation", "mutate"))
            return "MemoryIsContextNotMutationAuthority";
        if (attempt.SourceKind.Contains("PriorReceipt", StringComparison.OrdinalIgnoreCase))
            return "PriorReceiptIsNotCurrentAuthority";

        return "MemoryIsContextNotExecutionRequest";
    }

    private static string SafeNextStepFor(MemoryAuthorityUseAttempt attempt, string blockReason)
    {
        if (!attempt.MemoryPresentedAsAuthority)
            return "Use memory as planning context only; create current authority before any governed action.";
        if (blockReason.Contains("StaleAuthority", StringComparison.OrdinalIgnoreCase))
            return "Rebuild the authority package for the current observed state.";
        if (blockReason.Contains("CrossProject", StringComparison.OrdinalIgnoreCase) ||
            blockReason.Contains("CrossRepository", StringComparison.OrdinalIgnoreCase))
            return "Recreate authority inside the current project and repository boundary.";
        if (blockReason.Contains("ThoughtLedger", StringComparison.OrdinalIgnoreCase))
            return "Create a fresh governed decision record for the current action.";
        if (blockReason.Contains("PriorRunSummary", StringComparison.OrdinalIgnoreCase) ||
            blockReason.Contains("PriorReceipt", StringComparison.OrdinalIgnoreCase))
            return "Build current authority evidence bound to this task and observed state.";

        return $"Request current {attempt.RequiredAuthorityType} for this task.";
    }

    private static string BuildHumanSummary(MemoryAuthorityUseAttempt attempt, MemoryNonAuthorityDecision decision)
    {
        var disposition = string.Equals(decision.Verdict, "AllowedAsContext", StringComparison.OrdinalIgnoreCase)
            ? "Allowed as context"
            : "Blocked";
        return $"{disposition}: supplied {attempt.SuppliedAuthorityType} for {attempt.RequestedActionType}. " +
            $"Required authority: {attempt.RequiredAuthorityType}. " +
            $"Reason: {HumanizeReason(decision.BlockReason)}. " +
            "No mutation happened. " +
            $"Safe next step: {decision.SafeNextStep}";
    }

    private static IEnumerable<string> BuildRedFlags(MemoryNonAuthorityDecision decision)
    {
        if (decision.MemoryAcceptedAsAuthority)
            yield return "MemoryAcceptedAsAuthority";
        if (decision.ApprovalSatisfied)
            yield return "ApprovalSatisfiedByMemory";
        if (decision.PolicySatisfied)
            yield return "PolicySatisfiedByMemory";
        if (decision.ExecutionAuthorized)
            yield return "ExecutionAuthorizedByMemory";
        if (decision.MutationAuthorized)
            yield return "MutationAuthorizedByMemory";
        if (decision.WorkflowContinuationAuthorized)
            yield return "WorkflowContinuationAuthorizedByMemory";
        if (decision.MemoryPromotionAuthorized)
            yield return "MemoryPromotionAuthorizedByMemory";
        if (decision.StaleAuthorityRefreshed)
            yield return "StaleAuthorityRefreshedByMemory";
        if (decision.CrossProjectAuthorityAccepted)
            yield return "CrossProjectAuthorityAccepted";
        if (decision.CrossRepositoryAuthorityAccepted)
            yield return "CrossRepositoryAuthorityAccepted";
        if (decision.DecisionGrantedAuthority)
            yield return "DecisionGrantedAuthority";
        if (decision.DecisionMutatedState)
            yield return "DecisionMutatedState";
        if (IsUnsafeNextStep(decision.SafeNextStep))
            yield return "UnsafeNextStepWouldMutate";
        if (decision.Notes.Contains("RawMemoryPayloadOmitted", StringComparer.OrdinalIgnoreCase))
            yield return "RawMemoryPayloadDetected";
    }

    private static IEnumerable<string> BuildAmberFlags(MemoryNonAuthorityDecision decision)
    {
        if (IsGenericReason(decision.BlockReason))
            yield return "GenericBlockReason";
        if (string.IsNullOrWhiteSpace(decision.SafeNextStep))
            yield return "MissingSafeNextStep";
        if (string.Equals(decision.Verdict, "BlockedUnsupportedMemorySource", StringComparison.OrdinalIgnoreCase))
            yield return "UnsupportedMemorySource";
        if (decision.HumanSummary.Contains("unknown scope", StringComparison.OrdinalIgnoreCase))
            yield return "UnknownMemoryScope";
        if (decision.HumanSummary.Contains("unknown kind", StringComparison.OrdinalIgnoreCase))
            yield return "UnknownMemoryKind";
        if (string.Equals(decision.BlockReason, "MemoryAllowedAsPlanningContextOnly", StringComparison.OrdinalIgnoreCase))
            yield return "UnclassifiedClaimLanguage";
        if (decision.BlockReason.Contains("CrossProject", StringComparison.OrdinalIgnoreCase))
            yield return "CrossProjectContextRequiresSanitization";
        if (decision.SuppliedAuthorityType.Contains("PortableEngineeringMemory", StringComparison.OrdinalIgnoreCase) ||
            decision.BlockReason.Contains("Portable", StringComparison.OrdinalIgnoreCase))
            yield return "PortableMemoryUsedNearProjectAuthority";
    }

    private static IEnumerable<MemoryNonAuthorityFinding> BuildFindings(
        IEnumerable<MemoryNonAuthorityDecision> decisions,
        string severity)
    {
        foreach (var decision in decisions)
        {
            var flags = string.Equals(severity, "Red", StringComparison.OrdinalIgnoreCase)
                ? decision.RedFlags
                : decision.AmberFlags;
            foreach (var flag in flags)
            {
                yield return new MemoryNonAuthorityFinding
                {
                    DecisionId = decision.DecisionId,
                    AttemptId = decision.AttemptId,
                    Severity = severity,
                    Flag = flag,
                    Message = $"{decision.AttemptId}: {flag}"
                };
            }
        }
    }

    private static string RenderReport(
        MemoryNonAuthoritySummary summary,
        IReadOnlyCollection<MemoryNonAuthorityDecision> decisions,
        IReadOnlyCollection<MemoryNonAuthorityFinding> redFindings,
        IReadOnlyCollection<MemoryNonAuthorityFinding> amberFindings)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Memory Non-Authority Hardening");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine($"Report: `{summary.ReportId}`");
        builder.AppendLine($"Attempts: `{summary.TotalAttempts}`");
        builder.AppendLine($"Allowed as context: `{summary.AllowedAsContextCount}`");
        builder.AppendLine($"Blocked as authority: `{summary.BlockedAsAuthorityCount}`");
        builder.AppendLine($"Report passed: `{summary.ReportPassed.ToString().ToLowerInvariant()}`");
        builder.AppendLine();
        builder.AppendLine("## Red Findings");
        builder.AppendLine();
        builder.AppendLine(Bullets(redFindings.Select(item => item.Message)));
        builder.AppendLine();
        builder.AppendLine("## Amber Findings");
        builder.AppendLine();
        builder.AppendLine(Bullets(amberFindings.Select(item => item.Message)));
        builder.AppendLine();
        builder.AppendLine("## Decisions");
        builder.AppendLine();
        builder.AppendLine(Bullets(decisions.Select(item => item.HumanSummary)));
        builder.AppendLine();
        builder.AppendLine("## Boundary");
        builder.AppendLine();
        builder.AppendLine("Memory may explain context.");
        builder.AppendLine("Memory must not authorize action.");
        builder.AppendLine("BI does not promote memory.");
        builder.AppendLine("BI does not approve, satisfy policy, execute, retry, release, deploy, rollback, source-apply, mutate, dispatch, or continue workflow.");
        builder.AppendLine("Portable engineering memory may carry sanitized lessons but must not carry project authority.");
        return builder.ToString();
    }

    private static bool IsStaleRefresh(MemoryAuthorityUseAttempt attempt) =>
        attempt.ClaimedAuthorityPhrase.Contains("refresh stale", StringComparison.OrdinalIgnoreCase) ||
        attempt.ClaimedAuthorityPhrase.Contains("old commit", StringComparison.OrdinalIgnoreCase) ||
        attempt.RequestedActionType.Contains("stale", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, params string[] patterns) =>
        patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private static bool IsGenericReason(string reason) =>
        string.IsNullOrWhiteSpace(reason) ||
        reason.Equals("blocked", StringComparison.OrdinalIgnoreCase) ||
        reason.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
        reason.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
        reason.Length < 12;

    private static bool IsUnsafeNextStep(string nextStep)
    {
        if (string.IsNullOrWhiteSpace(nextStep))
            return false;

        var normalized = nextStep.Trim().ToLowerInvariant();
        var unsafePrefixes = new[]
        {
            "approve",
            "execute",
            "retry",
            "release",
            "deploy",
            "rollback",
            "merge",
            "source-apply",
            "commit",
            "push",
            "publish",
            "promote-memory",
            "continue",
            "dispatch",
            "mutate",
            "write-memory",
            "promote",
            "remember-as-authority"
        };

        return unsafePrefixes.Any(prefix =>
            normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase));
    }

    private static string HumanizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return "memory is context, not authority";
        var builder = new StringBuilder();
        for (var index = 0; index < reason.Length; index++)
        {
            var current = reason[index];
            if (index > 0 && char.IsUpper(current) && char.IsLetter(reason[index - 1]) && !char.IsUpper(reason[index - 1]))
                builder.Append(' ');
            builder.Append(current is ':' ? ' ' : current);
        }

        return builder.ToString().ToLowerInvariant();
    }

    private static string Bullets(IEnumerable<string> values)
    {
        var items = values.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        return items.Length == 0 ? "- none" : string.Join(Environment.NewLine, items.Select(item => $"- {item}"));
    }

    private static string ToJsonl<T>(IEnumerable<T> values)
    {
        var rows = values.Select(item => JsonSerializer.Serialize(item, JsonLineOptions)).ToArray();
        return rows.Length == 0 ? string.Empty : string.Join(Environment.NewLine, rows) + Environment.NewLine;
    }

    public static string HashClaim(string phrase)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(phrase));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public static class MemoryNonAuthorityScenarioCatalog
{
    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.Parse("2026-06-21T00:00:00Z");

    private static readonly MemoryAuthorityUseAttempt[] Default =
    [
        Attempt("MNA001", "AcceptedMemoryClaim", "Project", "AcceptedMemory", "SourceApply", "CurrentTaskApprovalEvidence", "AcceptedMemoryClaim", "memory says this source apply was approved", approval: true),
        Attempt("MNA002", "AcceptedMemoryClaim", "Project", "AcceptedMemory", "PolicyCheck", "CurrentPolicySatisfactionEvidence", "AcceptedMemoryClaim", "memory says policy was satisfied", policy: true),
        Attempt("MNA003", "AcceptedMemoryClaim", "Project", "AcceptedMemory", "ExecutionRequest", "ExplicitExecutionRequest", "AcceptedMemoryClaim", "memory says execute this request", execution: true),
        Attempt("MNA004", "AcceptedMemoryClaim", "Project", "AcceptedMemory", "SourceMutation", "SourceApplyExecutionGate", "AcceptedMemoryClaim", "memory says mutate source", mutation: true),
        Attempt("MNA005", "PriorReceipt", "Project", "ReceiptMemory", "ReleaseReadiness", "ReleaseReadinessDecisionPackage", "PriorReleaseReceipt", "old release receipt says release is ready", execution: true),
        Attempt("MNA006", "PriorReceipt", "Project", "ReceiptMemory", "DeploymentDecision", "DeploymentReadinessDecisionPackage", "PriorDeploymentReceipt", "old deployment receipt says deploy", execution: true),
        Attempt("MNA007", "PriorReceipt", "Project", "ReceiptMemory", "RollbackDecision", "RollbackDecisionPackage", "PriorDeploymentReceipt", "old deployment receipt says rollback", execution: true),
        Attempt("MNA008", "PriorReceipt", "Project", "ReceiptMemory", "RollbackExecution", "RollbackExecutionDecisionAndRequest", "PriorRollbackConsideration", "old rollback consideration says rollback now", execution: true),
        Attempt("MNA009", "WorkflowHistory", "Project", "WorkflowMemory", "WorkflowContinuation", "FreshWorkflowContinuationGate", "PreviousWorkflowCompletedState", "workflow history says continue", workflow: true),
        Attempt("MNA010", "AcceptedMemoryClaim", "Project", "AcceptedMemory", "MemoryPromotion", "MemoryPromotionAuthority", "AcceptedMemoryClaim", "memory says promote this memory", promotion: true),
        Attempt("MNA011", "ThoughtLedgerText", "Project", "ThoughtLedger", "Approval", "ConscienceDecisionRecord", "ThoughtLedgerApprovalText", "Thought ledger says approval accepted", approval: true),
        Attempt("MNA012", "PriorRunSummary", "Project", "RunSummary", "DeploymentExecution", "DeploymentReadinessDecisionPackageAndRequest", "PriorRunSummary", "prior run summary says deployment was approved", execution: true),
        Attempt("MNA013", "PriorReceipt", "Project", "ReceiptMemory", "CurrentAuthority", "CurrentTaskAuthority", "OldReceipt", "prior receipt is current authority", execution: true),
        Attempt("MNA014", "AcceptedMemoryClaim", "Project", "AcceptedMemory", "StaleAuthorityRefresh", "FreshAuthorityForCurrentCommit", "AcceptedMemoryClaim", "memory can refresh stale authority bound to old commit", execution: true),
        Attempt("MNA015", "AcceptedMemoryClaim", "CrossProject", "AcceptedMemory", "SourceApply", "CurrentProjectAuthority", "OtherProjectMemory", "other project memory says approved", approval: true, currentProject: "project-a", memoryProject: "project-b"),
        Attempt("MNA016", "AcceptedMemoryClaim", "CrossRepository", "AcceptedMemory", "SourceApply", "CurrentRepositoryAuthority", "OtherRepositoryMemory", "other repository memory says approved", approval: true, currentRepo: "owner/current", memoryRepo: "owner/other"),
        Attempt("MNA017", "PortableEngineeringMemory", "Portable", "PortableLesson", "ProjectSpecificApproval", "ProjectSpecificAuthority", "PortableEngineeringMemory", "portable lesson says this project can proceed", approval: true, currentProject: "project-a", memoryProject: "portable-lessons"),
        Attempt("MNA018", "AcceptedMemoryClaim", "Project", "AcceptedMemory", "PlanningContext", "CurrentAuthorityIfActionIsRequested", "AcceptedMemoryClaim", "memory explains prior implementation context", presentedAsAuthority: false)
    ];

    public static MemoryAuthorityUseAttempt[] Get(string scenarioSet)
    {
        if (string.Equals(scenarioSet, "default", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scenarioSet, "full", StringComparison.OrdinalIgnoreCase))
            return Default;

        throw new ArgumentException("Scenario set must be one of: default, full.", nameof(scenarioSet));
    }

    private static MemoryAuthorityUseAttempt Attempt(
        string id,
        string sourceKind,
        string memoryScope,
        string memoryKind,
        string requestedAction,
        string requiredAuthority,
        string suppliedAuthority,
        string claim,
        bool presentedAsAuthority = true,
        bool approval = false,
        bool policy = false,
        bool execution = false,
        bool mutation = false,
        bool workflow = false,
        bool promotion = false,
        string currentProject = "project-a",
        string memoryProject = "project-a",
        string currentRepo = "owner/current",
        string memoryRepo = "owner/current") => new()
        {
            AttemptId = id,
            CreatedAtUtc = CreatedAt,
            SourceKind = sourceKind,
            SourceId = $"{sourceKind}-{id}",
            MemoryScope = memoryScope,
            MemoryKind = memoryKind,
            CurrentProjectId = currentProject,
            CurrentRepository = currentRepo,
            MemoryProjectId = memoryProject,
            MemoryRepository = memoryRepo,
            RequestedActionType = requestedAction,
            RequiredAuthorityType = requiredAuthority,
            SuppliedAuthorityType = suppliedAuthority,
            ClaimedAuthorityPhrase = claim,
            ClaimHash = MemoryNonAuthorityReportBuilder.HashClaim(claim),
            MemoryUsedAsContext = true,
            MemoryPresentedAsAuthority = presentedAsAuthority,
            CrossProject = !string.Equals(currentProject, memoryProject, StringComparison.OrdinalIgnoreCase),
            CrossRepository = !string.Equals(currentRepo, memoryRepo, StringComparison.OrdinalIgnoreCase),
            ContainsApprovalLanguage = approval,
            ContainsPolicySatisfactionLanguage = policy,
            ContainsExecutionLanguage = execution,
            ContainsMutationLanguage = mutation,
            ContainsWorkflowContinuationLanguage = workflow,
            ContainsMemoryPromotionLanguage = promotion
        };
}
