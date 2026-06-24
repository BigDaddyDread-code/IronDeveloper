using System.Reflection;
using System.Text.RegularExpressions;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockD12PatchBaseFreshnessResolverTests
{
    private const string TenantId = "tenant-d12";
    private const string ProjectId = "project-d12";
    private const string OperationId = "op_0000000000000012";
    private const string CorrelationId = "corr_2123456789abcdef";
    private const string PatchHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string OtherPatchHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string BaseCommitSha = "1111111111111111111111111111111111111111";
    private const string MovedBaseCommitSha = "2222222222222222222222222222222222222222";
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.Parse("2026-06-24T11:30:00Z");
    private static readonly DateTimeOffset RecordedAtUtc = DateTimeOffset.Parse("2026-06-24T11:35:00Z");
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-24T11:40:00Z");

    [TestMethod]
    public void ValidRequestWithNoPatchArtifacts_ReturnsNoPatchArtifacts()
    {
        var result = Resolve([Rule(PatchArtifactKind.GeneratedPatch)], []);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.NoPatchArtifacts, result.ResolutionStatus);
        Assert.AreEqual(0, result.Assessments.Count);
        AssertContains(result.ForbiddenAuthorityImplications, "fresh patch metadata is not authority");
    }

    [TestMethod]
    public void FreshPatchArtifact_IsClassifiedFresh()
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch, freshFor: TimeSpan.FromHours(1), expiresAfter: TimeSpan.FromHours(2))],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, createdAtUtc: AsOfUtc.AddMinutes(-30))],
            [BaseObservation("main")]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.Assessed, result.ResolutionStatus);
        Assert.AreEqual(PatchBaseFreshnessState.Fresh, result.Assessments[0].FreshnessState);
        Assert.AreEqual(TimeSpan.FromMinutes(30), result.Assessments[0].Age);
        Assert.AreEqual("rule-generatedpatch", result.Assessments[0].RuleId);
        AssertContains(result.Warnings, "fresh patch metadata is not authority");
    }

    [TestMethod]
    public void StalePatchArtifact_IsClassifiedPatchStale()
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch, freshFor: TimeSpan.FromMinutes(20), expiresAfter: TimeSpan.FromHours(2))],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, createdAtUtc: AsOfUtc.AddMinutes(-30))],
            [BaseObservation("main")]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.Assessed, result.ResolutionStatus);
        Assert.AreEqual(PatchBaseFreshnessState.PatchStale, result.Assessments[0].FreshnessState);
        AssertContains(result.Warnings, "stale or expired patch metadata does not choose next safe action");
    }

    [TestMethod]
    public void ExpiredPatchArtifact_IsClassifiedPatchExpired()
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch, freshFor: TimeSpan.FromMinutes(10), expiresAfter: TimeSpan.FromMinutes(20))],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, createdAtUtc: AsOfUtc.AddMinutes(-30))],
            [BaseObservation("main")]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.Assessed, result.ResolutionStatus);
        Assert.AreEqual(PatchBaseFreshnessState.PatchExpired, result.Assessments[0].FreshnessState);
        AssertContains(result.ForbiddenAuthorityImplications, "patch expired is not next-safe-action selection");
    }

    [TestMethod]
    public void MissingRule_ReturnsMissingRules()
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.DryRunPatch)],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch)],
            [BaseObservation("main")]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.MissingRules, result.ResolutionStatus);
        Assert.AreEqual(PatchBaseFreshnessState.MissingRule, result.Assessments[0].FreshnessState);
        Assert.AreEqual("PatchBaseFreshnessRuleMissing", result.Assessments[0].Reason);
    }

    [TestMethod]
    public void MissingBaseObservation_ReturnsMissingBaseObservations()
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch)],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch)],
            []);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.MissingBaseObservations, result.ResolutionStatus);
        Assert.AreEqual(PatchBaseFreshnessState.MissingBaseObservation, result.Assessments[0].FreshnessState);
        Assert.AreEqual("BaseBranchObservationMissing", result.Assessments[0].Reason);
    }

    [TestMethod]
    public void BaseBranchMoved_ReturnsBaseBranchMoved()
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch)],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch)],
            [BaseObservation("main", observedBaseCommitSha: MovedBaseCommitSha)]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.Assessed, result.ResolutionStatus);
        Assert.AreEqual(PatchBaseFreshnessState.BaseBranchMoved, result.Assessments[0].FreshnessState);
        Assert.AreEqual("BaseBranchMoved", result.Assessments[0].Reason);
        AssertContains(result.ForbiddenAuthorityImplications, "base moved is not policy denial");
    }

    [TestMethod]
    public void PatchHashMismatch_ReturnsPatchHashMismatch()
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch)],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, patchHash: PatchHash)],
            [BaseObservation("main", observedPatchHash: OtherPatchHash)]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.Assessed, result.ResolutionStatus);
        Assert.AreEqual(PatchBaseFreshnessState.PatchHashMismatch, result.Assessments[0].FreshnessState);
        Assert.AreEqual("PatchHashMismatch", result.Assessments[0].Reason);
    }

    [TestMethod]
    public void MixedFreshStaleExpiredMovedResults_ReturnsMixedFreshness()
    {
        var result = Resolve(
            [
                Rule(PatchArtifactKind.GeneratedPatch, freshFor: TimeSpan.FromHours(1), expiresAfter: TimeSpan.FromHours(2)),
                Rule(PatchArtifactKind.DryRunPatch, freshFor: TimeSpan.FromMinutes(20), expiresAfter: TimeSpan.FromHours(2)),
                Rule(PatchArtifactKind.RollbackPatch, freshFor: TimeSpan.FromMinutes(10), expiresAfter: TimeSpan.FromMinutes(20)),
                Rule(PatchArtifactKind.RecoveryPatch)
            ],
            [
                PatchArtifact("patch-fresh", PatchArtifactKind.GeneratedPatch, baseBranch: "main", createdAtUtc: AsOfUtc.AddMinutes(-30)),
                PatchArtifact("patch-stale", PatchArtifactKind.DryRunPatch, baseBranch: "release", createdAtUtc: AsOfUtc.AddMinutes(-30)),
                PatchArtifact("patch-expired", PatchArtifactKind.RollbackPatch, baseBranch: "hotfix", createdAtUtc: AsOfUtc.AddMinutes(-30)),
                PatchArtifact("patch-moved", PatchArtifactKind.RecoveryPatch, baseBranch: "recovery")
            ],
            [
                BaseObservation("main"),
                BaseObservation("release"),
                BaseObservation("hotfix"),
                BaseObservation("recovery", observedBaseCommitSha: MovedBaseCommitSha)
            ]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.MixedFreshness, result.ResolutionStatus);
        CollectionAssert.AreEquivalent(
            new[]
            {
                PatchBaseFreshnessState.Fresh,
                PatchBaseFreshnessState.PatchStale,
                PatchBaseFreshnessState.PatchExpired,
                PatchBaseFreshnessState.BaseBranchMoved
            },
            result.Assessments.Select(static assessment => assessment.FreshnessState).ToArray());
    }

    [TestMethod]
    public void AgeIsCalculatedFromSuppliedAsOfUtc()
    {
        var suppliedAsOf = DateTimeOffset.Parse("2026-06-24T15:00:00Z");
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch, freshFor: TimeSpan.FromHours(4), expiresAfter: TimeSpan.FromHours(8))],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, createdAtUtc: DateTimeOffset.Parse("2026-06-24T12:00:00Z"))],
            [BaseObservation("main")],
            asOfUtc: suppliedAsOf);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(TimeSpan.FromHours(3), result.Assessments[0].Age);
        Assert.AreEqual(suppliedAsOf, result.AsOfUtc);
    }

    [TestMethod]
    public void AsOfBeforePatchCreated_ReturnsUnassessable()
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch)],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, createdAtUtc: AsOfUtc.AddMinutes(1))],
            [BaseObservation("main")]);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.Unassessable, result.ResolutionStatus);
        Assert.AreEqual(PatchBaseFreshnessState.Unassessable, result.Assessments[0].FreshnessState);
        AssertContains(result.Issues, "PatchArtifactCreatedAfterAsOf");
    }

    [TestMethod]
    public void PatchRecordedBeforeCreated_FailsClosed()
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch)],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, createdAtUtc: CreatedAtUtc, recordedAtUtc: CreatedAtUtc.AddSeconds(-1))],
            [BaseObservation("main")]);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.InvalidRequest, result.ResolutionStatus);
        AssertContains(result.Issues, "PatchArtifactRecordedBeforeCreated");
    }

    [TestMethod]
    public void DuplicateRuleIds_ReturnAmbiguousPatchBaseMetadata()
    {
        var result = Resolve(
            [
                Rule(PatchArtifactKind.GeneratedPatch, ruleId: "rule-duplicate"),
                Rule(PatchArtifactKind.DryRunPatch, ruleId: "rule-duplicate")
            ],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch)],
            [BaseObservation("main")]);

        AssertAmbiguous(result, "DuplicatePatchBaseFreshnessRuleId:rule-duplicate");
    }

    [TestMethod]
    public void DuplicatePatchArtifactIds_ReturnAmbiguousPatchBaseMetadata()
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch)],
            [
                PatchArtifact("patch-duplicate", PatchArtifactKind.GeneratedPatch, referenceId: "patch-ref-a"),
                PatchArtifact("patch-duplicate", PatchArtifactKind.GeneratedPatch, referenceId: "patch-ref-b")
            ],
            [BaseObservation("main")]);

        AssertAmbiguous(result, "DuplicatePatchArtifactId:patch-duplicate");
    }

    [TestMethod]
    public void DuplicateIndistinguishableBaseObservations_ReturnAmbiguousPatchBaseMetadata()
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch)],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch)],
            [BaseObservation("main"), BaseObservation("main")]);

        AssertAmbiguous(result, $"DuplicateBaseBranchObservation:main:{CorrelationId}:{OperationCorrelationSurfaceKind.TimelineEvent}:surface-main:{OperationReferenceKind.TimelineEventId}:base-ref-main");
    }

    [TestMethod]
    public void ConflictingRuleMetadata_ReturnsAmbiguousPatchBaseMetadata()
    {
        var result = Resolve(
            [
                Rule(PatchArtifactKind.GeneratedPatch, ruleId: "rule-conflict", source: "source-a"),
                Rule(PatchArtifactKind.GeneratedPatch, ruleId: "rule-conflict", source: "source-b")
            ],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch)],
            [BaseObservation("main")]);

        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.AmbiguousPatchBaseMetadata, result.ResolutionStatus);
        AssertContains(result.AmbiguousPatchBaseMetadata, "DuplicatePatchBaseFreshnessRuleId:rule-conflict");
        AssertContains(result.AmbiguousPatchBaseMetadata, "ConflictingPatchBaseFreshnessRuleMetadata:rule-conflict");
    }

    [TestMethod]
    public void ConflictingPatchArtifactMetadata_ReturnsAmbiguousPatchBaseMetadata()
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch)],
            [
                PatchArtifact("patch-conflict", PatchArtifactKind.GeneratedPatch, source: "source-a"),
                PatchArtifact("patch-conflict", PatchArtifactKind.GeneratedPatch, source: "source-b")
            ],
            [BaseObservation("main")]);

        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.AmbiguousPatchBaseMetadata, result.ResolutionStatus);
        AssertContains(result.AmbiguousPatchBaseMetadata, "DuplicatePatchArtifactId:patch-conflict");
        AssertContains(result.AmbiguousPatchBaseMetadata, "ConflictingPatchArtifactMetadata:patch-conflict");
    }

    [TestMethod]
    public void ConflictingBaseObservationMetadata_ReturnsAmbiguousPatchBaseMetadata()
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch)],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch)],
            [
                BaseObservation("main", observedBaseCommitSha: BaseCommitSha),
                BaseObservation("main", observedBaseCommitSha: MovedBaseCommitSha)
            ]);

        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.AmbiguousPatchBaseMetadata, result.ResolutionStatus);
        Assert.IsTrue(result.AmbiguousPatchBaseMetadata.Any(static item => item.StartsWith("ConflictingBaseBranchObservationMetadata:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void MultipleRulesForSamePatchKind_ReturnAmbiguousPatchBaseMetadata()
    {
        var result = Resolve(
            [
                Rule(PatchArtifactKind.GeneratedPatch, ruleId: "rule-a"),
                Rule(PatchArtifactKind.GeneratedPatch, ruleId: "rule-b")
            ],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch)],
            [BaseObservation("main")]);

        AssertAmbiguous(result, "MultiplePatchBaseFreshnessRulesForKind:GeneratedPatch");
    }

    [TestMethod]
    public void MultipleMatchingBaseObservations_ReturnAmbiguousPatchBaseMetadata()
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch)],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch)],
            [
                BaseObservation("main", surfaceId: "surface-a", referenceId: "base-ref-a"),
                BaseObservation("main", surfaceId: "surface-b", referenceId: "base-ref-b")
            ]);

        AssertAmbiguous(result, "MultipleBaseBranchObservationsForPatch:patch-generated");
    }

    [TestMethod]
    public void IndistinguishablePatchArtifacts_ReturnAmbiguousPatchBaseMetadata()
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch)],
            [
                PatchArtifact("patch-a", PatchArtifactKind.GeneratedPatch, referenceId: "same-ref", surfaceId: "same-surface"),
                PatchArtifact("patch-b", PatchArtifactKind.GeneratedPatch, referenceId: "same-ref", surfaceId: "same-surface")
            ],
            [BaseObservation("main")]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.AmbiguousPatchBaseMetadata, result.ResolutionStatus);
        Assert.IsTrue(result.AmbiguousPatchBaseMetadata.Any(static item => item.StartsWith("IndistinguishablePatchArtifacts:", StringComparison.Ordinal)));
        Assert.AreEqual(0, result.Assessments.Count);
    }

    [TestMethod]
    public void AmbiguityDiagnosticsSortDeterministically()
    {
        var result = Resolve(
            [
                Rule(PatchArtifactKind.GeneratedPatch, ruleId: "rule-z"),
                Rule(PatchArtifactKind.GeneratedPatch, ruleId: "rule-a")
            ],
            [
                PatchArtifact("patch-z", PatchArtifactKind.GeneratedPatch, referenceId: "same-ref", surfaceId: "same-surface"),
                PatchArtifact("patch-a", PatchArtifactKind.GeneratedPatch, referenceId: "same-ref", surfaceId: "same-surface")
            ],
            [BaseObservation("main")]);

        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.AmbiguousPatchBaseMetadata, result.ResolutionStatus);
        CollectionAssert.AreEqual(
            result.AmbiguousPatchBaseMetadata.OrderBy(static item => item, StringComparer.Ordinal).ToArray(),
            result.AmbiguousPatchBaseMetadata.ToArray());
    }

    [TestMethod]
    public void AssessmentsSortDeterministically()
    {
        var result = Resolve(
            [
                Rule(PatchArtifactKind.GeneratedPatch),
                Rule(PatchArtifactKind.DryRunPatch),
                Rule(PatchArtifactKind.RollbackPatch)
            ],
            [
                PatchArtifact("patch-z", PatchArtifactKind.GeneratedPatch, baseBranch: "z-main"),
                PatchArtifact("patch-a", PatchArtifactKind.DryRunPatch, baseBranch: "a-main"),
                PatchArtifact("patch-m", PatchArtifactKind.RollbackPatch, baseBranch: "m-main")
            ],
            [
                BaseObservation("z-main"),
                BaseObservation("a-main"),
                BaseObservation("m-main")
            ]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        CollectionAssert.AreEqual(
            new[] { "patch-a", "patch-z", "patch-m" },
            result.Assessments.Select(static assessment => assessment.PatchArtifactId).ToArray());
    }

    [DataTestMethod]
    [DataRow("", "project-d12", "op_0000000000000012", "2026-06-24T12:00:00Z", "PatchBaseFreshnessTenantIdRequired")]
    [DataRow("tenant d12", "project-d12", "op_0000000000000012", "2026-06-24T12:00:00Z", "PatchBaseFreshnessTenantIdInvalid")]
    [DataRow("tenant-d12", "", "op_0000000000000012", "2026-06-24T12:00:00Z", "PatchBaseFreshnessProjectIdRequired")]
    [DataRow("tenant-d12", "project d12", "op_0000000000000012", "2026-06-24T12:00:00Z", "PatchBaseFreshnessProjectIdInvalid")]
    [DataRow("tenant-d12", "project-d12", "", "2026-06-24T12:00:00Z", "OperationIdRequired")]
    [DataRow("tenant-d12", "project-d12", "run-123", "2026-06-24T12:00:00Z", "OperationIdMustBeBackendMintedCanonicalId")]
    [DataRow("tenant-d12", "project-d12", "op_0000000000000012", "0001-01-01T00:00:00Z", "PatchBaseFreshnessAsOfUtcRequired")]
    public void RequestScopeValidation_FailsClosed(
        string tenantId,
        string projectId,
        string operationId,
        string asOfUtc,
        string expectedIssue)
    {
        var result = PatchBaseFreshnessResolver.Resolve(new PatchBaseFreshnessResolverRequest
        {
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            AsOfUtc = DateTimeOffset.Parse(asOfUtc),
            Rules = [Rule(PatchArtifactKind.GeneratedPatch)],
            PatchArtifacts = [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch)],
            BaseBranchObservations = [BaseObservation("main")]
        });

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.InvalidRequest, result.ResolutionStatus);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void NullLists_FailClosed()
    {
        var nullRules = PatchBaseFreshnessResolver.Resolve(Request(rules: null!, patches: [], observations: []));
        var nullPatches = PatchBaseFreshnessResolver.Resolve(Request(rules: [], patches: null!, observations: []));
        var nullObservations = PatchBaseFreshnessResolver.Resolve(Request(rules: [], patches: [], observations: null!));

        AssertContains(nullRules.Issues, "PatchBaseFreshnessRulesRequired");
        AssertContains(nullPatches.Issues, "PatchArtifactsRequired");
        AssertContains(nullObservations.Issues, "BaseBranchObservationsRequired");
    }

    [DataTestMethod]
    [DataRow("", "PatchBaseFreshnessRuleIdRequired")]
    [DataRow("rule unsafe", "PatchBaseFreshnessRuleIdInvalid")]
    public void RuleIdValidation_FailsClosed(string ruleId, string expectedIssue)
    {
        var result = Resolve([Rule(PatchArtifactKind.GeneratedPatch, ruleId: ruleId)], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(PatchArtifactKind.Unknown, "PatchBaseFreshnessRulePatchKindRequired")]
    public void RulePatchKindValidation_FailsClosed(PatchArtifactKind patchKind, string expectedIssue)
    {
        var result = Resolve([Rule(patchKind)], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(0, 30, "PatchBaseFreshnessRuleFreshForInvalid")]
    [DataRow(-1, 30, "PatchBaseFreshnessRuleFreshForInvalid")]
    [DataRow(30, 0, "PatchBaseFreshnessRuleExpiresAfterInvalid")]
    [DataRow(30, -1, "PatchBaseFreshnessRuleExpiresAfterInvalid")]
    [DataRow(30, 20, "PatchBaseFreshnessRuleExpiresBeforeFreshWindow")]
    public void RuleWindowValidation_FailsClosed(int freshMinutes, int expiresMinutes, string expectedIssue)
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch, freshFor: TimeSpan.FromMinutes(freshMinutes), expiresAfter: TimeSpan.FromMinutes(expiresMinutes))],
            []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("", "PatchBaseFreshnessRuleSourceRequired")]
    [DataRow("source secret", "PatchBaseFreshnessRuleSourceInvalid")]
    public void RuleSourceValidation_FailsClosed(string source, string expectedIssue)
    {
        var result = Resolve([Rule(PatchArtifactKind.GeneratedPatch, source: source)], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void RuleCreatedTimestampRequired()
    {
        var result = Resolve([Rule(PatchArtifactKind.GeneratedPatch, useDefaultCreatedAt: true)], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "PatchBaseFreshnessRuleCreatedAtRequired");
    }

    [DataTestMethod]
    [DataRow("id", "", "PatchArtifactIdRequired")]
    [DataRow("id", "patch unsafe", "PatchArtifactIdInvalid")]
    [DataRow("kind", "Unknown", "PatchArtifactKindRequired")]
    [DataRow("hash", "", "PatchArtifactHashRequired")]
    [DataRow("hash", "latest", "PatchArtifactHashInvalid")]
    [DataRow("algorithm", "Unknown", "PatchArtifactHashAlgorithmRequired")]
    [DataRow("branch", "", "PatchArtifactBaseBranchRequired")]
    [DataRow("branch", "../main", "PatchArtifactBaseBranchInvalid")]
    [DataRow("sha", "", "PatchArtifactBaseCommitShaRequired")]
    [DataRow("sha", "not-a-sha", "PatchArtifactBaseCommitShaInvalid")]
    [DataRow("correlation", "bad-corr", "PatchArtifactCorrelationIdInvalid")]
    [DataRow("created", "", "PatchArtifactCreatedAtRequired")]
    [DataRow("recorded", "", "PatchArtifactRecordedAtRequired")]
    [DataRow("surface-kind", "Unknown", "PatchArtifactSurfaceKindRequired")]
    [DataRow("surface-id", "", "PatchArtifactSurfaceIdRequired")]
    [DataRow("surface-id", "surface unsafe", "PatchArtifactSurfaceIdInvalid")]
    [DataRow("reference-kind", "CommitSha", "PatchArtifactReferenceIdRequired")]
    [DataRow("reference-id", "without-kind", "PatchArtifactReferenceKindRequired")]
    [DataRow("reference-id", "ref unsafe", "PatchArtifactReferenceIdInvalid")]
    [DataRow("source", "", "PatchArtifactSourceRequired")]
    [DataRow("source", "source secret", "PatchArtifactSourceInvalid")]
    public void PatchArtifactValidation_FailsClosed(string field, string value, string expectedIssue)
    {
        var patch = field switch
        {
            "id" => PatchArtifact(value, PatchArtifactKind.GeneratedPatch),
            "kind" => PatchArtifact("patch-generated", Enum.Parse<PatchArtifactKind>(value)),
            "hash" => PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, patchHash: value),
            "algorithm" => PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, hashAlgorithm: Enum.Parse<PatchHashAlgorithm>(value)),
            "branch" => PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, baseBranch: value),
            "sha" => PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, baseCommitSha: value),
            "correlation" => PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, correlationId: value),
            "created" => PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, createdAtUtc: DateTimeOffset.MinValue),
            "recorded" => PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, recordedAtUtc: DateTimeOffset.MinValue),
            "surface-kind" => PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, surfaceKind: OperationCorrelationSurfaceKind.Unknown),
            "surface-id" => PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, surfaceId: value),
            "reference-kind" => PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, referenceKind: OperationReferenceKind.CommitSha, referenceId: ""),
            "reference-id" when value == "without-kind" => PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, referenceKind: OperationReferenceKind.Unknown, referenceId: "ref-1"),
            "reference-id" => PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, referenceKind: OperationReferenceKind.CommitSha, referenceId: value),
            "source" => PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, source: value),
            _ => throw new InvalidOperationException(field)
        };

        var result = Resolve([Rule(PatchArtifactKind.GeneratedPatch)], [patch], [BaseObservation("main")]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(true, null, "PatchArtifactRedactionReasonRequired")]
    [DataRow(false, "raw patch leaked", "PatchArtifactRedactionReasonInvalid")]
    public void RedactedPatchMetadataRequiresSafeReason(bool isRedacted, string? redactionReason, string expectedIssue)
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch)],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, isRedacted: isRedacted, redactionReason: redactionReason)],
            [BaseObservation("main")]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("branch", "", "BaseBranchObservationBaseBranchRequired")]
    [DataRow("branch", @"feature\bad", "BaseBranchObservationBaseBranchInvalid")]
    [DataRow("sha", "", "BaseBranchObservationCommitShaRequired")]
    [DataRow("sha", "not-a-sha", "BaseBranchObservationCommitShaInvalid")]
    [DataRow("patch-hash", "latest", "BaseBranchObservationPatchHashInvalid")]
    [DataRow("correlation", "bad-corr", "BaseBranchObservationCorrelationIdInvalid")]
    [DataRow("observed", "", "BaseBranchObservationObservedAtRequired")]
    [DataRow("recorded", "", "BaseBranchObservationRecordedAtRequired")]
    [DataRow("surface-kind", "Unknown", "BaseBranchObservationSurfaceKindRequired")]
    [DataRow("surface-id", "", "BaseBranchObservationSurfaceIdRequired")]
    [DataRow("reference-kind", "CommitSha", "BaseBranchObservationReferenceIdRequired")]
    [DataRow("reference-id", "without-kind", "BaseBranchObservationReferenceKindRequired")]
    [DataRow("reference-id", "ref unsafe", "BaseBranchObservationReferenceIdInvalid")]
    [DataRow("source", "", "BaseBranchObservationSourceRequired")]
    [DataRow("source", "source secret", "BaseBranchObservationSourceInvalid")]
    public void BaseObservationValidation_FailsClosed(string field, string value, string expectedIssue)
    {
        var observation = field switch
        {
            "branch" => BaseObservation(value),
            "sha" => BaseObservation("main", observedBaseCommitSha: value),
            "patch-hash" => BaseObservation("main", observedPatchHash: value),
            "correlation" => BaseObservation("main", correlationId: value),
            "observed" => BaseObservation("main", observedAtUtc: DateTimeOffset.MinValue),
            "recorded" => BaseObservation("main", recordedAtUtc: DateTimeOffset.MinValue),
            "surface-kind" => BaseObservation("main", surfaceKind: OperationCorrelationSurfaceKind.Unknown),
            "surface-id" => BaseObservation("main", surfaceId: value),
            "reference-kind" => BaseObservation("main", referenceKind: OperationReferenceKind.CommitSha, referenceId: ""),
            "reference-id" when value == "without-kind" => BaseObservation("main", referenceKind: OperationReferenceKind.Unknown, referenceId: "ref-1"),
            "reference-id" => BaseObservation("main", referenceKind: OperationReferenceKind.CommitSha, referenceId: value),
            "source" => BaseObservation("main", source: value),
            _ => throw new InvalidOperationException(field)
        };

        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch)],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch)],
            [observation]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void BaseObservationRecordedBeforeObserved_FailsClosed()
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch)],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch)],
            [BaseObservation("main", observedAtUtc: ObservedAtUtc, recordedAtUtc: ObservedAtUtc.AddSeconds(-1))]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "BaseBranchObservationRecordedBeforeObserved");
    }

    [DataTestMethod]
    [DataRow(true, null, "BaseBranchObservationRedactionReasonRequired")]
    [DataRow(false, "raw diff leaked", "BaseBranchObservationRedactionReasonInvalid")]
    public void RedactedBaseObservationRequiresSafeReason(bool isRedacted, string? redactionReason, string expectedIssue)
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch)],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch)],
            [BaseObservation("main", isRedacted: isRedacted, redactionReason: redactionReason)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("tenant-other", "project-d12", "op_0000000000000012", "rule", "PatchBaseFreshnessRuleTenantMismatch")]
    [DataRow("tenant-d12", "project-other", "op_0000000000000012", "rule", "PatchBaseFreshnessRuleProjectMismatch")]
    [DataRow("tenant-d12", "project-d12", "op_0000000000000099", "rule", "PatchBaseFreshnessRuleOperationMismatch")]
    [DataRow("tenant-other", "project-d12", "op_0000000000000012", "patch", "PatchArtifactTenantMismatch")]
    [DataRow("tenant-d12", "project-other", "op_0000000000000012", "patch", "PatchArtifactProjectMismatch")]
    [DataRow("tenant-d12", "project-d12", "op_0000000000000099", "patch", "PatchArtifactOperationMismatch")]
    [DataRow("tenant-other", "project-d12", "op_0000000000000012", "base", "BaseBranchObservationTenantMismatch")]
    [DataRow("tenant-d12", "project-other", "op_0000000000000012", "base", "BaseBranchObservationProjectMismatch")]
    [DataRow("tenant-d12", "project-d12", "op_0000000000000099", "base", "BaseBranchObservationOperationMismatch")]
    public void CrossScopeMetadataFailsClosed(
        string tenantId,
        string projectId,
        string operationId,
        string target,
        string expectedIssue)
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch, tenantId: target == "rule" ? tenantId : null, projectId: target == "rule" ? projectId : null, operationId: target == "rule" ? operationId : null)],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch, tenantId: target == "patch" ? tenantId : null, projectId: target == "patch" ? projectId : null, operationId: target == "patch" ? operationId : null)],
            [BaseObservation("main", tenantId: target == "base" ? tenantId : null, projectId: target == "base" ? projectId : null, operationId: target == "base" ? operationId : null)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("fresh patch metadata is not authority")]
    [DataRow("patch hash match is not approval")]
    [DataRow("matching base branch metadata is not source apply permission")]
    [DataRow("base moved is not policy denial")]
    [DataRow("patch expired is not next-safe-action selection")]
    [DataRow("patch/base freshness resolver is not validation staleness resolution")]
    [DataRow("patch/base freshness resolver is not merge readiness")]
    [DataRow("patch/base freshness resolver is not release readiness")]
    [DataRow("patch/base freshness resolver is not deployment readiness")]
    public void PatchBaseFreshnessDoesNotGrantAuthority(string boundary)
    {
        var result = Resolve(
            [Rule(PatchArtifactKind.GeneratedPatch)],
            [PatchArtifact("patch-generated", PatchArtifactKind.GeneratedPatch)],
            [BaseObservation("main")]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.ForbiddenAuthorityImplications, boundary);
    }

    [TestMethod]
    public void StaleAndExpiredPatchResultsDoNotChooseNextSafeAction()
    {
        var result = Resolve(
            [
                Rule(PatchArtifactKind.GeneratedPatch, freshFor: TimeSpan.FromMinutes(1), expiresAfter: TimeSpan.FromMinutes(2)),
                Rule(PatchArtifactKind.DryRunPatch, freshFor: TimeSpan.FromMinutes(1), expiresAfter: TimeSpan.FromHours(2))
            ],
            [
                PatchArtifact("patch-expired", PatchArtifactKind.GeneratedPatch, baseBranch: "main", createdAtUtc: AsOfUtc.AddMinutes(-30)),
                PatchArtifact("patch-stale", PatchArtifactKind.DryRunPatch, baseBranch: "release", createdAtUtc: AsOfUtc.AddMinutes(-30))
            ],
            [
                BaseObservation("main"),
                BaseObservation("release")
            ]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.Warnings, "stale or expired patch metadata does not choose next safe action");
        foreach (var property in typeof(PatchBaseFreshnessResolverResult).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            Assert.IsFalse(property.Name.Contains("NextSafeAction", StringComparison.OrdinalIgnoreCase));
        }
    }

    [TestMethod]
    public void ResultModelsExposeNoAuthorityOrRawPatchProperties()
    {
        var modelTypes = new[]
        {
            typeof(PatchBaseFreshnessAssessment),
            typeof(PatchBaseFreshnessResolverResult)
        };

        var forbiddenFragments = new[]
        {
            "Can",
            "Approval",
            "PolicySatisfied",
            "AuthorityGranted",
            "ActionAllowed",
            "NextSafeAction",
            "FreshEnough",
            "ReadyToMutate",
            "RawPatch",
            "RawDiff",
            "RawPayload",
            "PayloadText",
            "ExecutionProven"
        };

        foreach (var type in modelTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.Name == nameof(PatchBaseFreshnessResolverResult.ForbiddenAuthorityImplications))
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
    public void PriorD01ThroughD11ContractsRemainCompatible()
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
                    SurfaceKind = OperationCorrelationSurfaceKind.PatchPackageMetadata,
                    SurfaceId = "surface-patch",
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
    public void StaticScan_D12CoreAddsNoApiSqlUiStoreExecutorOrMutationSurface()
    {
        var source = StripStringLiterals(D12CoreSource());
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
            "Repository",
            "Store",
            "SaveChanges",
            "File.ReadAllText",
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
    public void StaticScan_D12CoreDoesNotUseSystemClock()
    {
        var source = D12CoreSource();

        AssertDoesNotContain(source, "DateTimeOffset.UtcNow");
        AssertDoesNotContain(source, "DateTime.UtcNow");
        AssertDoesNotContain(source, "DateTime.Now");
        AssertDoesNotContain(source, "TimeProvider");
        AssertDoesNotContain(source, "Stopwatch");
    }

    [TestMethod]
    public void StaticScan_D12CoreDoesNotReadRawPatchDiffOrInvokeGitProcess()
    {
        var source = StripStringLiterals(D12CoreSource());
        var forbiddenMarkers = new[]
        {
            "ReadPatch",
            "ReadDiff",
            "PatchContent",
            "DiffContent",
            "RawPatchReader",
            "RawDiffReader",
            "File.OpenRead",
            "File.ReadAllText",
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
        var receipt = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "D12_PATCH_BASE_FRESHNESS_RESOLVER.md"));

        AssertContains(receipt, "The patch hash / base branch freshness resolver classifies supplied patch artifact metadata and supplied base branch observation metadata using supplied freshness rules and supplied AsOfUtc only.");
        AssertContains(receipt, "Fresh patch metadata is not authority.");
        AssertContains(receipt, "Patch hash match is not approval.");
        AssertContains(receipt, "Complete patch/base assessment is not action allowed.");
    }

    private static PatchBaseFreshnessResolverResult Resolve(
        IReadOnlyList<PatchBaseFreshnessRule> rules,
        IReadOnlyList<PatchArtifactFreshnessMetadata> patches,
        IReadOnlyList<BaseBranchObservationMetadata>? observations = null,
        DateTimeOffset? asOfUtc = null) =>
        PatchBaseFreshnessResolver.Resolve(Request(rules, patches, observations ?? [], asOfUtc));

    private static PatchBaseFreshnessResolverRequest Request(
        IReadOnlyList<PatchBaseFreshnessRule> rules,
        IReadOnlyList<PatchArtifactFreshnessMetadata> patches,
        IReadOnlyList<BaseBranchObservationMetadata> observations,
        DateTimeOffset? asOfUtc = null) =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            AsOfUtc = asOfUtc ?? AsOfUtc,
            Rules = rules,
            PatchArtifacts = patches,
            BaseBranchObservations = observations
        };

    private static PatchBaseFreshnessRule Rule(
        PatchArtifactKind patchKind,
        string? tenantId = null,
        string? projectId = null,
        string? operationId = null,
        string? ruleId = null,
        TimeSpan? freshFor = null,
        TimeSpan? expiresAfter = null,
        bool requireBaseBranchMatch = true,
        bool requirePatchHashMatch = true,
        string source = "rule-source",
        DateTimeOffset? createdAtUtc = null,
        bool useDefaultCreatedAt = false) =>
        new()
        {
            TenantId = tenantId ?? TenantId,
            ProjectId = projectId ?? ProjectId,
            OperationId = operationId ?? OperationId,
            RuleId = ruleId ?? $"rule-{patchKind.ToString().ToLowerInvariant()}",
            PatchKind = patchKind,
            FreshFor = freshFor ?? TimeSpan.FromHours(1),
            ExpiresAfter = expiresAfter ?? TimeSpan.FromHours(2),
            RequireBaseBranchMatch = requireBaseBranchMatch,
            RequirePatchHashMatch = requirePatchHashMatch,
            Source = source,
            CreatedAtUtc = useDefaultCreatedAt ? default : createdAtUtc ?? AsOfUtc.AddHours(-1)
        };

    private static PatchArtifactFreshnessMetadata PatchArtifact(
        string patchArtifactId,
        PatchArtifactKind patchKind,
        string? tenantId = null,
        string? projectId = null,
        string? operationId = null,
        string correlationId = CorrelationId,
        string patchHash = PatchHash,
        PatchHashAlgorithm hashAlgorithm = PatchHashAlgorithm.Sha256,
        string baseBranch = "main",
        string baseCommitSha = BaseCommitSha,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? recordedAtUtc = null,
        OperationCorrelationSurfaceKind surfaceKind = OperationCorrelationSurfaceKind.PatchPackageMetadata,
        string? surfaceId = null,
        OperationReferenceKind referenceKind = OperationReferenceKind.PatchArtifactId,
        string? referenceId = null,
        string source = "patch-source",
        bool isRedacted = false,
        string? redactionReason = null) =>
        new()
        {
            TenantId = tenantId ?? TenantId,
            ProjectId = projectId ?? ProjectId,
            OperationId = operationId ?? OperationId,
            CorrelationId = correlationId,
            PatchArtifactId = patchArtifactId,
            PatchKind = patchKind,
            PatchHash = patchHash,
            HashAlgorithm = hashAlgorithm,
            BaseBranch = baseBranch,
            BaseCommitSha = baseCommitSha,
            CreatedAtUtc = createdAtUtc ?? CreatedAtUtc,
            RecordedAtUtc = recordedAtUtc ?? (createdAtUtc.HasValue ? createdAtUtc.Value.AddMinutes(5) : RecordedAtUtc),
            SurfaceKind = surfaceKind,
            SurfaceId = surfaceId ?? $"surface-{patchArtifactId}",
            ReferenceKind = referenceKind,
            ReferenceId = referenceId ?? $"ref-{patchArtifactId}",
            Source = source,
            IsRedacted = isRedacted,
            RedactionReason = redactionReason
        };

    private static BaseBranchObservationMetadata BaseObservation(
        string baseBranch,
        string? tenantId = null,
        string? projectId = null,
        string? operationId = null,
        string correlationId = CorrelationId,
        string observedBaseCommitSha = BaseCommitSha,
        string? observedPatchHash = PatchHash,
        DateTimeOffset? observedAtUtc = null,
        DateTimeOffset? recordedAtUtc = null,
        OperationCorrelationSurfaceKind surfaceKind = OperationCorrelationSurfaceKind.TimelineEvent,
        string? surfaceId = null,
        OperationReferenceKind referenceKind = OperationReferenceKind.TimelineEventId,
        string? referenceId = null,
        string source = "base-source",
        bool isRedacted = false,
        string? redactionReason = null) =>
        new()
        {
            TenantId = tenantId ?? TenantId,
            ProjectId = projectId ?? ProjectId,
            OperationId = operationId ?? OperationId,
            CorrelationId = correlationId,
            BaseBranch = baseBranch,
            ObservedBaseCommitSha = observedBaseCommitSha,
            ObservedPatchHash = observedPatchHash,
            ObservedAtUtc = observedAtUtc ?? ObservedAtUtc,
            RecordedAtUtc = recordedAtUtc ?? (observedAtUtc.HasValue ? observedAtUtc.Value.AddMinutes(5) : ObservedAtUtc.AddMinutes(5)),
            SurfaceKind = surfaceKind,
            SurfaceId = surfaceId ?? $"surface-{baseBranch}",
            ReferenceKind = referenceKind,
            ReferenceId = referenceId ?? $"base-ref-{baseBranch}",
            Source = source,
            IsRedacted = isRedacted,
            RedactionReason = redactionReason
        };

    private static void AssertAmbiguous(PatchBaseFreshnessResolverResult result, string expectedDiagnostic)
    {
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(PatchBaseFreshnessResolutionStatus.AmbiguousPatchBaseMetadata, result.ResolutionStatus);
        Assert.AreEqual(0, result.Assessments.Count);
        AssertContains(result.AmbiguousPatchBaseMetadata, expectedDiagnostic);
        AssertContains(result.Warnings, "ambiguous patch/base metadata does not choose a winner");
    }

    private static string D12CoreSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "PatchBaseFreshnessResolverModels.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "PatchBaseFreshnessResolverValidator.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "PatchBaseFreshnessResolver.cs")));
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
