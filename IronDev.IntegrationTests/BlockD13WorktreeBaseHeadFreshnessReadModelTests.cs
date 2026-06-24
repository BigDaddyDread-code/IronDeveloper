using System.Reflection;
using System.Text.RegularExpressions;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockD13WorktreeBaseHeadFreshnessReadModelTests
{
    private const string TenantId = "tenant-d13";
    private const string ProjectId = "project-d13";
    private const string OperationId = "op_0000000000000013";
    private const string CorrelationId = "corr_3123456789abcdef";
    private const string RepositoryIdentity = "BigDaddyDread-code/IronDeveloper";
    private const string BaseCommitSha = "1111111111111111111111111111111111111111";
    private const string HeadCommitSha = "2222222222222222222222222222222222222222";
    private const string MovedCommitSha = "3333333333333333333333333333333333333333";
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
    private static readonly DateTimeOffset CapturedAtUtc = DateTimeOffset.Parse("2026-06-24T11:20:00Z");
    private static readonly DateTimeOffset RecordedAtUtc = DateTimeOffset.Parse("2026-06-24T11:25:00Z");
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-24T11:30:00Z");

    [TestMethod]
    public void ValidRequestWithNoObservations_ReturnsNoObservations()
    {
        var result = Assemble([Rule()], [Expectation("expect-main")], []);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(WorktreeBaseHeadFreshnessResolutionStatus.NoObservations, result.ResolutionStatus);
        Assert.AreEqual(0, result.Assessments.Count);
        AssertContains(result.ForbiddenAuthorityImplications, "fresh worktree/base/head metadata is not authority");
    }

    [TestMethod]
    public void MatchingCleanWorktreeBaseHeadMetadata_IsFresh()
    {
        var result = Assemble([Rule()], [Expectation("expect-main")], [Observation("observe-main")]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(WorktreeBaseHeadFreshnessResolutionStatus.Assessed, result.ResolutionStatus);
        Assert.AreEqual(WorktreeBaseHeadFreshnessState.Fresh, result.Assessments[0].FreshnessState);
        Assert.AreEqual(TimeSpan.FromMinutes(30), result.Assessments[0].Age);
        Assert.AreEqual("rule-worktree-base-head", result.Assessments[0].RuleId);
        AssertContains(result.Warnings, "fresh worktree/base/head metadata is not authority");
    }

    [DataTestMethod]
    [DataRow(WorktreeStateKind.Dirty, true, false, false, WorktreeBaseHeadFreshnessState.WorktreeChanged, "WorktreeChanged")]
    [DataRow(WorktreeStateKind.UntrackedOnly, false, true, false, WorktreeBaseHeadFreshnessState.WorktreeChanged, "WorktreeChanged")]
    [DataRow(WorktreeStateKind.Conflicted, false, false, true, WorktreeBaseHeadFreshnessState.WorktreeConflicted, "WorktreeConflicted")]
    public void WorktreeStateClassifiesChangeOrConflict(
        WorktreeStateKind worktreeState,
        bool hasUncommitted,
        bool hasUntracked,
        bool hasConflicts,
        WorktreeBaseHeadFreshnessState expectedState,
        string expectedReason)
    {
        var result = Assemble(
            [Rule()],
            [Expectation("expect-main")],
            [Observation("observe-main", worktreeState: worktreeState, hasUncommittedChanges: hasUncommitted, hasUntrackedFiles: hasUntracked, hasConflicts: hasConflicts)]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(expectedState, result.Assessments[0].FreshnessState);
        Assert.AreEqual(expectedReason, result.Assessments[0].Reason);
    }

    [DataTestMethod]
    [DataRow("base-branch", "release/d13", BaseCommitSha, WorktreeBaseHeadFreshnessState.BaseMoved, "BaseBranchMoved")]
    [DataRow("base-commit", "main", MovedCommitSha, WorktreeBaseHeadFreshnessState.BaseMoved, "BaseCommitMoved")]
    [DataRow("head-branch", "main", BaseCommitSha, WorktreeBaseHeadFreshnessState.HeadMoved, "HeadBranchMoved")]
    [DataRow("head-commit", "main", BaseCommitSha, WorktreeBaseHeadFreshnessState.HeadMoved, "HeadCommitMoved")]
    public void BaseAndHeadMovementAreDiagnosticOnly(
        string movement,
        string branchValue,
        string commitValue,
        WorktreeBaseHeadFreshnessState expectedState,
        string expectedReason)
    {
        var observation = movement switch
        {
            "base-branch" => Observation("observe-main", baseBranch: branchValue),
            "base-commit" => Observation("observe-main", baseCommitSha: commitValue),
            "head-branch" => Observation("observe-main", headBranch: branchValue),
            "head-commit" => Observation("observe-main", headCommitSha: MovedCommitSha),
            _ => throw new InvalidOperationException(movement)
        };

        var result = Assemble([Rule()], [Expectation("expect-main")], [observation]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(expectedState, result.Assessments[0].FreshnessState);
        Assert.AreEqual(expectedReason, result.Assessments[0].Reason);
    }

    [DataTestMethod]
    [DataRow(HeadStateKind.Detached, WorktreeBaseHeadFreshnessState.HeadDetached, "HeadDetached")]
    [DataRow(HeadStateKind.Missing, WorktreeBaseHeadFreshnessState.HeadMissing, "HeadMissing")]
    public void HeadDetachedOrMissingAreClassified(HeadStateKind headState, WorktreeBaseHeadFreshnessState expectedState, string expectedReason)
    {
        var result = Assemble(
            [Rule()],
            [Expectation("expect-main")],
            [Observation("observe-main", headState: headState, headBranch: headState == HeadStateKind.Detached ? null : null, headCommitSha: headState == HeadStateKind.Missing ? null : HeadCommitSha)]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(expectedState, result.Assessments[0].FreshnessState);
        Assert.AreEqual(expectedReason, result.Assessments[0].Reason);
    }

    [TestMethod]
    public void RepositoryMismatch_IsClassifiedRepositoryMismatch()
    {
        var result = Assemble(
            [Rule()],
            [Expectation("expect-main")],
            [Observation("observe-main", repositoryIdentity: "OtherOwner/IronDeveloper")]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(WorktreeBaseHeadFreshnessState.RepositoryMismatch, result.Assessments[0].FreshnessState);
        Assert.AreEqual("RepositoryMismatch", result.Assessments[0].Reason);
    }

    [DataTestMethod]
    [DataRow(20, 60, 30, WorktreeBaseHeadFreshnessState.ObservationStale)]
    [DataRow(10, 20, 30, WorktreeBaseHeadFreshnessState.ObservationExpired)]
    public void ObservationAgeClassifiesStaleAndExpired(int freshMinutes, int expiresMinutes, int observedAgeMinutes, WorktreeBaseHeadFreshnessState expectedState)
    {
        var result = Assemble(
            [Rule(freshFor: TimeSpan.FromMinutes(freshMinutes), expiresAfter: TimeSpan.FromMinutes(expiresMinutes))],
            [Expectation("expect-main")],
            [Observation("observe-main", observedAtUtc: AsOfUtc.AddMinutes(-observedAgeMinutes))]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(expectedState, result.Assessments[0].FreshnessState);
        AssertContains(result.Warnings, "stale or expired observation metadata does not choose next safe action");
    }

    [TestMethod]
    public void MissingRule_ReturnsMissingRules()
    {
        var result = Assemble([], [Expectation("expect-main")], [Observation("observe-main")]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(WorktreeBaseHeadFreshnessResolutionStatus.MissingRules, result.ResolutionStatus);
        Assert.AreEqual(WorktreeBaseHeadFreshnessState.MissingRule, result.Assessments[0].FreshnessState);
        Assert.AreEqual("WorktreeBaseHeadFreshnessRuleMissing", result.Assessments[0].Reason);
    }

    [TestMethod]
    public void MissingExpectation_ReturnsMissingExpectations()
    {
        var result = Assemble([Rule()], [], [Observation("observe-main")]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(WorktreeBaseHeadFreshnessResolutionStatus.MissingExpectations, result.ResolutionStatus);
        Assert.AreEqual(WorktreeBaseHeadFreshnessState.MissingExpectation, result.Assessments[0].FreshnessState);
        Assert.AreEqual("WorktreeBaseHeadExpectationMissing", result.Assessments[0].Reason);
    }

    [TestMethod]
    public void MissingObservation_ReturnsMissingObservations()
    {
        var result = Assemble(
            [Rule()],
            [
                Expectation("expect-main", correlationId: CorrelationId),
                Expectation("expect-release", correlationId: "corr_3123456789abcdee", repositoryIdentity: "BigDaddyDread-code/IronDeveloperRelease")
            ],
            [Observation("observe-main", correlationId: CorrelationId)]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(WorktreeBaseHeadFreshnessResolutionStatus.MissingObservations, result.ResolutionStatus);
        Assert.IsTrue(result.Assessments.Any(static assessment => assessment.FreshnessState == WorktreeBaseHeadFreshnessState.MissingObservation));
    }

    [TestMethod]
    public void MixedFreshnessStates_ReturnsMixedFreshness()
    {
        var result = Assemble(
            [Rule()],
            [
                Expectation("expect-fresh", correlationId: "corr_3123456789abcde1", repositoryIdentity: "repo/fresh"),
                Expectation("expect-dirty", correlationId: "corr_3123456789abcde2", repositoryIdentity: "repo/dirty")
            ],
            [
                Observation("observe-fresh", correlationId: "corr_3123456789abcde1", repositoryIdentity: "repo/fresh"),
                Observation("observe-dirty", correlationId: "corr_3123456789abcde2", repositoryIdentity: "repo/dirty", worktreeState: WorktreeStateKind.Dirty, hasUncommittedChanges: true)
            ]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(WorktreeBaseHeadFreshnessResolutionStatus.MixedFreshness, result.ResolutionStatus);
        CollectionAssert.AreEquivalent(
            new[]
            {
                WorktreeBaseHeadFreshnessState.Fresh,
                WorktreeBaseHeadFreshnessState.WorktreeChanged
            },
            result.Assessments.Select(static assessment => assessment.FreshnessState).ToArray());
    }

    [TestMethod]
    public void AgeIsCalculatedFromSuppliedAsOfUtc()
    {
        var suppliedAsOf = DateTimeOffset.Parse("2026-06-24T15:00:00Z");
        var result = Assemble(
            [Rule(freshFor: TimeSpan.FromHours(4), expiresAfter: TimeSpan.FromHours(8))],
            [Expectation("expect-main")],
            [Observation("observe-main", observedAtUtc: DateTimeOffset.Parse("2026-06-24T12:00:00Z"))],
            asOfUtc: suppliedAsOf);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(TimeSpan.FromHours(3), result.Assessments[0].Age);
        Assert.AreEqual(suppliedAsOf, result.AsOfUtc);
    }

    [TestMethod]
    public void AsOfBeforeObservedTimestamp_ReturnsUnassessable()
    {
        var result = Assemble(
            [Rule()],
            [Expectation("expect-main")],
            [Observation("observe-main", observedAtUtc: AsOfUtc.AddMinutes(1))]);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(WorktreeBaseHeadFreshnessResolutionStatus.Unassessable, result.ResolutionStatus);
        AssertContains(result.Issues, "WorktreeBaseHeadObservationObservedAfterAsOf");
    }

    [TestMethod]
    public void ObservationRecordedBeforeObserved_FailsClosed()
    {
        var result = Assemble(
            [Rule()],
            [Expectation("expect-main")],
            [Observation("observe-main", observedAtUtc: ObservedAtUtc, recordedAtUtc: ObservedAtUtc.AddSeconds(-1))]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "WorktreeBaseHeadObservationRecordedBeforeObserved");
    }

    [TestMethod]
    public void ExpectationRecordedBeforeCaptured_FailsClosed()
    {
        var result = Assemble(
            [Rule()],
            [Expectation("expect-main", capturedAtUtc: CapturedAtUtc, recordedAtUtc: CapturedAtUtc.AddSeconds(-1))],
            [Observation("observe-main")]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "WorktreeBaseHeadExpectationRecordedBeforeCaptured");
    }

    [DataTestMethod]
    [DataRow("rule", "DuplicateWorktreeBaseHeadFreshnessRuleId:rule-duplicate")]
    [DataRow("expectation", "DuplicateWorktreeBaseHeadExpectationId:expect-duplicate")]
    [DataRow("observation", "DuplicateWorktreeBaseHeadObservationId:observe-duplicate")]
    public void DuplicateIds_ReturnAmbiguousObservations(string duplicateKind, string expectedDiagnostic)
    {
        var rules = duplicateKind == "rule"
            ? new[] { Rule(ruleId: "rule-duplicate"), Rule(ruleId: "rule-duplicate") }
            : [Rule()];
        var expectations = duplicateKind == "expectation"
            ? new[] { Expectation("expect-duplicate"), Expectation("expect-duplicate") }
            : [Expectation("expect-main")];
        var observations = duplicateKind == "observation"
            ? new[] { Observation("observe-duplicate"), Observation("observe-duplicate") }
            : [Observation("observe-main")];

        var result = Assemble(rules, expectations, observations);

        AssertAmbiguous(result, expectedDiagnostic);
    }

    [DataTestMethod]
    [DataRow("rule", "ConflictingWorktreeBaseHeadFreshnessRuleMetadata:rule-conflict")]
    [DataRow("expectation", "ConflictingWorktreeBaseHeadExpectationMetadata:expect-conflict")]
    [DataRow("observation", "ConflictingWorktreeBaseHeadObservationMetadata:observe-conflict")]
    public void ConflictingMetadata_ReturnsAmbiguousObservations(string target, string expectedDiagnostic)
    {
        var result = target switch
        {
            "rule" => Assemble(
                [Rule(ruleId: "rule-conflict", source: "source-a"), Rule(ruleId: "rule-conflict", source: "source-b")],
                [Expectation("expect-main")],
                [Observation("observe-main")]),
            "expectation" => Assemble(
                [Rule()],
                [Expectation("expect-conflict", source: "source-a"), Expectation("expect-conflict", source: "source-b")],
                [Observation("observe-main")]),
            "observation" => Assemble(
                [Rule()],
                [Expectation("expect-main")],
                [Observation("observe-conflict", source: "source-a"), Observation("observe-conflict", source: "source-b")]),
            _ => throw new InvalidOperationException(target)
        };

        Assert.AreEqual(WorktreeBaseHeadFreshnessResolutionStatus.AmbiguousObservations, result.ResolutionStatus);
        AssertContains(result.AmbiguousObservations, expectedDiagnostic);
    }

    [TestMethod]
    public void MultipleMatchingObservations_ReturnAmbiguousObservations()
    {
        var result = Assemble(
            [Rule()],
            [Expectation("expect-main")],
            [
                Observation("observe-a", surfaceId: "surface-a", referenceId: "ref-a"),
                Observation("observe-b", surfaceId: "surface-b", referenceId: "ref-b")
            ]);

        AssertAmbiguous(result, "MultipleWorktreeBaseHeadObservationsForExpectation:expect-main");
    }

    [TestMethod]
    public void AmbiguityDoesNotChooseWinner()
    {
        var result = Assemble(
            [Rule()],
            [Expectation("expect-main")],
            [
                Observation("observe-a", surfaceId: "surface-a", referenceId: "ref-a"),
                Observation("observe-b", surfaceId: "surface-b", referenceId: "ref-b")
            ]);

        Assert.AreEqual(WorktreeBaseHeadFreshnessResolutionStatus.AmbiguousObservations, result.ResolutionStatus);
        Assert.AreEqual(0, result.Assessments.Count);
        AssertContains(result.Warnings, "ambiguous worktree/base/head observations do not choose a winner");
    }

    [TestMethod]
    public void AssessmentsSortDeterministically()
    {
        var result = Assemble(
            [Rule()],
            [
                Expectation("expect-z", correlationId: "corr_3123456789abcde1", repositoryIdentity: "repo/z"),
                Expectation("expect-a", correlationId: "corr_3123456789abcde2", repositoryIdentity: "repo/a")
            ],
            [
                Observation("observe-z", correlationId: "corr_3123456789abcde1", repositoryIdentity: "repo/z"),
                Observation("observe-a", correlationId: "corr_3123456789abcde2", repositoryIdentity: "repo/a")
            ]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        CollectionAssert.AreEqual(
            new[] { "expect-a", "expect-z" },
            result.Assessments.Select(static assessment => assessment.ExpectationId).ToArray());
    }

    [TestMethod]
    public void AmbiguityDiagnosticsSortDeterministically()
    {
        var result = Assemble(
            [Rule(ruleId: "rule-z"), Rule(ruleId: "rule-a")],
            [Expectation("expect-main")],
            [Observation("observe-main")]);

        Assert.AreEqual(WorktreeBaseHeadFreshnessResolutionStatus.AmbiguousObservations, result.ResolutionStatus);
        CollectionAssert.AreEqual(
            result.AmbiguousObservations.OrderBy(static item => item, StringComparer.Ordinal).ToArray(),
            result.AmbiguousObservations.ToArray());
    }

    [DataTestMethod]
    [DataRow("", "project-d13", "op_0000000000000013", "2026-06-24T12:00:00Z", "WorktreeBaseHeadFreshnessTenantIdRequired")]
    [DataRow("tenant d13", "project-d13", "op_0000000000000013", "2026-06-24T12:00:00Z", "WorktreeBaseHeadFreshnessTenantIdInvalid")]
    [DataRow("tenant-d13", "", "op_0000000000000013", "2026-06-24T12:00:00Z", "WorktreeBaseHeadFreshnessProjectIdRequired")]
    [DataRow("tenant-d13", "project d13", "op_0000000000000013", "2026-06-24T12:00:00Z", "WorktreeBaseHeadFreshnessProjectIdInvalid")]
    [DataRow("tenant-d13", "project-d13", "", "2026-06-24T12:00:00Z", "OperationIdRequired")]
    [DataRow("tenant-d13", "project-d13", "run-123", "2026-06-24T12:00:00Z", "OperationIdMustBeBackendMintedCanonicalId")]
    [DataRow("tenant-d13", "project-d13", "op_0000000000000013", "0001-01-01T00:00:00Z", "WorktreeBaseHeadFreshnessAsOfUtcRequired")]
    public void RequestScopeValidation_FailsClosed(
        string tenantId,
        string projectId,
        string operationId,
        string asOfUtc,
        string expectedIssue)
    {
        var result = WorktreeBaseHeadFreshnessReadModelAssembler.Assemble(new WorktreeBaseHeadFreshnessReadModelRequest
        {
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            AsOfUtc = DateTimeOffset.Parse(asOfUtc),
            Rules = [Rule()],
            Expectations = [Expectation("expect-main")],
            Observations = [Observation("observe-main")]
        });

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(WorktreeBaseHeadFreshnessResolutionStatus.InvalidRequest, result.ResolutionStatus);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void NullLists_FailClosed()
    {
        var nullRules = WorktreeBaseHeadFreshnessReadModelAssembler.Assemble(Request(null!, [], []));
        var nullExpectations = WorktreeBaseHeadFreshnessReadModelAssembler.Assemble(Request([], null!, []));
        var nullObservations = WorktreeBaseHeadFreshnessReadModelAssembler.Assemble(Request([], [], null!));

        AssertContains(nullRules.Issues, "WorktreeBaseHeadFreshnessRulesRequired");
        AssertContains(nullExpectations.Issues, "WorktreeBaseHeadExpectationsRequired");
        AssertContains(nullObservations.Issues, "WorktreeBaseHeadObservationsRequired");
    }

    [DataTestMethod]
    [DataRow("", "WorktreeBaseHeadFreshnessRuleIdRequired")]
    [DataRow("rule unsafe", "WorktreeBaseHeadFreshnessRuleIdInvalid")]
    public void RuleIdValidation_FailsClosed(string ruleId, string expectedIssue)
    {
        var result = Assemble([Rule(ruleId: ruleId)], [], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(0, 30, "WorktreeBaseHeadFreshnessRuleFreshForInvalid")]
    [DataRow(-1, 30, "WorktreeBaseHeadFreshnessRuleFreshForInvalid")]
    [DataRow(30, 0, "WorktreeBaseHeadFreshnessRuleExpiresAfterInvalid")]
    [DataRow(30, -1, "WorktreeBaseHeadFreshnessRuleExpiresAfterInvalid")]
    [DataRow(30, 20, "WorktreeBaseHeadFreshnessRuleExpiresBeforeFreshWindow")]
    public void RuleWindowValidation_FailsClosed(int freshMinutes, int expiresMinutes, string expectedIssue)
    {
        var result = Assemble([Rule(freshFor: TimeSpan.FromMinutes(freshMinutes), expiresAfter: TimeSpan.FromMinutes(expiresMinutes))], [], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("", "WorktreeBaseHeadFreshnessRuleSourceRequired")]
    [DataRow("source secret", "WorktreeBaseHeadFreshnessRuleSourceInvalid")]
    public void RuleSourceValidation_FailsClosed(string source, string expectedIssue)
    {
        var result = Assemble([Rule(source: source)], [], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void RuleCreatedTimestampRequired()
    {
        var result = Assemble([Rule(useDefaultCreatedAt: true)], [], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "WorktreeBaseHeadFreshnessRuleCreatedAtRequired");
    }

    [DataTestMethod]
    [DataRow("id", "", "WorktreeBaseHeadExpectationIdRequired")]
    [DataRow("id", "expect unsafe", "WorktreeBaseHeadExpectationIdInvalid")]
    [DataRow("correlation", "bad-corr", "WorktreeBaseHeadExpectationCorrelationIdInvalid")]
    [DataRow("repository", "", "WorktreeBaseHeadExpectationRepositoryIdentityRequired")]
    [DataRow("repository", "repo secret", "WorktreeBaseHeadExpectationRepositoryIdentityInvalid")]
    [DataRow("base-branch", "", "WorktreeBaseHeadExpectationBaseBranchRequired")]
    [DataRow("base-branch", "../main", "WorktreeBaseHeadExpectationBaseBranchInvalid")]
    [DataRow("base-sha", "", "WorktreeBaseHeadExpectationBaseCommitShaRequired")]
    [DataRow("base-sha", "not-a-sha", "WorktreeBaseHeadExpectationBaseCommitShaInvalid")]
    [DataRow("head-branch", "", "WorktreeBaseHeadExpectationHeadBranchRequired")]
    [DataRow("head-branch", "../head", "WorktreeBaseHeadExpectationHeadBranchInvalid")]
    [DataRow("head-sha", "", "WorktreeBaseHeadExpectationHeadCommitShaRequired")]
    [DataRow("head-sha", "not-a-sha", "WorktreeBaseHeadExpectationHeadCommitShaInvalid")]
    [DataRow("worktree-state", "Unknown", "WorktreeBaseHeadExpectationWorktreeStateRequired")]
    [DataRow("head-state", "Unknown", "WorktreeBaseHeadExpectationHeadStateRequired")]
    [DataRow("captured", "", "WorktreeBaseHeadExpectationCapturedAtRequired")]
    [DataRow("recorded", "", "WorktreeBaseHeadExpectationRecordedAtRequired")]
    [DataRow("surface-kind", "Unknown", "WorktreeBaseHeadExpectationSurfaceKindRequired")]
    [DataRow("surface-id", "", "WorktreeBaseHeadExpectationSurfaceIdRequired")]
    [DataRow("surface-id", "surface unsafe", "WorktreeBaseHeadExpectationSurfaceIdInvalid")]
    [DataRow("reference-kind", "CommitSha", "WorktreeBaseHeadExpectationReferenceIdRequired")]
    [DataRow("reference-id", "without-kind", "WorktreeBaseHeadExpectationReferenceKindRequired")]
    [DataRow("reference-id", "ref unsafe", "WorktreeBaseHeadExpectationReferenceIdInvalid")]
    [DataRow("source", "", "WorktreeBaseHeadExpectationSourceRequired")]
    [DataRow("source", "source secret", "WorktreeBaseHeadExpectationSourceInvalid")]
    public void ExpectationValidation_FailsClosed(string field, string value, string expectedIssue)
    {
        var expectation = field switch
        {
            "id" => Expectation(value),
            "correlation" => Expectation("expect-main", correlationId: value),
            "repository" => Expectation("expect-main", repositoryIdentity: value),
            "base-branch" => Expectation("expect-main", baseBranch: value),
            "base-sha" => Expectation("expect-main", baseCommitSha: value),
            "head-branch" => Expectation("expect-main", headBranch: value),
            "head-sha" => Expectation("expect-main", headCommitSha: value),
            "worktree-state" => Expectation("expect-main", expectedWorktreeState: Enum.Parse<WorktreeStateKind>(value)),
            "head-state" => Expectation("expect-main", expectedHeadState: Enum.Parse<HeadStateKind>(value)),
            "captured" => Expectation("expect-main", capturedAtUtc: DateTimeOffset.MinValue),
            "recorded" => Expectation("expect-main", recordedAtUtc: DateTimeOffset.MinValue),
            "surface-kind" => Expectation("expect-main", surfaceKind: OperationCorrelationSurfaceKind.Unknown),
            "surface-id" => Expectation("expect-main", surfaceId: value),
            "reference-kind" => Expectation("expect-main", referenceKind: OperationReferenceKind.CommitSha, referenceId: ""),
            "reference-id" when value == "without-kind" => Expectation("expect-main", referenceKind: OperationReferenceKind.Unknown, referenceId: "ref-1"),
            "reference-id" => Expectation("expect-main", referenceKind: OperationReferenceKind.CommitSha, referenceId: value),
            "source" => Expectation("expect-main", source: value),
            _ => throw new InvalidOperationException(field)
        };

        var result = Assemble([Rule()], [expectation], [Observation("observe-main")]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(true, null, "WorktreeBaseHeadExpectationRedactionReasonRequired")]
    [DataRow(false, "raw source leaked", "WorktreeBaseHeadExpectationRedactionReasonInvalid")]
    public void RedactedExpectationRequiresSafeReason(bool isRedacted, string? redactionReason, string expectedIssue)
    {
        var result = Assemble(
            [Rule()],
            [Expectation("expect-main", isRedacted: isRedacted, redactionReason: redactionReason)],
            [Observation("observe-main")]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("id", "", "WorktreeBaseHeadObservationIdRequired")]
    [DataRow("id", "observe unsafe", "WorktreeBaseHeadObservationIdInvalid")]
    [DataRow("correlation", "bad-corr", "WorktreeBaseHeadObservationCorrelationIdInvalid")]
    [DataRow("repository", "", "WorktreeBaseHeadObservationRepositoryIdentityRequired")]
    [DataRow("repository", "repo secret", "WorktreeBaseHeadObservationRepositoryIdentityInvalid")]
    [DataRow("worktree-state", "Unknown", "WorktreeBaseHeadObservationWorktreeStateRequired")]
    [DataRow("head-state", "Unknown", "WorktreeBaseHeadObservationHeadStateRequired")]
    [DataRow("base-branch", "", "WorktreeBaseHeadObservationBaseBranchRequired")]
    [DataRow("base-branch", "../main", "WorktreeBaseHeadObservationBaseBranchInvalid")]
    [DataRow("base-sha", "", "WorktreeBaseHeadObservationBaseCommitShaRequired")]
    [DataRow("base-sha", "not-a-sha", "WorktreeBaseHeadObservationBaseCommitShaInvalid")]
    [DataRow("head-branch", "", "WorktreeBaseHeadObservationHeadBranchRequired")]
    [DataRow("head-sha", "", "WorktreeBaseHeadObservationHeadCommitShaRequired")]
    [DataRow("observed", "", "WorktreeBaseHeadObservationObservedAtRequired")]
    [DataRow("recorded", "", "WorktreeBaseHeadObservationRecordedAtRequired")]
    [DataRow("surface-kind", "Unknown", "WorktreeBaseHeadObservationSurfaceKindRequired")]
    [DataRow("surface-id", "", "WorktreeBaseHeadObservationSurfaceIdRequired")]
    [DataRow("reference-kind", "CommitSha", "WorktreeBaseHeadObservationReferenceIdRequired")]
    [DataRow("reference-id", "without-kind", "WorktreeBaseHeadObservationReferenceKindRequired")]
    [DataRow("reference-id", "ref unsafe", "WorktreeBaseHeadObservationReferenceIdInvalid")]
    [DataRow("source", "", "WorktreeBaseHeadObservationSourceRequired")]
    [DataRow("source", "source secret", "WorktreeBaseHeadObservationSourceInvalid")]
    public void ObservationValidation_FailsClosed(string field, string value, string expectedIssue)
    {
        var observation = field switch
        {
            "id" => Observation(value),
            "correlation" => Observation("observe-main", correlationId: value),
            "repository" => Observation("observe-main", repositoryIdentity: value),
            "worktree-state" => Observation("observe-main", worktreeState: Enum.Parse<WorktreeStateKind>(value)),
            "head-state" => Observation("observe-main", headState: Enum.Parse<HeadStateKind>(value)),
            "base-branch" => Observation("observe-main", baseBranch: value),
            "base-sha" => Observation("observe-main", baseCommitSha: value),
            "head-branch" => Observation("observe-main", headBranch: value),
            "head-sha" => Observation("observe-main", headCommitSha: value),
            "observed" => Observation("observe-main", observedAtUtc: DateTimeOffset.MinValue),
            "recorded" => Observation("observe-main", recordedAtUtc: DateTimeOffset.MinValue),
            "surface-kind" => Observation("observe-main", surfaceKind: OperationCorrelationSurfaceKind.Unknown),
            "surface-id" => Observation("observe-main", surfaceId: value),
            "reference-kind" => Observation("observe-main", referenceKind: OperationReferenceKind.CommitSha, referenceId: ""),
            "reference-id" when value == "without-kind" => Observation("observe-main", referenceKind: OperationReferenceKind.Unknown, referenceId: "ref-1"),
            "reference-id" => Observation("observe-main", referenceKind: OperationReferenceKind.CommitSha, referenceId: value),
            "source" => Observation("observe-main", source: value),
            _ => throw new InvalidOperationException(field)
        };

        var result = Assemble([Rule()], [Expectation("expect-main")], [observation]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(true, null, "WorktreeBaseHeadObservationRedactionReasonRequired")]
    [DataRow(false, "raw diff leaked", "WorktreeBaseHeadObservationRedactionReasonInvalid")]
    public void RedactedObservationRequiresSafeReason(bool isRedacted, string? redactionReason, string expectedIssue)
    {
        var result = Assemble(
            [Rule()],
            [Expectation("expect-main")],
            [Observation("observe-main", isRedacted: isRedacted, redactionReason: redactionReason)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("tenant-other", "project-d13", "op_0000000000000013", "rule", "WorktreeBaseHeadFreshnessRuleTenantMismatch")]
    [DataRow("tenant-d13", "project-other", "op_0000000000000013", "rule", "WorktreeBaseHeadFreshnessRuleProjectMismatch")]
    [DataRow("tenant-d13", "project-d13", "op_0000000000000099", "rule", "WorktreeBaseHeadFreshnessRuleOperationMismatch")]
    [DataRow("tenant-other", "project-d13", "op_0000000000000013", "expectation", "WorktreeBaseHeadExpectationTenantMismatch")]
    [DataRow("tenant-d13", "project-other", "op_0000000000000013", "expectation", "WorktreeBaseHeadExpectationProjectMismatch")]
    [DataRow("tenant-d13", "project-d13", "op_0000000000000099", "expectation", "WorktreeBaseHeadExpectationOperationMismatch")]
    [DataRow("tenant-other", "project-d13", "op_0000000000000013", "observation", "WorktreeBaseHeadObservationTenantMismatch")]
    [DataRow("tenant-d13", "project-other", "op_0000000000000013", "observation", "WorktreeBaseHeadObservationProjectMismatch")]
    [DataRow("tenant-d13", "project-d13", "op_0000000000000099", "observation", "WorktreeBaseHeadObservationOperationMismatch")]
    public void CrossScopeMetadataFailsClosed(string tenantId, string projectId, string operationId, string target, string expectedIssue)
    {
        var result = Assemble(
            [Rule(tenantId: target == "rule" ? tenantId : null, projectId: target == "rule" ? projectId : null, operationId: target == "rule" ? operationId : null)],
            [Expectation("expect-main", tenantId: target == "expectation" ? tenantId : null, projectId: target == "expectation" ? projectId : null, operationId: target == "expectation" ? operationId : null)],
            [Observation("observe-main", tenantId: target == "observation" ? tenantId : null, projectId: target == "observation" ? projectId : null, operationId: target == "observation" ? operationId : null)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("fresh worktree/base/head metadata is not authority")]
    [DataRow("clean worktree metadata is not commit permission")]
    [DataRow("matching head metadata is not push permission")]
    [DataRow("matching base metadata is not merge readiness")]
    [DataRow("dirty worktree metadata is not policy denial")]
    [DataRow("detached head metadata is not next-safe-action selection")]
    [DataRow("worktree/base/head freshness read model is not source apply")]
    [DataRow("worktree/base/head freshness read model is not patch/base freshness resolution")]
    [DataRow("worktree/base/head freshness read model is not release readiness")]
    [DataRow("worktree/base/head freshness read model is not deployment readiness")]
    public void WorktreeBaseHeadFreshnessDoesNotGrantAuthority(string boundary)
    {
        var result = Assemble([Rule()], [Expectation("expect-main")], [Observation("observe-main")]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.ForbiddenAuthorityImplications, boundary);
    }

    [TestMethod]
    public void DirtyDetachedStaleAndExpiredResultsDoNotChooseNextSafeAction()
    {
        var result = Assemble(
            [Rule(freshFor: TimeSpan.FromMinutes(1), expiresAfter: TimeSpan.FromMinutes(2))],
            [Expectation("expect-main")],
            [Observation("observe-main", observedAtUtc: AsOfUtc.AddMinutes(-30))]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(WorktreeBaseHeadFreshnessState.ObservationExpired, result.Assessments[0].FreshnessState);
        AssertContains(result.Warnings, "stale or expired observation metadata does not choose next safe action");
        foreach (var property in typeof(WorktreeBaseHeadFreshnessReadModel).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            Assert.IsFalse(property.Name.Contains("NextSafeAction", StringComparison.OrdinalIgnoreCase));
        }
    }

    [TestMethod]
    public void ResultModelsExposeNoAuthorityOrRawSourceProperties()
    {
        var modelTypes = new[]
        {
            typeof(WorktreeBaseHeadFreshnessAssessment),
            typeof(WorktreeBaseHeadFreshnessReadModel)
        };

        var forbiddenFragments = new[]
        {
            "Can",
            "Approval",
            "PolicySatisfied",
            "AuthorityGranted",
            "ActionAllowed",
            "NextSafeAction",
            "ReadyToApply",
            "ReadyToCommit",
            "ReadyToPush",
            "RawPatch",
            "RawDiff",
            "RawSource",
            "SourceContent",
            "RawPayload",
            "PayloadText",
            "ExecutionProven"
        };

        foreach (var type in modelTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.Name == nameof(WorktreeBaseHeadFreshnessReadModel.ForbiddenAuthorityImplications))
                {
                    continue;
                }

                foreach (var forbidden in forbiddenFragments)
                {
                    Assert.IsFalse(
                        property.Name.Contains(forbidden, StringComparison.Ordinal),
                        $"{type.Name}.{property.Name} contains forbidden fragment {forbidden}.");
                }
            }
        }
    }

    [TestMethod]
    public void PriorD01ThroughD12ContractsRemainCompatible()
    {
        var identity = OperationIdentityValidator.ValidateOperationId(OperationId);
        Assert.IsTrue(identity.IsValid, string.Join(", ", identity.Issues));

        Assert.AreEqual(OperationIdentityLookupStatus.FoundOne, OperationIdentityLookupStatus.FoundOne);
        Assert.IsTrue(OperationCorrelationValidator.ValidateGroup(new OperationCorrelationGroup
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            Links =
            [
                new OperationCorrelationLink
                {
                    TenantId = TenantId,
                    ProjectId = ProjectId,
                    OperationId = OperationId,
                    CorrelationId = CorrelationId,
                    SurfaceKind = OperationCorrelationSurfaceKind.TimelineEvent,
                    SurfaceId = "surface-worktree",
                    ObservedAtUtc = AsOfUtc,
                    Source = "compatibility"
                }
            ]
        }).IsValid);
        Assert.AreEqual(GovernedOperationTimelineEventKind.OperationMinted, GovernedOperationTimelineEventKind.OperationMinted);
        Assert.AreEqual(OperationProjectedStatusKind.Minted, OperationProjectedStatusKind.Minted);
        Assert.AreEqual(nameof(BlockD06StatusProjectionRebuildTests), typeof(BlockD06StatusProjectionRebuildTests).Name);
        Assert.AreEqual(MissingEvidenceResolutionStatus.Complete, MissingEvidenceResolutionStatus.Complete);
        Assert.AreEqual(ForbiddenActionResolutionStatus.NoForbiddenFactsObserved, ForbiddenActionResolutionStatus.NoForbiddenFactsObserved);
        Assert.AreEqual(ReceiptReferenceResolutionStatus.NoReferences, ReceiptReferenceResolver.Resolve(new ReceiptReferenceResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            RequestedReferences = [],
            AvailableReceipts = []
        }).ResolutionStatus);
        Assert.AreEqual(EvidenceResolutionStatus.NoReferences, EvidenceResolver.Resolve(new EvidenceResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            RequestedReferences = [],
            AvailableEvidence = [],
            SuppliedPayloadsForRedaction = []
        }).ResolutionStatus);
        Assert.AreEqual(ValidationStalenessResolutionStatus.NoValidationResults, ValidationStalenessResolver.Resolve(new ValidationStalenessResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            AsOfUtc = AsOfUtc,
            Rules = [],
            ValidationResults = []
        }).ResolutionStatus);
        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.NoPatchArtifacts, PatchBaseFreshnessResolver.Resolve(new PatchBaseFreshnessResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            AsOfUtc = AsOfUtc,
            Rules = [],
            PatchArtifacts = [],
            BaseBranchObservations = []
        }).ResolutionStatus);
    }

    [TestMethod]
    public void A02AndA05ReadAdaptersRemainReadOnly()
    {
        AssertDoesNotContain(A02StatusReadSource(), "RunProcessAsync");
        AssertDoesNotContain(A02StatusReadSource(), "SaveChanges");
        AssertDoesNotContain(A05TimelineSource(), "RunProcessAsync");
        AssertDoesNotContain(A05TimelineSource(), "SaveChanges");
    }

    [TestMethod]
    public void StaticScan_D13CoreAddsNoApiSqlUiStoreExecutorOrMutationSurface()
    {
        var source = StripStringLiterals(D13CoreSource());
        var forbiddenMarkers = new[]
        {
            "Controller",
            "HttpGet",
            "MapGet",
            "OpenApi",
            "Tauri",
            "MigrationBuilder",
            "DbContext",
            "SqlConnection",
            "SaveChanges",
            "File.ReadAllText",
            "Directory.GetFiles",
            "ProcessStartInfo",
            "Process.Start",
            "RunProcessAsync",
            "LibGit2Sharp",
            "GitClient",
            "MissingEvidenceResolver.Resolve",
            "ForbiddenActionResolver.Resolve",
            "ReceiptReferenceResolver.Resolve",
            "EvidenceResolver.Resolve",
            "ValidationStalenessResolver.Resolve",
            "PatchBaseFreshnessResolver.Resolve",
            "SourceApply",
            "ControlledCommit",
            "ControlledPush",
            "PullRequest",
            "MergeExecutor",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "MemoryPromotion",
            "ContinueWorkflow",
            "AcceptApproval",
            "PolicySatisfaction"
        };

        foreach (var marker in forbiddenMarkers)
        {
            AssertDoesNotContain(source, marker);
        }
    }

    [TestMethod]
    public void StaticScan_D13CoreDoesNotUseSystemClock()
    {
        var source = D13CoreSource();

        AssertDoesNotContain(source, "DateTimeOffset.UtcNow");
        AssertDoesNotContain(source, "DateTime.UtcNow");
        AssertDoesNotContain(source, "DateTime.Now");
        AssertDoesNotContain(source, "TimeProvider");
        AssertDoesNotContain(source, "Stopwatch");
    }

    [TestMethod]
    public void StaticScan_D13CoreDoesNotReadRawSourcePatchDiffOrInvokeGitProcess()
    {
        var source = StripStringLiterals(D13CoreSource());
        var forbiddenMarkers = new[]
        {
            "ReadSource",
            "ReadPatch",
            "ReadDiff",
            "SourceContent",
            "PatchContent",
            "DiffContent",
            "RawSourceReader",
            "RawPatchReader",
            "RawDiffReader",
            "File.OpenRead",
            "File.ReadAllText",
            "Directory.Enumerate",
            "Directory.GetFiles",
            "ProcessStartInfo",
            "Process.Start",
            "git ",
            "git.exe",
            "LibGit2Sharp"
        };

        foreach (var marker in forbiddenMarkers)
        {
            AssertDoesNotContain(source, marker);
        }
    }

    [TestMethod]
    public void ReceiptRecordsRequiredBoundaries()
    {
        var receipt = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "D13_WORKTREE_BASE_HEAD_FRESHNESS_READ_MODEL.md"));

        AssertContains(receipt, "The worktree/base/head freshness read model classifies supplied expectation and observation metadata using supplied freshness rules and supplied AsOfUtc only.");
        AssertContains(receipt, "Fresh worktree/base/head metadata is not authority.");
        AssertContains(receipt, "Clean worktree metadata is not commit permission.");
        AssertContains(receipt, "Complete worktree/base/head assessment is not action allowed.");
    }

    private static WorktreeBaseHeadFreshnessReadModel Assemble(
        IReadOnlyList<WorktreeBaseHeadFreshnessRule> rules,
        IReadOnlyList<ExpectedWorktreeBaseHeadMetadata> expectations,
        IReadOnlyList<ObservedWorktreeBaseHeadMetadata> observations,
        DateTimeOffset? asOfUtc = null) =>
        WorktreeBaseHeadFreshnessReadModelAssembler.Assemble(Request(rules, expectations, observations, asOfUtc));

    private static WorktreeBaseHeadFreshnessReadModelRequest Request(
        IReadOnlyList<WorktreeBaseHeadFreshnessRule> rules,
        IReadOnlyList<ExpectedWorktreeBaseHeadMetadata> expectations,
        IReadOnlyList<ObservedWorktreeBaseHeadMetadata> observations,
        DateTimeOffset? asOfUtc = null) =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            AsOfUtc = asOfUtc ?? AsOfUtc,
            Rules = rules,
            Expectations = expectations,
            Observations = observations
        };

    private static WorktreeBaseHeadFreshnessRule Rule(
        string? tenantId = null,
        string? projectId = null,
        string? operationId = null,
        string ruleId = "rule-worktree-base-head",
        TimeSpan? freshFor = null,
        TimeSpan? expiresAfter = null,
        bool requireRepositoryIdentityMatch = true,
        bool requireWorktreeClean = true,
        bool requireBaseBranchMatch = true,
        bool requireBaseCommitMatch = true,
        bool requireHeadBranchMatch = true,
        bool requireHeadCommitMatch = true,
        bool requireAttachedHead = true,
        string source = "rule-source",
        DateTimeOffset? createdAtUtc = null,
        bool useDefaultCreatedAt = false) =>
        new()
        {
            TenantId = tenantId ?? TenantId,
            ProjectId = projectId ?? ProjectId,
            OperationId = operationId ?? OperationId,
            RuleId = ruleId,
            ObservationFreshFor = freshFor ?? TimeSpan.FromHours(1),
            ObservationExpiresAfter = expiresAfter ?? TimeSpan.FromHours(2),
            RequireRepositoryIdentityMatch = requireRepositoryIdentityMatch,
            RequireWorktreeClean = requireWorktreeClean,
            RequireBaseBranchMatch = requireBaseBranchMatch,
            RequireBaseCommitMatch = requireBaseCommitMatch,
            RequireHeadBranchMatch = requireHeadBranchMatch,
            RequireHeadCommitMatch = requireHeadCommitMatch,
            RequireAttachedHead = requireAttachedHead,
            Source = source,
            CreatedAtUtc = useDefaultCreatedAt ? default : createdAtUtc ?? AsOfUtc.AddHours(-1)
        };

    private static ExpectedWorktreeBaseHeadMetadata Expectation(
        string expectationId,
        string? tenantId = null,
        string? projectId = null,
        string? operationId = null,
        string correlationId = CorrelationId,
        string repositoryIdentity = RepositoryIdentity,
        string baseBranch = "main",
        string baseCommitSha = BaseCommitSha,
        string? headBranch = "feature/d13",
        string? headCommitSha = HeadCommitSha,
        WorktreeStateKind expectedWorktreeState = WorktreeStateKind.Clean,
        HeadStateKind expectedHeadState = HeadStateKind.Attached,
        DateTimeOffset? capturedAtUtc = null,
        DateTimeOffset? recordedAtUtc = null,
        OperationCorrelationSurfaceKind surfaceKind = OperationCorrelationSurfaceKind.TimelineEvent,
        string? surfaceId = null,
        OperationReferenceKind referenceKind = OperationReferenceKind.TimelineEventId,
        string? referenceId = null,
        string source = "expectation-source",
        bool isRedacted = false,
        string? redactionReason = null) =>
        new()
        {
            TenantId = tenantId ?? TenantId,
            ProjectId = projectId ?? ProjectId,
            OperationId = operationId ?? OperationId,
            CorrelationId = correlationId,
            ExpectationId = expectationId,
            RepositoryIdentity = repositoryIdentity,
            BaseBranch = baseBranch,
            BaseCommitSha = baseCommitSha,
            HeadBranch = headBranch,
            HeadCommitSha = headCommitSha,
            ExpectedWorktreeState = expectedWorktreeState,
            ExpectedHeadState = expectedHeadState,
            CapturedAtUtc = capturedAtUtc ?? CapturedAtUtc,
            RecordedAtUtc = recordedAtUtc ?? (capturedAtUtc.HasValue ? capturedAtUtc.Value.AddMinutes(5) : RecordedAtUtc),
            SurfaceKind = surfaceKind,
            SurfaceId = surfaceId ?? $"surface-{expectationId}",
            ReferenceKind = referenceKind,
            ReferenceId = referenceId ?? $"ref-{expectationId}",
            Source = source,
            IsRedacted = isRedacted,
            RedactionReason = redactionReason
        };

    private static ObservedWorktreeBaseHeadMetadata Observation(
        string observationId,
        string? tenantId = null,
        string? projectId = null,
        string? operationId = null,
        string correlationId = CorrelationId,
        string repositoryIdentity = RepositoryIdentity,
        WorktreeStateKind worktreeState = WorktreeStateKind.Clean,
        HeadStateKind headState = HeadStateKind.Attached,
        string baseBranch = "main",
        string baseCommitSha = BaseCommitSha,
        string? headBranch = "feature/d13",
        string? headCommitSha = HeadCommitSha,
        bool hasUncommittedChanges = false,
        bool hasUntrackedFiles = false,
        bool hasConflicts = false,
        DateTimeOffset? observedAtUtc = null,
        DateTimeOffset? recordedAtUtc = null,
        OperationCorrelationSurfaceKind surfaceKind = OperationCorrelationSurfaceKind.TimelineEvent,
        string? surfaceId = null,
        OperationReferenceKind referenceKind = OperationReferenceKind.TimelineEventId,
        string? referenceId = null,
        string source = "observation-source",
        bool isRedacted = false,
        string? redactionReason = null) =>
        new()
        {
            TenantId = tenantId ?? TenantId,
            ProjectId = projectId ?? ProjectId,
            OperationId = operationId ?? OperationId,
            CorrelationId = correlationId,
            ObservationId = observationId,
            RepositoryIdentity = repositoryIdentity,
            WorktreeState = worktreeState,
            HeadState = headState,
            BaseBranch = baseBranch,
            BaseCommitSha = baseCommitSha,
            HeadBranch = headBranch,
            HeadCommitSha = headCommitSha,
            HasUncommittedChanges = hasUncommittedChanges,
            HasUntrackedFiles = hasUntrackedFiles,
            HasConflicts = hasConflicts,
            ObservedAtUtc = observedAtUtc ?? ObservedAtUtc,
            RecordedAtUtc = recordedAtUtc ?? (observedAtUtc.HasValue ? observedAtUtc.Value.AddMinutes(5) : ObservedAtUtc.AddMinutes(5)),
            SurfaceKind = surfaceKind,
            SurfaceId = surfaceId ?? $"surface-{observationId}",
            ReferenceKind = referenceKind,
            ReferenceId = referenceId ?? $"ref-{observationId}",
            Source = source,
            IsRedacted = isRedacted,
            RedactionReason = redactionReason
        };

    private static void AssertAmbiguous(WorktreeBaseHeadFreshnessReadModel result, string expectedDiagnostic)
    {
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(WorktreeBaseHeadFreshnessResolutionStatus.AmbiguousObservations, result.ResolutionStatus);
        Assert.AreEqual(0, result.Assessments.Count);
        AssertContains(result.AmbiguousObservations, expectedDiagnostic);
        AssertContains(result.Warnings, "ambiguous worktree/base/head observations do not choose a winner");
    }

    private static string D13CoreSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "WorktreeBaseHeadFreshnessReadModelModels.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "WorktreeBaseHeadFreshnessReadModelValidator.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "WorktreeBaseHeadFreshnessReadModelAssembler.cs")));
    }

    private static string A02StatusReadSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "GovernedOperationStatusReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "GovernedOperationStatusReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "OperationStatusFrontendReadinessBackendTruthSource.cs")));
    }

    private static string A05TimelineSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "OperationTimelineReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "OperationTimelineReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "OperationTimelineFrontendReadinessBackendTruthSource.cs")));
    }

    private static string StripStringLiterals(string source) =>
        Regex.Replace(source, "\"(?:\\\\.|[^\"])*\"", "\"\"", RegexOptions.Singleline);

    private static void AssertContains<T>(IEnumerable<T> values, T expected) =>
        Assert.IsTrue(
            values.Contains(expected),
            $"Expected '{expected}' in [{string.Join(", ", values)}].");

    private static void AssertContains(string value, string expected) =>
        Assert.IsTrue(
            value.Contains(expected, StringComparison.Ordinal),
            $"Expected marker '{expected}' was not present.");

    private static void AssertDoesNotContain(string value, string unexpected) =>
        Assert.IsFalse(
            value.Contains(unexpected, StringComparison.Ordinal),
            $"Unexpected marker '{unexpected}' was present.");

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "IronDev.slnx")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
