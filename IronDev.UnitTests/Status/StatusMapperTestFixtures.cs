namespace IronDev.UnitTests.Status;

internal static class StatusMapperTestFixtures
{
    internal static readonly DateTimeOffset ObservedAtUtc =
        new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

    internal static AuthorityProfileStatusRequest AuthorityProfileRequest(
        AuthorityProfileKind profileKind = AuthorityProfileKind.BoundedRunAuthority,
        RunAuthorityOperationKind operationKind = RunAuthorityOperationKind.SourceApply,
        OperationEligibilityDecision? eligibilityDecision = null,
        IReadOnlyCollection<string>? evidenceRefs = null,
        DateTimeOffset? grantExpiresAtUtc = null) =>
        new()
        {
            OperationId = "operation:g03:authority-profile",
            OperationKind = operationKind,
            Subject = "G03 authority profile status",
            ProfileKind = profileKind,
            Repository = "repo:g03",
            Branch = "feature/g03",
            RunId = "run:g03",
            PatchHash = "patch-hash:g03",
            ObservedAtUtc = ObservedAtUtc,
            EligibilityDecision = eligibilityDecision ?? EligibleDecision(operationKind),
            GrantExpiresAtUtc = grantExpiresAtUtc,
            EvidenceRefs = evidenceRefs ??
            [
                "bounded-run-authority-grant:g03",
                "operation-eligibility-decision:g03",
                "validation-result:g03"
            ],
            ReceiptRefs = []
        };

    internal static OperationEligibilityDecision EligibleDecision(RunAuthorityOperationKind operationKind) =>
        new()
        {
            IsEligibleUnderProfileAndGrant = true,
            OperationKind = operationKind,
            BlockedReasons = [],
            MissingEvidence = [],
            ForbiddenActions = ["do not execute from operation eligibility decision alone"],
            RequiredIndependentChecks = ["fresh authority", "fresh validation"]
        };

    internal static OperationEligibilityDecision BlockedDecision(
        RunAuthorityOperationKind operationKind,
        IReadOnlyCollection<string>? blockedReasons = null,
        IReadOnlyCollection<string>? missingEvidence = null) =>
        new()
        {
            IsEligibleUnderProfileAndGrant = false,
            OperationKind = operationKind,
            BlockedReasons = blockedReasons ?? ["FocusedG03MissingAuthority"],
            MissingEvidence = missingEvidence ?? ["bounded-run-authority-grant:g03"],
            ForbiddenActions = ["do not execute from blocked eligibility"],
            RequiredIndependentChecks = ["fresh authority", "fresh validation"]
        };

    internal static PatchProposalStatusInput ReadyPatchProposal() =>
        new()
        {
            OperationId = "operation:g03:patch-proposal",
            ProposalId = "proposal:g03",
            PatchHash = "patch-hash:g03",
            Subject = "G03 patch proposal",
            StatusKind = PatchProposalStatusKind.ReadyForReview,
            ArtifactRefs = ["patch-package:g03", "review-summary:g03"],
            ValidationRefs = ["validation-result:g03"],
            BlockedReasons = [],
            MissingEvidence = [],
            ForbiddenActions = [],
            ObservedAtUtc = ObservedAtUtc
        };

    internal static ControlledSourceApplyStatusInput EligibleSourceApply() =>
        new()
        {
            OperationId = "operation:g03:source-apply",
            SourceApplyId = "source-apply:g03",
            Subject = "G03 source apply",
            RepoId = "repo:g03",
            Branch = "feature/g03",
            PatchHash = "patch-hash:g03",
            StatusKind = ControlledSourceApplyStatusKind.Eligible,
            EvidenceRefs =
            [
                "accepted-source-apply-request:g03",
                "policy-satisfaction:g03",
                "dry-run:g03",
                "patch-artifact:g03",
                "rollback-plan:g03",
                "worktree-state:g03"
            ],
            ReceiptRefs = [],
            BlockedReasons = [],
            MissingEvidence = [],
            ForbiddenActions = [],
            ObservedAtUtc = ObservedAtUtc
        };

    internal static void AssertContains(
        IEnumerable<string> values,
        string expected,
        string? because = null) =>
        CollectionAssert.Contains(values.ToList(), expected, because ?? expected);

    internal static void AssertContainsSubstring(
        IEnumerable<string> values,
        string expected,
        string? because = null) =>
        Assert.IsTrue(
            values.Any(value => value.Contains(expected, StringComparison.OrdinalIgnoreCase)),
            because ?? expected);

    internal static void AssertDoesNotContainSubstring(
        IEnumerable<string> values,
        string unexpected,
        string? because = null) =>
        Assert.IsFalse(
            values.Any(value => value.Contains(unexpected, StringComparison.OrdinalIgnoreCase)),
            because ?? unexpected);

    internal static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
