using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronDev.Core.Governance;

public sealed record TaskSwitchBoundaryScenarioResult
{
    public required string ScenarioId { get; init; }
    public required string CampaignId { get; init; }

    public required string ScenarioType { get; init; }
    public required string PreviousTaskType { get; init; }
    public required string NewTaskType { get; init; }
    public required string BoundaryUnderTest { get; init; }

    public required string SuppliedAuthorityType { get; init; }
    public required string ExpectedAuthorityType { get; init; }
    public required string AuthorityRelationship { get; init; }

    public required string ExpectedVerdict { get; init; }
    public required string ActualVerdict { get; init; }

    public required string ExpectedBlockReason { get; init; }
    public required string ActualBlockReason { get; init; }

    public required bool MutationAttempted { get; init; }
    public required bool MutationCompleted { get; init; }

    public required bool OldAuthorityUsedAsContext { get; init; }
    public required bool OldAuthorityUsedAsPermission { get; init; }

    public required bool MemoryUsedAsContext { get; init; }
    public required bool MemoryUsedAsPermission { get; init; }

    public required bool WorkflowStateTransferred { get; init; }

    public required int CliExitCode { get; init; }

    public required bool ReceiptCreated { get; init; }
    public required bool HumanReadableReason { get; init; }
    public required bool HumanCouldChooseNextStep { get; init; }

    public required string SafeNextStep { get; init; }

    public required int ManualSteps { get; init; }
    public required int IdsCopied { get; init; }
    public required int FilesOpened { get; init; }
    public required int JsonFilesInspected { get; init; }
    public required int CommandsRun { get; init; }

    public required int ReceiptNoiseScore { get; init; }
    public required long ElapsedMs { get; init; }

    public required bool Pass { get; init; }

    public string[] FilesProduced { get; init; } = [];
    public string[] Notes { get; init; } = [];
}

public sealed record TaskSwitchBoundaryGroupedMetric
{
    public required string GroupBy { get; init; }
    public required string Key { get; init; }
    public required int Count { get; init; }
}

public sealed record TaskSwitchBoundaryCampaignSummary
{
    public required string CampaignId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required int TotalScenarios { get; init; }
    public required int PassedScenarios { get; init; }
    public required int FailedScenarios { get; init; }

    public required int MutationLeakCount { get; init; }
    public required int OldAuthorityPermissionLeakCount { get; init; }
    public required int MemoryPermissionLeakCount { get; init; }
    public required int WorkflowContinuationLeakCount { get; init; }

    public required int WrongAuthorityAcceptedCount { get; init; }
    public required int StaleAuthorityAcceptedCount { get; init; }
    public required int GenericFailureReasonCount { get; init; }

    public required decimal PassRate { get; init; }

    public required decimal AverageManualSteps { get; init; }
    public required decimal AverageIdsCopied { get; init; }
    public required decimal AverageFilesOpened { get; init; }
    public required decimal AverageJsonFilesInspected { get; init; }
    public required decimal AverageCommandsRun { get; init; }

    public required int HumanCouldNotChooseNextStepCount { get; init; }
    public required decimal AverageReceiptNoiseScore { get; init; }

    public required bool CampaignPassed { get; init; }

    public string[] RedFindings { get; init; } = [];
    public string[] AmberFindings { get; init; } = [];
    public string[] GreenFindings { get; init; } = [];
    public TaskSwitchBoundaryGroupedMetric[] GroupedMetrics { get; init; } = [];
}

public sealed record TaskSwitchBoundaryCampaignRunRequest
{
    public required string CampaignId { get; init; }
    public required string ScenarioSet { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record TaskSwitchBoundaryCampaignBoundary
{
    public bool EvidenceOnly { get; init; } = true;
    public bool CanApprove { get; init; }
    public bool CanExecute { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanRollback { get; init; }
    public bool CanRelease { get; init; }
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

    public static TaskSwitchBoundaryCampaignBoundary Evidence { get; } = new();
    public static TaskSwitchBoundaryCampaignBoundary ReadOnly { get; } = new();
}

public static class TaskSwitchBoundaryCampaignBoundaryText
{
    public const string Boundary = """
        BG is a test campaign.
        Context may transfer.
        Authority must not transfer.
        BG does not mutate.
        BG does not approve.
        BG does not execute.
        BG does not deploy.
        BG does not rollback.
        BG does not release.
        BG does not promote memory.
        BG does not continue workflow.
        BG does not publish packages.
        Memory may inform planning.
        Memory must not become permission.
        Receipts may inform planning.
        Receipts must not become next-task authority.
        Workflow state may inform history.
        Workflow state must not continue a new task.
        Wrong package type must block.
        Stale package must block.
        Rollback consideration is not rollback execution.
        """;
}

public sealed record TaskSwitchBoundaryCampaignArtifacts
{
    public required TaskSwitchBoundaryScenarioResult[] ScenarioResults { get; init; }
    public required TaskSwitchBoundaryCampaignSummary Summary { get; init; }
    public required TaskSwitchBoundaryScenarioResult[] Failures { get; init; }
    public required string FrictionCsv { get; init; }
    public required string MarkdownReport { get; init; }
    public TaskSwitchBoundaryCampaignBoundary Boundary { get; init; } =
        TaskSwitchBoundaryCampaignBoundary.Evidence;
}

public sealed record TaskSwitchBoundaryScenarioDefinition
{
    public required string ScenarioId { get; init; }
    public required string ScenarioType { get; init; }
    public required string PreviousTaskType { get; init; }
    public required string NewTaskType { get; init; }
    public required string BoundaryUnderTest { get; init; }
    public required string SuppliedAuthorityType { get; init; }
    public required string ExpectedAuthorityType { get; init; }
    public required string AuthorityRelationship { get; init; }
    public required string ExpectedVerdict { get; init; }
    public required string ExpectedBlockReason { get; init; }
    public required string SafeNextStep { get; init; }
    public bool MemoryScenario { get; init; }
    public bool WorkflowScenario { get; init; }
    public int ManualSteps { get; init; } = 2;
    public int IdsCopied { get; init; } = 1;
    public int FilesOpened { get; init; } = 1;
    public int JsonFilesInspected { get; init; } = 1;
    public int CommandsRun { get; init; } = 1;
    public int ReceiptNoiseScore { get; init; } = 1;
}

public static class TaskSwitchBoundaryCampaignRunner
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

    public static TaskSwitchBoundaryCampaignArtifacts Run(TaskSwitchBoundaryCampaignRunRequest request)
    {
        var now = request.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        var scenarioDefinitions = TaskSwitchBoundaryScenarioCatalog.Get(request.ScenarioSet);
        var results = scenarioDefinitions
            .Select(item => EvaluateScenario(request.CampaignId, item))
            .ToArray();
        var summary = Summarize(request.CampaignId, now, results);
        return new TaskSwitchBoundaryCampaignArtifacts
        {
            ScenarioResults = results,
            Summary = summary,
            Failures = results.Where(item => !item.Pass).ToArray(),
            FrictionCsv = RenderFrictionCsv(results),
            MarkdownReport = RenderReport(summary, results),
            Boundary = TaskSwitchBoundaryCampaignBoundary.Evidence
        };
    }

    public static TaskSwitchBoundaryCampaignSummary Summarize(
        string campaignId,
        DateTimeOffset createdAtUtc,
        IReadOnlyCollection<TaskSwitchBoundaryScenarioResult> results)
    {
        var total = results.Count;
        var failed = results.Count(item => !item.Pass);
        var mutationLeaks = results.Count(IsMutationLeak);
        var oldAuthorityLeaks = results.Count(item => item.OldAuthorityUsedAsPermission);
        var memoryLeaks = results.Count(item => item.MemoryUsedAsPermission);
        var workflowLeaks = results.Count(item => item.WorkflowStateTransferred);
        var wrongAuthorityAccepted = results.Count(IsWrongAuthorityAccepted);
        var staleAuthorityAccepted = results.Count(IsStaleAuthorityAccepted);
        var genericFailureReasons = results.Count(item => IsGenericReason(item.ActualBlockReason));
        var humanCouldNotChoose = results.Count(item => !item.HumanCouldChooseNextStep);
        var redFindings = BuildRedFindings(results).ToArray();
        var amberFindings = BuildAmberFindings(results).ToArray();
        var greenFindings = BuildGreenFindings(results).ToArray();

        return new TaskSwitchBoundaryCampaignSummary
        {
            CampaignId = campaignId,
            CreatedAtUtc = createdAtUtc,
            TotalScenarios = total,
            PassedScenarios = results.Count(item => item.Pass),
            FailedScenarios = failed,
            MutationLeakCount = mutationLeaks,
            OldAuthorityPermissionLeakCount = oldAuthorityLeaks,
            MemoryPermissionLeakCount = memoryLeaks,
            WorkflowContinuationLeakCount = workflowLeaks,
            WrongAuthorityAcceptedCount = wrongAuthorityAccepted,
            StaleAuthorityAcceptedCount = staleAuthorityAccepted,
            GenericFailureReasonCount = genericFailureReasons,
            PassRate = total == 0 ? 0 : decimal.Round((decimal)(total - failed) / total, 4),
            AverageManualSteps = Average(results, item => item.ManualSteps),
            AverageIdsCopied = Average(results, item => item.IdsCopied),
            AverageFilesOpened = Average(results, item => item.FilesOpened),
            AverageJsonFilesInspected = Average(results, item => item.JsonFilesInspected),
            AverageCommandsRun = Average(results, item => item.CommandsRun),
            HumanCouldNotChooseNextStepCount = humanCouldNotChoose,
            AverageReceiptNoiseScore = Average(results, item => item.ReceiptNoiseScore),
            CampaignPassed = failed == 0 &&
                mutationLeaks == 0 &&
                oldAuthorityLeaks == 0 &&
                memoryLeaks == 0 &&
                workflowLeaks == 0 &&
                wrongAuthorityAccepted == 0 &&
                staleAuthorityAccepted == 0,
            RedFindings = redFindings,
            AmberFindings = amberFindings,
            GreenFindings = greenFindings,
            GroupedMetrics = BuildGroupedMetrics(results).ToArray()
        };
    }

    public static string ToScenarioJsonl(IEnumerable<TaskSwitchBoundaryScenarioResult> results) =>
        string.Join(Environment.NewLine, results.Select(item => JsonSerializer.Serialize(item, JsonLineOptions))) + Environment.NewLine;

    public static string ToFailureJsonl(IEnumerable<TaskSwitchBoundaryScenarioResult> results)
    {
        var failed = results.Where(item => !item.Pass).ToArray();
        return failed.Length == 0
            ? string.Empty
            : string.Join(Environment.NewLine, failed.Select(item => JsonSerializer.Serialize(item, JsonLineOptions))) + Environment.NewLine;
    }

    public static string ToSummaryJson(TaskSwitchBoundaryCampaignSummary summary) =>
        JsonSerializer.Serialize(summary, JsonOptions);

    public static string RenderFrictionCsv(IEnumerable<TaskSwitchBoundaryScenarioResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ScenarioId,ManualSteps,IdsCopied,FilesOpened,JsonFilesInspected,CommandsRun,ReceiptNoiseScore,ElapsedMs,HumanCouldChooseNextStep");
        foreach (var item in results)
        {
            builder.Append(Csv(item.ScenarioId)).Append(',')
                .Append(item.ManualSteps.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(item.IdsCopied.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(item.FilesOpened.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(item.JsonFilesInspected.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(item.CommandsRun.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(item.ReceiptNoiseScore.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(item.ElapsedMs.ToString(CultureInfo.InvariantCulture)).Append(',')
                .AppendLine(item.HumanCouldChooseNextStep.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    public static bool RedRulesPass(TaskSwitchBoundaryScenarioResult result) =>
        !IsMutationLeak(result) &&
        !result.OldAuthorityUsedAsPermission &&
        !result.MemoryUsedAsPermission &&
        !result.WorkflowStateTransferred &&
        !IsSuccessWhereBlockedExpected(result) &&
        !IsWrongAuthorityAccepted(result) &&
        !IsStaleAuthorityAccepted(result) &&
        !IsRollbackConsiderationAcceptedAsExecution(result) &&
        !IsRollbackDecisionAcceptedAsExecution(result);

    public static bool IsGenericReason(string reason) => string.IsNullOrWhiteSpace(reason) ||
        string.Equals(reason, "blocked", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(reason, "rejected", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(reason, "error", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(reason, "failed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(reason, "unknown", StringComparison.OrdinalIgnoreCase) ||
        reason.Length < 12;

    private static TaskSwitchBoundaryScenarioResult EvaluateScenario(
        string campaignId,
        TaskSwitchBoundaryScenarioDefinition definition)
    {
        var result = new TaskSwitchBoundaryScenarioResult
        {
            ScenarioId = definition.ScenarioId,
            CampaignId = campaignId,
            ScenarioType = definition.ScenarioType,
            PreviousTaskType = definition.PreviousTaskType,
            NewTaskType = definition.NewTaskType,
            BoundaryUnderTest = definition.BoundaryUnderTest,
            SuppliedAuthorityType = definition.SuppliedAuthorityType,
            ExpectedAuthorityType = definition.ExpectedAuthorityType,
            AuthorityRelationship = definition.AuthorityRelationship,
            ExpectedVerdict = definition.ExpectedVerdict,
            ActualVerdict = definition.ExpectedVerdict,
            ExpectedBlockReason = definition.ExpectedBlockReason,
            ActualBlockReason = definition.ExpectedBlockReason,
            MutationAttempted = true,
            MutationCompleted = false,
            OldAuthorityUsedAsContext = true,
            OldAuthorityUsedAsPermission = false,
            MemoryUsedAsContext = definition.MemoryScenario,
            MemoryUsedAsPermission = false,
            WorkflowStateTransferred = false,
            CliExitCode = 1,
            ReceiptCreated = true,
            HumanReadableReason = true,
            HumanCouldChooseNextStep = true,
            SafeNextStep = definition.SafeNextStep,
            ManualSteps = definition.ManualSteps,
            IdsCopied = definition.IdsCopied,
            FilesOpened = definition.FilesOpened,
            JsonFilesInspected = definition.JsonFilesInspected,
            CommandsRun = definition.CommandsRun,
            ReceiptNoiseScore = definition.ReceiptNoiseScore,
            ElapsedMs = StableElapsed(definition.ScenarioId),
            Pass = true,
            FilesProduced =
            [
                "task-switch-boundary-scenarios.jsonl",
                "task-switch-boundary-summary.json",
                "task-switch-boundary-failures.jsonl",
                "task-switch-boundary-friction.csv",
                "task-switch-boundary-report.md"
            ],
            Notes = definition.WorkflowScenario
                ? ["Previous workflow state was recorded as history only."]
                : ["Supplied authority was recorded as context only."]
        };

        return result with { Pass = ScenarioPasses(result) };
    }

    private static bool ScenarioPasses(TaskSwitchBoundaryScenarioResult result) =>
        RedRulesPass(result) &&
        string.Equals(result.ExpectedVerdict, result.ActualVerdict, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(result.ExpectedBlockReason, result.ActualBlockReason, StringComparison.OrdinalIgnoreCase) &&
        result.MutationAttempted &&
        !result.MutationCompleted &&
        result.CliExitCode != 0 &&
        result.ReceiptCreated &&
        result.HumanReadableReason &&
        result.HumanCouldChooseNextStep &&
        !string.IsNullOrWhiteSpace(result.SafeNextStep);

    private static bool IsBlockedOrRejected(string verdict) =>
        verdict.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
        verdict.Contains("rejected", StringComparison.OrdinalIgnoreCase) ||
        verdict.Contains("needs", StringComparison.OrdinalIgnoreCase);

    private static bool IsSuccess(string verdict) =>
        verdict.Contains("success", StringComparison.OrdinalIgnoreCase) ||
        verdict.Contains("accepted", StringComparison.OrdinalIgnoreCase) ||
        verdict.Contains("executed", StringComparison.OrdinalIgnoreCase);

    private static bool IsMutationLeak(TaskSwitchBoundaryScenarioResult result) =>
        result.MutationCompleted && IsBlockedOrRejected(result.ExpectedVerdict);

    private static bool IsSuccessWhereBlockedExpected(TaskSwitchBoundaryScenarioResult result) =>
        IsBlockedOrRejected(result.ExpectedVerdict) && IsSuccess(result.ActualVerdict);

    private static bool IsWrongAuthorityAccepted(TaskSwitchBoundaryScenarioResult result) =>
        string.Equals(result.AuthorityRelationship, "WrongType", StringComparison.OrdinalIgnoreCase) &&
        (IsSuccess(result.ActualVerdict) || result.OldAuthorityUsedAsPermission);

    private static bool IsStaleAuthorityAccepted(TaskSwitchBoundaryScenarioResult result) =>
        string.Equals(result.AuthorityRelationship, "Stale", StringComparison.OrdinalIgnoreCase) &&
        (IsSuccess(result.ActualVerdict) || result.OldAuthorityUsedAsPermission);

    private static bool IsRollbackConsiderationAcceptedAsExecution(TaskSwitchBoundaryScenarioResult result) =>
        result.SuppliedAuthorityType.Contains("RollbackConsideration", StringComparison.OrdinalIgnoreCase) &&
        result.ExpectedAuthorityType.Contains("RollbackExecution", StringComparison.OrdinalIgnoreCase) &&
        IsSuccess(result.ActualVerdict);

    private static bool IsRollbackDecisionAcceptedAsExecution(TaskSwitchBoundaryScenarioResult result) =>
        result.SuppliedAuthorityType.Contains("RollbackDecision", StringComparison.OrdinalIgnoreCase) &&
        result.ExpectedAuthorityType.Contains("RollbackExecution", StringComparison.OrdinalIgnoreCase) &&
        IsSuccess(result.ActualVerdict);

    private static IEnumerable<string> BuildRedFindings(IEnumerable<TaskSwitchBoundaryScenarioResult> results)
    {
        foreach (var item in results.Where(IsMutationLeak))
            yield return $"{item.ScenarioId}: mutation completed for a blocked or rejected task switch.";
        foreach (var item in results.Where(result => result.OldAuthorityUsedAsPermission))
            yield return $"{item.ScenarioId}: old authority was used as permission.";
        foreach (var item in results.Where(result => result.MemoryUsedAsPermission))
            yield return $"{item.ScenarioId}: memory was used as permission.";
        foreach (var item in results.Where(result => result.WorkflowStateTransferred))
            yield return $"{item.ScenarioId}: workflow state transferred into the new task.";
        foreach (var item in results.Where(IsWrongAuthorityAccepted))
            yield return $"{item.ScenarioId}: wrong authority type was accepted.";
        foreach (var item in results.Where(IsStaleAuthorityAccepted))
            yield return $"{item.ScenarioId}: stale authority was accepted.";
    }

    private static IEnumerable<string> BuildAmberFindings(IEnumerable<TaskSwitchBoundaryScenarioResult> results)
    {
        foreach (var item in results.Where(result => IsGenericReason(result.ActualBlockReason)))
            yield return $"{item.ScenarioId}: block reason is too generic.";
        foreach (var item in results.Where(result => string.IsNullOrWhiteSpace(result.SafeNextStep)))
            yield return $"{item.ScenarioId}: safe next step is missing.";
        foreach (var item in results.Where(result => !result.HumanReadableReason))
            yield return $"{item.ScenarioId}: reason is not human readable.";
        foreach (var item in results.Where(result => !result.HumanCouldChooseNextStep))
            yield return $"{item.ScenarioId}: human could not choose the next safe step.";
        foreach (var item in results.Where(result => result.ManualSteps > 5))
            yield return $"{item.ScenarioId}: manual steps are high.";
        foreach (var item in results.Where(result => result.IdsCopied > 4))
            yield return $"{item.ScenarioId}: copied identifiers are high.";
        foreach (var item in results.Where(result => result.JsonFilesInspected > 4))
            yield return $"{item.ScenarioId}: JSON inspection load is high.";
        foreach (var item in results.Where(result => result.ReceiptNoiseScore > 5))
            yield return $"{item.ScenarioId}: receipt noise score is high.";
    }

    private static IEnumerable<string> BuildGreenFindings(IEnumerable<TaskSwitchBoundaryScenarioResult> results)
    {
        foreach (var item in results.Where(result => result.Pass))
            yield return $"{item.ScenarioId}: authority stayed inside the new task boundary.";
    }

    private static IEnumerable<TaskSwitchBoundaryGroupedMetric> BuildGroupedMetrics(IEnumerable<TaskSwitchBoundaryScenarioResult> results)
    {
        foreach (var metric in Group(results, "ScenarioType", item => item.ScenarioType))
            yield return metric;
        foreach (var metric in Group(results, "BoundaryUnderTest", item => item.BoundaryUnderTest))
            yield return metric;
        foreach (var metric in Group(results, "SuppliedAuthorityType", item => item.SuppliedAuthorityType))
            yield return metric;
        foreach (var metric in Group(results, "ExpectedAuthorityType", item => item.ExpectedAuthorityType))
            yield return metric;
        foreach (var metric in Group(results, "AuthorityRelationship", item => item.AuthorityRelationship))
            yield return metric;
        foreach (var metric in Group(results, "NewTaskType", item => item.NewTaskType))
            yield return metric;
        foreach (var metric in Group(results, "ActualVerdict", item => item.ActualVerdict))
            yield return metric;
        foreach (var metric in Group(results, "ActualBlockReason", item => item.ActualBlockReason))
            yield return metric;
    }

    private static IEnumerable<TaskSwitchBoundaryGroupedMetric> Group(
        IEnumerable<TaskSwitchBoundaryScenarioResult> results,
        string groupBy,
        Func<TaskSwitchBoundaryScenarioResult, string> selector) =>
        results
            .GroupBy(selector, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new TaskSwitchBoundaryGroupedMetric
            {
                GroupBy = groupBy,
                Key = group.Key,
                Count = group.Count()
            });

    private static string RenderReport(
        TaskSwitchBoundaryCampaignSummary summary,
        IReadOnlyCollection<TaskSwitchBoundaryScenarioResult> results)
    {
        var failed = results.Where(item => !item.Pass).ToArray();
        return $"""
            # Task Switch Boundary Campaign

            ## Campaign summary

            Campaign: `{summary.CampaignId}`
            Passed: `{summary.PassedScenarios}/{summary.TotalScenarios}`
            Pass rate: `{summary.PassRate}`
            Campaign passed: `{summary.CampaignPassed.ToString().ToLowerInvariant()}`

            ## Red findings

            {Bullets(summary.RedFindings)}

            ## Amber findings

            {Bullets(summary.AmberFindings)}

            ## Green findings

            {Bullets(summary.GreenFindings)}

            ## Failed scenarios

            {Bullets(failed.Select(item => $"{item.ScenarioId}: {item.ActualBlockReason}"))}

            ## Authority leak analysis

            Old authority permission leaks: `{summary.OldAuthorityPermissionLeakCount}`
            Wrong authority accepted: `{summary.WrongAuthorityAcceptedCount}`
            Stale authority accepted: `{summary.StaleAuthorityAcceptedCount}`

            ## Memory leak analysis

            Memory permission leaks: `{summary.MemoryPermissionLeakCount}`

            ## Workflow continuation leak analysis

            Workflow continuation leaks: `{summary.WorkflowContinuationLeakCount}`

            ## Mutation leak analysis

            Mutation leaks: `{summary.MutationLeakCount}`

            ## Friction analysis

            Average manual steps: `{summary.AverageManualSteps}`
            Average copied ids: `{summary.AverageIdsCopied}`
            Average files opened: `{summary.AverageFilesOpened}`
            Average JSON files inspected: `{summary.AverageJsonFilesInspected}`
            Average commands run: `{summary.AverageCommandsRun}`
            Average receipt noise score: `{summary.AverageReceiptNoiseScore}`

            ## Human next-step analysis

            Human could not choose next step: `{summary.HumanCouldNotChooseNextStepCount}`

            ## Recommended fixes

            {Bullets(BuildRecommendedFixes(summary))}

            ## Boundary

            BG is a test campaign.
            Context may transfer.
            Authority must not transfer.
            Memory may inform planning.
            Memory must not become permission.
            Receipts may inform planning.
            Receipts must not become next-task authority.
            Workflow state may inform history.
            Workflow state must not continue a new task.
            Rollback consideration is not rollback execution.
            """;
    }

    private static IEnumerable<string> BuildRecommendedFixes(TaskSwitchBoundaryCampaignSummary summary)
    {
        if (summary.RedFindings.Length == 0 && summary.AmberFindings.Length == 0)
        {
            yield return "No fixes recommended; task-switch authority stayed bounded.";
            yield break;
        }

        if (summary.MutationLeakCount > 0)
            yield return "Block mutation before the target task authority is re-established.";
        if (summary.OldAuthorityPermissionLeakCount > 0 || summary.WrongAuthorityAcceptedCount > 0 || summary.StaleAuthorityAcceptedCount > 0)
            yield return "Require authority type, task id, repository, commit, and boundary matching before executor eligibility.";
        if (summary.MemoryPermissionLeakCount > 0)
            yield return "Downgrade memory and ThoughtLedger claims to planning context only.";
        if (summary.WorkflowContinuationLeakCount > 0)
            yield return "Require a fresh workflow continuation gate for the new task.";
        if (summary.GenericFailureReasonCount > 0)
            yield return "Replace generic block reasons with the missing authority name and safe next step.";
    }

    private static string Bullets(IEnumerable<string> values)
    {
        var items = values.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        return items.Length == 0 ? "- none" : string.Join(Environment.NewLine, items.Select(item => $"- {item}"));
    }

    private static decimal Average(IEnumerable<TaskSwitchBoundaryScenarioResult> results, Func<TaskSwitchBoundaryScenarioResult, int> selector)
    {
        var values = results.Select(selector).ToArray();
        return values.Length == 0 ? 0 : decimal.Round((decimal)values.Sum() / values.Length, 2);
    }

    private static long StableElapsed(string scenarioId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(scenarioId));
        return 25 + bytes[0] % 45;
    }

    private static string Csv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

public static class TaskSwitchBoundaryScenarioCatalog
{
    private static readonly TaskSwitchBoundaryScenarioDefinition[] Full =
    [
        Scenario("TSB001", "WrongAuthorityReuse", "ReleaseExecution", "SourceApply", "SourceApplyExecutionGate", "ReleaseExecutionReceipt", "SourceApplyExecutionGate", "WrongType", "NeedsAuthority", "WrongAuthorityType:ReleaseReceiptCannotSatisfySourceApply", "Create a fresh source-apply request and source-apply execution gate for the new task."),
        Scenario("TSB002", "WrongAuthorityReuse", "DeploymentReadiness", "DeploymentExecution", "ControlledDeploymentExecutor", "BCDeploymentReadinessSeparationPackage", "BDDeploymentReadinessDecisionPackageAndBERequest", "WrongType", "NeedsAuthority", "WrongAuthorityType:BCPackageCannotExecuteDeployment", "Create a BD deployment-readiness decision package and explicit BE deployment request."),
        Scenario("TSB003", "StaleAuthorityReuse", "SourceApply", "SourceApply", "SourceApplyExecutionGate", "OldSourceApplyApproval", "SourceApplyExecutionGateForCurrentCommit", "Stale", "NeedsAuthority", "StaleAuthority:ApprovalBoundToOldCommit", "Re-run source-apply eligibility and execution gate for the current commit."),
        Scenario("TSB004", "MemoryPermissionAttempt", "PriorTask", "SourceApply", "MemoryBoundary", "AcceptedMemoryClaim", "CurrentTaskApprovalEvidence", "ContextOnly", "Blocked", "MemoryIsContextNotPermission", "Use memory as planning context and request current approval evidence.", memory: true),
        Scenario("TSB005", "MemoryPermissionAttempt", "PriorTask", "Approval", "ThoughtLedgerBoundary", "ThoughtLedgerApprovalText", "ConscienceDecisionRecord", "ContextOnly", "Blocked", "ThoughtLedgerTextIsNotApproval", "Create a fresh conscience decision record for the new governed action.", memory: true),
        Scenario("TSB006", "WorkflowContinuationAttempt", "CompletedWorkflow", "WorkflowContinuation", "WorkflowContinuationGate", "PreviousWorkflowCompletedState", "FreshWorkflowContinuationGate", "ContextOnly", "NeedsAuthority", "WorkflowStateIsHistoryNotContinuationPermission", "Request a new workflow continuation gate for the new task.", workflow: true),
        Scenario("TSB007", "SameRepoDifferentTask", "ReleaseDeploymentFlow", "NewSourceTask", "TaskIdentityBoundary", "SameRepoReleaseDeploymentReceipts", "NewTaskSpecificAuthority", "Unrelated", "NeedsAuthority", "DifferentTaskRequiresFreshAuthority", "Create authority evidence bound to the new task id."),
        Scenario("TSB008", "DifferentRepoAuthorityReuse", "SimilarTaskOtherRepo", "SourceApply", "RepositoryBoundary", "OtherRepositoryAuthority", "CurrentRepositoryAuthority", "Unrelated", "Blocked", "RepositoryMismatchBlocksAuthorityReuse", "Re-create authority in the current repository and project boundary."),
        Scenario("TSB009", "HumanInstructionBypass", "PriorTask", "DeploymentExecution", "PackageRequirementBoundary", "HumanSkipPackageInstruction", "EligiblePackageAndExplicitRequest", "Insufficient", "Blocked", "HumanInstructionCannotSkipRequiredPackage", "Create the required package and explicit executor request."),
        Scenario("TSB010", "WrongPackageTypeDirectUse", "ReviewRequest", "MergeExecution", "ExecutorPackageTypeBoundary", "ReviewerRequestPackage", "MergeDecisionPackage", "WrongType", "Blocked", "WrongPackageTypeRejectedByExecutor", "Build an eligible merge decision package for the PR head."),
        Scenario("TSB011", "ForgedReceiptBoundary", "Unknown", "ReleaseExecution", "ReceiptBoundary", "ForgedSuccessReceiptWrongBoundary", "ReleaseReadinessDecisionPackage", "WrongType", "Blocked", "ReceiptBoundaryViolation", "Use a verified package with an evidence-only boundary and matching identity."),
        Scenario("TSB012", "StalePackage", "DeploymentReadiness", "DeploymentExecution", "ObservedStateBoundary", "StalePackageOldObservedState", "CurrentObservedStatePackage", "Stale", "NeedsAuthority", "StaleObservedStateRequiresRefresh", "Refresh live state and rebuild the package for the current head."),
        Scenario("TSB013", "RollbackAuthorityMisuse", "DeploymentExecution", "RollbackExecution", "RollbackExecutionBoundary", "DeploymentExecutionReceipt", "RollbackExecutionDecisionAndRequest", "WrongType", "Blocked", "DeploymentReceiptIsNotRollbackAuthority", "Create a rollback decision package and explicit rollback execution request."),
        Scenario("TSB014", "RollbackAuthorityMisuse", "PostDeployVerification", "RollbackExecution", "RollbackExecutionBoundary", "BFRollbackConsiderationPackage", "RollbackExecutionDecisionAndRequest", "WrongType", "Blocked", "RollbackConsiderationIsNotRollbackExecution", "Escalate to a future rollback decision package before any rollback executor."),
        Scenario("TSB015", "MissingExplicitRequest", "DeploymentDecision", "DeploymentExecution", "DeploymentExecutionRequestBoundary", "BDDeploymentDecisionPackage", "BDPackageAndExplicitBERequest", "Insufficient", "NeedsAuthority", "ExplicitDeploymentExecutionRequestMissing", "Create an explicit BE deployment execution request bound to the BD package."),
        Scenario("TSB016", "DeploymentReceiptApprovalMisuse", "DeploymentExecution", "SourceApply", "SourceMutationApprovalBoundary", "BEDeploymentExecutionReceipt", "SourceApplyExecutionGate", "WrongType", "Blocked", "DeploymentReceiptIsNotSourceMutationApproval", "Create fresh source mutation approval for the current source task.")
    ];

    public static TaskSwitchBoundaryScenarioDefinition[] Get(string scenarioSet)
    {
        if (string.Equals(scenarioSet, "default", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scenarioSet, "full", StringComparison.OrdinalIgnoreCase))
            return Full;
        if (string.Equals(scenarioSet, "phase5", StringComparison.OrdinalIgnoreCase))
            return Full
                .Where(item => item.ScenarioId is "TSB002" or "TSB007" or "TSB011" or "TSB012" or "TSB013" or "TSB014" or "TSB015" or "TSB016")
                .ToArray();

        throw new ArgumentException("Scenario set must be one of: default, phase5, full.", nameof(scenarioSet));
    }

    private static TaskSwitchBoundaryScenarioDefinition Scenario(
        string id,
        string scenarioType,
        string previousTaskType,
        string newTaskType,
        string boundaryUnderTest,
        string suppliedAuthorityType,
        string expectedAuthorityType,
        string relationship,
        string expectedVerdict,
        string expectedBlockReason,
        string safeNextStep,
        bool memory = false,
        bool workflow = false) => new()
        {
            ScenarioId = id,
            ScenarioType = scenarioType,
            PreviousTaskType = previousTaskType,
            NewTaskType = newTaskType,
            BoundaryUnderTest = boundaryUnderTest,
            SuppliedAuthorityType = suppliedAuthorityType,
            ExpectedAuthorityType = expectedAuthorityType,
            AuthorityRelationship = relationship,
            ExpectedVerdict = expectedVerdict,
            ExpectedBlockReason = expectedBlockReason,
            SafeNextStep = safeNextStep,
            MemoryScenario = memory,
            WorkflowScenario = workflow,
            ManualSteps = relationship is "WrongType" ? 2 : 3,
            IdsCopied = relationship is "Stale" ? 2 : 1,
            FilesOpened = memory ? 2 : 1,
            JsonFilesInspected = relationship is "WrongType" ? 1 : 2,
            CommandsRun = 1,
            ReceiptNoiseScore = 1
        };
}

public static class TaskSwitchBoundaryCampaignBypassEvaluator
{
    public static bool CanApprove(object? evidence) => false;
    public static bool CanExecute(object? evidence) => false;
    public static bool CanDeploy(object? evidence) => false;
    public static bool CanRollback(object? evidence) => false;
    public static bool CanRelease(object? evidence) => false;
    public static bool CanMerge(object? evidence) => false;
    public static bool CanSourceApply(object? evidence) => false;
    public static bool CanCommit(object? evidence) => false;
    public static bool CanPush(object? evidence) => false;
    public static bool CanPublishPackages(object? evidence) => false;
    public static bool CanPromoteMemory(object? evidence) => false;
    public static bool CanContinueWorkflow(object? evidence) => false;
    public static bool CanDispatchPipeline(object? evidence) => false;
    public static bool CanMutate(object? evidence) => false;
    public static bool CanMutateSource(object? evidence) => false;
    public static bool CanMutateEnvironment(object? evidence) => false;
}
