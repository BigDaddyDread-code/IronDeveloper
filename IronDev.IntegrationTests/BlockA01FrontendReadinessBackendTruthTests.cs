using IronDev.Api.Controllers;
using IronDev.Core.Auth;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using IronDev.Infrastructure.Services.RunReports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockA01FrontendReadinessBackendTruthTests
{
    private const string OperationId = "operation-a01";
    private const string PackageId = "patch-package-a01";
    private const string ValidationResultId = "validation-result-a01";
    private const string EvidenceRef = "evidence:a01";
    private const string ReceiptRef = "receipt:a01";
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-22T00:00:00Z");

    [TestMethod]
    public void FrontendReadiness_UsesBackendReadApiNotEmptySnapshot()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.Api", "Program.cs"));

        StringAssert.Contains(program, "BackendFrontendReadinessReadApi");
        StringAssert.Contains(program, "RunReportFrontendReadinessBackendTruthSource");
        Assert.IsFalse(program.Contains("AddSingleton<IFrontendReadinessReadApi>(FrontendReadinessReadApi.Empty)", StringComparison.Ordinal));
    }

    [TestMethod]
    public void FrontendReadiness_StatusReadsRealBackendStatus()
    {
        var model = Api().GetOperationStatus(OperationId);

        Assert.IsNotNull(model);
        Assert.AreEqual(OperationId, model.OperationId);
        Assert.AreEqual("SourceApply", model.OperationKind);
        Assert.AreEqual("Blocked", model.State);
    }

    [TestMethod]
    public void FrontendReadiness_StatusPreservesBlockedReasons()
    {
        var model = Api().GetOperationStatus(OperationId)!;

        AssertContains(model.BlockedReasons, "MissingExplicitSourceApplyAuthority");
    }

    [TestMethod]
    public void FrontendReadiness_StatusPreservesMissingEvidence()
    {
        var model = Api().GetOperationStatus(OperationId)!;

        AssertContains(model.MissingEvidence, "accepted-source-apply-request:source-apply-a01");
    }

    [TestMethod]
    public void FrontendReadiness_StatusPreservesForbiddenActions()
    {
        var model = Api().GetOperationStatus(OperationId)!;

        AssertContains(model.ForbiddenActions, "do not apply source without explicit source-apply authority");
        AssertContains(model.ForbiddenActions, "do not continue workflow from status, receipt, memory, or UI text");
    }

    [TestMethod]
    public void FrontendReadiness_StatusAddsReadOnlyBoundary()
    {
        var model = Api().GetOperationStatus(OperationId)!;

        AssertReadOnly(model.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_CompactModeCannotHideForbiddenActions()
    {
        var envelope = OkEnvelope(Controller().GetOperationStatus(OperationId, compact: true));

        AssertContains(envelope.Warnings, "Compact mode was requested but authority-critical fields are still returned.");
        AssertContains(envelope.Data!.ForbiddenActions, "do not apply source without explicit source-apply authority");
    }

    [TestMethod]
    public void FrontendReadiness_CompactModeCannotHideMissingEvidence()
    {
        var envelope = OkEnvelope(Controller().GetOperationStatus(OperationId, compact: true));

        AssertContains(envelope.Data!.MissingEvidence, "accepted-source-apply-request:source-apply-a01");
    }

    [TestMethod]
    public void FrontendReadiness_EvidenceMetadataIsReferenceOnly()
    {
        var model = Api().GetEvidenceMetadata(EvidenceRef)!;

        Assert.IsTrue(model.ReferenceOnly);
        Assert.IsFalse(model.ContainsRawPayload);
        Assert.AreEqual("[redacted: private material]", model.Summary);
        AssertReadOnly(model.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_ReceiptMetadataDoesNotGrantAuthority()
    {
        var model = Api().GetReceiptMetadata(ReceiptRef)!;

        Assert.IsTrue(model.ReferenceOnly);
        Assert.IsFalse(model.GrantsAuthority);
        Assert.IsFalse(model.ContinuesWorkflow);
        AssertReadOnly(model.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_TimelineDoesNotContinueWorkflow()
    {
        var model = Api().GetOperationTimeline(OperationId)!;

        AssertReadOnly(model.Boundary);
        Assert.IsFalse(model.Boundary.CanContinueWorkflow);
        AssertContains(model.Entries.Single().EvidenceRefs, EvidenceRef);
    }

    [TestMethod]
    public void FrontendReadiness_NotFoundDoesNotInventStatus()
    {
        var api = new BackendFrontendReadinessReadApi([], new TestTenantContext(1));
        var result = new FrontendReadinessController(api).GetOperationStatus("missing-operation");

        Assert.IsNull(api.GetOperationStatus("missing-operation"));
        var notFound = result.Result as NotFoundObjectResult;
        Assert.IsNotNull(notFound);
        var envelope = (FrontendReadinessApiEnvelope<FrontendOperationStatusReadModel>)notFound.Value!;
        Assert.AreEqual("not_found", envelope.Status);
        Assert.IsNull(envelope.Data);
        AssertReadOnly(envelope.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_AuthorizationRequired()
    {
        var attributes = typeof(FrontendReadinessController).GetCustomAttributes(inherit: false);

        Assert.IsTrue(attributes.Any(attribute => attribute is AuthorizeAttribute));
    }

    [TestMethod]
    public void FrontendReadiness_TenantScopeIsEnforcedWhereBackendSourceHasTenant()
    {
        var scopedSource = Source();
        scopedSource.Tenant = 42;
        var wrongTenant = new BackendFrontendReadinessReadApi([scopedSource], new TestTenantContext(41));
        var matchingTenant = new BackendFrontendReadinessReadApi([scopedSource], new TestTenantContext(42));

        Assert.IsNull(wrongTenant.GetOperationStatus(OperationId));
        Assert.IsNotNull(matchingTenant.GetOperationStatus(OperationId));
    }

    [TestMethod]
    public void FrontendReadiness_RunReportSourceRequiresRecordTenantMatch()
    {
        using var fixture = RunReportFixture.Create(tenantId: 42, includeFreshnessEvidence: false);
        var wrongTenant = RunReportApi(fixture.Root, 41);
        var matchingTenant = RunReportApi(fixture.Root, 42);

        Assert.IsNull(wrongTenant.GetOperationStatus(fixture.RunId));
        Assert.IsNotNull(matchingTenant.GetOperationStatus(fixture.RunId));
    }

    [TestMethod]
    public void FrontendReadiness_RunReportSourceBlocksTenantlessRecords()
    {
        using var fixture = RunReportFixture.Create(tenantId: null, includeFreshnessEvidence: false);
        var api = RunReportApi(fixture.Root, 42);

        Assert.IsNull(api.GetOperationStatus(fixture.RunId));
    }

    [TestMethod]
    public void FrontendReadiness_RunReportValidationFreshnessUnknownIsStale()
    {
        using var fixture = RunReportFixture.Create(tenantId: 42, includeFreshnessEvidence: false);
        var metadata = RunReportApi(fixture.Root, 42).GetValidationResultMetadata($"run-validation:{fixture.RunId}");

        Assert.IsNotNull(metadata);
        Assert.IsTrue(metadata.IsStale);
        AssertContains(metadata.WhatWasSkipped, "FreshnessUnknown");
    }

    [TestMethod]
    public void FrontendReadiness_RunReportValidationFreshnessEvidenceCanMarkNotStale()
    {
        using var fixture = RunReportFixture.Create(tenantId: 42, includeFreshnessEvidence: true);
        var metadata = RunReportApi(fixture.Root, 42).GetValidationResultMetadata($"run-validation:{fixture.RunId}");

        Assert.IsNotNull(metadata);
        Assert.IsFalse(metadata.IsStale);
        Assert.IsFalse(metadata.WhatWasSkipped.Contains("FreshnessUnknown", StringComparer.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void StaticScan_FrontendReadinessAddsNoMutationEndpoints()
    {
        var root = FindRepositoryRoot();
        var backendSource = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "BackendFrontendReadinessReadApi.cs"));
        var coreSource = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "FrontendReadinessReadModels.cs"));
        var program = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Program.cs"));
        var controller = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "FrontendReadinessController.cs"));
        var source = string.Join(Environment.NewLine, backendSource, coreSource, program);

        foreach (var marker in new[]
                 {
                     "[HttpPost",
                     "[HttpPut",
                     "[HttpPatch",
                     "[HttpDelete",
                     "RunProcessAsync",
                     "ProcessStartInfo",
                     "ControlledSourceApplyExecutor",
                     "ControlledRollbackExecutor",
                     "ControlledCommitExecutor",
                     "ControlledPushExecutor",
                     "ControlledDraftPullRequestExecutor",
                     "MergeExecutor",
                     "ReleaseExecutor",
                     "DeploymentExecutor",
                     "MemoryPromotionExecutor",
                     "WorkflowContinuationExecutor",
                     "CanCreateApproval = true",
                     "CanAcceptApproval = true",
                     "CanSatisfyPolicy = true",
                     "CanExecute = true",
                     "CanMutateSource = true",
                     "CanContinueWorkflow = true"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }

        StringAssert.Contains(controller, "[HttpPost(\"action-requests\")]");
        Assert.IsFalse(controller.Contains("[HttpPut", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(controller.Contains("[HttpPatch", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(controller.Contains("[HttpDelete", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void StaticScan_FrontendReadinessDoesNotExposePrivateMaterial()
    {
        var root = FindRepositoryRoot();
        var backendSource = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "BackendFrontendReadinessReadApi.cs"));
        var model = Api().GetEvidenceMetadata(EvidenceRef)!;

        Assert.IsFalse(backendSource.Contains("ReadEvidenceTextAsync", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(backendSource.Contains("NormalizedDiff", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(backendSource.Contains("PatchDiffText", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(model.Summary.Contains("rawPrompt", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(model.Summary.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    private static IFrontendReadinessReadApi Api(SeededBackendTruthSource? source = null, int tenantId = 1) =>
        new BackendFrontendReadinessReadApi([source ?? Source()], new TestTenantContext(tenantId));

    private static FrontendReadinessController Controller() =>
        new(Api());

    private static IFrontendReadinessReadApi RunReportApi(string root, int tenantId) =>
        new BackendFrontendReadinessReadApi(
            [new RunReportFrontendReadinessBackendTruthSource(new FileRunReportService(root), new InMemoryRunEventStore())],
            new TestTenantContext(tenantId));

    private static SeededBackendTruthSource Source() =>
        new()
        {
            Statuses = new Dictionary<string, GovernedOperationStatus>(StringComparer.OrdinalIgnoreCase)
            {
                [OperationId] = Status()
            },
            Timelines = new Dictionary<string, FrontendOperationTimelineReadModel>(StringComparer.OrdinalIgnoreCase)
            {
                [OperationId] = new()
                {
                    OperationId = OperationId,
                    Entries =
                    [
                        new FrontendTimelineEntry
                        {
                            EntryId = "timeline-a01",
                            EventKind = "BackendStatusObserved",
                            Summary = "Backend status was observed.",
                            EvidenceRefs = [EvidenceRef],
                            ReceiptRefs = [ReceiptRef],
                            ObservedAtUtc = ObservedAtUtc
                        }
                    ],
                    Boundary = FrontendReadBoundary.ReadOnlyStatus
                }
            },
            PatchPackages = new Dictionary<string, FrontendPatchPackageMetadataReadModel>(StringComparer.OrdinalIgnoreCase)
            {
                [PackageId] = PatchPackage()
            },
            ValidationResults = new Dictionary<string, FrontendValidationResultMetadataReadModel>(StringComparer.OrdinalIgnoreCase)
            {
                [ValidationResultId] = ValidationResult()
            },
            Evidence = new Dictionary<string, FrontendEvidenceMetadataReadModel>(StringComparer.OrdinalIgnoreCase)
            {
                [EvidenceRef] = new()
                {
                    EvidenceRef = EvidenceRef,
                    EvidenceKind = "PatchPackage",
                    Summary = "rawPrompt secret should not render",
                    ContainsRawPayload = true,
                    Warnings = ["Evidence ref is not approval."],
                    Boundary = FrontendReadBoundary.ReadOnlyStatus
                }
            },
            Receipts = new Dictionary<string, FrontendReceiptMetadataReadModel>(StringComparer.OrdinalIgnoreCase)
            {
                [ReceiptRef] = new()
                {
                    ReceiptRef = ReceiptRef,
                    ReceiptKind = "PatchPackageReceipt",
                    Summary = "Receipt metadata only.",
                    ReferenceOnly = false,
                    GrantsAuthority = true,
                    ContinuesWorkflow = true,
                    Warnings = ["Receipt ref is not authority."],
                    Boundary = FrontendReadBoundary.ReadOnlyStatus
                }
            }
        };

    private static GovernedOperationStatus Status() =>
        new()
        {
            OperationId = OperationId,
            OperationKind = "SourceApply",
            Subject = "repo:BigDaddyDread-code/IronDeveloper branch:frontend/readiness-real-backend-truth run:run-a01 patch:sha256:a01 scope:IronDev.Core/Governance/Example.cs",
            State = GovernedOperationState.Blocked,
            BlockedReasons = ["MissingExplicitSourceApplyAuthority"],
            MissingEvidence = ["accepted-source-apply-request:source-apply-a01"],
            NextSafeActions = ["request governed source-apply authority"],
            ForbiddenActions = ["do not apply source without explicit source-apply authority"],
            EvidenceRefs = [EvidenceRef, "patch-package:patch-package-a01", "validation-result:validation-result-a01"],
            ReceiptRefs = [ReceiptRef],
            ObservedAtUtc = ObservedAtUtc,
            ExpiresAtUtc = ObservedAtUtc.AddHours(1)
        };

    private static FrontendPatchPackageMetadataReadModel PatchPackage() =>
        new()
        {
            PackageId = PackageId,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "frontend/readiness-real-backend-truth",
            RunId = "run-a01",
            PatchHash = "sha256:a01",
            ProposedFilePaths = ["IronDev.Core/Governance/FrontendReadinessReadModels.cs"],
            ArtifactRefs = ["patch-artifact:a01"],
            EvidenceRefs = [EvidenceRef],
            ReceiptRefs = [ReceiptRef],
            ReviewSummaryRef = "review-summary:a01",
            KnownRisksRef = "known-risks:a01",
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static FrontendValidationResultMetadataReadModel ValidationResult() =>
        new()
        {
            ValidationResultId = ValidationResultId,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "frontend/readiness-real-backend-truth",
            RunId = "run-a01",
            PatchHash = "sha256:a01",
            Outcome = "Blocked",
            WhatRan = ["Focused A01"],
            WhatPassed = [],
            WhatFailed = [],
            WhatWasSkipped = ["Build"],
            IsStale = false,
            EvidenceRefs = [EvidenceRef],
            ReceiptRefs = [ReceiptRef],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static FrontendReadinessApiEnvelope<T> OkEnvelope<T>(ActionResult<FrontendReadinessApiEnvelope<T>> result)
    {
        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        return (FrontendReadinessApiEnvelope<T>)ok.Value!;
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

    private sealed class SeededBackendTruthSource : FrontendReadinessBackendTruthSource
    {
        public override string SourceName => "seeded-a01";
        public int? Tenant { get; set; }
        public override int? TenantId => Tenant;
        public IReadOnlyDictionary<string, GovernedOperationStatus> Statuses { get; init; } =
            new Dictionary<string, GovernedOperationStatus>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, FrontendOperationTimelineReadModel> Timelines { get; init; } =
            new Dictionary<string, FrontendOperationTimelineReadModel>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, FrontendPatchPackageMetadataReadModel> PatchPackages { get; init; } =
            new Dictionary<string, FrontendPatchPackageMetadataReadModel>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, FrontendValidationResultMetadataReadModel> ValidationResults { get; init; } =
            new Dictionary<string, FrontendValidationResultMetadataReadModel>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, FrontendEvidenceMetadataReadModel> Evidence { get; init; } =
            new Dictionary<string, FrontendEvidenceMetadataReadModel>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, FrontendReceiptMetadataReadModel> Receipts { get; init; } =
            new Dictionary<string, FrontendReceiptMetadataReadModel>(StringComparer.OrdinalIgnoreCase);

        public override GovernedOperationStatus? GetOperationStatus(string operationId) =>
            Statuses.GetValueOrDefault(operationId);

        public override FrontendOperationTimelineReadModel? GetOperationTimeline(string operationId) =>
            Timelines.GetValueOrDefault(operationId);

        public override FrontendPatchPackageMetadataReadModel? GetPatchPackageMetadata(string packageId) =>
            PatchPackages.GetValueOrDefault(packageId);

        public override FrontendValidationResultMetadataReadModel? GetValidationResultMetadata(string validationResultId) =>
            ValidationResults.GetValueOrDefault(validationResultId);

        public override FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(string evidenceRef) =>
            Evidence.GetValueOrDefault(evidenceRef);

        public override FrontendReceiptMetadataReadModel? GetReceiptMetadata(string receiptRef) =>
            Receipts.GetValueOrDefault(receiptRef);
    }

    private sealed class TestTenantContext(int tenantId) : ICurrentTenantContext
    {
        public int TenantId { get; } = tenantId;
    }

    private sealed class RunReportFixture : IDisposable
    {
        public required string Root { get; init; }
        public required string RunId { get; init; }

        public static RunReportFixture Create(int? tenantId, bool includeFreshnessEvidence)
        {
            var root = Path.Combine(Path.GetTempPath(), $"IronDev-A01-{Guid.NewGuid():N}");
            var runId = $"run-a01-{Guid.NewGuid():N}";
            var runRoot = Path.Combine(root, runId);
            Directory.CreateDirectory(runRoot);

            if (includeFreshnessEvidence)
            {
                var evidenceRoot = Path.Combine(runRoot, "evidence");
                Directory.CreateDirectory(evidenceRoot);
                File.WriteAllText(Path.Combine(evidenceRoot, "repo-freshness.txt"), "repo freshness observed");
            }

            var tenantLine = tenantId is null ? "" : $"""
                  "TenantId": "{tenantId}",
            """;
            File.WriteAllText(Path.Combine(runRoot, "report.json"), $$"""
                {
                  {{tenantLine}}
                  "Project": "IronDev",
                  "Title": "A01 backend truth run",
                  "Status": "Completed",
                  "Summary": "A01 backend truth run completed.",
                  "Build": {
                    "Status": "Passed",
                    "Command": "dotnet build IronDev.slnx"
                  }
                }
                """);

            return new RunReportFixture
            {
                Root = root,
                RunId = runId
            };
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
