using IronDev.Core.Auth;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockA07ValidationResultMetadataReadAdapterTests
{
    private const string ValidationResultId = "validation-result:a07";
    private const string RepositoryName = "BigDaddyDread-code/IronDeveloper";
    private const string Branch = "frontend/validation-result-metadata-read-adapter";
    private const string RunId = "run-a07";
    private const string PatchHash = "sha256:a07";
    private const string Outcome = "Passed";
    private const string WhatRan = "Focused A07";
    private const string WhatPassed = "A07 repository tests";
    private const string WhatFailed = "No failures";
    private const string WhatWasSkipped = "No stable lane";
    private const string EvidenceRef = "validation-evidence:a07";
    private const string ReceiptRef = "validation-receipt:a07";
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-23T05:00:00Z");

    [TestMethod]
    public void ValidationResultMetadataRepository_ReturnsMetadataByValidationResultId()
    {
        var result = Repository(Record()).GetByValidationResultId(ValidationResultId, Scope());

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Metadata);
        Assert.AreEqual(ValidationResultId, result.Metadata.ValidationResultId);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_ReturnsNotFoundForMissingValidationResultId()
    {
        var result = Repository().GetByValidationResultId("missing-validation-result:a07", Scope());

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, "ValidationResultMetadataNotFound");
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_PreservesValidationResultId() =>
        Assert.AreEqual(ValidationResultId, Metadata().ValidationResultId);

    [TestMethod]
    public void ValidationResultMetadataRepository_PreservesRepository() =>
        Assert.AreEqual(RepositoryName, Metadata().Repository);

    [TestMethod]
    public void ValidationResultMetadataRepository_PreservesBranch() =>
        Assert.AreEqual(Branch, Metadata().Branch);

    [TestMethod]
    public void ValidationResultMetadataRepository_PreservesRunId() =>
        Assert.AreEqual(RunId, Metadata().RunId);

    [TestMethod]
    public void ValidationResultMetadataRepository_PreservesPatchHash() =>
        Assert.AreEqual(PatchHash, Metadata().PatchHash);

    [TestMethod]
    public void ValidationResultMetadataRepository_PreservesOutcome() =>
        Assert.AreEqual(Outcome, Metadata().Outcome);

    [TestMethod]
    public void ValidationResultMetadataRepository_PreservesWhatRan() =>
        AssertContains(Metadata().WhatRan, WhatRan);

    [TestMethod]
    public void ValidationResultMetadataRepository_PreservesWhatPassed() =>
        AssertContains(Metadata().WhatPassed, WhatPassed);

    [TestMethod]
    public void ValidationResultMetadataRepository_PreservesWhatFailed() =>
        AssertContains(Metadata().WhatFailed, WhatFailed);

    [TestMethod]
    public void ValidationResultMetadataRepository_PreservesWhatWasSkipped() =>
        AssertContains(Metadata().WhatWasSkipped, WhatWasSkipped);

    [TestMethod]
    public void ValidationResultMetadataRepository_PreservesEvidenceRefs() =>
        AssertContains(Metadata().EvidenceRefs, EvidenceRef);

    [TestMethod]
    public void ValidationResultMetadataRepository_PreservesReceiptRefs() =>
        AssertContains(Metadata().ReceiptRefs, ReceiptRef);

    [TestMethod]
    public void ValidationResultMetadataRepository_PreservesStaleFlag()
    {
        var metadata = Repository(Record(isStale: true)).GetByValidationResultId(ValidationResultId, Scope()).Metadata!;

        Assert.IsTrue(metadata.IsStale);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_FreshnessUnknownMarksStale()
    {
        var metadata = Repository(Record(freshnessKnown: false)).GetByValidationResultId(ValidationResultId, Scope()).Metadata!;

        Assert.IsTrue(metadata.IsStale);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_FreshnessUnknownAddsSkippedReason()
    {
        var metadata = Repository(Record(freshnessKnown: false)).GetByValidationResultId(ValidationResultId, Scope()).Metadata!;

        AssertContains(metadata.WhatWasSkipped, "FreshnessUnknown");
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_ExpiredValidationMarksStale()
    {
        var metadata = Repository(Record(expiresAtUtc: DateTimeOffset.Parse("2020-01-01T00:00:00Z")))
            .GetByValidationResultId(ValidationResultId, Scope())
            .Metadata!;

        Assert.IsTrue(metadata.IsStale);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_StalePassedValidationRemainsStale()
    {
        var metadata = Repository(Record(outcome: "Passed", isStale: true)).GetByValidationResultId(ValidationResultId, Scope()).Metadata!;

        Assert.AreEqual("Passed", metadata.Outcome);
        Assert.IsTrue(metadata.IsStale);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_ForcesReadOnlyBoundary()
    {
        var result = Repository(Record()).GetByValidationResultId(ValidationResultId, Scope());

        AssertReadOnly(result.Boundary);
        AssertReadOnly(result.Metadata!.Boundary);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_DoesNotExposeRawValidationPayload()
    {
        var metadata = Metadata();

        AssertNoUnsafeMaterial(metadata.Repository);
        AssertNoUnsafeMaterial(metadata.Branch);
        AssertNoUnsafeMaterial(metadata.RunId);
        AssertNoUnsafeMaterial(metadata.PatchHash);
        AssertNoUnsafeMaterial(metadata.Outcome);
        Assert.IsFalse(metadata.WhatRan.Any(ContainsUnsafeMaterial));
        Assert.IsFalse(metadata.WhatPassed.Any(ContainsUnsafeMaterial));
        Assert.IsFalse(metadata.WhatFailed.Any(ContainsUnsafeMaterial));
        Assert.IsFalse(metadata.WhatWasSkipped.Any(ContainsUnsafeMaterial));
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_DoesNotReplaceMissingObservedAtWithUtcNow()
    {
        var result = Repository(Record(observedAtUtc: DateTimeOffset.MinValue)).GetByValidationResultId(ValidationResultId, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "ValidationResultMetadataObservedAtRequired");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_RawLogPayloadFailsClosedOrRedacts()
    {
        var result = Repository(Record(containsRawLogPayload: true)).GetByValidationResultId(ValidationResultId, Scope());

        AssertContains(result.Issues, "ValidationResultRawLogPayloadBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_RawCommandOutputFailsClosedOrRedacts()
    {
        var result = Repository(Record(containsRawCommandOutput: true)).GetByValidationResultId(ValidationResultId, Scope());

        AssertContains(result.Issues, "ValidationResultRawCommandOutputBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_RawTestOutputFailsClosedOrRedacts()
    {
        var result = Repository(Record(containsRawTestOutput: true)).GetByValidationResultId(ValidationResultId, Scope());

        AssertContains(result.Issues, "ValidationResultRawTestOutputBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_RawBuildOutputFailsClosedOrRedacts()
    {
        var result = Repository(Record(containsRawBuildOutput: true)).GetByValidationResultId(ValidationResultId, Scope());

        AssertContains(result.Issues, "ValidationResultRawBuildOutputBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_PatchPayloadFailsClosedOrRedacts()
    {
        var result = Repository(Record(containsPatchPayload: true)).GetByValidationResultId(ValidationResultId, Scope());

        AssertContains(result.Issues, "ValidationResultPatchPayloadBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_FullDiffFailsClosedOrRedacts()
    {
        var result = Repository(Record(containsFullDiff: true)).GetByValidationResultId(ValidationResultId, Scope());

        AssertContains(result.Issues, "ValidationResultFullDiffBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_PrivateMaterialFailsClosedOrRedacts()
    {
        var result = Repository(Record(repository: "safe repo", containsPrivateMaterial: true))
            .GetByValidationResultId(ValidationResultId, Scope());

        AssertContains(result.Issues, "ValidationResultPrivateMaterialBlocked");
        AssertRedacted(result.Metadata!);
        Assert.IsFalse(result.Metadata!.Repository.Contains("safe repo", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_HiddenMaterialFailsClosedOrRedacts()
    {
        var result = Repository(Record(containsHiddenMaterial: true)).GetByValidationResultId(ValidationResultId, Scope());

        AssertContains(result.Issues, "ValidationResultHiddenMaterialBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_SecretMaterialFailsClosedOrRedacts()
    {
        var result = Repository(Record(containsSecretMaterial: true)).GetByValidationResultId(ValidationResultId, Scope());

        AssertContains(result.Issues, "ValidationResultSecretMaterialBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_ApprovalClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsApproval: true)).GetByValidationResultId(ValidationResultId, Scope());

        AssertContains(result.Issues, "ValidationResultApprovalClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_PolicySatisfactionClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsPolicySatisfaction: true)).GetByValidationResultId(ValidationResultId, Scope());

        AssertContains(result.Issues, "ValidationResultPolicySatisfactionClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_SourceApplyAuthorityClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsSourceApplyAuthority: true)).GetByValidationResultId(ValidationResultId, Scope());

        AssertContains(result.Issues, "ValidationResultSourceApplyAuthorityClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_ExecutionClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsExecution: true)).GetByValidationResultId(ValidationResultId, Scope());

        AssertContains(result.Issues, "ValidationResultExecutionClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_CommitPushOrPrAuthorityClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsCommitAuthority: true, claimsPushAuthority: true, claimsPullRequestAuthority: true))
            .GetByValidationResultId(ValidationResultId, Scope());

        AssertContains(result.Issues, "ValidationResultDownstreamAuthorityClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_WorkflowContinuationClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsContinuation: true)).GetByValidationResultId(ValidationResultId, Scope());

        AssertContains(result.Issues, "ValidationResultContinuationClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_ReleaseOrDeploymentClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsReleaseOrDeploymentAuthority: true)).GetByValidationResultId(ValidationResultId, Scope());

        AssertContains(result.Issues, "ValidationResultReleaseDeploymentClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_InvalidStoredMetadataFailsClosed()
    {
        var result = Repository(Record(repository: " ")).GetByValidationResultId(ValidationResultId, Scope());

        AssertContains(result.Issues, "ValidationResultMetadataRepositoryRequired");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_AllowsMatchingTenant()
    {
        var result = Repository(Record(tenantId: 42)).GetByValidationResultId(ValidationResultId, Scope(42));

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Metadata);
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_RejectsWrongTenant()
    {
        var result = Repository(Record(tenantId: 42)).GetByValidationResultId(ValidationResultId, Scope(41));

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, "ValidationResultMetadataTenantMismatch");
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_RejectsTenantlessTenantScopedRecord()
    {
        var result = Repository(Record(tenantId: null)).GetByValidationResultId(ValidationResultId, Scope(42));

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, "TenantScopedValidationResultMetadataRecordTenantRequired");
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_RejectsUnscopedTenantRead()
    {
        var result = Repository(Record(tenantId: 42)).GetByValidationResultId(ValidationResultId, FrontendReadinessReadScope.Unscoped);

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, "TenantScopedValidationResultMetadataRequiresTenantScope");
    }

    [TestMethod]
    public void ValidationResultMetadataRepository_AllowsExplicitGlobalRecord()
    {
        var result = Repository(Record(tenantId: null, isTenantScoped: false))
            .GetByValidationResultId(ValidationResultId, FrontendReadinessReadScope.Unscoped);

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Metadata);
    }

    [TestMethod]
    public void FrontendReadiness_ValidationResultMetadataUsesRepositoryBeforeRunReport()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.Api", "Program.cs"));
        Assert.IsTrue(
            program.IndexOf("ValidationResultMetadataFrontendReadinessBackendTruthSource", StringComparison.Ordinal) <
            program.IndexOf("RunReportFrontendReadinessBackendTruthSource", StringComparison.Ordinal),
            "Validation result metadata source must be registered before run reports.");

        var api = Api(
            Repository(Record(repository: "canonical-validation-repo")),
            new SeededValidationResultBackendTruthSource(FallbackMetadata(repository: "run-report-validation-repo")));

        var model = api.GetValidationResultMetadata(ValidationResultId);

        Assert.IsNotNull(model);
        Assert.AreEqual("canonical-validation-repo", model.Repository);
    }

    [TestMethod]
    public void FrontendReadiness_ValidationResultMetadataDoesNotInventMetadataWhenRepositoryMisses()
    {
        var api = Api(Repository());

        Assert.IsNull(api.GetValidationResultMetadata(ValidationResultId));
    }

    [TestMethod]
    public void FrontendReadiness_ValidationResultMetadataKeepsReadOnlyBoundary() =>
        AssertReadOnly(Api().GetValidationResultMetadata(ValidationResultId)!.Boundary);

    [TestMethod]
    public void FrontendReadiness_ValidationResultMetadataEvidenceRefsRemainReferencesOnly()
    {
        var model = Api().GetValidationResultMetadata(ValidationResultId)!;

        AssertContains(model.EvidenceRefs, EvidenceRef);
        AssertReadOnly(model.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_ValidationResultMetadataReceiptRefsRemainReferencesOnly()
    {
        var model = Api().GetValidationResultMetadata(ValidationResultId)!;

        AssertContains(model.ReceiptRefs, ReceiptRef);
        AssertReadOnly(model.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_ValidationResultMetadataPrivateMaterialRedacted()
    {
        var api = Api(Repository(Record(repository: "private reasoning about validation", isTenantScoped: false)));

        var model = api.GetValidationResultMetadata(ValidationResultId)!;

        Assert.AreEqual("[redacted: private material]", model.Repository);
    }

    [TestMethod]
    public void FrontendReadiness_ValidationResultMetadataDoesNotApproveOrSatisfyPolicy()
    {
        var model = Api().GetValidationResultMetadata(ValidationResultId)!;

        Assert.IsFalse(model.Boundary.CanCreateApproval);
        Assert.IsFalse(model.Boundary.CanAcceptApproval);
        Assert.IsFalse(model.Boundary.CanSatisfyPolicy);
    }

    [TestMethod]
    public void FrontendReadiness_ValidationResultMetadataDoesNotAuthorizeSourceApply()
    {
        var model = Api().GetValidationResultMetadata(ValidationResultId)!;

        Assert.IsFalse(model.Boundary.CanExecute);
        Assert.IsFalse(model.Boundary.CanMutateSource);
    }

    [TestMethod]
    public void FrontendReadiness_ValidationResultMetadataDoesNotAuthorizeDownstreamMutation()
    {
        var model = Api().GetValidationResultMetadata(ValidationResultId)!;

        Assert.IsFalse(model.Boundary.CanCommit);
        Assert.IsFalse(model.Boundary.CanPush);
        Assert.IsFalse(model.Boundary.CanCreatePullRequest);
        Assert.IsFalse(model.Boundary.CanMerge);
        Assert.IsFalse(model.Boundary.CanRelease);
        Assert.IsFalse(model.Boundary.CanDeploy);
        Assert.IsFalse(model.Boundary.CanPromoteMemory);
        Assert.IsFalse(model.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void StaticScan_A07AddsNoMutationEndpoint()
    {
        var source = A07Source();

        foreach (var marker in new[]
                 {
                     "[HttpPost]",
                     "[HttpPut]",
                     "[HttpPatch]",
                     "[HttpDelete]",
                     "CreateApproval",
                     "AcceptApproval",
                     "SatisfyPolicy",
                     "GrantsAuthority = true",
                     "ContinuesWorkflow = true",
                     "CanCreateApproval = true",
                     "CanSatisfyPolicy = true",
                     "CanExecute = true",
                     "CanMutateSource = true",
                     "CanCommit = true",
                     "CanPush = true",
                     "CanCreatePullRequest = true",
                     "CanMerge = true",
                     "CanRelease = true",
                     "CanDeploy = true",
                     "CanPromoteMemory = true",
                     "CanContinueWorkflow = true"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), marker);
        }
    }

    [TestMethod]
    public void StaticScan_A07AddsNoExecutorOrProviderMutationPath()
    {
        var source = A07Source();

        foreach (var marker in new[]
                 {
                     "SourceApplyExecutor",
                     "RollbackExecutor",
                     "CommitExecutor",
                     "PushExecutor",
                     "DraftPullRequestExecutor",
                     "MergeExecutor",
                     "ReleaseExecutor",
                     "DeploymentExecutor",
                     "MemoryPromotionExecutor",
                     "WorkflowContinuation",
                     "ContinueWorkflow",
                     "ApplyPatch",
                     "ApplySource",
                     "ProcessStartInfo",
                     "RunProcessAsync",
                     "git apply",
                     "git commit",
                     "git push",
                     "gh pr create"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), marker);
        }
    }

    [TestMethod]
    public void StaticScan_A07DoesNotReadRawValidationPayloads()
    {
        var source = A07Source();

        foreach (var marker in new[]
                 {
                     "ReadValidationLogAsync",
                     "ReadValidationOutputAsync",
                     "ReadCommandOutputAsync",
                     "ReadTestOutputAsync",
                     "ReadBuildOutputAsync",
                     "ReadPatchPayloadAsync",
                     "ReadPatchTextAsync",
                     "ReadDiffTextAsync",
                     "ReadReceiptTextAsync",
                     "ReadEvidenceTextAsync",
                     "PatchDiffText",
                     "NormalizedDiff",
                     "raw validation log",
                     "full command output",
                     "raw test output",
                     "raw build output",
                     "raw patch",
                     "full diff",
                     "diff --git",
                     "rawPrompt",
                     "rawCompletion",
                     "rawToolOutput"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), marker);
        }
    }

    [TestMethod]
    public void StaticScan_A07DoesNotExposePrivateMaterial()
    {
        var source = A07Source();

        foreach (var marker in new[]
                 {
                     "chainOfThought",
                     "chain of thought",
                     "private reasoning",
                     "scratchpad",
                     "bearer token",
                     "api key",
                     "password",
                     "private key"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), marker);
        }
    }

    private static FrontendValidationResultMetadataReadModel Metadata() =>
        Repository(Record()).GetByValidationResultId(ValidationResultId, Scope()).Metadata!;

    private static IValidationResultMetadataReadRepository Repository(params ValidationResultMetadataReadRecord[] records) =>
        new ValidationResultMetadataReadRepository(records);

    private static IFrontendReadinessReadApi Api(
        IValidationResultMetadataReadRepository? repository = null,
        IFrontendReadinessBackendTruthSource? fallback = null)
    {
        var sources = new List<IFrontendReadinessBackendTruthSource>
        {
            new ValidationResultMetadataFrontendReadinessBackendTruthSource(repository ?? Repository(Record()))
        };

        if (fallback is not null)
            sources.Add(fallback);

        return new BackendFrontendReadinessReadApi(sources, new TestTenantContext(42));
    }

    private static ValidationResultMetadataReadRecord Record(
        string validationResultId = ValidationResultId,
        string repository = RepositoryName,
        string branch = Branch,
        string runId = RunId,
        string patchHash = PatchHash,
        string outcome = Outcome,
        int? tenantId = 42,
        bool isTenantScoped = true,
        DateTimeOffset? observedAtUtc = null,
        DateTimeOffset? expiresAtUtc = null,
        bool freshnessKnown = true,
        bool isStale = false,
        IReadOnlyCollection<string>? whatRan = null,
        IReadOnlyCollection<string>? whatPassed = null,
        IReadOnlyCollection<string>? whatFailed = null,
        IReadOnlyCollection<string>? whatWasSkipped = null,
        IReadOnlyCollection<string>? evidenceRefs = null,
        IReadOnlyCollection<string>? receiptRefs = null,
        bool containsRawLogPayload = false,
        bool containsRawCommandOutput = false,
        bool containsRawTestOutput = false,
        bool containsRawBuildOutput = false,
        bool containsPatchPayload = false,
        bool containsFullDiff = false,
        bool containsPrivateMaterial = false,
        bool containsHiddenMaterial = false,
        bool containsSecretMaterial = false,
        bool claimsApproval = false,
        bool claimsPolicySatisfaction = false,
        bool claimsSourceApplyAuthority = false,
        bool claimsExecution = false,
        bool claimsCommitAuthority = false,
        bool claimsPushAuthority = false,
        bool claimsPullRequestAuthority = false,
        bool claimsContinuation = false,
        bool claimsReleaseOrDeploymentAuthority = false,
        IReadOnlyCollection<string>? warnings = null,
        IReadOnlyCollection<string>? authorityWarnings = null) =>
        new()
        {
            ValidationResultId = validationResultId,
            Repository = repository,
            Branch = branch,
            RunId = runId,
            PatchHash = patchHash,
            Outcome = outcome,
            TenantId = tenantId,
            IsTenantScoped = isTenantScoped,
            ObservedAtUtc = observedAtUtc ?? ObservedAtUtc,
            ExpiresAtUtc = expiresAtUtc ?? ObservedAtUtc.AddDays(1),
            FreshnessKnown = freshnessKnown,
            IsStale = isStale,
            WhatRan = whatRan ?? [WhatRan],
            WhatPassed = whatPassed ?? [WhatPassed],
            WhatFailed = whatFailed ?? [WhatFailed],
            WhatWasSkipped = whatWasSkipped ?? [WhatWasSkipped],
            EvidenceRefs = evidenceRefs ?? [EvidenceRef],
            ReceiptRefs = receiptRefs ?? [ReceiptRef],
            ContainsRawLogPayload = containsRawLogPayload,
            ContainsRawCommandOutput = containsRawCommandOutput,
            ContainsRawTestOutput = containsRawTestOutput,
            ContainsRawBuildOutput = containsRawBuildOutput,
            ContainsPatchPayload = containsPatchPayload,
            ContainsFullDiff = containsFullDiff,
            ContainsPrivateMaterial = containsPrivateMaterial,
            ContainsHiddenMaterial = containsHiddenMaterial,
            ContainsSecretMaterial = containsSecretMaterial,
            ClaimsApproval = claimsApproval,
            ClaimsPolicySatisfaction = claimsPolicySatisfaction,
            ClaimsSourceApplyAuthority = claimsSourceApplyAuthority,
            ClaimsExecution = claimsExecution,
            ClaimsCommitAuthority = claimsCommitAuthority,
            ClaimsPushAuthority = claimsPushAuthority,
            ClaimsPullRequestAuthority = claimsPullRequestAuthority,
            ClaimsContinuation = claimsContinuation,
            ClaimsReleaseOrDeploymentAuthority = claimsReleaseOrDeploymentAuthority,
            Warnings = warnings ?? [],
            AuthorityWarnings = authorityWarnings ?? []
        };

    private static FrontendValidationResultMetadataReadModel FallbackMetadata(string repository = "fallback-validation-repo") =>
        new()
        {
            ValidationResultId = ValidationResultId,
            Repository = repository,
            Branch = "fallback-branch",
            RunId = "fallback-run",
            PatchHash = "fallback-hash",
            Outcome = "Fallback",
            WhatRan = ["fallback lane"],
            WhatPassed = [],
            WhatFailed = [],
            WhatWasSkipped = ["fallback skip"],
            IsStale = true,
            EvidenceRefs = ["fallback-evidence:a07"],
            ReceiptRefs = ["fallback-receipt:a07"],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static void AssertRedacted(FrontendValidationResultMetadataReadModel metadata)
    {
        Assert.AreEqual("[redacted]", metadata.Repository);
        Assert.AreEqual("[redacted]", metadata.Branch);
        Assert.AreEqual("[redacted]", metadata.RunId);
        Assert.AreEqual("[redacted]", metadata.PatchHash);
        Assert.AreEqual("UnsafeValidationMetadata", metadata.Outcome);
        Assert.AreEqual(0, metadata.WhatRan.Count);
        Assert.AreEqual(0, metadata.WhatPassed.Count);
        Assert.AreEqual(0, metadata.WhatFailed.Count);
        AssertContains(metadata.WhatWasSkipped, "ValidationMetadataUnsafe");
        Assert.IsTrue(metadata.IsStale);
        Assert.AreEqual(0, metadata.EvidenceRefs.Count);
        Assert.AreEqual(0, metadata.ReceiptRefs.Count);
        AssertReadOnly(metadata.Boundary);
    }

    private static FrontendReadinessReadScope Scope(int tenantId = 42) => new(tenantId);

    private static void AssertReadOnly(FrontendReadBoundary boundary)
    {
        Assert.IsTrue(boundary.ReadOnly);
        Assert.IsTrue(boundary.StatusOnly);
        Assert.IsFalse(boundary.CanCreateApproval);
        Assert.IsFalse(boundary.CanAcceptApproval);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
        Assert.IsFalse(boundary.CanExecute);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanRollback);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanCreatePullRequest);
        Assert.IsFalse(boundary.CanMarkReadyForReview);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
    }

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(values.Contains(expected, StringComparer.OrdinalIgnoreCase), string.Join(Environment.NewLine, values));

    private static void AssertNoUnsafeMaterial(string value) =>
        Assert.IsFalse(ContainsUnsafeMaterial(value), value);

    private static bool ContainsUnsafeMaterial(string value) =>
        value.Contains("raw validation log", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("full command output", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("raw test output", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("raw build output", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("raw patch", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("full diff", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("private reasoning", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("scratchpad", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("bearer token", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("api key", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("private key", StringComparison.OrdinalIgnoreCase);

    private static string A07Source()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "ValidationResultMetadataReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "ValidationResultMetadataReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "ValidationResultMetadataFrontendReadinessBackendTruthSource.cs")));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed class SeededValidationResultBackendTruthSource(FrontendValidationResultMetadataReadModel metadata)
        : FrontendReadinessBackendTruthSource
    {
        public override string SourceName => "seeded-run-report";

        public override FrontendValidationResultMetadataReadModel? GetValidationResultMetadata(string validationResultId) =>
            string.Equals(validationResultId, metadata.ValidationResultId, StringComparison.OrdinalIgnoreCase)
                ? metadata
                : null;
    }

    private sealed class TestTenantContext(int tenantId) : ICurrentTenantContext
    {
        public int TenantId { get; } = tenantId;
    }
}
