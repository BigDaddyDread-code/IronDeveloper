using IronDev.Core.Auth;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockA06PatchPackageMetadataReadAdapterTests
{
    private const string PackageId = "patch-package:a06";
    private const string RepositoryName = "BigDaddyDread-code/IronDeveloper";
    private const string Branch = "frontend/patch-package-metadata-read-adapter";
    private const string RunId = "run-a06";
    private const string PatchHash = "sha256:a06";
    private const string ProposedFilePath = "IronDev.Core/Governance/PatchPackageMetadataReadRepository.cs";
    private const string ArtifactRef = "patch-package-artifact:a06";
    private const string EvidenceRef = "patch-package-evidence:a06";
    private const string ReceiptRef = "patch-package-receipt:a06";
    private const string ReviewSummaryRef = "review-summary:a06";
    private const string KnownRisksRef = "known-risks:a06";
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-23T04:00:00Z");

    [TestMethod]
    public void PatchPackageMetadataRepository_ReturnsMetadataByPackageId()
    {
        var result = Repository(Record()).GetByPackageId(PackageId, Scope());

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Metadata);
        Assert.AreEqual(PackageId, result.Metadata.PackageId);
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_ReturnsNotFoundForMissingPackageId()
    {
        var result = Repository().GetByPackageId("missing-package:a06", Scope());

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, "PatchPackageMetadataNotFound");
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_PreservesPackageId() =>
        Assert.AreEqual(PackageId, Metadata().PackageId);

    [TestMethod]
    public void PatchPackageMetadataRepository_PreservesRepository() =>
        Assert.AreEqual(RepositoryName, Metadata().Repository);

    [TestMethod]
    public void PatchPackageMetadataRepository_PreservesBranch() =>
        Assert.AreEqual(Branch, Metadata().Branch);

    [TestMethod]
    public void PatchPackageMetadataRepository_PreservesRunId() =>
        Assert.AreEqual(RunId, Metadata().RunId);

    [TestMethod]
    public void PatchPackageMetadataRepository_PreservesPatchHash() =>
        Assert.AreEqual(PatchHash, Metadata().PatchHash);

    [TestMethod]
    public void PatchPackageMetadataRepository_PreservesProposedFilePaths() =>
        AssertContains(Metadata().ProposedFilePaths, ProposedFilePath);

    [TestMethod]
    public void PatchPackageMetadataRepository_PreservesArtifactRefs() =>
        AssertContains(Metadata().ArtifactRefs, ArtifactRef);

    [TestMethod]
    public void PatchPackageMetadataRepository_PreservesEvidenceRefs() =>
        AssertContains(Metadata().EvidenceRefs, EvidenceRef);

    [TestMethod]
    public void PatchPackageMetadataRepository_PreservesReceiptRefs() =>
        AssertContains(Metadata().ReceiptRefs, ReceiptRef);

    [TestMethod]
    public void PatchPackageMetadataRepository_PreservesReviewSummaryRef() =>
        Assert.AreEqual(ReviewSummaryRef, Metadata().ReviewSummaryRef);

    [TestMethod]
    public void PatchPackageMetadataRepository_PreservesKnownRisksRef() =>
        Assert.AreEqual(KnownRisksRef, Metadata().KnownRisksRef);

    [TestMethod]
    public void PatchPackageMetadataRepository_ForcesReadOnlyBoundary()
    {
        var result = Repository(Record()).GetByPackageId(PackageId, Scope());

        AssertReadOnly(result.Boundary);
        AssertReadOnly(result.Metadata!.Boundary);
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_DoesNotExposeRawPatchPayload()
    {
        var metadata = Metadata();

        AssertNoUnsafeMaterial(metadata.Repository);
        AssertNoUnsafeMaterial(metadata.Branch);
        AssertNoUnsafeMaterial(metadata.RunId);
        AssertNoUnsafeMaterial(metadata.PatchHash);
        AssertNoUnsafeMaterial(metadata.ReviewSummaryRef);
        AssertNoUnsafeMaterial(metadata.KnownRisksRef);
        Assert.IsFalse(metadata.ProposedFilePaths.Any(ContainsUnsafeMaterial));
        Assert.IsFalse(metadata.ArtifactRefs.Any(ContainsUnsafeMaterial));
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_DoesNotReplaceMissingObservedAtWithUtcNow()
    {
        var result = Repository(Record(observedAtUtc: DateTimeOffset.MinValue)).GetByPackageId(PackageId, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "PatchPackageMetadataObservedAtRequired");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_RawPatchPayloadFailsClosedOrRedacts()
    {
        var result = Repository(Record(containsRawPatchPayload: true)).GetByPackageId(PackageId, Scope());

        AssertContains(result.Issues, "PatchPackageRawPayloadBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_FullDiffFailsClosedOrRedacts()
    {
        var result = Repository(Record(containsFullDiff: true)).GetByPackageId(PackageId, Scope());

        AssertContains(result.Issues, "PatchPackageFullDiffBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_PrivateMaterialFailsClosedOrRedacts()
    {
        var result = Repository(Record(repository: "safe repo", containsPrivateMaterial: true)).GetByPackageId(PackageId, Scope());

        AssertContains(result.Issues, "PatchPackagePrivateMaterialBlocked");
        AssertRedacted(result.Metadata!);
        Assert.IsFalse(result.Metadata!.Repository.Contains("safe repo", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_HiddenMaterialFailsClosedOrRedacts()
    {
        var result = Repository(Record(containsHiddenMaterial: true)).GetByPackageId(PackageId, Scope());

        AssertContains(result.Issues, "PatchPackageHiddenMaterialBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_SecretMaterialFailsClosedOrRedacts()
    {
        var result = Repository(Record(containsSecretMaterial: true)).GetByPackageId(PackageId, Scope());

        AssertContains(result.Issues, "PatchPackageSecretMaterialBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_SourceApplyAuthorityClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsSourceApplyAuthority: true)).GetByPackageId(PackageId, Scope());

        AssertContains(result.Issues, "PatchPackageSourceApplyAuthorityClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_ApprovalClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsApproval: true)).GetByPackageId(PackageId, Scope());

        AssertContains(result.Issues, "PatchPackageApprovalClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_PolicySatisfactionClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsPolicySatisfaction: true)).GetByPackageId(PackageId, Scope());

        AssertContains(result.Issues, "PatchPackagePolicySatisfactionClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_ExecutionClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsExecution: true)).GetByPackageId(PackageId, Scope());

        AssertContains(result.Issues, "PatchPackageExecutionClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_CommitPushOrPrAuthorityClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsCommitAuthority: true, claimsPushAuthority: true, claimsPullRequestAuthority: true))
            .GetByPackageId(PackageId, Scope());

        AssertContains(result.Issues, "PatchPackageDownstreamAuthorityClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_WorkflowContinuationClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsContinuation: true)).GetByPackageId(PackageId, Scope());

        AssertContains(result.Issues, "PatchPackageContinuationClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_ReleaseOrDeploymentClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsReleaseOrDeploymentAuthority: true)).GetByPackageId(PackageId, Scope());

        AssertContains(result.Issues, "PatchPackageReleaseDeploymentClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_InvalidStoredMetadataFailsClosed()
    {
        var result = Repository(Record(repository: " ")).GetByPackageId(PackageId, Scope());

        AssertContains(result.Issues, "PatchPackageMetadataRepositoryRequired");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_AllowsMatchingTenant()
    {
        var result = Repository(Record(tenantId: 42)).GetByPackageId(PackageId, Scope(42));

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Metadata);
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_RejectsWrongTenant()
    {
        var result = Repository(Record(tenantId: 42)).GetByPackageId(PackageId, Scope(41));

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, "PatchPackageMetadataTenantMismatch");
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_RejectsTenantlessTenantScopedRecord()
    {
        var result = Repository(Record(tenantId: null)).GetByPackageId(PackageId, Scope(42));

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, "TenantScopedPatchPackageMetadataRecordTenantRequired");
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_RejectsUnscopedTenantRead()
    {
        var result = Repository(Record(tenantId: 42)).GetByPackageId(PackageId, FrontendReadinessReadScope.Unscoped);

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, "TenantScopedPatchPackageMetadataRequiresTenantScope");
    }

    [TestMethod]
    public void PatchPackageMetadataRepository_AllowsExplicitGlobalRecord()
    {
        var result = Repository(Record(tenantId: null, isTenantScoped: false))
            .GetByPackageId(PackageId, FrontendReadinessReadScope.Unscoped);

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Metadata);
    }

    [TestMethod]
    public void FrontendReadiness_PatchPackageMetadataUsesRepositoryBeforeRunReport()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.Api", "Program.cs"));
        Assert.IsTrue(
            program.IndexOf("PatchPackageMetadataFrontendReadinessBackendTruthSource", StringComparison.Ordinal) <
            program.IndexOf("RunReportFrontendReadinessBackendTruthSource", StringComparison.Ordinal),
            "Patch package metadata source must be registered before run reports.");

        var api = Api(
            Repository(Record(repository: "canonical-package-repo")),
            new SeededPatchPackageBackendTruthSource(FallbackMetadata(repository: "run-report-package-repo")));

        var model = api.GetPatchPackageMetadata(PackageId);

        Assert.IsNotNull(model);
        Assert.AreEqual("canonical-package-repo", model.Repository);
    }

    [TestMethod]
    public void FrontendReadiness_PatchPackageMetadataDoesNotInventMetadataWhenRepositoryMisses()
    {
        var api = Api(Repository());

        Assert.IsNull(api.GetPatchPackageMetadata(PackageId));
    }

    [TestMethod]
    public void FrontendReadiness_PatchPackageMetadataKeepsReadOnlyBoundary() =>
        AssertReadOnly(Api().GetPatchPackageMetadata(PackageId)!.Boundary);

    [TestMethod]
    public void FrontendReadiness_PatchPackageMetadataEvidenceRefsRemainReferencesOnly()
    {
        var model = Api().GetPatchPackageMetadata(PackageId)!;

        AssertContains(model.EvidenceRefs, EvidenceRef);
        AssertReadOnly(model.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_PatchPackageMetadataReceiptRefsRemainReferencesOnly()
    {
        var model = Api().GetPatchPackageMetadata(PackageId)!;

        AssertContains(model.ReceiptRefs, ReceiptRef);
        AssertReadOnly(model.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_PatchPackageMetadataArtifactRefsRemainReferencesOnly()
    {
        var model = Api().GetPatchPackageMetadata(PackageId)!;

        AssertContains(model.ArtifactRefs, ArtifactRef);
        AssertReadOnly(model.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_PatchPackageMetadataPrivateMaterialRedacted()
    {
        var api = Api(Repository(Record(repository: "private reasoning about source", isTenantScoped: false)));

        var model = api.GetPatchPackageMetadata(PackageId)!;

        Assert.AreEqual("[redacted: private material]", model.Repository);
    }

    [TestMethod]
    public void FrontendReadiness_PatchPackageMetadataDoesNotApproveOrSatisfyPolicy()
    {
        var model = Api().GetPatchPackageMetadata(PackageId)!;

        Assert.IsFalse(model.Boundary.CanCreateApproval);
        Assert.IsFalse(model.Boundary.CanAcceptApproval);
        Assert.IsFalse(model.Boundary.CanSatisfyPolicy);
    }

    [TestMethod]
    public void FrontendReadiness_PatchPackageMetadataDoesNotAuthorizeSourceApply()
    {
        var model = Api().GetPatchPackageMetadata(PackageId)!;

        Assert.IsFalse(model.Boundary.CanExecute);
        Assert.IsFalse(model.Boundary.CanMutateSource);
    }

    [TestMethod]
    public void FrontendReadiness_PatchPackageMetadataDoesNotAuthorizeDownstreamMutation()
    {
        var model = Api().GetPatchPackageMetadata(PackageId)!;

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
    public void StaticScan_A06AddsNoMutationEndpoint()
    {
        var source = A06Source();

        foreach (var forbidden in new[]
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
            Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), forbidden);
        }
    }

    [TestMethod]
    public void StaticScan_A06AddsNoExecutorOrProviderMutationPath()
    {
        var source = A06Source();

        foreach (var forbidden in new[]
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
            Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), forbidden);
        }
    }

    [TestMethod]
    public void StaticScan_A06DoesNotReadRawPatchPayloads()
    {
        var source = A06Source();

        foreach (var forbidden in new[]
                 {
                     "ReadPatchPayloadAsync",
                     "ReadPatchTextAsync",
                     "ReadDiffTextAsync",
                     "ReadTimelinePayloadAsync",
                     "ReadEventPayloadAsync",
                     "ReadReceiptTextAsync",
                     "ReadEvidenceTextAsync",
                     "PatchDiffText",
                     "NormalizedDiff",
                     "rawPatch",
                     "raw patch",
                     "full diff",
                     "diff --git",
                     "rawPrompt",
                     "raw prompt",
                     "rawCompletion",
                     "raw completion",
                     "rawToolOutput",
                     "raw tool output"
                 })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), forbidden);
        }
    }

    [TestMethod]
    public void StaticScan_A06DoesNotExposePrivateMaterial()
    {
        var source = A06Source();

        foreach (var forbidden in new[]
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
            Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), forbidden);
        }
    }

    private static FrontendPatchPackageMetadataReadModel Metadata() =>
        Repository(Record()).GetByPackageId(PackageId, Scope()).Metadata!;

    private static IPatchPackageMetadataReadRepository Repository(params PatchPackageMetadataReadRecord[] records) =>
        new PatchPackageMetadataReadRepository(records);

    private static IFrontendReadinessReadApi Api(
        IPatchPackageMetadataReadRepository? repository = null,
        IFrontendReadinessBackendTruthSource? fallback = null)
    {
        var sources = new List<IFrontendReadinessBackendTruthSource>
        {
            new PatchPackageMetadataFrontendReadinessBackendTruthSource(repository ?? Repository(Record()))
        };

        if (fallback is not null)
            sources.Add(fallback);

        return new BackendFrontendReadinessReadApi(sources, new TestTenantContext(42));
    }

    private static PatchPackageMetadataReadRecord Record(
        string packageId = PackageId,
        string repository = RepositoryName,
        string branch = Branch,
        string runId = RunId,
        string patchHash = PatchHash,
        int? tenantId = 42,
        bool isTenantScoped = true,
        DateTimeOffset? observedAtUtc = null,
        IReadOnlyCollection<string>? proposedFilePaths = null,
        IReadOnlyCollection<string>? artifactRefs = null,
        IReadOnlyCollection<string>? evidenceRefs = null,
        IReadOnlyCollection<string>? receiptRefs = null,
        string reviewSummaryRef = ReviewSummaryRef,
        string knownRisksRef = KnownRisksRef,
        bool containsRawPatchPayload = false,
        bool containsFullDiff = false,
        bool containsPrivateMaterial = false,
        bool containsHiddenMaterial = false,
        bool containsSecretMaterial = false,
        bool claimsSourceApplyAuthority = false,
        bool claimsApproval = false,
        bool claimsPolicySatisfaction = false,
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
            PackageId = packageId,
            Repository = repository,
            Branch = branch,
            RunId = runId,
            PatchHash = patchHash,
            TenantId = tenantId,
            IsTenantScoped = isTenantScoped,
            ObservedAtUtc = observedAtUtc ?? ObservedAtUtc,
            ExpiresAtUtc = ObservedAtUtc.AddHours(1),
            ProposedFilePaths = proposedFilePaths ?? [ProposedFilePath],
            ArtifactRefs = artifactRefs ?? [ArtifactRef],
            EvidenceRefs = evidenceRefs ?? [EvidenceRef],
            ReceiptRefs = receiptRefs ?? [ReceiptRef],
            ReviewSummaryRef = reviewSummaryRef,
            KnownRisksRef = knownRisksRef,
            ContainsRawPatchPayload = containsRawPatchPayload,
            ContainsFullDiff = containsFullDiff,
            ContainsPrivateMaterial = containsPrivateMaterial,
            ContainsHiddenMaterial = containsHiddenMaterial,
            ContainsSecretMaterial = containsSecretMaterial,
            ClaimsSourceApplyAuthority = claimsSourceApplyAuthority,
            ClaimsApproval = claimsApproval,
            ClaimsPolicySatisfaction = claimsPolicySatisfaction,
            ClaimsExecution = claimsExecution,
            ClaimsCommitAuthority = claimsCommitAuthority,
            ClaimsPushAuthority = claimsPushAuthority,
            ClaimsPullRequestAuthority = claimsPullRequestAuthority,
            ClaimsContinuation = claimsContinuation,
            ClaimsReleaseOrDeploymentAuthority = claimsReleaseOrDeploymentAuthority,
            Warnings = warnings ?? [],
            AuthorityWarnings = authorityWarnings ?? []
        };

    private static FrontendPatchPackageMetadataReadModel FallbackMetadata(string repository = "fallback-package-repo") =>
        new()
        {
            PackageId = PackageId,
            Repository = repository,
            Branch = "fallback-branch",
            RunId = "fallback-run",
            PatchHash = "fallback-hash",
            ProposedFilePaths = ["fallback-file.cs"],
            ArtifactRefs = ["fallback-artifact:a06"],
            EvidenceRefs = ["fallback-evidence:a06"],
            ReceiptRefs = ["fallback-receipt:a06"],
            ReviewSummaryRef = "fallback-review-summary:a06",
            KnownRisksRef = "fallback-known-risks:a06",
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static void AssertRedacted(FrontendPatchPackageMetadataReadModel metadata)
    {
        Assert.AreEqual("[redacted]", metadata.Repository);
        Assert.AreEqual("[redacted]", metadata.Branch);
        Assert.AreEqual("[redacted]", metadata.RunId);
        Assert.AreEqual("[redacted]", metadata.PatchHash);
        Assert.AreEqual("[redacted]", metadata.ReviewSummaryRef);
        Assert.AreEqual("[redacted]", metadata.KnownRisksRef);
        Assert.AreEqual(0, metadata.ProposedFilePaths.Count);
        Assert.AreEqual(0, metadata.ArtifactRefs.Count);
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
        value.Contains("raw patch", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("full diff", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("private reasoning", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("scratchpad", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("bearer token", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("api key", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("private key", StringComparison.OrdinalIgnoreCase);

    private static string A06Source()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "PatchPackageMetadataReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "PatchPackageMetadataReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "PatchPackageMetadataFrontendReadinessBackendTruthSource.cs")));
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

    private sealed class SeededPatchPackageBackendTruthSource(FrontendPatchPackageMetadataReadModel metadata)
        : FrontendReadinessBackendTruthSource
    {
        public override string SourceName => "seeded-run-report";

        public override FrontendPatchPackageMetadataReadModel? GetPatchPackageMetadata(string packageId) =>
            string.Equals(packageId, metadata.PackageId, StringComparison.OrdinalIgnoreCase)
                ? metadata
                : null;
    }

    private sealed class TestTenantContext(int tenantId) : ICurrentTenantContext
    {
        public int TenantId { get; } = tenantId;
    }
}
