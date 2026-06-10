using IronDev.Core.AgentMemory;
using IronDev.Core.AgentMemory.Execution;

namespace IronDev.Infrastructure.AgentMemory;

public sealed class MemoryExecutionGate : IMemoryExecutionGate
{
    private readonly IConscienceMemoryGovernanceService _governance;

    public MemoryExecutionGate(IConscienceMemoryGovernanceService governance)
    {
        _governance = governance ?? throw new ArgumentNullException(nameof(governance));
    }

    public async Task<MemoryExecutionGateResult> EvaluateAsync(
        MemoryBackedExecutionContext? context,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            return new MemoryExecutionGateResult
            {
                Decision = MemoryExecutionGateDecision.NotMemoryBacked,
                MayProceedToPolicyGate = true,
                Summary = "Execution is not memory-backed.",
                Evidence = BuildEvidence(null, MemoryExecutionGateDecision.NotMemoryBacked, null, [])
            };
        }

        var shapeIssues = ValidateContextShape(context);
        if (shapeIssues.Count > 0)
        {
            return Blocked(context, null, shapeIssues, "Memory-backed execution context is incomplete.");
        }

        var governanceResult = context.SuppliedGovernanceResult;
        if (governanceResult is not null)
        {
            var mismatchIssues = ValidateSuppliedResult(context, governanceResult);
            if (mismatchIssues.Count > 0)
            {
                return Blocked(context, governanceResult, mismatchIssues, "Supplied memory governance result does not match this execution context.");
            }
        }
        else if (context.RequireGovernanceCheck)
        {
            governanceResult = await _governance.CheckAsync(BuildGovernanceRequest(context), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return Blocked(
                context,
                null,
                [MismatchIssue("Memory-backed execution requires a governance check result or a fresh Conscience memory governance check.")],
                "Memory-backed execution did not provide or request governance.");
        }

        return MapGovernanceResult(context, governanceResult);
    }

    private static IReadOnlyList<MemoryGovernanceIssue> ValidateContextShape(MemoryBackedExecutionContext context)
    {
        var issues = new List<MemoryGovernanceIssue>();
        if (context.Scope is null ||
            string.IsNullOrWhiteSpace(context.Scope.TenantId) ||
            string.IsNullOrWhiteSpace(context.Scope.ProjectId) ||
            string.IsNullOrWhiteSpace(context.Scope.CampaignId) ||
            string.IsNullOrWhiteSpace(context.Scope.RunId) ||
            string.IsNullOrWhiteSpace(context.Scope.AgentId))
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.MissingScope,
                Severity = MemoryGovernanceIssueSeverity.Critical,
                Summary = "Memory-backed execution requires complete tenant, project, campaign, run, and agent scope."
            });
        }

        if (string.IsNullOrWhiteSpace(context.DecisionId))
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.MissingDecisionId,
                Severity = MemoryGovernanceIssueSeverity.Critical,
                Summary = "Memory-backed execution requires a decision ID."
            });
        }

        if (context.ReferencedArtifacts is null ||
            context.ReferencedArtifacts.Count == 0 ||
            context.ReferencedArtifacts.All(reference => reference is null || !HasAnyReference(reference)) ||
            context.ReferencedArtifacts.All(reference => reference is null || !HasGovernanceReference(reference)))
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.MissingReferencedArtifacts,
                Severity = MemoryGovernanceIssueSeverity.Critical,
                Summary = "Memory-backed execution requires at least one memory, influence, or handoff reference."
            });
        }

        return issues;
    }

    private static IReadOnlyList<MemoryGovernanceIssue> ValidateSuppliedResult(
        MemoryBackedExecutionContext context,
        MemoryGovernanceCheckResult result)
    {
        var issues = new List<MemoryGovernanceIssue>();
        if (string.IsNullOrWhiteSpace(result.GovernanceCheckId))
            issues.Add(MismatchIssue("Supplied memory governance result is missing GovernanceCheckId."));
        if (result.CheckedAt == default)
            issues.Add(MismatchIssue("Supplied memory governance result is missing CheckedAt."));
        if (!ScopesMatch(context.Scope, result.Scope))
            issues.Add(MismatchIssue("Supplied memory governance result scope does not match execution context."));
        if (!string.Equals(context.DecisionId, result.DecisionId, StringComparison.Ordinal))
            issues.Add(MismatchIssue("Supplied memory governance result decision ID does not match execution context."));
        if (context.ActionType != result.ActionType)
            issues.Add(MismatchIssue("Supplied memory governance result action type does not match execution context."));
        if (!string.IsNullOrWhiteSpace(context.CorrelationId) &&
            !string.Equals(context.CorrelationId, result.CorrelationId, StringComparison.Ordinal))
        {
            issues.Add(MismatchIssue("Supplied memory governance result correlation ID does not match execution context."));
        }

        return issues;
    }

    private static MemoryExecutionGateResult MapGovernanceResult(
        MemoryBackedExecutionContext context,
        MemoryGovernanceCheckResult governanceResult)
    {
        if (context.ActionType == MemoryGovernanceActionType.SourceMutation &&
            governanceResult.Decision == MemoryGovernanceDecision.Allow)
        {
            var issues = MergeIssues(
                governanceResult.Issues,
                new MemoryGovernanceIssue
                {
                    Code = MemoryGovernanceIssueCode.SourceMutationRequiresApprovalBeyondMemory,
                    Severity = MemoryGovernanceIssueSeverity.Warning,
                    Summary = "Memory governance cannot authorize source mutation; execution must continue through policy and explicit approval gates."
                });
            return Warning(context, governanceResult, issues, "Memory governance allowed context, but source mutation still requires outer policy and approval.");
        }

        if (context.ActionType == MemoryGovernanceActionType.ExternalEffect &&
            governanceResult.Decision == MemoryGovernanceDecision.Allow)
        {
            var issues = MergeIssues(
                governanceResult.Issues,
                new MemoryGovernanceIssue
                {
                    Code = MemoryGovernanceIssueCode.ExternalEffectRequiresApprovalBeyondMemory,
                    Severity = MemoryGovernanceIssueSeverity.Warning,
                    Summary = "Memory governance cannot authorize external effects; execution must continue through policy and explicit approval gates."
                });
            return Warning(context, governanceResult, issues, "Memory governance allowed context, but external effects still require outer policy and approval.");
        }

        return governanceResult.Decision switch
        {
            MemoryGovernanceDecision.Block => Blocked(context, governanceResult, governanceResult.Issues, "Memory governance blocked this execution."),
            MemoryGovernanceDecision.Warn => Warning(context, governanceResult, governanceResult.Issues, "Memory governance produced warnings; execution may only proceed to the normal policy and approval gates."),
            _ => new MemoryExecutionGateResult
            {
                Decision = MemoryExecutionGateDecision.Allowed,
                MayProceedToPolicyGate = true,
                Summary = "Memory governance allowed execution to proceed to the normal policy and approval gates.",
                GovernanceResult = governanceResult,
                Issues = governanceResult.Issues,
                Evidence = BuildEvidence(context, MemoryExecutionGateDecision.Allowed, governanceResult, governanceResult.Issues)
            }
        };
    }

    private static MemoryExecutionGateResult Warning(
        MemoryBackedExecutionContext context,
        MemoryGovernanceCheckResult governanceResult,
        IReadOnlyList<MemoryGovernanceIssue> issues,
        string summary) =>
        new()
        {
            Decision = MemoryExecutionGateDecision.WarningRequiresOuterApproval,
            MayProceedToPolicyGate = true,
            Summary = summary,
            GovernanceResult = governanceResult,
            Issues = issues,
            Evidence = BuildEvidence(context, MemoryExecutionGateDecision.WarningRequiresOuterApproval, governanceResult, issues)
        };

    private static MemoryExecutionGateResult Blocked(
        MemoryBackedExecutionContext context,
        MemoryGovernanceCheckResult? governanceResult,
        IReadOnlyList<MemoryGovernanceIssue> issues,
        string summary) =>
        new()
        {
            Decision = MemoryExecutionGateDecision.Blocked,
            MayProceedToPolicyGate = false,
            Summary = summary,
            GovernanceResult = governanceResult,
            Issues = issues,
            Evidence = BuildEvidence(context, MemoryExecutionGateDecision.Blocked, governanceResult, issues)
        };

    private static MemoryGovernanceCheckRequest BuildGovernanceRequest(MemoryBackedExecutionContext context) =>
        new()
        {
            Scope = context.Scope,
            ActionType = context.ActionType,
            DecisionId = context.DecisionId,
            ReferencedArtifacts = context.ReferencedArtifacts
                .Where(HasGovernanceReference)
                .Select(reference => new MemoryGovernanceReferencedArtifact
                {
                    MemoryItemId = reference.MemoryItemId,
                    InfluenceId = reference.InfluenceId,
                    HandoffMemorySliceId = reference.HandoffMemorySliceId,
                    DecisionId = FirstNonEmpty(reference.DecisionId, context.DecisionId),
                    ThoughtLedgerEntryId = reference.ThoughtLedgerEntryId
                })
                .ToArray(),
            RequestedAt = context.RequestedAt,
            TargetAgentId = context.TargetAgentId,
            ToolName = context.ToolName,
            AffectedArtifactType = context.AffectedArtifactType,
            AffectedArtifactId = context.AffectedArtifactId,
            CorrelationId = context.CorrelationId,
            InfluenceRecordRequired = context.InfluenceRecordRequired
        };

    private static MemoryExecutionEvidence BuildEvidence(
        MemoryBackedExecutionContext? context,
        MemoryExecutionGateDecision gateDecision,
        MemoryGovernanceCheckResult? governanceResult,
        IReadOnlyList<MemoryGovernanceIssue> issues)
    {
        IReadOnlyList<MemoryBackedExecutionReference> references = context?.ReferencedArtifacts ?? [];
        return new MemoryExecutionEvidence
        {
            IsMemoryBacked = context is not null,
            GovernanceCheckId = governanceResult?.GovernanceCheckId,
            DecisionId = context?.DecisionId ?? governanceResult?.DecisionId,
            GateDecision = gateDecision,
            GovernanceDecision = governanceResult?.Decision,
            IssueCodes = issues.Select(issue => issue.Code).Distinct().ToArray(),
            MemoryItemIds = references
                .Select(reference => reference.MemoryItemId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            InfluenceIds = references
                .Select(reference => reference.InfluenceId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            HandoffMemorySliceIds = references
                .Select(reference => reference.HandoffMemorySliceId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static bool HasAnyReference(MemoryBackedExecutionReference reference) =>
        !string.IsNullOrWhiteSpace(reference.MemoryItemId) ||
        !string.IsNullOrWhiteSpace(reference.InfluenceId) ||
        !string.IsNullOrWhiteSpace(reference.HandoffMemorySliceId) ||
        !string.IsNullOrWhiteSpace(reference.ProposalId);

    private static bool HasGovernanceReference(MemoryBackedExecutionReference reference) =>
        !string.IsNullOrWhiteSpace(reference.MemoryItemId) ||
        !string.IsNullOrWhiteSpace(reference.InfluenceId) ||
        !string.IsNullOrWhiteSpace(reference.HandoffMemorySliceId);

    private static bool ScopesMatch(AgentMemoryScope left, AgentMemoryScope right) =>
        string.Equals(left.TenantId, right.TenantId, StringComparison.Ordinal) &&
        string.Equals(left.ProjectId, right.ProjectId, StringComparison.Ordinal) &&
        string.Equals(left.CampaignId, right.CampaignId, StringComparison.Ordinal) &&
        string.Equals(left.RunId, right.RunId, StringComparison.Ordinal) &&
        string.Equals(left.AgentId, right.AgentId, StringComparison.Ordinal);

    private static MemoryGovernanceIssue MismatchIssue(string summary) =>
        new()
        {
            Code = MemoryGovernanceIssueCode.GovernanceResultMismatch,
            Severity = MemoryGovernanceIssueSeverity.Critical,
            Summary = summary
        };

    private static IReadOnlyList<MemoryGovernanceIssue> MergeIssues(
        IReadOnlyList<MemoryGovernanceIssue> existing,
        MemoryGovernanceIssue additional) =>
        existing.Any(issue => issue.Code == additional.Code)
            ? existing
            : existing.Concat([additional]).ToArray();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
