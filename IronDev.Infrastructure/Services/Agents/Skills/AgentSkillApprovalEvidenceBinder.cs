using IronDev.Core.Agents.Skills;

namespace IronDev.Infrastructure.Services.Agents.Skills;

public sealed class AgentSkillApprovalEvidenceBinder : IAgentSkillApprovalEvidenceBinder
{
    private static readonly IReadOnlySet<string> ApprovedSkillIds = new HashSet<string>(StringComparer.Ordinal)
    {
        AgentSkillIds.WorkspacePrepare,
        AgentSkillIds.WorkspaceValidate,
        AgentSkillIds.WorkspaceDiff,
        AgentSkillIds.WorkspacePromotionPackage,
        AgentSkillIds.WorkspaceFailurePackage
    };

    private readonly Func<DateTimeOffset> _utcNow;

    public AgentSkillApprovalEvidenceBinder(Func<DateTimeOffset>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public AgentSkillApprovalEvidenceBinding Bind(AgentSkillApprovalEvidenceBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestPackage);
        ArgumentNullException.ThrowIfNull(request.ReviewPackage);

        var evidence = request.ApprovalEvidence ?? request.ReviewPackage.ApprovalEvidence;
        if (evidence is null)
        {
            return Invalid(
                approvalId: string.Empty,
                request.RequestPackage.RequestId,
                request.ReviewPackage.ReviewId,
                request.RequestPackage.SkillId,
                ["Approval evidence was not supplied."]);
        }

        var blockers = new List<string>();
        var warnings = new List<string>(evidence.Warnings);
        var now = request.NowUtc ?? _utcNow();
        var expectedWorkspaceMutation = ApprovedSkillIds.Contains(request.RequestPackage.SkillId);

        if (!evidence.ApprovalEvidenceAvailable)
            blockers.Add("Approval evidence is not available.");
        if (!ApprovedSkillIds.Contains(request.RequestPackage.SkillId))
            blockers.Add("Approval evidence cannot authorize this skill.");
        if (!string.Equals(evidence.Decision, AgentSkillApprovalDecisions.Approved, StringComparison.Ordinal))
            blockers.Add($"Approval evidence decision must be approved, not {evidence.Decision}.");
        if (!string.Equals(evidence.ApprovedByKind, AgentSkillApprovalActorKinds.Human, StringComparison.Ordinal) &&
            !string.Equals(evidence.ApprovedByKind, AgentSkillApprovalActorKinds.SystemTestFixture, StringComparison.Ordinal))
            blockers.Add("Approval evidence approvedByKind must be human or system_test_fixture.");
        if (string.IsNullOrWhiteSpace(evidence.ApprovedBy))
            blockers.Add("Approval evidence is missing approvedBy.");
        if (string.IsNullOrWhiteSpace(evidence.Reason))
            blockers.Add("Approval evidence is missing reason.");
        if (!string.Equals(evidence.ProjectId, request.RequestPackage.ProjectId, StringComparison.Ordinal))
            blockers.Add("Approval evidence projectId does not match the request package.");
        if (!string.Equals(evidence.RequestId, request.RequestPackage.RequestId, StringComparison.Ordinal))
            blockers.Add("Approval evidence requestId does not match the request package.");
        if (!string.Equals(evidence.ReviewId, request.ReviewPackage.ReviewId, StringComparison.Ordinal))
            blockers.Add("Approval evidence reviewId does not match the review package.");
        if (!string.Equals(evidence.SkillId, request.RequestPackage.SkillId, StringComparison.Ordinal) ||
            !string.Equals(evidence.SkillId, request.ReviewPackage.SkillId, StringComparison.Ordinal))
            blockers.Add("Approval evidence skillId does not match the request/review package.");
        if (!string.Equals(evidence.ApprovedAction, AgentSkillRequestContextRecommendedActions.ExecuteApprovedRequest, StringComparison.Ordinal))
            blockers.Add("Approval evidence approvedAction must be execute_approved_request.");
        if (evidence.ApprovedUtc == default)
            blockers.Add("Approval evidence is missing approvedUtc.");
        if (evidence.ExpiresUtc is not null && evidence.ExpiresUtc <= now)
            blockers.Add("Approval evidence has expired.");
        if (!evidence.AllowsExecution)
            blockers.Add("Approval evidence does not allow execution.");
        if (evidence.AllowsSourceMutation)
            blockers.Add("Approval evidence cannot authorize source mutation.");
        if (evidence.AllowsExternalSystem)
            blockers.Add("Approval evidence cannot authorize external system access.");
        if (evidence.AllowsTicketCreation)
            blockers.Add("Approval evidence cannot authorize ticket creation.");
        if (evidence.AllowsMemoryWrite)
            blockers.Add("Approval evidence cannot authorize memory writes.");
        if (evidence.AllowsGitOperation)
            blockers.Add("Approval evidence cannot authorize git operations.");
        if (evidence.AllowsGithubOperation)
            blockers.Add("Approval evidence cannot authorize GitHub operations.");
        if (evidence.AllowsWorkspaceMutation != expectedWorkspaceMutation)
            blockers.Add(expectedWorkspaceMutation
                ? "Approval evidence must allow workspace-local evidence mutation for this skill."
                : "Approval evidence cannot authorize workspace mutation for this skill.");

        blockers.AddRange(evidence.Blockers);

        return new AgentSkillApprovalEvidenceBinding
        {
            BindingValid = blockers.Count == 0,
            ApprovalId = evidence.ApprovalId,
            RequestId = evidence.RequestId,
            ReviewId = evidence.ReviewId,
            SkillId = evidence.SkillId,
            AllowsExecution = blockers.Count == 0 && evidence.AllowsExecution,
            AllowsWorkspaceMutation = blockers.Count == 0 && evidence.AllowsWorkspaceMutation,
            EvidencePaths = evidence.EvidencePaths
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Warnings = warnings
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Blockers = blockers
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static AgentSkillApprovalEvidenceBinding Invalid(
        string approvalId,
        string requestId,
        string reviewId,
        string skillId,
        IReadOnlyList<string> blockers) =>
        new()
        {
            BindingValid = false,
            ApprovalId = approvalId,
            RequestId = requestId,
            ReviewId = reviewId,
            SkillId = skillId,
            AllowsExecution = false,
            AllowsWorkspaceMutation = false,
            EvidencePaths = [],
            Warnings = [],
            Blockers = blockers
        };
}
