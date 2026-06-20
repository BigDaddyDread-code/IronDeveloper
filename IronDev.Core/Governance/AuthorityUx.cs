using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronDev.Core.Governance;

public sealed record AuthorityUxExplanation
{
    public required string ExplanationId { get; init; }
    public required string SourceKind { get; init; }
    public required string SourceId { get; init; }

    public required string SourceVerdict { get; init; }
    public required string SourceBlockReason { get; init; }
    public required string BlockReasonCategory { get; init; }

    public required string PreviousTaskType { get; init; }
    public required string NewTaskType { get; init; }
    public required string BoundaryUnderTest { get; init; }

    public required string SuppliedAuthorityType { get; init; }
    public required string RequiredAuthorityType { get; init; }
    public required string AuthorityRelationship { get; init; }

    public required bool MutationAttempted { get; init; }
    public required bool MutationCompleted { get; init; }

    public required bool OldAuthorityUsedAsContext { get; init; }
    public required bool OldAuthorityUsedAsPermission { get; init; }

    public required bool MemoryUsedAsContext { get; init; }
    public required bool MemoryUsedAsPermission { get; init; }

    public required bool WorkflowStateTransferred { get; init; }

    public required bool HumanReadableReason { get; init; }
    public required bool HumanCouldChooseNextStep { get; init; }

    public required string HumanSummary { get; init; }
    public required string SafeNextStep { get; init; }

    public required bool ExplanationChangedVerdict { get; init; }
    public required bool ExplanationGrantedAuthority { get; init; }

    public string[] RedFlags { get; init; } = [];
    public string[] AmberFlags { get; init; } = [];
    public string[] Notes { get; init; } = [];
}

public sealed record AuthorityUxSummary
{
    public required string ReportId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required int TotalExplanations { get; init; }
    public required int BlockedCount { get; init; }
    public required int NeedsAuthorityCount { get; init; }
    public required int SuccessCount { get; init; }

    public required int MutationCompletedCount { get; init; }
    public required int OldAuthorityPermissionLeakCount { get; init; }
    public required int MemoryPermissionLeakCount { get; init; }
    public required int WorkflowTransferCount { get; init; }

    public required int GenericReasonCount { get; init; }
    public required int MissingSafeNextStepCount { get; init; }
    public required int HumanCannotChooseNextStepCount { get; init; }

    public required int ExplanationChangedVerdictCount { get; init; }
    public required int ExplanationGrantedAuthorityCount { get; init; }

    public required bool ReportPassed { get; init; }

    public string[] RedFindings { get; init; } = [];
    public string[] AmberFindings { get; init; } = [];
    public string[] GreenFindings { get; init; } = [];
}

public sealed record AuthorityUxBoundary
{
    public bool EvidenceOnly { get; init; } = true;
    public bool ExplanationOnly { get; init; } = true;

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

    public static AuthorityUxBoundary Explanation { get; } = new();
    public static AuthorityUxBoundary ReadOnly { get; } = new();
}

public sealed record AuthorityUxFinding
{
    public required string ExplanationId { get; init; }
    public required string SourceId { get; init; }
    public required string Severity { get; init; }
    public required string Flag { get; init; }
    public required string Message { get; init; }
}

public sealed record AuthorityUxArtifacts
{
    public required AuthorityUxExplanation[] Explanations { get; init; }
    public required AuthorityUxSummary Summary { get; init; }
    public required AuthorityUxFinding[] RedFindings { get; init; }
    public required AuthorityUxFinding[] AmberFindings { get; init; }
    public required string MarkdownReport { get; init; }
    public AuthorityUxBoundary Boundary { get; init; } = AuthorityUxBoundary.Explanation;
}

public static class AuthorityUxReportBuilder
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

    private static readonly string[] KnownAuthorityRelationships =
    [
        "WrongType",
        "Stale",
        "ContextOnly",
        "Unrelated",
        "Insufficient",
        "Missing",
        "BoundaryViolation",
        "Unknown"
    ];

    public static AuthorityUxArtifacts BuildFromCampaignDirectory(
        string campaignDirectory,
        string? reportId = null,
        DateTimeOffset? createdAtUtc = null)
    {
        var scenarios = ReadTaskSwitchScenarios(campaignDirectory);
        return BuildFromTaskSwitchScenarios(
            reportId ?? new DirectoryInfo(Path.GetFullPath(campaignDirectory)).Name,
            createdAtUtc ?? DateTimeOffset.UtcNow,
            scenarios);
    }

    public static AuthorityUxArtifacts BuildFromTaskSwitchScenarios(
        string reportId,
        DateTimeOffset createdAtUtc,
        IEnumerable<TaskSwitchBoundaryScenarioResult> scenarios)
    {
        var explanations = scenarios.Select(ExplainTaskSwitchScenario).ToArray();
        var summary = Summarize(reportId, createdAtUtc, explanations);
        var redFindings = BuildFindings(explanations, "Red").ToArray();
        var amberFindings = BuildFindings(explanations, "Amber").ToArray();

        return new AuthorityUxArtifacts
        {
            Explanations = explanations,
            Summary = summary,
            RedFindings = redFindings,
            AmberFindings = amberFindings,
            MarkdownReport = RenderReport(summary, explanations, redFindings, amberFindings),
            Boundary = AuthorityUxBoundary.Explanation
        };
    }

    public static AuthorityUxExplanation ExplainTaskSwitchScenario(TaskSwitchBoundaryScenarioResult scenario)
    {
        var relationship = NormalizeAuthorityRelationship(scenario.AuthorityRelationship);
        var blockCategory = ClassifyBlockReason(scenario.ActualBlockReason);
        var explanation = new AuthorityUxExplanation
        {
            ExplanationId = $"authority-ux-{scenario.CampaignId}-{scenario.ScenarioId}",
            SourceKind = nameof(TaskSwitchBoundaryScenarioResult),
            SourceId = scenario.ScenarioId,
            SourceVerdict = scenario.ActualVerdict,
            SourceBlockReason = scenario.ActualBlockReason,
            BlockReasonCategory = blockCategory,
            PreviousTaskType = scenario.PreviousTaskType,
            NewTaskType = scenario.NewTaskType,
            BoundaryUnderTest = scenario.BoundaryUnderTest,
            SuppliedAuthorityType = scenario.SuppliedAuthorityType,
            RequiredAuthorityType = scenario.ExpectedAuthorityType,
            AuthorityRelationship = relationship,
            MutationAttempted = scenario.MutationAttempted,
            MutationCompleted = scenario.MutationCompleted,
            OldAuthorityUsedAsContext = scenario.OldAuthorityUsedAsContext,
            OldAuthorityUsedAsPermission = scenario.OldAuthorityUsedAsPermission,
            MemoryUsedAsContext = scenario.MemoryUsedAsContext,
            MemoryUsedAsPermission = scenario.MemoryUsedAsPermission,
            WorkflowStateTransferred = scenario.WorkflowStateTransferred,
            HumanReadableReason = scenario.HumanReadableReason,
            HumanCouldChooseNextStep = scenario.HumanCouldChooseNextStep,
            HumanSummary = string.Empty,
            SafeNextStep = scenario.SafeNextStep,
            ExplanationChangedVerdict = false,
            ExplanationGrantedAuthority = false,
            Notes =
            [
                "Explanation is not permission.",
                "Interpretability is not authority.",
                "Safe next step is not execution."
            ]
        };

        return explanation with
        {
            HumanSummary = BuildHumanSummary(explanation),
            RedFlags = BuildRedFlags(explanation).ToArray(),
            AmberFlags = BuildAmberFlags(explanation, scenario.ManualSteps, scenario.JsonFilesInspected, scenario.ReceiptNoiseScore).ToArray()
        };
    }

    public static AuthorityUxExplanation ExplainUnsupportedReceipt(string sourceKind, string sourceId) =>
        BuildUnsupportedReceipt(sourceKind, sourceId) with
        {
            HumanSummary = "Authority state could not be interpreted safely. No mutation should proceed from this explanation.",
            RedFlags = [],
            AmberFlags = ["UnsupportedReceiptKind", "UnknownAuthorityRelationship", "UnclassifiedBlockReason"]
        };

    public static AuthorityUxExplanation RecalculateFlags(AuthorityUxExplanation explanation) =>
        explanation with
        {
            RedFlags = BuildRedFlags(explanation).ToArray(),
            AmberFlags = BuildAmberFlags(explanation, manualSteps: 0, jsonFilesInspected: 0, receiptNoiseScore: 0).ToArray()
        };

    public static AuthorityUxSummary Summarize(
        string reportId,
        DateTimeOffset createdAtUtc,
        IReadOnlyCollection<AuthorityUxExplanation> explanations)
    {
        var redFindings = BuildFindingMessages(explanations, "Red").ToArray();
        var amberFindings = BuildFindingMessages(explanations, "Amber").ToArray();
        return new AuthorityUxSummary
        {
            ReportId = reportId,
            CreatedAtUtc = createdAtUtc,
            TotalExplanations = explanations.Count,
            BlockedCount = explanations.Count(item => IsBlocked(item.SourceVerdict)),
            NeedsAuthorityCount = explanations.Count(item => IsNeedsAuthority(item.SourceVerdict)),
            SuccessCount = explanations.Count(item => IsSuccess(item.SourceVerdict)),
            MutationCompletedCount = explanations.Count(item => item.MutationCompleted),
            OldAuthorityPermissionLeakCount = explanations.Count(item => item.OldAuthorityUsedAsPermission),
            MemoryPermissionLeakCount = explanations.Count(item => item.MemoryUsedAsPermission),
            WorkflowTransferCount = explanations.Count(item => item.WorkflowStateTransferred),
            GenericReasonCount = explanations.Count(item => item.AmberFlags.Contains("GenericBlockReason", StringComparer.OrdinalIgnoreCase)),
            MissingSafeNextStepCount = explanations.Count(item => string.IsNullOrWhiteSpace(item.SafeNextStep)),
            HumanCannotChooseNextStepCount = explanations.Count(item => !item.HumanCouldChooseNextStep),
            ExplanationChangedVerdictCount = explanations.Count(item => item.ExplanationChangedVerdict),
            ExplanationGrantedAuthorityCount = explanations.Count(item => item.ExplanationGrantedAuthority),
            ReportPassed = explanations.All(item => item.RedFlags.Length == 0 && !item.ExplanationChangedVerdict && !item.ExplanationGrantedAuthority),
            RedFindings = redFindings,
            AmberFindings = amberFindings,
            GreenFindings = explanations
                .Where(item => item.RedFlags.Length == 0)
                .Select(item => $"{item.SourceId}: authority explanation preserved the original verdict and granted no authority.")
                .ToArray()
        };
    }

    public static string ToExplanationJsonl(IEnumerable<AuthorityUxExplanation> explanations) =>
        ToJsonl(explanations);

    public static string ToSummaryJson(AuthorityUxSummary summary) =>
        JsonSerializer.Serialize(summary, JsonOptions);

    public static string ToRedFindingsJsonl(IEnumerable<AuthorityUxFinding> findings) =>
        ToJsonl(findings);

    public static string ToAmberFindingsJsonl(IEnumerable<AuthorityUxFinding> findings) =>
        ToJsonl(findings);

    private static TaskSwitchBoundaryScenarioResult[] ReadTaskSwitchScenarios(string campaignDirectory)
    {
        var path = Path.Combine(Path.GetFullPath(campaignDirectory), "task-switch-boundary-scenarios.jsonl");
        if (!File.Exists(path))
            throw new FileNotFoundException("task-switch-boundary-scenarios.jsonl was not found.", path);

        var scenarios = new List<TaskSwitchBoundaryScenarioResult>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var scenario = JsonSerializer.Deserialize<TaskSwitchBoundaryScenarioResult>(line, JsonLineOptions);
            if (scenario is null)
                throw new InvalidDataException("A task-switch boundary scenario row could not be read.");
            scenarios.Add(scenario);
        }

        return scenarios.ToArray();
    }

    private static AuthorityUxExplanation BuildUnsupportedReceipt(string sourceKind, string sourceId) => new()
    {
        ExplanationId = $"authority-ux-unsupported-{sourceId}",
        SourceKind = sourceKind,
        SourceId = sourceId,
        SourceVerdict = "Blocked",
        SourceBlockReason = "UnsupportedReceiptKind",
        BlockReasonCategory = "UnsupportedReceiptKind",
        PreviousTaskType = "Unknown",
        NewTaskType = "Unknown",
        BoundaryUnderTest = "Unknown",
        SuppliedAuthorityType = sourceKind,
        RequiredAuthorityType = "KnownGovernedAuthority",
        AuthorityRelationship = "Unknown",
        MutationAttempted = false,
        MutationCompleted = false,
        OldAuthorityUsedAsContext = false,
        OldAuthorityUsedAsPermission = false,
        MemoryUsedAsContext = false,
        MemoryUsedAsPermission = false,
        WorkflowStateTransferred = false,
        HumanReadableReason = true,
        HumanCouldChooseNextStep = true,
        HumanSummary = string.Empty,
        SafeNextStep = "Interpret this receipt through a supported authority adapter before any governed action.",
        ExplanationChangedVerdict = false,
        ExplanationGrantedAuthority = false,
        Notes = ["Unknown receipt kinds stay blocked and cannot grant authority."]
    };

    private static IEnumerable<string> BuildRedFlags(AuthorityUxExplanation explanation)
    {
        if (explanation.MutationCompleted)
            yield return "MutationCompleted";
        if (explanation.OldAuthorityUsedAsPermission)
            yield return "OldAuthorityUsedAsPermission";
        if (explanation.MemoryUsedAsPermission)
            yield return "MemoryUsedAsPermission";
        if (explanation.WorkflowStateTransferred)
            yield return "WorkflowStateTransferred";
        if (explanation.ExplanationChangedVerdict)
            yield return "ExplanationChangedVerdict";
        if (explanation.ExplanationGrantedAuthority)
            yield return "ExplanationGrantedAuthority";
        if (explanation.SuppliedAuthorityType.Contains("RollbackConsideration", StringComparison.OrdinalIgnoreCase) &&
            explanation.RequiredAuthorityType.Contains("RollbackExecution", StringComparison.OrdinalIgnoreCase) &&
            IsSuccess(explanation.SourceVerdict))
            yield return "RollbackConsiderationTreatedAsExecution";
        if (IsSuccess(explanation.SourceVerdict) && IsAuthorityRelationshipUnsafe(explanation.AuthorityRelationship))
            yield return "SuccessVerdictFromBlockedAuthority";
        if (IsUnsafeNextStep(explanation.SafeNextStep))
            yield return "UnsafeNextStepWouldMutate";
    }

    private static IEnumerable<string> BuildAmberFlags(
        AuthorityUxExplanation explanation,
        int manualSteps,
        int jsonFilesInspected,
        int receiptNoiseScore)
    {
        if (TaskSwitchBoundaryCampaignRunner.IsGenericReason(explanation.SourceBlockReason))
            yield return "GenericBlockReason";
        if (string.IsNullOrWhiteSpace(explanation.SafeNextStep))
            yield return "MissingSafeNextStep";
        if (!explanation.HumanCouldChooseNextStep)
            yield return "HumanCannotChooseNextStep";
        if (string.Equals(explanation.BlockReasonCategory, "UnsupportedReceiptKind", StringComparison.OrdinalIgnoreCase))
            yield return "UnsupportedReceiptKind";
        if (string.Equals(explanation.AuthorityRelationship, "Unknown", StringComparison.OrdinalIgnoreCase))
            yield return "UnknownAuthorityRelationship";
        if (string.Equals(explanation.BlockReasonCategory, "GenericOrUnclear", StringComparison.OrdinalIgnoreCase))
            yield return "UnclassifiedBlockReason";
        if (receiptNoiseScore > 5)
            yield return "HighReceiptNoise";
        if (manualSteps > 5)
            yield return "HighManualSteps";
        if (jsonFilesInspected > 4)
            yield return "HighJsonInspectionLoad";
    }

    private static IEnumerable<AuthorityUxFinding> BuildFindings(
        IEnumerable<AuthorityUxExplanation> explanations,
        string severity)
    {
        foreach (var explanation in explanations)
        {
            var flags = string.Equals(severity, "Red", StringComparison.OrdinalIgnoreCase)
                ? explanation.RedFlags
                : explanation.AmberFlags;
            foreach (var flag in flags)
            {
                yield return new AuthorityUxFinding
                {
                    ExplanationId = explanation.ExplanationId,
                    SourceId = explanation.SourceId,
                    Severity = severity,
                    Flag = flag,
                    Message = $"{explanation.SourceId}: {flag}"
                };
            }
        }
    }

    private static IEnumerable<string> BuildFindingMessages(
        IEnumerable<AuthorityUxExplanation> explanations,
        string severity) =>
        BuildFindings(explanations, severity)
            .Select(item => item.Message);

    private static string RenderReport(
        AuthorityUxSummary summary,
        IReadOnlyCollection<AuthorityUxExplanation> explanations,
        IReadOnlyCollection<AuthorityUxFinding> redFindings,
        IReadOnlyCollection<AuthorityUxFinding> amberFindings)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Authority UX / Receipt Interpretability");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine($"Report: `{summary.ReportId}`");
        builder.AppendLine($"Explanations: `{summary.TotalExplanations}`");
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
        builder.AppendLine("## Explanations");
        builder.AppendLine();
        builder.AppendLine(Bullets(explanations.Select(item => item.HumanSummary)));
        builder.AppendLine();
        builder.AppendLine("## Boundary");
        builder.AppendLine();
        builder.AppendLine("Explanation is not permission.");
        builder.AppendLine("Interpretability is not authority.");
        builder.AppendLine("Safe next step is not execution.");
        builder.AppendLine("Memory may explain context but cannot authorize action.");
        builder.AppendLine("Workflow history may explain context but cannot continue action.");
        builder.AppendLine("Rollback consideration may explain risk but cannot execute rollback.");
        return builder.ToString();
    }

    private static string BuildHumanSummary(AuthorityUxExplanation explanation)
    {
        var verdict = IsSuccess(explanation.SourceVerdict)
            ? "Success"
            : IsNeedsAuthority(explanation.SourceVerdict)
                ? "Needs authority"
                : "Blocked";
        return $"{verdict}: supplied {explanation.SuppliedAuthorityType} for {explanation.NewTaskType}. " +
            $"Required authority: {explanation.RequiredAuthorityType}. " +
            $"Reason: {HumanizeReason(explanation.SourceBlockReason)}. " +
            $"Mutation attempted: {YesNo(explanation.MutationAttempted)}. " +
            $"Mutation completed: {YesNo(explanation.MutationCompleted)}. " +
            $"Safe next step: {explanation.SafeNextStep}";
    }

    public static string ClassifyBlockReason(string reason)
    {
        if (reason.Contains("WrongAuthorityType", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("WrongPackageType", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("DeploymentReceiptIsNotRollbackAuthority", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("DeploymentReceiptIsNotSourceMutationApproval", StringComparison.OrdinalIgnoreCase))
            return "WrongAuthorityType";
        if (reason.Contains("StaleAuthority", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("StaleObservedState", StringComparison.OrdinalIgnoreCase))
            return "StaleAuthority";
        if (reason.Contains("MemoryIsContext", StringComparison.OrdinalIgnoreCase))
            return "MemoryIsContextOnly";
        if (reason.Contains("ThoughtLedger", StringComparison.OrdinalIgnoreCase))
            return "ThoughtLedgerIsNotApproval";
        if (reason.Contains("WorkflowState", StringComparison.OrdinalIgnoreCase))
            return "WorkflowStateIsHistoryOnly";
        if (reason.Contains("RepositoryMismatch", StringComparison.OrdinalIgnoreCase))
            return "RepositoryMismatch";
        if (reason.Contains("DifferentTask", StringComparison.OrdinalIgnoreCase))
            return "TaskMismatch";
        if (reason.Contains("ExplicitDeploymentExecutionRequestMissing", StringComparison.OrdinalIgnoreCase))
            return "MissingExplicitRequest";
        if (reason.Contains("HumanInstructionCannotSkipRequiredPackage", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("PackageRequired", StringComparison.OrdinalIgnoreCase))
            return "PackageRequired";
        if (reason.Contains("RollbackConsideration", StringComparison.OrdinalIgnoreCase))
            return "RollbackConsiderationOnly";
        if (reason.Contains("ReceiptBoundaryViolation", StringComparison.OrdinalIgnoreCase))
            return "ReceiptBoundaryViolation";
        if (reason.Contains("UnsupportedReceiptKind", StringComparison.OrdinalIgnoreCase))
            return "UnsupportedReceiptKind";
        return "GenericOrUnclear";
    }

    private static string NormalizeAuthorityRelationship(string relationship) =>
        KnownAuthorityRelationships.Contains(relationship, StringComparer.OrdinalIgnoreCase)
            ? KnownAuthorityRelationships.First(item => string.Equals(item, relationship, StringComparison.OrdinalIgnoreCase))
            : "Unknown";

    private static bool IsAuthorityRelationshipUnsafe(string relationship) =>
        relationship is "WrongType" or "Stale" or "ContextOnly" or "Unrelated" or "Insufficient" or "Missing" or "BoundaryViolation" or "Unknown";

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
            "run execute",
            "run deploy",
            "run rollback",
            "run release",
            "run merge",
            "run commit",
            "run push"
        };

        return unsafePrefixes.Any(prefix =>
            normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase));
    }

    private static string HumanizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return "authority state could not be interpreted safely";
        var text = reason.Replace(':', ' ');
        var builder = new StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (index > 0 && char.IsUpper(current) && char.IsLetter(text[index - 1]) && !char.IsUpper(text[index - 1]))
                builder.Append(' ');
            builder.Append(current);
        }

        return builder.ToString().ToLowerInvariant();
    }

    private static bool IsBlocked(string verdict) =>
        verdict.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
        verdict.Contains("rejected", StringComparison.OrdinalIgnoreCase);

    private static bool IsNeedsAuthority(string verdict) =>
        verdict.Contains("needs", StringComparison.OrdinalIgnoreCase);

    private static bool IsSuccess(string verdict) =>
        verdict.Contains("success", StringComparison.OrdinalIgnoreCase) ||
        verdict.Contains("accepted", StringComparison.OrdinalIgnoreCase) ||
        verdict.Contains("executed", StringComparison.OrdinalIgnoreCase);

    private static string YesNo(bool value) => value ? "yes" : "no";

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
}
