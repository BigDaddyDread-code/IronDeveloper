using IronDev.Core.Auth;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockA03EvidenceMetadataReadAdapterTests
{
    private const string EvidenceRef = "evidence-metadata:a03";
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-23T01:00:00Z");

    [TestMethod]
    public void EvidenceMetadataRepository_ReturnsMetadataByEvidenceRef()
    {
        var result = Repository(Record()).GetByEvidenceRef(EvidenceRef, Scope());

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Metadata);
        Assert.AreEqual(EvidenceRef, result.Metadata.EvidenceRef);
    }

    [TestMethod]
    public void EvidenceMetadataRepository_ReturnsNotFoundForMissingEvidenceRef()
    {
        var result = Repository().GetByEvidenceRef("missing-evidence:a03", Scope());

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, "EvidenceMetadataNotFound");
    }

    [TestMethod]
    public void EvidenceMetadataRepository_PreservesEvidenceKind()
    {
        var result = Repository(Record(evidenceKind: "PatchProposalEvidence")).GetByEvidenceRef(EvidenceRef, Scope());

        Assert.AreEqual("PatchProposalEvidence", result.Metadata!.EvidenceKind);
    }

    [TestMethod]
    public void EvidenceMetadataRepository_PreservesSafeSummary()
    {
        var result = Repository(Record(summary: "Patch proposal package metadata reference.")).GetByEvidenceRef(EvidenceRef, Scope());

        Assert.AreEqual("Patch proposal package metadata reference.", result.Metadata!.Summary);
    }

    [TestMethod]
    public void EvidenceMetadataRepository_ForcesReferenceOnlyMetadata()
    {
        var result = Repository(Record()).GetByEvidenceRef(EvidenceRef, Scope());

        Assert.IsTrue(result.Metadata!.ReferenceOnly);
        Assert.IsFalse(result.Metadata.ContainsRawPayload);
        AssertReadOnly(result.Metadata.Boundary);
    }

    [TestMethod]
    public void EvidenceMetadataRepository_AddsNonAuthorityWarnings()
    {
        var result = Repository(Record()).GetByEvidenceRef(EvidenceRef, Scope());

        AssertContains(result.Metadata!.Warnings, "Evidence metadata is reference-only.");
        AssertContains(result.Metadata.Warnings, "Evidence ref is not approval.");
        AssertContains(result.Metadata.Warnings, "Evidence ref is not authority.");
        AssertContains(result.Metadata.Warnings, "Evidence ref is not policy satisfaction.");
        AssertContains(result.Metadata.Warnings, "Evidence ref does not authorize execution or workflow continuation.");
    }

    [TestMethod]
    public void EvidenceMetadataRepository_RedactsRawPayloadMetadata()
    {
        var result = Repository(Record(containsRawPayload: true)).GetByEvidenceRef(EvidenceRef, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "EvidenceMetadataRawPayloadBlocked");
        Assert.AreEqual("RedactedEvidenceMetadata", result.Metadata!.EvidenceKind);
        Assert.AreEqual("[redacted: evidence metadata unavailable]", result.Metadata.Summary);
        Assert.IsFalse(result.Metadata.ContainsRawPayload);
    }

    [TestMethod]
    public void EvidenceMetadataRepository_RedactsPrivateMaterialMetadata()
    {
        var result = Repository(Record(containsPrivateMaterial: true)).GetByEvidenceRef(EvidenceRef, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "EvidenceMetadataPrivateMaterialBlocked");
        Assert.AreEqual("RedactedEvidenceMetadata", result.Metadata!.EvidenceKind);
    }

    [TestMethod]
    public void EvidenceMetadataRepository_RedactsHiddenMaterialMetadata()
    {
        var result = Repository(Record(containsHiddenMaterial: true)).GetByEvidenceRef(EvidenceRef, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "EvidenceMetadataHiddenMaterialBlocked");
        Assert.AreEqual("RedactedEvidenceMetadata", result.Metadata!.EvidenceKind);
    }

    [TestMethod]
    public void EvidenceMetadataRepository_RedactsPatchPayloadMetadata()
    {
        var result = Repository(Record(containsPatchPayload: true)).GetByEvidenceRef(EvidenceRef, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "EvidenceMetadataPatchPayloadBlocked");
        Assert.AreEqual("RedactedEvidenceMetadata", result.Metadata!.EvidenceKind);
    }

    [TestMethod]
    public void EvidenceMetadataRepository_RedactsAuthorityClaimMetadata()
    {
        var result = Repository(Record(summary: "This evidence has authority to execute.")).GetByEvidenceRef(EvidenceRef, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "EvidenceMetadataAuthorityClaimBlocked");
        Assert.AreEqual("RedactedEvidenceMetadata", result.Metadata!.EvidenceKind);
        AssertContains(result.Metadata.Warnings, "Evidence ref is not authority.");
    }

    [TestMethod]
    public void EvidenceMetadataRepository_InvalidStoredMetadataFailsClosed()
    {
        var result = Repository(Record(evidenceKind: " ")).GetByEvidenceRef(EvidenceRef, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "EvidenceMetadataKindRequired");
        Assert.AreEqual("RedactedEvidenceMetadata", result.Metadata!.EvidenceKind);
    }

    [TestMethod]
    public void EvidenceMetadataRepository_AllowsMatchingTenant()
    {
        var result = Repository(Record(tenantId: 42)).GetByEvidenceRef(EvidenceRef, Scope(42));

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Metadata);
    }

    [TestMethod]
    public void EvidenceMetadataRepository_RejectsWrongTenant()
    {
        var result = Repository(Record(tenantId: 42)).GetByEvidenceRef(EvidenceRef, Scope(41));

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, "EvidenceMetadataTenantMismatch");
    }

    [TestMethod]
    public void EvidenceMetadataRepository_RejectsTenantlessTenantScopedRecord()
    {
        var result = Repository(Record(tenantId: null)).GetByEvidenceRef(EvidenceRef, Scope(42));

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, "TenantScopedEvidenceMetadataRecordTenantRequired");
    }

    [TestMethod]
    public void EvidenceMetadataRepository_RejectsUnscopedTenantRead()
    {
        var result = Repository(Record(tenantId: 42)).GetByEvidenceRef(EvidenceRef, FrontendReadinessReadScope.Unscoped);

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, "TenantScopedEvidenceMetadataRequiresTenantScope");
    }

    [TestMethod]
    public void EvidenceMetadataRepository_AllowsGlobalMetadataWhenExplicitlyUnscoped()
    {
        var result = Repository(Record(tenantId: null, isTenantScoped: false))
            .GetByEvidenceRef(EvidenceRef, FrontendReadinessReadScope.Unscoped);

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Metadata);
    }

    [TestMethod]
    public void FrontendReadiness_EvidenceMetadataUsesRepositoryBeforeRunReport()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.Api", "Program.cs"));
        Assert.IsTrue(
            program.IndexOf("EvidenceMetadataFrontendReadinessBackendTruthSource", StringComparison.Ordinal) <
            program.IndexOf("RunReportFrontendReadinessBackendTruthSource", StringComparison.Ordinal),
            "Evidence metadata source must be registered before run reports.");

        var api = Api(
            Repository(Record(evidenceKind: "CanonicalEvidenceMetadata")),
            new SeededEvidenceBackendTruthSource(Evidence(kind: "RunReportEvidence")));

        var model = api.GetEvidenceMetadata(EvidenceRef);

        Assert.IsNotNull(model);
        Assert.AreEqual("CanonicalEvidenceMetadata", model.EvidenceKind);
    }

    [TestMethod]
    public void FrontendReadiness_EvidenceMetadataDoesNotInventMetadataWhenRepositoryMisses()
    {
        var api = Api(Repository());

        Assert.IsNull(api.GetEvidenceMetadata(EvidenceRef));
    }

    [TestMethod]
    public void FrontendReadiness_EvidenceMetadataKeepsReadOnlyBoundary()
    {
        var model = Api().GetEvidenceMetadata(EvidenceRef)!;

        AssertReadOnly(model.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_EvidenceMetadataForcesReferenceOnlyOutput()
    {
        var model = Api().GetEvidenceMetadata(EvidenceRef)!;

        Assert.IsTrue(model.ReferenceOnly);
        Assert.IsFalse(model.ContainsRawPayload);
        AssertContains(model.Warnings, "Evidence metadata is reference-only.");
    }

    [TestMethod]
    public void FrontendReadiness_EvidenceMetadataRedactsUnsafeMetadata()
    {
        var model = Api(Repository(Record(containsRawPayload: true))).GetEvidenceMetadata(EvidenceRef);

        Assert.IsNotNull(model);
        Assert.AreEqual("RedactedEvidenceMetadata", model.EvidenceKind);
        Assert.AreEqual("[redacted: evidence metadata unavailable]", model.Summary);
        Assert.IsFalse(model.ContainsRawPayload);
    }

    [TestMethod]
    public void FrontendReadiness_EvidenceMetadataDoesNotGrantAuthority()
    {
        var model = Api().GetEvidenceMetadata(EvidenceRef)!;

        AssertContains(model.Warnings, "Evidence ref is not approval.");
        AssertContains(model.Warnings, "Evidence ref is not policy satisfaction.");
        AssertContains(model.Warnings, "Evidence ref does not authorize execution or workflow continuation.");
        AssertReadOnly(model.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_EvidenceMetadataSanitizesPrivateSummaryText()
    {
        var api = Api(Repository(Record(summary: "safe summary", warnings: ["private reasoning must stay hidden"])));

        var model = api.GetEvidenceMetadata(EvidenceRef);

        Assert.IsNotNull(model);
        AssertContains(model.Warnings, "[redacted: private material]");
        Assert.AreEqual("safe summary", model.Summary);
    }

    [TestMethod]
    public void StaticScan_A03AddsNoMutationEndpoint()
    {
        var source = A03Source();

        foreach (var marker in new[]
                 {
                     "[HttpPost",
                     "[HttpPut",
                     "[HttpPatch",
                     "[HttpDelete",
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
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [TestMethod]
    public void StaticScan_A03AddsNoExecutorOrProviderMutationPath()
    {
        var source = A03Source();

        foreach (var marker in new[]
                 {
                     "CreateApproval",
                     "AcceptApproval",
                     "SatisfyPolicy",
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
                     "ProcessStartInfo",
                     "RunProcessAsync",
                     "git commit",
                     "git push",
                     "gh pr create"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [TestMethod]
    public void StaticScan_A03AddsNoRawEvidencePayloadReader()
    {
        var source = A03Source();

        foreach (var marker in new[]
                 {
                     "File.ReadAllText",
                     "File.OpenRead",
                     "ReadToEnd",
                     "JsonDocument.Parse",
                     "PatchDiffText",
                     "ReviewSummaryText",
                     "KnownRisksText",
                     "ValidationSummaryText",
                     "rawPrompt",
                     "rawCompletion",
                     "rawToolOutput",
                     "full diff"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [TestMethod]
    public void StaticScan_A03DoesNotExposePrivateMaterial()
    {
        var source = A03Source();

        foreach (var marker in new[]
                 {
                     "chain-of-thought",
                     "scratchpad",
                     "bearer token",
                     "api key",
                     "private key",
                     "raw patch payload"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    private static IEvidenceMetadataReadRepository Repository(params EvidenceMetadataReadRecord[] records) =>
        new EvidenceMetadataReadRepository(records);

    private static IFrontendReadinessReadApi Api(
        IEvidenceMetadataReadRepository? repository = null,
        IFrontendReadinessBackendTruthSource? fallback = null)
    {
        var sources = new List<IFrontendReadinessBackendTruthSource>
        {
            new EvidenceMetadataFrontendReadinessBackendTruthSource(repository ?? Repository(Record()))
        };

        if (fallback is not null)
            sources.Add(fallback);

        return new BackendFrontendReadinessReadApi(sources, new TestTenantContext(42));
    }

    private static EvidenceMetadataReadRecord Record(
        string evidenceRef = EvidenceRef,
        string evidenceKind = "PatchPackageEvidence",
        string summary = "Patch package evidence metadata reference.",
        int? tenantId = 42,
        bool isTenantScoped = true,
        bool containsRawPayload = false,
        bool containsPrivateMaterial = false,
        bool containsPatchPayload = false,
        bool containsHiddenMaterial = false,
        IReadOnlyCollection<string>? warnings = null,
        IReadOnlyCollection<string>? authorityWarnings = null,
        DateTimeOffset? observedAtUtc = null) =>
        new()
        {
            EvidenceRef = evidenceRef,
            EvidenceKind = evidenceKind,
            Summary = summary,
            TenantId = tenantId,
            IsTenantScoped = isTenantScoped,
            ContainsRawPayload = containsRawPayload,
            ContainsPrivateMaterial = containsPrivateMaterial,
            ContainsPatchPayload = containsPatchPayload,
            ContainsHiddenMaterial = containsHiddenMaterial,
            Warnings = warnings ?? ["Evidence was produced by backend governance."],
            AuthorityWarnings = authorityWarnings ?? ["Evidence ref is not approval."],
            ObservedAtUtc = observedAtUtc ?? ObservedAtUtc
        };

    private static FrontendEvidenceMetadataReadModel Evidence(
        string evidenceRef = EvidenceRef,
        string kind = "FallbackEvidence",
        string summary = "Fallback evidence metadata.") =>
        new()
        {
            EvidenceRef = evidenceRef,
            EvidenceKind = kind,
            Summary = summary,
            ReferenceOnly = true,
            ContainsRawPayload = false,
            Warnings = ["Fallback evidence is reference-only."],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

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

    private static string A03Source()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "EvidenceMetadataReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "EvidenceMetadataReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "EvidenceMetadataFrontendReadinessBackendTruthSource.cs")));
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

    private sealed class SeededEvidenceBackendTruthSource(FrontendEvidenceMetadataReadModel metadata)
        : FrontendReadinessBackendTruthSource
    {
        public override string SourceName => "seeded-run-report";

        public override FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(string evidenceRef) =>
            string.Equals(evidenceRef, metadata.EvidenceRef, StringComparison.OrdinalIgnoreCase)
                ? metadata
                : null;
    }

    private sealed class TestTenantContext(int tenantId) : ICurrentTenantContext
    {
        public int TenantId { get; } = tenantId;
    }
}
