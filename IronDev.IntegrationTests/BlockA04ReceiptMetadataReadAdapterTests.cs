using IronDev.Core.Auth;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockA04ReceiptMetadataReadAdapterTests
{
    private const string ReceiptRef = "receipt-metadata:a04";
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-23T02:00:00Z");

    [TestMethod]
    public void ReceiptMetadataRepository_ReturnsMetadataByReceiptRef()
    {
        var result = Repository(Record()).GetByReceiptRef(ReceiptRef, Scope());

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Metadata);
        Assert.AreEqual(ReceiptRef, result.Metadata.ReceiptRef);
    }

    [TestMethod]
    public void ReceiptMetadataRepository_ReturnsNotFoundForMissingReceiptRef()
    {
        var result = Repository().GetByReceiptRef("missing-receipt:a04", Scope());

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, "ReceiptMetadataNotFound");
    }

    [TestMethod]
    public void ReceiptMetadataRepository_PreservesReceiptKind()
    {
        var result = Repository(Record(receiptKind: "SourceApplyReceipt")).GetByReceiptRef(ReceiptRef, Scope());

        Assert.AreEqual("SourceApplyReceipt", result.Metadata!.ReceiptKind);
    }

    [TestMethod]
    public void ReceiptMetadataRepository_PreservesSanitizedSummary()
    {
        var result = Repository(Record(summary: "Source apply receipt metadata reference.")).GetByReceiptRef(ReceiptRef, Scope());

        Assert.AreEqual("Source apply receipt metadata reference.", result.Metadata!.Summary);
    }

    [TestMethod]
    public void ReceiptMetadataRepository_ForcesReferenceOnly()
    {
        var result = Repository(Record()).GetByReceiptRef(ReceiptRef, Scope());

        Assert.IsTrue(result.Metadata!.ReferenceOnly);
        AssertReadOnly(result.Metadata.Boundary);
    }

    [TestMethod]
    public void ReceiptMetadataRepository_ForcesNoAuthority()
    {
        var result = Repository(Record()).GetByReceiptRef(ReceiptRef, Scope());

        Assert.IsFalse(result.Metadata!.GrantsAuthority);
    }

    [TestMethod]
    public void ReceiptMetadataRepository_ForcesNoWorkflowContinuation()
    {
        var result = Repository(Record()).GetByReceiptRef(ReceiptRef, Scope());

        Assert.IsFalse(result.Metadata!.ContinuesWorkflow);
    }

    [TestMethod]
    public void ReceiptMetadataRepository_AddsNonAuthorityWarnings()
    {
        var result = Repository(Record()).GetByReceiptRef(ReceiptRef, Scope());

        AssertContains(result.Metadata!.Warnings, "Receipt metadata is reference-only.");
        AssertContains(result.Metadata.Warnings, "Receipt ref is not authority.");
        AssertContains(result.Metadata.Warnings, "Receipt ref is not approval.");
        AssertContains(result.Metadata.Warnings, "Receipt ref is not policy satisfaction.");
        AssertContains(result.Metadata.Warnings, "Receipt ref does not authorize execution.");
        AssertContains(result.Metadata.Warnings, "Receipt ref does not continue workflow.");
    }

    [TestMethod]
    public void ReceiptMetadataRepository_RawPayloadFailsClosedOrRedacts()
    {
        var result = Repository(Record(containsRawPayload: true)).GetByReceiptRef(ReceiptRef, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "ReceiptMetadataRawPayloadBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ReceiptMetadataRepository_PrivateMaterialFailsClosedOrRedacts()
    {
        var result = Repository(Record(containsPrivateMaterial: true, summary: "safe summary")).GetByReceiptRef(ReceiptRef, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "ReceiptMetadataPrivateMaterialBlocked");
        AssertRedacted(result.Metadata!);
        Assert.IsFalse(result.Metadata!.Summary.Contains("safe summary", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ReceiptMetadataRepository_HiddenMaterialFailsClosedOrRedacts()
    {
        var result = Repository(Record(containsHiddenMaterial: true)).GetByReceiptRef(ReceiptRef, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "ReceiptMetadataHiddenMaterialBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ReceiptMetadataRepository_RawPatchOrFullDiffFailsClosedOrRedacts()
    {
        var result = Repository(Record(containsPatchPayload: true)).GetByReceiptRef(ReceiptRef, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "ReceiptMetadataPatchPayloadBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ReceiptMetadataRepository_AuthorityClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsAuthority: true)).GetByReceiptRef(ReceiptRef, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "ReceiptMetadataAuthorityClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ReceiptMetadataRepository_WorkflowContinuationClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsContinuation: true)).GetByReceiptRef(ReceiptRef, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "ReceiptMetadataContinuationClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ReceiptMetadataRepository_ApprovalClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsApproval: true)).GetByReceiptRef(ReceiptRef, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "ReceiptMetadataApprovalClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ReceiptMetadataRepository_PolicySatisfactionClaimFailsClosedOrRedacts()
    {
        var result = Repository(Record(claimsPolicySatisfaction: true)).GetByReceiptRef(ReceiptRef, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "ReceiptMetadataPolicySatisfactionClaimBlocked");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ReceiptMetadataRepository_InvalidStoredMetadataFailsClosed()
    {
        var result = Repository(Record(receiptKind: " ")).GetByReceiptRef(ReceiptRef, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "ReceiptMetadataKindRequired");
        AssertRedacted(result.Metadata!);
    }

    [TestMethod]
    public void ReceiptMetadataRepository_AllowsMatchingTenant()
    {
        var result = Repository(Record(tenantId: 42)).GetByReceiptRef(ReceiptRef, Scope(42));

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Metadata);
    }

    [TestMethod]
    public void ReceiptMetadataRepository_RejectsWrongTenant()
    {
        var result = Repository(Record(tenantId: 42)).GetByReceiptRef(ReceiptRef, Scope(41));

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, "ReceiptMetadataTenantMismatch");
    }

    [TestMethod]
    public void ReceiptMetadataRepository_RejectsTenantlessTenantScopedRecord()
    {
        var result = Repository(Record(tenantId: null)).GetByReceiptRef(ReceiptRef, Scope(42));

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, "TenantScopedReceiptMetadataRecordTenantRequired");
    }

    [TestMethod]
    public void ReceiptMetadataRepository_RejectsUnscopedTenantRead()
    {
        var result = Repository(Record(tenantId: 42)).GetByReceiptRef(ReceiptRef, FrontendReadinessReadScope.Unscoped);

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, "TenantScopedReceiptMetadataRequiresTenantScope");
    }

    [TestMethod]
    public void ReceiptMetadataRepository_AllowsExplicitGlobalRecord()
    {
        var result = Repository(Record(tenantId: null, isTenantScoped: false))
            .GetByReceiptRef(ReceiptRef, FrontendReadinessReadScope.Unscoped);

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Metadata);
    }

    [TestMethod]
    public void FrontendReadiness_ReceiptMetadataUsesRepositoryBeforeRunReport()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.Api", "Program.cs"));
        Assert.IsTrue(
            program.IndexOf("ReceiptMetadataFrontendReadinessBackendTruthSource", StringComparison.Ordinal) <
            program.IndexOf("RunReportFrontendReadinessBackendTruthSource", StringComparison.Ordinal),
            "Receipt metadata source must be registered before run reports.");

        var api = Api(
            Repository(Record(receiptKind: "CanonicalReceiptMetadata")),
            new SeededReceiptBackendTruthSource(Receipt(kind: "RunReportReceipt")));

        var model = api.GetReceiptMetadata(ReceiptRef);

        Assert.IsNotNull(model);
        Assert.AreEqual("CanonicalReceiptMetadata", model.ReceiptKind);
    }

    [TestMethod]
    public void FrontendReadiness_ReceiptMetadataDoesNotInventMetadataWhenRepositoryMisses()
    {
        var api = Api(Repository());

        Assert.IsNull(api.GetReceiptMetadata(ReceiptRef));
    }

    [TestMethod]
    public void FrontendReadiness_ReceiptMetadataKeepsReadOnlyBoundary()
    {
        var model = Api().GetReceiptMetadata(ReceiptRef)!;

        AssertReadOnly(model.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_ReceiptMetadataForcesReferenceOnly()
    {
        var model = Api().GetReceiptMetadata(ReceiptRef)!;

        Assert.IsTrue(model.ReferenceOnly);
        AssertContains(model.Warnings, "Receipt metadata is reference-only.");
    }

    [TestMethod]
    public void FrontendReadiness_ReceiptMetadataForcesNoAuthority()
    {
        var model = Api(Repository(Record(claimsAuthority: true))).GetReceiptMetadata(ReceiptRef);

        Assert.IsNotNull(model);
        Assert.IsFalse(model.GrantsAuthority);
        AssertRedacted(model);
    }

    [TestMethod]
    public void FrontendReadiness_ReceiptMetadataForcesNoWorkflowContinuation()
    {
        var model = Api(Repository(Record(claimsContinuation: true))).GetReceiptMetadata(ReceiptRef);

        Assert.IsNotNull(model);
        Assert.IsFalse(model.ContinuesWorkflow);
        AssertRedacted(model);
    }

    [TestMethod]
    public void FrontendReadiness_ReceiptMetadataPrivateMaterialRedacted()
    {
        var api = Api(Repository(Record(summary: "safe summary", warnings: ["private reasoning must stay hidden"])));

        var model = api.GetReceiptMetadata(ReceiptRef);

        Assert.IsNotNull(model);
        AssertContains(model.Warnings, "[redacted: private material]");
        Assert.AreEqual("safe summary", model.Summary);
    }

    [TestMethod]
    public void FrontendReadiness_ReceiptMetadataDoesNotApproveOrSatisfyPolicy()
    {
        var model = Api().GetReceiptMetadata(ReceiptRef)!;

        Assert.IsFalse(model.GrantsAuthority);
        AssertContains(model.Warnings, "Receipt ref is not approval.");
        AssertContains(model.Warnings, "Receipt ref is not policy satisfaction.");
    }

    [TestMethod]
    public void FrontendReadiness_ReceiptMetadataDoesNotAuthorizeDownstreamMutation()
    {
        var model = Api().GetReceiptMetadata(ReceiptRef)!;

        Assert.IsFalse(model.GrantsAuthority);
        Assert.IsFalse(model.ContinuesWorkflow);
        AssertContains(model.Warnings, "Receipt ref does not authorize execution.");
        AssertContains(model.Warnings, "Receipt ref does not continue workflow.");
        AssertReadOnly(model.Boundary);
    }

    [TestMethod]
    public void StaticScan_A04AddsNoMutationEndpoint()
    {
        var source = A04Source();

        foreach (var marker in new[]
                 {
                     "[HttpPost",
                     "[HttpPut",
                     "[HttpPatch",
                     "[HttpDelete",
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
                     "CanContinueWorkflow = true",
                     "GrantsAuthority = true",
                     "ContinuesWorkflow = true"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [TestMethod]
    public void StaticScan_A04AddsNoExecutorOrProviderMutationPath()
    {
        var source = A04Source();

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
                     "ContinueWorkflow",
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
    public void StaticScan_A04DoesNotReadRawReceiptPayloads()
    {
        var source = A04Source();

        foreach (var marker in new[]
                 {
                     "ReadReceiptTextAsync",
                     "ReadEvidenceTextAsync",
                     "File.ReadAllText",
                     "File.OpenRead",
                     "ReadToEnd",
                     "JsonDocument.Parse",
                     "PatchDiffText",
                     "NormalizedDiff",
                     "rawPrompt",
                     "rawCompletion",
                     "rawToolOutput",
                     "raw patch",
                     "full diff"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [TestMethod]
    public void StaticScan_A04DoesNotExposePrivateMaterial()
    {
        var source = A04Source();

        foreach (var marker in new[]
                 {
                     "chainOfThought",
                     "chain of thought",
                     "private reasoning",
                     "scratchpad",
                     "bearer token",
                     "api key",
                     "password",
                     "secret",
                     "private key",
                     "raw completion",
                     "raw tool output"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    private static IReceiptMetadataReadRepository Repository(params ReceiptMetadataReadRecord[] records) =>
        new ReceiptMetadataReadRepository(records);

    private static IFrontendReadinessReadApi Api(
        IReceiptMetadataReadRepository? repository = null,
        IFrontendReadinessBackendTruthSource? fallback = null)
    {
        var sources = new List<IFrontendReadinessBackendTruthSource>
        {
            new ReceiptMetadataFrontendReadinessBackendTruthSource(repository ?? Repository(Record()))
        };

        if (fallback is not null)
            sources.Add(fallback);

        return new BackendFrontendReadinessReadApi(sources, new TestTenantContext(42));
    }

    private static ReceiptMetadataReadRecord Record(
        string receiptRef = ReceiptRef,
        string receiptKind = "SourceApplyReceipt",
        string summary = "Source apply receipt metadata reference.",
        int? tenantId = 42,
        bool isTenantScoped = true,
        bool containsRawPayload = false,
        bool containsPrivateMaterial = false,
        bool containsPatchPayload = false,
        bool containsHiddenMaterial = false,
        bool claimsAuthority = false,
        bool claimsContinuation = false,
        bool claimsApproval = false,
        bool claimsPolicySatisfaction = false,
        IReadOnlyCollection<string>? warnings = null,
        IReadOnlyCollection<string>? authorityWarnings = null,
        DateTimeOffset? observedAtUtc = null) =>
        new()
        {
            ReceiptRef = receiptRef,
            ReceiptKind = receiptKind,
            Summary = summary,
            OperationId = "operation-a04",
            OperationKind = "ReceiptMetadataRead",
            Subject = "repo:BigDaddyDread-code/IronDeveloper branch:frontend/receipt-metadata-read-adapter",
            TenantId = tenantId,
            IsTenantScoped = isTenantScoped,
            ContainsRawPayload = containsRawPayload,
            ContainsPrivateMaterial = containsPrivateMaterial,
            ContainsPatchPayload = containsPatchPayload,
            ContainsHiddenMaterial = containsHiddenMaterial,
            ClaimsAuthority = claimsAuthority,
            ClaimsContinuation = claimsContinuation,
            ClaimsApproval = claimsApproval,
            ClaimsPolicySatisfaction = claimsPolicySatisfaction,
            Warnings = warnings ?? ["Receipt was produced by backend governance."],
            AuthorityWarnings = authorityWarnings ?? ["Receipt ref is not authority."],
            ObservedAtUtc = observedAtUtc ?? ObservedAtUtc
        };

    private static FrontendReceiptMetadataReadModel Receipt(
        string receiptRef = ReceiptRef,
        string kind = "FallbackReceipt",
        string summary = "Fallback receipt metadata.") =>
        new()
        {
            ReceiptRef = receiptRef,
            ReceiptKind = kind,
            Summary = summary,
            ReferenceOnly = true,
            GrantsAuthority = false,
            ContinuesWorkflow = false,
            Warnings = ["Fallback receipt is reference-only."],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static FrontendReadinessReadScope Scope(int tenantId = 42) => new(tenantId);

    private static void AssertRedacted(FrontendReceiptMetadataReadModel metadata)
    {
        Assert.AreEqual("RedactedReceiptMetadata", metadata.ReceiptKind);
        Assert.AreEqual("[redacted: receipt metadata unavailable]", metadata.Summary);
        Assert.IsTrue(metadata.ReferenceOnly);
        Assert.IsFalse(metadata.GrantsAuthority);
        Assert.IsFalse(metadata.ContinuesWorkflow);
        AssertReadOnly(metadata.Boundary);
    }

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

    private static string A04Source()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "ReceiptMetadataReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "ReceiptMetadataReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "ReceiptMetadataFrontendReadinessBackendTruthSource.cs")));
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

    private sealed class SeededReceiptBackendTruthSource(FrontendReceiptMetadataReadModel metadata)
        : FrontendReadinessBackendTruthSource
    {
        public override string SourceName => "seeded-run-report";

        public override FrontendReceiptMetadataReadModel? GetReceiptMetadata(string receiptRef) =>
            string.Equals(receiptRef, metadata.ReceiptRef, StringComparison.OrdinalIgnoreCase)
                ? metadata
                : null;
    }

    private sealed class TestTenantContext(int tenantId) : ICurrentTenantContext
    {
        public int TenantId { get; } = tenantId;
    }
}
