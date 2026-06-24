using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockD11ValidationResultStalenessResolverTests
{
    private const string TenantId = "tenant-d11";
    private const string ProjectId = "project-d11";
    private const string OperationId = "op_0000000000000011";
    private const string CorrelationId = "corr_1123456789abcdef";
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
    private static readonly DateTimeOffset CompletedAtUtc = DateTimeOffset.Parse("2026-06-24T11:30:00Z");
    private static readonly DateTimeOffset RecordedAtUtc = DateTimeOffset.Parse("2026-06-24T11:35:00Z");

    [TestMethod]
    public void ValidRequestWithNoValidationResults_ReturnsNoValidationResults()
    {
        var result = Resolve([Rule(ValidationResultKind.FocusedTests)], []);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ValidationStalenessResolutionStatus.NoValidationResults, result.ResolutionStatus);
        Assert.AreEqual(0, result.Assessments.Count);
        AssertContains(result.ForbiddenAuthorityImplications, "fresh validation is not authority");
    }

    [TestMethod]
    public void FreshValidationResult_IsClassifiedFresh()
    {
        var result = Resolve(
            [Rule(ValidationResultKind.FocusedTests, freshFor: TimeSpan.FromHours(1), expiresAfter: TimeSpan.FromHours(2))],
            [ValidationResult("validation-focused", ValidationResultKind.FocusedTests, completedAtUtc: AsOfUtc.AddMinutes(-30))]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ValidationStalenessResolutionStatus.Assessed, result.ResolutionStatus);
        Assert.AreEqual(ValidationStalenessState.Fresh, result.Assessments[0].StalenessState);
        Assert.AreEqual(TimeSpan.FromMinutes(30), result.Assessments[0].Age);
        Assert.AreEqual("rule-focusedtests", result.Assessments[0].RuleId);
        AssertContains(result.Warnings, "fresh validation is not authority");
    }

    [TestMethod]
    public void StaleValidationResult_IsClassifiedStale()
    {
        var result = Resolve(
            [Rule(ValidationResultKind.FocusedTests, freshFor: TimeSpan.FromMinutes(20), expiresAfter: TimeSpan.FromHours(2))],
            [ValidationResult("validation-focused", ValidationResultKind.FocusedTests, completedAtUtc: AsOfUtc.AddMinutes(-30))]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ValidationStalenessResolutionStatus.Assessed, result.ResolutionStatus);
        Assert.AreEqual(ValidationStalenessState.Stale, result.Assessments[0].StalenessState);
        AssertContains(result.Warnings, "stale or expired validation does not choose next safe action");
    }

    [TestMethod]
    public void ExpiredValidationResult_IsClassifiedExpired()
    {
        var result = Resolve(
            [Rule(ValidationResultKind.FocusedTests, freshFor: TimeSpan.FromMinutes(10), expiresAfter: TimeSpan.FromMinutes(20))],
            [ValidationResult("validation-focused", ValidationResultKind.FocusedTests, completedAtUtc: AsOfUtc.AddMinutes(-30))]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ValidationStalenessResolutionStatus.Assessed, result.ResolutionStatus);
        Assert.AreEqual(ValidationStalenessState.Expired, result.Assessments[0].StalenessState);
        AssertContains(result.ForbiddenAuthorityImplications, "expired validation is not policy decision");
    }

    [TestMethod]
    public void MissingRule_ReturnsMissingRules()
    {
        var result = Resolve(
            [Rule(ValidationResultKind.Build)],
            [ValidationResult("validation-focused", ValidationResultKind.FocusedTests)]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ValidationStalenessResolutionStatus.MissingRules, result.ResolutionStatus);
        Assert.AreEqual(ValidationStalenessState.MissingRule, result.Assessments[0].StalenessState);
        Assert.AreEqual("ValidationStalenessRuleMissing", result.Assessments[0].Reason);
    }

    [TestMethod]
    public void MixedFreshStaleExpiredResults_ReturnsMixedStaleness()
    {
        var result = Resolve(
            [
                Rule(ValidationResultKind.FocusedTests, freshFor: TimeSpan.FromHours(1), expiresAfter: TimeSpan.FromHours(2)),
                Rule(ValidationResultKind.Build, freshFor: TimeSpan.FromMinutes(20), expiresAfter: TimeSpan.FromHours(2)),
                Rule(ValidationResultKind.DiffCheck, freshFor: TimeSpan.FromMinutes(10), expiresAfter: TimeSpan.FromMinutes(20))
            ],
            [
                ValidationResult("validation-focused", ValidationResultKind.FocusedTests, completedAtUtc: AsOfUtc.AddMinutes(-30)),
                ValidationResult("validation-build", ValidationResultKind.Build, completedAtUtc: AsOfUtc.AddMinutes(-30)),
                ValidationResult("validation-diff", ValidationResultKind.DiffCheck, completedAtUtc: AsOfUtc.AddMinutes(-30))
            ]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ValidationStalenessResolutionStatus.MixedStaleness, result.ResolutionStatus);
        CollectionAssert.AreEqual(
            new[]
            {
                ValidationStalenessState.Stale,
                ValidationStalenessState.Expired,
                ValidationStalenessState.Fresh
            },
            result.Assessments.Select(static assessment => assessment.StalenessState).ToArray());
    }

    [TestMethod]
    public void FailedValidationResultCanBeFreshWithoutBecomingForbiddenAction()
    {
        var result = Resolve(
            [Rule(ValidationResultKind.FocusedTests)],
            [ValidationResult("validation-failed", ValidationResultKind.FocusedTests, outcome: ValidationResultOutcome.Failed)]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ValidationResultOutcome.Failed, result.Assessments[0].Outcome);
        Assert.AreEqual(ValidationStalenessState.Fresh, result.Assessments[0].StalenessState);
        AssertContains(result.ForbiddenAuthorityImplications, "failed validation is not forbidden-action resolution");
        AssertDoesNotContain(string.Join("|", result.Warnings), "forbidden action");
    }

    [TestMethod]
    public void PassedExpiredValidationDoesNotBecomeApproval()
    {
        var result = Resolve(
            [Rule(ValidationResultKind.Build, freshFor: TimeSpan.FromMinutes(10), expiresAfter: TimeSpan.FromMinutes(20))],
            [ValidationResult("validation-build", ValidationResultKind.Build, outcome: ValidationResultOutcome.Passed, completedAtUtc: AsOfUtc.AddMinutes(-30))]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ValidationStalenessState.Expired, result.Assessments[0].StalenessState);
        AssertContains(result.ForbiddenAuthorityImplications, "passed validation is not approval");
    }

    [DataTestMethod]
    [DataRow(ValidationResultOutcome.Skipped)]
    [DataRow(ValidationResultOutcome.Cancelled)]
    [DataRow(ValidationResultOutcome.NotRun)]
    [DataRow(ValidationResultOutcome.Errored)]
    public void NonPassingOutcomesArePreservedAsMetadata(ValidationResultOutcome outcome)
    {
        var result = Resolve(
            [Rule(ValidationResultKind.FocusedTests)],
            [ValidationResult("validation-focused", ValidationResultKind.FocusedTests, outcome: outcome)]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(outcome, result.Assessments[0].Outcome);
        Assert.AreEqual(ValidationStalenessState.Fresh, result.Assessments[0].StalenessState);
    }

    [TestMethod]
    public void AgeIsCalculatedFromSuppliedAsOfUtc()
    {
        var suppliedAsOf = DateTimeOffset.Parse("2026-06-24T15:00:00Z");
        var result = Resolve(
            [Rule(ValidationResultKind.Build, freshFor: TimeSpan.FromHours(4), expiresAfter: TimeSpan.FromHours(8))],
            [ValidationResult("validation-build", ValidationResultKind.Build, completedAtUtc: DateTimeOffset.Parse("2026-06-24T12:00:00Z"))],
            asOfUtc: suppliedAsOf);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(TimeSpan.FromHours(3), result.Assessments[0].Age);
        Assert.AreEqual(suppliedAsOf, result.AsOfUtc);
    }

    [TestMethod]
    public void AsOfBeforeCompletedAt_ReturnsUnassessable()
    {
        var result = Resolve(
            [Rule(ValidationResultKind.Build)],
            [ValidationResult("validation-build", ValidationResultKind.Build, completedAtUtc: AsOfUtc.AddMinutes(1))]);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(ValidationStalenessResolutionStatus.Unassessable, result.ResolutionStatus);
        Assert.AreEqual(ValidationStalenessState.Unassessable, result.Assessments[0].StalenessState);
        AssertContains(result.Issues, "ValidationResultCompletedAfterAsOf");
    }

    [TestMethod]
    public void RecordedBeforeCompleted_FailsClosed()
    {
        var result = Resolve(
            [Rule(ValidationResultKind.Build)],
            [ValidationResult("validation-build", ValidationResultKind.Build, completedAtUtc: CompletedAtUtc, recordedAtUtc: CompletedAtUtc.AddSeconds(-1))]);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(ValidationStalenessResolutionStatus.InvalidRequest, result.ResolutionStatus);
        AssertContains(result.Issues, "ValidationResultRecordedBeforeCompleted");
    }

    [TestMethod]
    public void DuplicateRuleIds_ReturnAmbiguousValidationResults()
    {
        var result = Resolve(
            [
                Rule(ValidationResultKind.Build, ruleId: "rule-duplicate"),
                Rule(ValidationResultKind.Build, ruleId: "rule-duplicate")
            ],
            [ValidationResult("validation-build", ValidationResultKind.Build)]);

        AssertAmbiguous(result, "DuplicateValidationStalenessRuleId:rule-duplicate");
    }

    [TestMethod]
    public void DuplicateValidationResultIds_ReturnAmbiguousValidationResults()
    {
        var result = Resolve(
            [Rule(ValidationResultKind.Build)],
            [
                ValidationResult("validation-duplicate", ValidationResultKind.Build),
                ValidationResult("validation-duplicate", ValidationResultKind.Build)
            ]);

        AssertAmbiguous(result, "DuplicateValidationResultId:validation-duplicate");
    }

    [TestMethod]
    public void ConflictingRuleMetadata_ReturnsAmbiguousValidationResults()
    {
        var result = Resolve(
            [
                Rule(ValidationResultKind.Build, ruleId: "rule-conflict", source: "source-a"),
                Rule(ValidationResultKind.Build, ruleId: "rule-conflict", source: "source-b")
            ],
            [ValidationResult("validation-build", ValidationResultKind.Build)]);

        Assert.AreEqual(ValidationStalenessResolutionStatus.AmbiguousValidationResults, result.ResolutionStatus);
        AssertContains(result.AmbiguousValidationResults, "DuplicateValidationStalenessRuleId:rule-conflict");
        AssertContains(result.AmbiguousValidationResults, "ConflictingValidationStalenessRuleMetadata:rule-conflict");
    }

    [TestMethod]
    public void ConflictingValidationResultMetadata_ReturnsAmbiguousValidationResults()
    {
        var result = Resolve(
            [Rule(ValidationResultKind.Build)],
            [
                ValidationResult("validation-conflict", ValidationResultKind.Build, source: "source-a"),
                ValidationResult("validation-conflict", ValidationResultKind.Build, source: "source-b")
            ]);

        Assert.AreEqual(ValidationStalenessResolutionStatus.AmbiguousValidationResults, result.ResolutionStatus);
        AssertContains(result.AmbiguousValidationResults, "DuplicateValidationResultId:validation-conflict");
        AssertContains(result.AmbiguousValidationResults, "ConflictingValidationResultMetadata:validation-conflict");
    }

    [TestMethod]
    public void MultipleRulesForSameValidationKind_ReturnAmbiguousValidationResults()
    {
        var result = Resolve(
            [
                Rule(ValidationResultKind.Build, ruleId: "rule-build-a"),
                Rule(ValidationResultKind.Build, ruleId: "rule-build-b")
            ],
            [ValidationResult("validation-build", ValidationResultKind.Build)]);

        AssertAmbiguous(result, "MultipleValidationStalenessRulesForKind:Build");
    }

    [TestMethod]
    public void IndistinguishableValidationResults_ReturnAmbiguousValidationResults()
    {
        var result = Resolve(
            [Rule(ValidationResultKind.Build)],
            [
                ValidationResult("validation-a", ValidationResultKind.Build),
                ValidationResult("validation-b", ValidationResultKind.Build)
            ]);

        AssertAmbiguous(result, $"IndistinguishableValidationResults:{ValidationResultKind.Build}:{CorrelationId}:validation-source:{OperationCorrelationSurfaceKind.ValidationResult}:surface-build:{OperationReferenceKind.Unknown}:");
    }

    [TestMethod]
    public void AmbiguityDoesNotChooseWinner()
    {
        var result = Resolve(
            [
                Rule(ValidationResultKind.Build, ruleId: "rule-build-a"),
                Rule(ValidationResultKind.Build, ruleId: "rule-build-b")
            ],
            [ValidationResult("validation-build", ValidationResultKind.Build)]);

        Assert.AreEqual(ValidationStalenessResolutionStatus.AmbiguousValidationResults, result.ResolutionStatus);
        Assert.AreEqual(0, result.Assessments.Count);
        AssertContains(result.Warnings, "ambiguous validation results do not choose a winner");
    }

    [TestMethod]
    public void AssessmentsSortDeterministically()
    {
        var result = Resolve(
            [
                Rule(ValidationResultKind.FocusedTests),
                Rule(ValidationResultKind.Build),
                Rule(ValidationResultKind.DiffCheck)
            ],
            [
                ValidationResult("validation-z", ValidationResultKind.FocusedTests),
                ValidationResult("validation-a", ValidationResultKind.Build),
                ValidationResult("validation-m", ValidationResultKind.DiffCheck)
            ]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        CollectionAssert.AreEqual(
            new[] { "validation-a", "validation-m", "validation-z" },
            result.Assessments.Select(static assessment => assessment.ValidationResultId).ToArray());
    }

    [TestMethod]
    public void AmbiguityDiagnosticsSortDeterministically()
    {
        var result = Resolve(
            [
                Rule(ValidationResultKind.Build, ruleId: "rule-z"),
                Rule(ValidationResultKind.Build, ruleId: "rule-a")
            ],
            [
                ValidationResult("validation-z", ValidationResultKind.Build),
                ValidationResult("validation-a", ValidationResultKind.Build)
            ]);

        Assert.AreEqual(ValidationStalenessResolutionStatus.AmbiguousValidationResults, result.ResolutionStatus);
        CollectionAssert.AreEqual(
            result.AmbiguousValidationResults.OrderBy(static item => item, StringComparer.Ordinal).ToArray(),
            result.AmbiguousValidationResults.ToArray());
    }

    [DataTestMethod]
    [DataRow("", "project-d11", "op_0000000000000011", "2026-06-24T12:00:00Z", "ValidationStalenessTenantIdRequired")]
    [DataRow("tenant d11", "project-d11", "op_0000000000000011", "2026-06-24T12:00:00Z", "ValidationStalenessTenantIdInvalid")]
    [DataRow("tenant-d11", "", "op_0000000000000011", "2026-06-24T12:00:00Z", "ValidationStalenessProjectIdRequired")]
    [DataRow("tenant-d11", "project d11", "op_0000000000000011", "2026-06-24T12:00:00Z", "ValidationStalenessProjectIdInvalid")]
    [DataRow("tenant-d11", "project-d11", "", "2026-06-24T12:00:00Z", "OperationIdRequired")]
    [DataRow("tenant-d11", "project-d11", "run-123", "2026-06-24T12:00:00Z", "OperationIdMustBeBackendMintedCanonicalId")]
    [DataRow("tenant-d11", "project-d11", "op_0000000000000011", "0001-01-01T00:00:00Z", "ValidationStalenessAsOfUtcRequired")]
    public void RequestScopeValidation_FailsClosed(
        string tenantId,
        string projectId,
        string operationId,
        string asOfUtc,
        string expectedIssue)
    {
        var result = ValidationStalenessResolver.Resolve(new ValidationStalenessResolverRequest
        {
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            AsOfUtc = DateTimeOffset.Parse(asOfUtc),
            Rules = [Rule(ValidationResultKind.Build)],
            ValidationResults = [ValidationResult("validation-build", ValidationResultKind.Build)]
        });

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(ValidationStalenessResolutionStatus.InvalidRequest, result.ResolutionStatus);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void NullRulesList_FailsClosed()
    {
        var result = ValidationStalenessResolver.Resolve(new ValidationStalenessResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            AsOfUtc = AsOfUtc,
            Rules = null!,
            ValidationResults = []
        });

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "ValidationStalenessRulesRequired");
    }

    [TestMethod]
    public void NullValidationResultsList_FailsClosed()
    {
        var result = ValidationStalenessResolver.Resolve(new ValidationStalenessResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            AsOfUtc = AsOfUtc,
            Rules = [],
            ValidationResults = null!
        });

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "ValidationResultsRequired");
    }

    [DataTestMethod]
    [DataRow("", "ValidationStalenessRuleIdRequired")]
    [DataRow("rule unsafe", "ValidationStalenessRuleIdInvalid")]
    public void RuleIdValidation_FailsClosed(string ruleId, string expectedIssue)
    {
        var result = Resolve([Rule(ValidationResultKind.Build, ruleId: ruleId)], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(ValidationResultKind.Unknown, "ValidationStalenessRuleKindRequired")]
    public void RuleValidationKindValidation_FailsClosed(ValidationResultKind validationKind, string expectedIssue)
    {
        var result = Resolve([Rule(validationKind)], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(0, 30, "ValidationStalenessRuleFreshForInvalid")]
    [DataRow(-1, 30, "ValidationStalenessRuleFreshForInvalid")]
    [DataRow(30, 0, "ValidationStalenessRuleExpiresAfterInvalid")]
    [DataRow(30, -1, "ValidationStalenessRuleExpiresAfterInvalid")]
    [DataRow(30, 20, "ValidationStalenessRuleExpiresBeforeFreshWindow")]
    public void RuleWindowValidation_FailsClosed(int freshMinutes, int expiresMinutes, string expectedIssue)
    {
        var result = Resolve(
            [Rule(ValidationResultKind.Build, freshFor: TimeSpan.FromMinutes(freshMinutes), expiresAfter: TimeSpan.FromMinutes(expiresMinutes))],
            []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("", "ValidationStalenessRuleSourceRequired")]
    [DataRow("source secret", "ValidationStalenessRuleSourceInvalid")]
    public void RuleSourceValidation_FailsClosed(string source, string expectedIssue)
    {
        var result = Resolve([Rule(ValidationResultKind.Build, source: source)], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void MissingRuleCreatedAt_FailsClosed()
    {
        var result = Resolve([Rule(ValidationResultKind.Build, useDefaultCreatedAt: true)], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "ValidationStalenessRuleCreatedAtRequired");
    }

    [DataTestMethod]
    [DataRow("", "ValidationResultIdRequired")]
    [DataRow("validation unsafe", "ValidationResultIdInvalid")]
    public void ValidationResultIdValidation_FailsClosed(string validationResultId, string expectedIssue)
    {
        var result = Resolve([Rule(ValidationResultKind.Build)], [ValidationResult(validationResultId, ValidationResultKind.Build)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(ValidationResultKind.Unknown, "ValidationResultKindRequired")]
    public void ValidationResultKindValidation_FailsClosed(ValidationResultKind validationKind, string expectedIssue)
    {
        var result = Resolve([Rule(ValidationResultKind.Build)], [ValidationResult("validation-build", validationKind)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(ValidationResultOutcome.Unknown, "ValidationResultOutcomeRequired")]
    public void ValidationResultOutcomeValidation_FailsClosed(ValidationResultOutcome outcome, string expectedIssue)
    {
        var result = Resolve([Rule(ValidationResultKind.Build)], [ValidationResult("validation-build", ValidationResultKind.Build, outcome: outcome)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("", "ValidationResultCorrelationIdRequired")]
    [DataRow("corr bad", "ValidationResultCorrelationIdInvalid")]
    [DataRow("op_0000000000000011", "ValidationResultCorrelationIdInvalid")]
    public void ValidationResultCorrelationValidation_FailsClosed(string correlationId, string expectedIssue)
    {
        var result = Resolve([Rule(ValidationResultKind.Build)], [ValidationResult("validation-build", ValidationResultKind.Build, correlationId: correlationId)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("completed", "ValidationResultCompletedAtRequired")]
    [DataRow("recorded", "ValidationResultRecordedAtRequired")]
    public void ValidationResultTimestampValidation_FailsClosed(string missing, string expectedIssue)
    {
        var result = Resolve(
            [Rule(ValidationResultKind.Build)],
            [
                ValidationResult(
                    "validation-build",
                    ValidationResultKind.Build,
                    completedAtUtc: missing == "completed" ? default : CompletedAtUtc,
                    recordedAtUtc: missing == "recorded" ? default : RecordedAtUtc)
            ]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(OperationCorrelationSurfaceKind.Unknown, "surface-build", "ValidationResultSurfaceKindRequired")]
    [DataRow(OperationCorrelationSurfaceKind.ValidationResult, "", "ValidationResultSurfaceIdRequired")]
    [DataRow(OperationCorrelationSurfaceKind.ValidationResult, "surface unsafe", "ValidationResultSurfaceIdInvalid")]
    public void ValidationResultSurfaceValidation_FailsClosed(
        OperationCorrelationSurfaceKind surfaceKind,
        string surfaceId,
        string expectedIssue)
    {
        var result = Resolve(
            [Rule(ValidationResultKind.Build)],
            [ValidationResult("validation-build", ValidationResultKind.Build, surfaceKind: surfaceKind, surfaceId: surfaceId)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(OperationReferenceKind.CommitSha, null, "ValidationResultReferenceIdRequired")]
    [DataRow(OperationReferenceKind.Unknown, "commit-1", "ValidationResultReferenceKindRequired")]
    [DataRow(OperationReferenceKind.CommitSha, "commit unsafe", "ValidationResultReferenceIdInvalid")]
    public void ValidationResultReferencePairValidation_FailsClosed(
        OperationReferenceKind referenceKind,
        string? referenceId,
        string expectedIssue)
    {
        var result = Resolve(
            [Rule(ValidationResultKind.Build)],
            [ValidationResult("validation-build", ValidationResultKind.Build, referenceKind: referenceKind, referenceId: referenceId)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("", "ValidationResultSourceRequired")]
    [DataRow("source secret", "ValidationResultSourceInvalid")]
    public void ValidationResultSourceValidation_FailsClosed(string source, string expectedIssue)
    {
        var result = Resolve([Rule(ValidationResultKind.Build)], [ValidationResult("validation-build", ValidationResultKind.Build, source: source)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(true, null, "ValidationResultRedactionReasonRequired")]
    [DataRow(false, "raw validation log leaked", "ValidationResultRedactionReasonInvalid")]
    public void RedactedValidationMetadataRequiresSafeReason(bool isRedacted, string? redactionReason, string expectedIssue)
    {
        var result = Resolve(
            [Rule(ValidationResultKind.Build)],
            [ValidationResult("validation-build", ValidationResultKind.Build, isRedacted: isRedacted, redactionReason: redactionReason)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("tenant-other", "project-d11", "op_0000000000000011", "ValidationStalenessRuleTenantMismatch")]
    [DataRow("tenant-d11", "project-other", "op_0000000000000011", "ValidationStalenessRuleProjectMismatch")]
    [DataRow("tenant-d11", "project-d11", "op_0000000000000099", "ValidationStalenessRuleOperationMismatch")]
    public void CrossScopeRulesFailClosed(string tenantId, string projectId, string operationId, string expectedIssue)
    {
        var result = Resolve(
            [Rule(ValidationResultKind.Build, tenantId: tenantId, projectId: projectId, operationId: operationId)],
            []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("tenant-other", "project-d11", "op_0000000000000011", "ValidationResultTenantMismatch")]
    [DataRow("tenant-d11", "project-other", "op_0000000000000011", "ValidationResultProjectMismatch")]
    [DataRow("tenant-d11", "project-d11", "op_0000000000000099", "ValidationResultOperationMismatch")]
    public void CrossScopeValidationResultsFailClosed(string tenantId, string projectId, string operationId, string expectedIssue)
    {
        var result = Resolve(
            [Rule(ValidationResultKind.Build)],
            [ValidationResult("validation-build", ValidationResultKind.Build, tenantId: tenantId, projectId: projectId, operationId: operationId)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("fresh validation is not authority")]
    [DataRow("passed validation is not approval")]
    [DataRow("validation staleness resolver is not policy satisfaction")]
    [DataRow("validation staleness resolver is not source apply")]
    [DataRow("validation staleness resolver is not merge readiness")]
    [DataRow("validation staleness resolver is not release readiness")]
    [DataRow("validation staleness resolver is not deployment readiness")]
    [DataRow("stale validation is not denial")]
    [DataRow("expired validation is not policy decision")]
    public void ValidationStalenessDoesNotGrantAuthority(string boundary)
    {
        var result = Resolve([Rule(ValidationResultKind.Build)], [ValidationResult("validation-build", ValidationResultKind.Build)]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.ForbiddenAuthorityImplications, boundary);
    }

    [TestMethod]
    public void StaleAndExpiredValidationDoNotChooseNextSafeAction()
    {
        var result = Resolve(
            [
                Rule(ValidationResultKind.Build, freshFor: TimeSpan.FromMinutes(1), expiresAfter: TimeSpan.FromMinutes(2)),
                Rule(ValidationResultKind.DiffCheck, freshFor: TimeSpan.FromMinutes(1), expiresAfter: TimeSpan.FromHours(2))
            ],
            [
                ValidationResult("validation-build", ValidationResultKind.Build, completedAtUtc: AsOfUtc.AddMinutes(-30)),
                ValidationResult("validation-diff", ValidationResultKind.DiffCheck, completedAtUtc: AsOfUtc.AddMinutes(-30))
            ]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.Warnings, "stale or expired validation does not choose next safe action");
        foreach (var property in typeof(ValidationStalenessResolverResult).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            Assert.IsFalse(property.Name.Contains("NextSafeAction", StringComparison.OrdinalIgnoreCase));
        }
    }

    [TestMethod]
    public void ResultModelsExposeNoAuthorityOrRawLogProperties()
    {
        var modelTypes = new[]
        {
            typeof(ValidationResultStalenessAssessment),
            typeof(ValidationStalenessResolverResult)
        };

        var forbiddenFragments = new[]
        {
            "Can",
            "Approval",
            "PolicySatisfied",
            "AuthorityGranted",
            "ActionAllowed",
            "NextSafeAction",
            "ValidationFreshEnough",
            "FreshEnoughToProceed",
            "RawValidationLog",
            "RawPayload",
            "PayloadText",
            "ExecutionProven"
        };

        foreach (var type in modelTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
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
    public void PriorD01ThroughD10ContractsRemainCompatible()
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
                    SurfaceKind = OperationCorrelationSurfaceKind.ValidationResult,
                    SurfaceId = "surface-build",
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
    public void StaticScan_D11CoreAddsNoApiSqlUiStoreExecutorOrMutationSurface()
    {
        var source = D11CoreSource();
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
            "ReadValidationLog",
            "RawValidationLogReader",
            "ProcessStartInfo",
            "RunProcessAsync",
            "dotnet test",
            "MissingEvidenceResolver.Resolve",
            "ForbiddenActionResolver.Resolve",
            "ReceiptReferenceResolver.Resolve",
            "EvidenceResolver.Resolve",
            "PatchFreshness",
            "Worktree",
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
    public void StaticScan_D11CoreDoesNotUseSystemClock()
    {
        var source = D11CoreSource();

        AssertDoesNotContain(source, "DateTimeOffset.UtcNow");
        AssertDoesNotContain(source, "DateTime.UtcNow");
        AssertDoesNotContain(source, "DateTime.Now");
        AssertDoesNotContain(source, "TimeProvider");
        AssertDoesNotContain(source, "Stopwatch");
    }

    [TestMethod]
    public void ReceiptRecordsRequiredBoundaries()
    {
        var receipt = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "D11_VALIDATION_RESULT_STALENESS_RESOLVER.md"));

        AssertContains(receipt, "The validation result staleness resolver classifies supplied validation metadata using supplied staleness rules and supplied AsOfUtc only.");
        AssertContains(receipt, "Fresh validation is not authority.");
        AssertContains(receipt, "Passed validation is not approval.");
        AssertContains(receipt, "Complete validation assessment is not action allowed.");
    }

    private static ValidationStalenessResolverResult Resolve(
        IReadOnlyList<ValidationStalenessRule> rules,
        IReadOnlyList<ValidationResultMetadata> validationResults,
        DateTimeOffset? asOfUtc = null) =>
        ValidationStalenessResolver.Resolve(new ValidationStalenessResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            AsOfUtc = asOfUtc ?? AsOfUtc,
            Rules = rules,
            ValidationResults = validationResults
        });

    private static ValidationStalenessRule Rule(
        ValidationResultKind validationKind,
        string? tenantId = null,
        string? projectId = null,
        string? operationId = null,
        string? ruleId = null,
        TimeSpan? freshFor = null,
        TimeSpan? expiresAfter = null,
        string source = "rule-source",
        DateTimeOffset? createdAtUtc = null,
        bool useDefaultCreatedAt = false) =>
        new()
        {
            TenantId = tenantId ?? TenantId,
            ProjectId = projectId ?? ProjectId,
            OperationId = operationId ?? OperationId,
            RuleId = ruleId ?? $"rule-{validationKind.ToString().ToLowerInvariant()}",
            ValidationKind = validationKind,
            FreshFor = freshFor ?? TimeSpan.FromHours(1),
            ExpiresAfter = expiresAfter ?? TimeSpan.FromHours(2),
            Source = source,
            CreatedAtUtc = useDefaultCreatedAt ? default : createdAtUtc ?? AsOfUtc.AddHours(-1)
        };

    private static ValidationResultMetadata ValidationResult(
        string validationResultId,
        ValidationResultKind validationKind,
        string? tenantId = null,
        string? projectId = null,
        string? operationId = null,
        string correlationId = CorrelationId,
        ValidationResultOutcome outcome = ValidationResultOutcome.Passed,
        DateTimeOffset? completedAtUtc = null,
        DateTimeOffset? recordedAtUtc = null,
        OperationCorrelationSurfaceKind surfaceKind = OperationCorrelationSurfaceKind.ValidationResult,
        string surfaceId = "surface-build",
        OperationReferenceKind referenceKind = OperationReferenceKind.Unknown,
        string? referenceId = null,
        string source = "validation-source",
        bool isRedacted = false,
        string? redactionReason = null) =>
        new()
        {
            TenantId = tenantId ?? TenantId,
            ProjectId = projectId ?? ProjectId,
            OperationId = operationId ?? OperationId,
            CorrelationId = correlationId,
            ValidationResultId = validationResultId,
            ValidationKind = validationKind,
            Outcome = outcome,
            CompletedAtUtc = completedAtUtc ?? CompletedAtUtc,
            RecordedAtUtc = recordedAtUtc ?? (completedAtUtc.HasValue ? completedAtUtc.Value.AddMinutes(5) : RecordedAtUtc),
            SurfaceKind = surfaceKind,
            SurfaceId = surfaceId,
            ReferenceKind = referenceKind,
            ReferenceId = referenceId,
            Source = source,
            IsRedacted = isRedacted,
            RedactionReason = redactionReason
        };

    private static void AssertAmbiguous(ValidationStalenessResolverResult result, string expectedDiagnostic)
    {
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ValidationStalenessResolutionStatus.AmbiguousValidationResults, result.ResolutionStatus);
        Assert.AreEqual(0, result.Assessments.Count);
        AssertContains(result.AmbiguousValidationResults, expectedDiagnostic);
    }

    private static string D11CoreSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "ValidationStalenessResolverModels.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "ValidationStalenessResolverValidator.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "ValidationStalenessResolver.cs")));
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
