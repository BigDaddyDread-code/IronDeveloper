using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public enum PullRequestCreationGateDecision
{
    Blocked = 0,
    NeedsMoreEvidence,
    CreateDraftPullRequest
}

public enum PullRequestCreationExecutionStatus
{
    Created = 0,
    Blocked
}

public sealed record PullRequestBoundary
{
    public bool EvidenceOnly { get; init; } = true;
    public bool CanCreateDraftPullRequest { get; init; }
    public bool CanCreateNonDraftPullRequest { get; init; }
    public bool CanCommit { get; init; }
    public bool CanStageFiles { get; init; }
    public bool CanPush { get; init; }
    public bool CanForcePush { get; init; }
    public bool CanCreateBranch { get; init; }
    public bool CanMerge { get; init; }
    public bool CanMarkReadyForReview { get; init; }
    public bool CanRequestReviewers { get; init; }
    public bool CanApprove { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanMutateWorkspace { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanSatisfyPolicy { get; init; }
    public bool DraftPullRequestCreated { get; init; }

    public static PullRequestBoundary Evidence { get; } = new();
    public static PullRequestBoundary DraftExecutor { get; } = new() { EvidenceOnly = false, CanCreateDraftPullRequest = true };
    public static PullRequestBoundary DraftReceipt { get; } = new() { EvidenceOnly = false, DraftPullRequestCreated = true };
}

public static class PullRequestBoundaryText
{
    public const string Boundary = """
        Block AM creates a controlled draft pull request only.
        It does not commit.
        It does not push.
        It does not create branches.
        It does not create non-draft PRs.
        It does not mark ready for review.
        It does not request reviewers.
        It does not merge.
        It does not release.
        It does not deploy.
        It does not continue workflow.
        """;
}

public sealed record PullRequestCreationRequestInput
{
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required string BaseBranch { get; init; }
    public required string HeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string CommitPackageRequestId { get; init; }
    public required string CommitReadinessReviewId { get; init; }
    public required string RequestedBy { get; init; }
    public required string Reason { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public DateTimeOffset? RequestedAtUtc { get; init; }
}

public sealed record PullRequestCreationRequest
{
    public required string PullRequestCreationRequestId { get; init; }
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required string BaseBranch { get; init; }
    public required string HeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string CommitPackageRequestId { get; init; }
    public required string CommitReadinessReviewId { get; init; }
    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required string Reason { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public bool DraftRequired { get; init; } = true;
    public bool RequestsReviewers { get; init; }
    public bool RequestsReadyForReview { get; init; }
    public bool RequestsMerge { get; init; }
    public bool RequestsRelease { get; init; }
    public bool RequestsDeploy { get; init; }
    public PullRequestBoundary Boundary { get; init; } = PullRequestBoundary.Evidence;
}

public static class PullRequestCreationRequestWriter
{
    public static PullRequestCreationRequest Create(PullRequestCreationRequestInput input) => new()
    {
        PullRequestCreationRequestId = $"pr_req_{PullRequestHashing.ShortHash($"{input.RunId}|{input.RepositoryFullName}|{input.HeadBranch}|{input.ExpectedHeadSha}")}",
        RunId = PullRequestText.Safe(input.RunId),
        ProjectId = PullRequestText.Safe(input.ProjectId),
        RepositoryFullName = PullRequestText.Safe(input.RepositoryFullName),
        BaseBranch = PullRequestText.Safe(input.BaseBranch),
        HeadBranch = PullRequestText.Safe(input.HeadBranch),
        ExpectedHeadSha = PullRequestText.Safe(input.ExpectedHeadSha),
        CommitPackageRequestId = PullRequestText.Safe(input.CommitPackageRequestId),
        CommitReadinessReviewId = PullRequestText.Safe(input.CommitReadinessReviewId),
        RequestedBy = PullRequestText.Safe(input.RequestedBy),
        RequestedAtUtc = input.RequestedAtUtc ?? DateTimeOffset.UtcNow,
        Reason = PullRequestText.Safe(input.Reason),
        EvidenceRefs = PullRequestText.SafeList(input.EvidenceRefs),
        DraftRequired = true,
        Boundary = PullRequestBoundary.Evidence
    };
}

public sealed record RemoteBranchState
{
    public required string RepositoryFullName { get; init; }
    public required string BranchName { get; init; }
    public bool Exists { get; init; }
    public string? HeadSha { get; init; }
}

public sealed record ExistingPullRequestState
{
    public bool Exists { get; init; }
    public int? PullRequestNumber { get; init; }
    public string? Url { get; init; }
    public bool Draft { get; init; }
}

public sealed record PullRequestBranchValidationReport
{
    public required string PullRequestBranchValidationReportId { get; init; }
    public required string RunId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required string BaseBranch { get; init; }
    public required string HeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public string? ObservedHeadSha { get; init; }
    public bool RemoteHeadBranchExists { get; init; }
    public bool BaseBranchExists { get; init; }
    public bool ExistingOpenPullRequestExists { get; init; }
    public int? ExistingOpenPullRequestNumber { get; init; }
    public string? ExistingOpenPullRequestUrl { get; init; }
    public bool Passed { get; init; }
    public string[] Issues { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public PullRequestBoundary Boundary { get; init; } = PullRequestBoundary.Evidence;
}

public static class PullRequestBranchValidator
{
    public static PullRequestBranchValidationReport Validate(
        PullRequestCreationRequest request,
        RemoteBranchState head,
        RemoteBranchState baseBranch,
        ExistingPullRequestState existingPr,
        DateTimeOffset? now = null)
    {
        var issues = new List<string>();
        if (!string.Equals(head.RepositoryFullName, request.RepositoryFullName, StringComparison.OrdinalIgnoreCase))
            issues.Add("RepositoryFullNameMismatch");
        if (!head.Exists)
            issues.Add("RemoteHeadBranchMissing");
        if (!baseBranch.Exists)
            issues.Add("BaseBranchMissing");
        if (head.Exists && !string.Equals(head.HeadSha, request.ExpectedHeadSha, StringComparison.OrdinalIgnoreCase))
            issues.Add("ExpectedHeadShaMismatch");
        if (existingPr.Exists)
            issues.Add("ExistingOpenPullRequestForHeadBranch");

        return new PullRequestBranchValidationReport
        {
            PullRequestBranchValidationReportId = $"pr_branch_{PullRequestHashing.ShortHash($"{request.PullRequestCreationRequestId}|{head.HeadSha}|{string.Join(",", issues)}")}",
            RunId = request.RunId,
            RepositoryFullName = request.RepositoryFullName,
            BaseBranch = request.BaseBranch,
            HeadBranch = request.HeadBranch,
            ExpectedHeadSha = request.ExpectedHeadSha,
            ObservedHeadSha = PullRequestText.SafeOrNull(head.HeadSha),
            RemoteHeadBranchExists = head.Exists,
            BaseBranchExists = baseBranch.Exists,
            ExistingOpenPullRequestExists = existingPr.Exists,
            ExistingOpenPullRequestNumber = existingPr.PullRequestNumber,
            ExistingOpenPullRequestUrl = PullRequestText.SafeOrNull(existingPr.Url),
            Passed = issues.Count == 0,
            Issues = issues.ToArray(),
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
            Boundary = PullRequestBoundary.Evidence
        };
    }
}

public sealed record PullRequestEvidenceValidationReport
{
    public required string PullRequestEvidenceValidationReportId { get; init; }
    public required string RunId { get; init; }
    public required string CommitPackageRequestId { get; init; }
    public required string CommitReadinessReviewId { get; init; }
    public bool CommitPackageReviewExists { get; init; }
    public bool CommitReadinessReviewReady { get; init; }
    public bool CommitEvidenceHasBlockingGaps { get; init; }
    public bool UnsafeMaterialFound { get; init; }
    public bool ArtifactConsistencyBlocked { get; init; }
    public bool CommitPackageBoundarySafe { get; init; }
    public bool Passed { get; init; }
    public string[] Issues { get; init; } = [];
    public string[] EvidenceRefs { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public PullRequestBoundary Boundary { get; init; } = PullRequestBoundary.Evidence;
}

public static class PullRequestEvidenceValidator
{
    public static PullRequestEvidenceValidationReport Validate(
        PullRequestCreationRequest request,
        CommitReadinessReview? review,
        CommitEvidenceBundle? bundle,
        CommitPackageBoundaryReport? boundaryReport,
        IEnumerable<string>? unsafeMaterialFindings,
        IEnumerable<string>? artifactConsistencyBlockers,
        DateTimeOffset? now = null)
    {
        var unsafeFindings = PullRequestText.SafeList(unsafeMaterialFindings);
        var artifactBlockers = PullRequestText.SafeList(artifactConsistencyBlockers);
        var issues = new List<string>();
        if (review is null)
            issues.Add("MissingCommitReadinessReview");
        if (review is not null && review.Decision != CommitReadinessDecision.ReadyForHumanCommitReview)
            issues.Add("CommitReadinessReviewNotReady");
        if (bundle is null)
            issues.Add("MissingCommitEvidenceBundle");
        if (bundle?.MissingEvidence.Length > 0)
            issues.Add("CommitEvidenceHasBlockingGaps");
        if (boundaryReport is null)
            issues.Add("MissingCommitPackageBoundaryReport");
        if (boundaryReport?.Boundary.CanCreatePullRequest == true || boundaryReport?.Boundary.CanCreateCommit == true || boundaryReport?.Boundary.CanPush == true)
            issues.Add("CommitPackageBoundaryClaimsAuthority");
        if (unsafeFindings.Length > 0)
            issues.Add("UnsafeMaterialFinding");
        if (artifactBlockers.Length > 0)
            issues.Add("ArtifactConsistencyBlocker");

        return new PullRequestEvidenceValidationReport
        {
            PullRequestEvidenceValidationReportId = $"pr_evidence_{PullRequestHashing.ShortHash($"{request.PullRequestCreationRequestId}|{string.Join(",", issues)}")}",
            RunId = request.RunId,
            CommitPackageRequestId = request.CommitPackageRequestId,
            CommitReadinessReviewId = request.CommitReadinessReviewId,
            CommitPackageReviewExists = review is not null,
            CommitReadinessReviewReady = review?.Decision == CommitReadinessDecision.ReadyForHumanCommitReview,
            CommitEvidenceHasBlockingGaps = bundle?.MissingEvidence.Length > 0,
            UnsafeMaterialFound = unsafeFindings.Length > 0,
            ArtifactConsistencyBlocked = artifactBlockers.Length > 0,
            CommitPackageBoundarySafe = boundaryReport is not null && boundaryReport.Boundary is { CanCreatePullRequest: false, CanCreateCommit: false, CanPush: false },
            Passed = issues.Count == 0,
            Issues = issues.ToArray(),
            EvidenceRefs = PullRequestText.SafeList(["commit-readiness-review.json", "commit-evidence-bundle.json", "commit-package-boundary-report.json"]),
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
            Boundary = PullRequestBoundary.Evidence
        };
    }
}

public sealed record PullRequestTextProposal
{
    public required string PullRequestTextProposalId { get; init; }
    public required string RunId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required string BaseBranch { get; init; }
    public required string HeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string ProposedTitle { get; init; }
    public required string ProposedBody { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public string[] RiskNotes { get; init; } = [];
    public bool HumanEditRequired { get; init; } = true;
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public PullRequestBoundary Boundary { get; init; } = PullRequestBoundary.Evidence;
}

public static class PullRequestTextProposalBuilder
{
    private static readonly string[] ForbiddenPhrases =
    [
        "approved",
        "ready to merge",
        "ready for review",
        "ready to release",
        "deploy now",
        "ship it",
        "auto-merge",
        "autonomous",
        "self-approved",
        "policy satisfied"
    ];

    public static PullRequestTextProposal Build(
        PullRequestCreationRequest request,
        CommitFileManifest? manifest,
        CommitEvidenceBundle? evidence,
        CommitReadinessReview? review,
        IEnumerable<string>? riskNotes = null,
        DateTimeOffset? now = null)
    {
        var title = Sanitize(request.Reason);
        if (string.IsNullOrWhiteSpace(title))
            title = "Controlled draft pull request";
        if (title.Length > 90)
            title = title[..90].TrimEnd();

        var changedFiles = manifest?.IncludedFiles.Length > 0 ? manifest.IncludedFiles : ["changed files unavailable"];
        var validation = evidence?.ValidationEvidenceRefs.Length > 0 ? evidence.ValidationEvidenceRefs : ["validation evidence unavailable"];
        var risks = PullRequestText.SafeList(riskNotes);
        if (risks.Length == 0)
            risks = ["Manual review remains required before ready-for-review, merge, release, or deployment."];

        var body = $"""
            ## Summary

            {title}

            ## Changed Files

            {RenderBullets(changedFiles)}

            ## Validation Evidence

            {RenderBullets(validation)}

            ## Known Risks

            {RenderBullets(risks)}

            ## Commit Package

            - Commit package request: `{request.CommitPackageRequestId}`
            - Commit readiness review: `{request.CommitReadinessReviewId}`
            - Commit readiness decision: `{review?.Decision.ToString() ?? "missing"}`
            - Expected head SHA: `{request.ExpectedHeadSha}`

            ## Boundary

            Block AM creates a controlled draft pull request only. A draft PR is not approval, merge readiness, release readiness, deployment readiness, or workflow continuation.

            Manual review remains required before any ready-for-review, reviewer request, merge, release, deployment, or continuation action.
            """;

        return new PullRequestTextProposal
        {
            PullRequestTextProposalId = $"pr_text_{PullRequestHashing.ShortHash($"{request.PullRequestCreationRequestId}|{title}")}",
            RunId = request.RunId,
            RepositoryFullName = request.RepositoryFullName,
            BaseBranch = request.BaseBranch,
            HeadBranch = request.HeadBranch,
            ExpectedHeadSha = request.ExpectedHeadSha,
            ProposedTitle = title,
            ProposedBody = Sanitize(body),
            EvidenceRefs = PullRequestText.SafeList([request.CommitPackageRequestId, request.CommitReadinessReviewId, .. validation]),
            RiskNotes = risks,
            HumanEditRequired = true,
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
            Boundary = PullRequestBoundary.Evidence
        };
    }

    public static bool ContainsForbiddenPhrase(string? value) =>
        !string.IsNullOrWhiteSpace(value) && ForbiddenPhrases.Any(phrase => value.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    private static string Sanitize(string value)
    {
        var result = PullRequestText.Safe(value);
        foreach (var phrase in ForbiddenPhrases)
            result = Regex.Replace(result, Regex.Escape(phrase), "requires review", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return result;
    }

    private static string RenderBullets(IEnumerable<string> values) =>
        string.Join(Environment.NewLine, PullRequestText.SafeList(values).DefaultIfEmpty("(none)").Select(item => $"- {item}"));
}

public sealed record PullRequestCreationGate
{
    public required string PullRequestCreationGateId { get; init; }
    public required string RunId { get; init; }
    public required PullRequestCreationGateDecision Decision { get; init; }
    public string? AllowedOperation { get; init; }
    public string[] Reasons { get; init; } = [];
    public required string PullRequestCreationRequestId { get; init; }
    public string? ConscienceDecisionId { get; init; }
    public string? ThoughtLedgerRef { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public PullRequestBoundary Boundary { get; init; } = PullRequestBoundary.Evidence;
}

public static class PullRequestCreationGateBuilder
{
    public static PullRequestCreationGate Build(
        PullRequestCreationRequest request,
        PullRequestBranchValidationReport? branchValidation,
        PullRequestEvidenceValidationReport? evidenceValidation,
        PullRequestTextProposal? textProposal,
        CommitReadinessReview? commitReview,
        ConscienceDecision? conscienceDecision,
        string? thoughtLedgerRef,
        DateTimeOffset? now = null)
    {
        var reasons = new List<string>();
        if (!request.DraftRequired) reasons.Add("DraftRequiredMissing");
        if (request.RequestsReviewers) reasons.Add("ReviewerRequestForbidden");
        if (request.RequestsReadyForReview) reasons.Add("ReadyForReviewRequestForbidden");
        if (request.RequestsMerge) reasons.Add("MergeRequestForbidden");
        if (request.RequestsRelease) reasons.Add("ReleaseRequestForbidden");
        if (request.RequestsDeploy) reasons.Add("DeployRequestForbidden");
        if (branchValidation is null) reasons.Add("MissingBranchValidation");
        if (branchValidation is { Passed: false }) reasons.AddRange(branchValidation.Issues);
        if (evidenceValidation is null) reasons.Add("MissingEvidenceValidation");
        if (evidenceValidation is { Passed: false }) reasons.AddRange(evidenceValidation.Issues);
        if (textProposal is null) reasons.Add("MissingPullRequestTextProposal");
        if (textProposal is not null && (PullRequestTextProposalBuilder.ContainsForbiddenPhrase(textProposal.ProposedTitle) || PullRequestTextProposalBuilder.ContainsForbiddenPhrase(textProposal.ProposedBody)))
            reasons.Add("ForbiddenAuthorityLanguage");
        if (commitReview is null) reasons.Add("MissingCommitReadinessReview");
        if (commitReview is not null && commitReview.Decision != CommitReadinessDecision.ReadyForHumanCommitReview) reasons.Add("CommitReadinessReviewNotReady");
        if (conscienceDecision is null) reasons.Add("MissingConscienceDecision");
        if (conscienceDecision is not null && conscienceDecision.ActionKind != GovernedActionKind.DraftPullRequestCreation) reasons.Add("ConscienceDecisionActionMismatch");
        if (conscienceDecision is not null && conscienceDecision.Decision != ConscienceDecisionOutcome.Allow) reasons.Add("ConscienceDecisionDoesNotAllowDraftPullRequest");
        if (string.IsNullOrWhiteSpace(thoughtLedgerRef)) reasons.Add("MissingThoughtLedgerRef");

        var decision = reasons.Count == 0 ? PullRequestCreationGateDecision.CreateDraftPullRequest : PullRequestCreationGateDecision.Blocked;
        return new PullRequestCreationGate
        {
            PullRequestCreationGateId = $"pr_gate_{PullRequestHashing.ShortHash($"{request.PullRequestCreationRequestId}|{decision}|{string.Join(",", reasons)}")}",
            RunId = request.RunId,
            Decision = decision,
            AllowedOperation = decision == PullRequestCreationGateDecision.CreateDraftPullRequest ? nameof(PullRequestCreationGateDecision.CreateDraftPullRequest) : null,
            Reasons = reasons.Count == 0 ? ["Draft pull request creation is gated for the verified branch and expected head SHA only."] : reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            PullRequestCreationRequestId = request.PullRequestCreationRequestId,
            ConscienceDecisionId = conscienceDecision?.DecisionId,
            ThoughtLedgerRef = PullRequestText.SafeOrNull(thoughtLedgerRef),
            EvidenceRefs = PullRequestText.SafeList(["pull-request-creation-request.json", "pull-request-branch-validation.json", "pull-request-evidence-validation.json", "pull-request-text-proposal.json", "commit-readiness-review.json"]),
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
            Boundary = PullRequestBoundary.Evidence
        };
    }
}

public sealed record PullRequestDraftCreateCommand
{
    public required string RepositoryFullName { get; init; }
    public required string BaseBranch { get; init; }
    public required string HeadBranch { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required bool Draft { get; init; }
};

public sealed record PullRequestCreatedResult
{
    public required int Number { get; init; }
    public required string Url { get; init; }
    public required bool Draft { get; init; }
}

public interface IDraftPullRequestCreator
{
    Task<PullRequestCreatedResult> CreateDraftPullRequestAsync(PullRequestDraftCreateCommand command, CancellationToken cancellationToken);
}

public sealed record PullRequestCreationReceipt
{
    public required string PullRequestCreationReceiptId { get; init; }
    public required string RunId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string PullRequestUrl { get; init; }
    public required string BaseBranch { get; init; }
    public required string HeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string ObservedHeadSha { get; init; }
    public required bool Draft { get; init; }
    public required string CreatedBy { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required string GateDecisionId { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public PullRequestBoundary Boundary { get; init; } = PullRequestBoundary.DraftReceipt;
}

public sealed record PullRequestStatusReport
{
    public required string PullRequestStatusReportId { get; init; }
    public required string RunId { get; init; }
    public int? PullRequestNumber { get; init; }
    public string? PullRequestUrl { get; init; }
    public bool Draft { get; init; }
    public bool MarkedReadyForReview { get; init; }
    public bool ReviewersRequested { get; init; }
    public bool Merged { get; init; }
    public bool Released { get; init; }
    public bool Deployed { get; init; }
    public bool WorkflowContinued { get; init; }
    public PullRequestBoundary Boundary { get; init; } = PullRequestBoundary.Evidence;
}

public sealed record PullRequestCreationExecutionResult
{
    public required PullRequestCreationExecutionStatus Status { get; init; }
    public string[] Issues { get; init; } = [];
    public PullRequestCreationReceipt? Receipt { get; init; }
    public PullRequestStatusReport? StatusReport { get; init; }
    public PullRequestBoundary Boundary { get; init; } = PullRequestBoundary.Evidence;
}

public static class DraftPullRequestExecutor
{
    public static async Task<PullRequestCreationExecutionResult> CreateDraftAsync(
        PullRequestCreationRequest request,
        PullRequestCreationGate gate,
        PullRequestTextProposal proposal,
        RemoteBranchState observedHead,
        ExistingPullRequestState existingPr,
        IDraftPullRequestCreator creator,
        string createdBy,
        CancellationToken cancellationToken,
        DateTimeOffset? now = null)
    {
        var issues = new List<string>();
        if (gate.Decision != PullRequestCreationGateDecision.CreateDraftPullRequest) issues.Add("GateDidNotAllowCreateDraftPullRequest");
        if (!string.Equals(gate.AllowedOperation, nameof(PullRequestCreationGateDecision.CreateDraftPullRequest), StringComparison.Ordinal)) issues.Add("PullRequestCreationGateOperationMismatch");
        if (!string.Equals(gate.PullRequestCreationRequestId, request.PullRequestCreationRequestId, StringComparison.OrdinalIgnoreCase)) issues.Add("PullRequestCreationGateRequestMismatch");
        if (!request.DraftRequired) issues.Add("DraftRequiredMissing");
        if (!observedHead.Exists) issues.Add("RemoteHeadBranchMissing");
        if (!string.Equals(observedHead.HeadSha, request.ExpectedHeadSha, StringComparison.OrdinalIgnoreCase)) issues.Add("ExpectedHeadShaMismatch");
        if (existingPr.Exists) issues.Add("ExistingOpenPullRequestForHeadBranch");
        if (!string.Equals(proposal.RepositoryFullName, request.RepositoryFullName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(proposal.BaseBranch, request.BaseBranch, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(proposal.HeadBranch, request.HeadBranch, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(proposal.ExpectedHeadSha, request.ExpectedHeadSha, StringComparison.OrdinalIgnoreCase))
            issues.Add("PullRequestTextProposalRequestMismatch");
        if (PullRequestTextProposalBuilder.ContainsForbiddenPhrase(proposal.ProposedTitle) || PullRequestTextProposalBuilder.ContainsForbiddenPhrase(proposal.ProposedBody))
            issues.Add("ForbiddenAuthorityLanguage");

        if (issues.Count > 0)
            return new PullRequestCreationExecutionResult
            {
                Status = PullRequestCreationExecutionStatus.Blocked,
                Issues = issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Boundary = PullRequestBoundary.Evidence
            };

        var created = await creator.CreateDraftPullRequestAsync(new PullRequestDraftCreateCommand
        {
            RepositoryFullName = request.RepositoryFullName,
            BaseBranch = request.BaseBranch,
            HeadBranch = request.HeadBranch,
            Title = proposal.ProposedTitle,
            Body = proposal.ProposedBody,
            Draft = true
        }, cancellationToken).ConfigureAwait(false);

        if (!created.Draft)
            return new PullRequestCreationExecutionResult
            {
                Status = PullRequestCreationExecutionStatus.Blocked,
                Issues = ["DraftPullRequestCreatorReturnedNonDraftPullRequest"],
                Boundary = PullRequestBoundary.Evidence
            };

        var createdAt = now ?? DateTimeOffset.UtcNow;
        var receipt = new PullRequestCreationReceipt
        {
            PullRequestCreationReceiptId = $"pr_receipt_{PullRequestHashing.ShortHash($"{request.PullRequestCreationRequestId}|{created.Number}|{created.Url}")}",
            RunId = request.RunId,
            RepositoryFullName = request.RepositoryFullName,
            PullRequestNumber = created.Number,
            PullRequestUrl = PullRequestText.Safe(created.Url),
            BaseBranch = request.BaseBranch,
            HeadBranch = request.HeadBranch,
            ExpectedHeadSha = request.ExpectedHeadSha,
            ObservedHeadSha = observedHead.HeadSha ?? string.Empty,
            Draft = true,
            CreatedBy = PullRequestText.Safe(createdBy),
            CreatedAtUtc = createdAt,
            GateDecisionId = gate.PullRequestCreationGateId,
            EvidenceRefs = PullRequestText.SafeList([gate.PullRequestCreationGateId, proposal.PullRequestTextProposalId, request.PullRequestCreationRequestId]),
            Boundary = PullRequestBoundary.DraftReceipt
        };
        var status = new PullRequestStatusReport
        {
            PullRequestStatusReportId = $"pr_status_{PullRequestHashing.ShortHash(receipt.PullRequestCreationReceiptId)}",
            RunId = request.RunId,
            PullRequestNumber = created.Number,
            PullRequestUrl = PullRequestText.Safe(created.Url),
            Draft = true,
            Boundary = PullRequestBoundary.Evidence
        };

        return new PullRequestCreationExecutionResult
        {
            Status = PullRequestCreationExecutionStatus.Created,
            Receipt = receipt,
            StatusReport = status,
            Boundary = PullRequestBoundary.DraftExecutor
        };
    }
}

public sealed record PullRequestCreationBypassReport
{
    public required string PullRequestCreationBypassReportId { get; init; }
    public required string RunId { get; init; }
    public string[] EvidenceSubjects { get; init; } = [];
    public bool PullRequestCreated { get; init; }
    public bool NonDraftPullRequestCreated { get; init; }
    public bool CommitCreated { get; init; }
    public bool PushPerformed { get; init; }
    public bool ReadyForReviewMarked { get; init; }
    public bool ReviewersRequested { get; init; }
    public bool Merged { get; init; }
    public bool Released { get; init; }
    public bool Deployed { get; init; }
    public bool WorkflowContinued { get; init; }
    public PullRequestBoundary Boundary { get; init; } = PullRequestBoundary.Evidence;
}

public static class PullRequestCreationBypassEvaluator
{
    public static PullRequestCreationBypassReport Evaluate(string runId, IEnumerable<string> evidenceSubjects) => new()
    {
        PullRequestCreationBypassReportId = $"pr_bypass_{PullRequestHashing.ShortHash(runId)}",
        RunId = PullRequestText.Safe(runId),
        EvidenceSubjects = PullRequestText.SafeList(evidenceSubjects),
        Boundary = PullRequestBoundary.Evidence
    };

    public static bool CanCreatePullRequest(object? evidence) => false;
}

internal static class PullRequestText
{
    public static string Safe(string? value) => (value ?? string.Empty).Trim();
    public static string? SafeOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public static string[] SafeList(IEnumerable<string>? values) => values?
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Select(item => item.Trim().Replace('\\', '/'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
        .ToArray() ?? [];
}

internal static class PullRequestHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
