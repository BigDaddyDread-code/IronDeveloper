using System.Reflection;
using System.Text.Json;
using IronDev.Api.Controllers;
using IronDev.Core.Auth;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockA12FrontendReadinessAuthorizationTenantScopeProofTests
{
    private const int TenantId = 42;
    private const int WrongTenantId = 41;
    private const string OperationId = "operation-a12";
    private const string PackageId = "patch-package:a12";
    private const string ValidationResultId = "validation-result:a12";
    private const string EvidenceRef = "evidence:a12";
    private const string ReceiptRef = "receipt:a12";
    private const string HiddenRepository = "foreign-hidden-repo-a12";
    private const string HiddenBranch = "foreign-hidden-branch-a12";
    private const string HiddenRunId = "foreign-hidden-run-a12";
    private const string HiddenPatchHash = "sha256:foreign-hidden-a12";
    private const string HiddenEvidenceRef = "foreign-evidence:a12";
    private const string HiddenReceiptRef = "foreign-receipt:a12";
    private const string HiddenArtifactRef = "foreign-artifact:a12";
    private const string HiddenSummary = "foreign hidden tenant summary a12";
    private const string FallbackSummary = "visible fallback should not win a12";
    private static readonly DateTimeOffset HiddenObservedAtUtc = DateTimeOffset.Parse("2031-01-01T00:00:00Z");
    private static readonly DateTimeOffset HiddenExpiresAtUtc = HiddenObservedAtUtc.AddHours(1);
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-23T07:00:00Z");

    private static readonly string[] ReadEndpointNames =
    [
        nameof(FrontendReadinessController.GetOperationStatus),
        nameof(FrontendReadinessController.GetOperationTimeline),
        nameof(FrontendReadinessController.GetPatchPackageMetadata),
        nameof(FrontendReadinessController.GetPatchPackageArtifacts),
        nameof(FrontendReadinessController.GetValidationResultMetadata),
        nameof(FrontendReadinessController.GetEvidenceMetadata),
        nameof(FrontendReadinessController.GetReceiptMetadata)
    ];

    [TestMethod]
    public void Auth_FrontendReadiness_ControllerRequiresAuthorize()
    {
        Assert.IsTrue(
            typeof(FrontendReadinessController).GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any(),
            "Frontend readiness controller must require authorization.");
    }

    [TestMethod]
    public void Auth_FrontendReadiness_ReadEndpointsRequireAuthorize()
    {
        foreach (var method in ReadEndpointMethods())
            AssertEndpointRequiresAuthorize(method);
    }

    [TestMethod]
    public void Auth_FrontendReadiness_ReadEndpointsDoNotAllowAnonymous()
    {
        foreach (var method in ReadEndpointMethods())
            AssertNoAllowAnonymous(method);
    }

    [TestMethod]
    public void Auth_FrontendReadiness_ActionRequestEndpointIsOutOfScopeForA12()
    {
        var method = ControllerMethod(nameof(FrontendReadinessController.CreateActionRequest));

        Assert.IsNotNull(method.GetCustomAttribute<HttpPostAttribute>());
        Assert.IsFalse(ReadEndpointNames.Contains(method.Name, StringComparer.Ordinal));
    }

    [TestMethod]
    public void Auth_FrontendReadiness_UnauthenticatedOperationStatusIsRejected() =>
        AssertEndpointRequiresAuthorize(ControllerMethod(nameof(FrontendReadinessController.GetOperationStatus)));

    [TestMethod]
    public void Auth_FrontendReadiness_UnauthenticatedTimelineIsRejected() =>
        AssertEndpointRequiresAuthorize(ControllerMethod(nameof(FrontendReadinessController.GetOperationTimeline)));

    [TestMethod]
    public void Auth_FrontendReadiness_UnauthenticatedPatchMetadataIsRejected() =>
        AssertEndpointRequiresAuthorize(ControllerMethod(nameof(FrontendReadinessController.GetPatchPackageMetadata)));

    [TestMethod]
    public void Auth_FrontendReadiness_UnauthenticatedPatchArtifactsIsRejected() =>
        AssertEndpointRequiresAuthorize(ControllerMethod(nameof(FrontendReadinessController.GetPatchPackageArtifacts)));

    [TestMethod]
    public void Auth_FrontendReadiness_UnauthenticatedValidationMetadataIsRejected() =>
        AssertEndpointRequiresAuthorize(ControllerMethod(nameof(FrontendReadinessController.GetValidationResultMetadata)));

    [TestMethod]
    public void Auth_FrontendReadiness_UnauthenticatedEvidenceMetadataIsRejected() =>
        AssertEndpointRequiresAuthorize(ControllerMethod(nameof(FrontendReadinessController.GetEvidenceMetadata)));

    [TestMethod]
    public void Auth_FrontendReadiness_UnauthenticatedReceiptMetadataIsRejected() =>
        AssertEndpointRequiresAuthorize(ControllerMethod(nameof(FrontendReadinessController.GetReceiptMetadata)));

    [TestMethod]
    public void TenantScope_OperationStatus_AllowsMatchingTenant() =>
        AssertMatchingTenantCanRead(ReadSurface.OperationStatus);

    [TestMethod]
    public void TenantScope_OperationStatus_RejectsWrongTenant() =>
        AssertWrongTenantCannotRead(ReadSurface.OperationStatus);

    [TestMethod]
    public void TenantScope_OperationStatus_RejectsMissingTenant() =>
        AssertMissingTenantCannotRead(ReadSurface.OperationStatus);

    [TestMethod]
    public void TenantScope_OperationStatus_DoesNotFallbackOnWrongTenant() =>
        AssertWrongTenantDoesNotFallback(ReadSurface.OperationStatus);

    [TestMethod]
    public void TenantScope_OperationStatus_DoesNotLeakHiddenSubject() =>
        AssertNotVisibleDoesNotLeak(ReadSurface.OperationStatus);

    [TestMethod]
    public void TenantScope_OperationStatus_GlobalRecordRequiresExplicitNonTenantScoped() =>
        AssertExplicitGlobalCanReadAndTenantlessScopedCannot(ReadSurface.OperationStatus);

    [TestMethod]
    public void TenantScope_EvidenceMetadata_AllowsMatchingTenant() =>
        AssertMatchingTenantCanRead(ReadSurface.EvidenceMetadata);

    [TestMethod]
    public void TenantScope_EvidenceMetadata_RejectsWrongTenant() =>
        AssertWrongTenantCannotRead(ReadSurface.EvidenceMetadata);

    [TestMethod]
    public void TenantScope_EvidenceMetadata_RejectsMissingTenant() =>
        AssertMissingTenantCannotRead(ReadSurface.EvidenceMetadata);

    [TestMethod]
    public void TenantScope_EvidenceMetadata_DoesNotFallbackOnWrongTenant() =>
        AssertWrongTenantDoesNotFallback(ReadSurface.EvidenceMetadata);

    [TestMethod]
    public void TenantScope_EvidenceMetadata_DoesNotLeakHiddenSummary() =>
        AssertNotVisibleDoesNotLeak(ReadSurface.EvidenceMetadata);

    [TestMethod]
    public void TenantScope_EvidenceMetadata_GlobalRecordRequiresExplicitNonTenantScoped() =>
        AssertExplicitGlobalCanReadAndTenantlessScopedCannot(ReadSurface.EvidenceMetadata);

    [TestMethod]
    public void TenantScope_ReceiptMetadata_AllowsMatchingTenant() =>
        AssertMatchingTenantCanRead(ReadSurface.ReceiptMetadata);

    [TestMethod]
    public void TenantScope_ReceiptMetadata_RejectsWrongTenant() =>
        AssertWrongTenantCannotRead(ReadSurface.ReceiptMetadata);

    [TestMethod]
    public void TenantScope_ReceiptMetadata_RejectsMissingTenant() =>
        AssertMissingTenantCannotRead(ReadSurface.ReceiptMetadata);

    [TestMethod]
    public void TenantScope_ReceiptMetadata_DoesNotFallbackOnWrongTenant() =>
        AssertWrongTenantDoesNotFallback(ReadSurface.ReceiptMetadata);

    [TestMethod]
    public void TenantScope_ReceiptMetadata_DoesNotLeakHiddenSummary() =>
        AssertNotVisibleDoesNotLeak(ReadSurface.ReceiptMetadata);

    [TestMethod]
    public void TenantScope_ReceiptMetadata_GlobalRecordRequiresExplicitNonTenantScoped() =>
        AssertExplicitGlobalCanReadAndTenantlessScopedCannot(ReadSurface.ReceiptMetadata);

    [TestMethod]
    public void TenantScope_Timeline_AllowsMatchingTenant() =>
        AssertMatchingTenantCanRead(ReadSurface.Timeline);

    [TestMethod]
    public void TenantScope_Timeline_RejectsWrongTenant() =>
        AssertWrongTenantCannotRead(ReadSurface.Timeline);

    [TestMethod]
    public void TenantScope_Timeline_RejectsMissingTenant() =>
        AssertMissingTenantCannotRead(ReadSurface.Timeline);

    [TestMethod]
    public void TenantScope_Timeline_DoesNotFallbackOnWrongTenant() =>
        AssertWrongTenantDoesNotFallback(ReadSurface.Timeline);

    [TestMethod]
    public void TenantScope_Timeline_DoesNotLeakHiddenEntrySummary() =>
        AssertNotVisibleDoesNotLeak(ReadSurface.Timeline);

    [TestMethod]
    public void TenantScope_Timeline_GlobalRecordRequiresExplicitNonTenantScoped() =>
        AssertExplicitGlobalCanReadAndTenantlessScopedCannot(ReadSurface.Timeline);

    [TestMethod]
    public void TenantScope_PatchPackageMetadata_AllowsMatchingTenant() =>
        AssertMatchingTenantCanRead(ReadSurface.PatchPackageMetadata);

    [TestMethod]
    public void TenantScope_PatchPackageMetadata_RejectsWrongTenant() =>
        AssertWrongTenantCannotRead(ReadSurface.PatchPackageMetadata);

    [TestMethod]
    public void TenantScope_PatchPackageMetadata_RejectsMissingTenant() =>
        AssertMissingTenantCannotRead(ReadSurface.PatchPackageMetadata);

    [TestMethod]
    public void TenantScope_PatchPackageMetadata_DoesNotFallbackOnWrongTenant() =>
        AssertWrongTenantDoesNotFallback(ReadSurface.PatchPackageMetadata);

    [TestMethod]
    public void TenantScope_PatchPackageMetadata_DoesNotLeakRepositoryBranchRunOrHash() =>
        AssertNotVisibleDoesNotLeak(ReadSurface.PatchPackageMetadata);

    [TestMethod]
    public void TenantScope_PatchPackageMetadata_GlobalRecordRequiresExplicitNonTenantScoped() =>
        AssertExplicitGlobalCanReadAndTenantlessScopedCannot(ReadSurface.PatchPackageMetadata);

    [TestMethod]
    public void TenantScope_ValidationMetadata_AllowsMatchingTenant() =>
        AssertMatchingTenantCanRead(ReadSurface.ValidationMetadata);

    [TestMethod]
    public void TenantScope_ValidationMetadata_RejectsWrongTenant() =>
        AssertWrongTenantCannotRead(ReadSurface.ValidationMetadata);

    [TestMethod]
    public void TenantScope_ValidationMetadata_RejectsMissingTenant() =>
        AssertMissingTenantCannotRead(ReadSurface.ValidationMetadata);

    [TestMethod]
    public void TenantScope_ValidationMetadata_DoesNotFallbackOnWrongTenant() =>
        AssertWrongTenantDoesNotFallback(ReadSurface.ValidationMetadata);

    [TestMethod]
    public void TenantScope_ValidationMetadata_DoesNotLeakValidationOutcomeOrRefs() =>
        AssertNotVisibleDoesNotLeak(ReadSurface.ValidationMetadata);

    [TestMethod]
    public void TenantScope_ValidationMetadata_GlobalRecordRequiresExplicitNonTenantScoped() =>
        AssertExplicitGlobalCanReadAndTenantlessScopedCannot(ReadSurface.ValidationMetadata);

    [TestMethod]
    public void TenantScope_PatchArtifacts_AllowsMatchingTenant_IfTenantScoped() =>
        AssertMatchingTenantCanRead(ReadSurface.PatchArtifacts);

    [TestMethod]
    public void TenantScope_PatchArtifacts_RejectsWrongTenant_IfTenantScoped() =>
        AssertWrongTenantCannotRead(ReadSurface.PatchArtifacts);

    [TestMethod]
    public void TenantScope_PatchArtifacts_DoesNotFallbackOnWrongTenant_IfTenantScoped() =>
        AssertWrongTenantDoesNotFallback(ReadSurface.PatchArtifacts);

    [TestMethod]
    public void TenantScope_NotVisibleEnvelopePreservesReadState()
    {
        var outcome = WrongTenantOutcome(ReadSurface.EvidenceMetadata);
        Assert.AreEqual(FrontendReadinessReadStateKind.NotVisible, outcome.ReadState.Kind);
    }

    [TestMethod]
    public void TenantScope_NotVisibleEnvelopePreservesFreshness()
    {
        var outcome = WrongTenantOutcome(ReadSurface.EvidenceMetadata);
        Assert.IsNotNull(outcome.Freshness);
        Assert.IsTrue(
            outcome.Freshness.Kind is FrontendReadinessFreshnessKind.Unknown or FrontendReadinessFreshnessKind.NotApplicable,
            outcome.Freshness.Kind.ToString());
    }

    [TestMethod]
    public void TenantScope_NotVisibleEnvelopePreservesBoundary()
    {
        var outcome = WrongTenantOutcome(ReadSurface.EvidenceMetadata);
        AssertReadOnly(outcome.Boundary);
    }

    [TestMethod]
    public void TenantScope_NotVisibleEnvelopePreservesWarnings()
    {
        var outcome = WrongTenantOutcome(ReadSurface.EvidenceMetadata);
        AssertContains(outcome.Warnings, "Frontend readiness read endpoints are read-only.");
    }

    [TestMethod]
    public void TenantScope_NotVisibleEnvelopePreservesErrors()
    {
        var outcome = WrongTenantOutcome(ReadSurface.EvidenceMetadata);
        Assert.IsTrue(outcome.Errors.Count > 0);
        Assert.AreEqual("FRONTEND_READINESS_NOTVISIBLE", outcome.Errors[0].Code);
    }

    [TestMethod]
    public void TenantScope_NotVisibleEnvelopeDoesNotExposeData() =>
        Assert.IsNull(WrongTenantOutcome(ReadSurface.EvidenceMetadata).Data);

    [TestMethod]
    public void TenantScope_NotVisibleEnvelopeDoesNotExposeEvidenceRefs() =>
        AssertHiddenTextAbsent(WrongTenantOutcome(ReadSurface.EvidenceMetadata), HiddenEvidenceRef);

    [TestMethod]
    public void TenantScope_NotVisibleEnvelopeDoesNotExposeReceiptRefs() =>
        AssertHiddenTextAbsent(WrongTenantOutcome(ReadSurface.ReceiptMetadata), HiddenReceiptRef);

    [TestMethod]
    public void TenantScope_NotVisibleEnvelopeDoesNotExposeArtifactRefs() =>
        AssertHiddenTextAbsent(WrongTenantOutcome(ReadSurface.PatchPackageMetadata), HiddenArtifactRef);

    [TestMethod]
    public void TenantScope_NotVisibleEnvelopeDoesNotExposeTimestamps() =>
        AssertHiddenTextAbsent(WrongTenantOutcome(ReadSurface.EvidenceMetadata), "2031-01-01");

    [TestMethod]
    public void TenantScope_WrongTenantCanonicalDoesNotFallbackToVisibleOperationStatus() =>
        AssertWrongTenantDoesNotFallback(ReadSurface.OperationStatus);

    [TestMethod]
    public void TenantScope_WrongTenantCanonicalDoesNotFallbackToVisibleEvidence() =>
        AssertWrongTenantDoesNotFallback(ReadSurface.EvidenceMetadata);

    [TestMethod]
    public void TenantScope_WrongTenantCanonicalDoesNotFallbackToVisibleReceipt() =>
        AssertWrongTenantDoesNotFallback(ReadSurface.ReceiptMetadata);

    [TestMethod]
    public void TenantScope_WrongTenantCanonicalDoesNotFallbackToVisibleTimeline() =>
        AssertWrongTenantDoesNotFallback(ReadSurface.Timeline);

    [TestMethod]
    public void TenantScope_WrongTenantCanonicalDoesNotFallbackToVisiblePatchMetadata() =>
        AssertWrongTenantDoesNotFallback(ReadSurface.PatchPackageMetadata);

    [TestMethod]
    public void TenantScope_WrongTenantCanonicalDoesNotFallbackToVisibleValidation() =>
        AssertWrongTenantDoesNotFallback(ReadSurface.ValidationMetadata);

    [TestMethod]
    public void TenantScope_TrueNotFoundMayFallbackWithBoundaryPreserved()
    {
        var api = Api(ReadSurface.OperationStatus, callerTenant: TenantId, sources:
        [
            Source(ReadSurface.OperationStatus, tenantId: TenantId, includeRecord: false),
            FallbackSource(ReadSurface.OperationStatus)
        ]);

        var outcome = Read(ReadSurface.OperationStatus, api);

        Assert.AreEqual(FrontendReadinessReadStateKind.Available, outcome.ReadState.Kind);
        Assert.IsNotNull(outcome.Data);
        AssertReadOnly(outcome.Boundary);
        AssertHiddenTextPresent(outcome, FallbackSummary);
    }

    [TestMethod]
    public void TenantScope_UnavailableCanonicalDoesNotFallbackToVisibleOperationStatus()
    {
        var api = Api(ReadSurface.OperationStatus, callerTenant: TenantId, sources:
        [
            new ThrowingSource(ReadSurface.OperationStatus),
            FallbackSource(ReadSurface.OperationStatus)
        ]);

        var outcome = Read(ReadSurface.OperationStatus, api);

        Assert.AreEqual(FrontendReadinessReadStateKind.Unavailable, outcome.ReadState.Kind);
        Assert.IsNull(outcome.Data);
        AssertHiddenTextAbsent(outcome, FallbackSummary);
    }

    [TestMethod]
    public void TenantScope_InvalidCanonicalDoesNotFallbackToVisibleOperationStatus()
    {
        var invalid = OperationStatusRecord(tenantId: TenantId, status: OperationStatus(operationKind: string.Empty));
        var api = Api(ReadSurface.OperationStatus, callerTenant: TenantId, sources:
        [
            new OperationStatusFrontendReadinessBackendTruthSource(new GovernedOperationStatusReadRepository([invalid])),
            FallbackSource(ReadSurface.OperationStatus)
        ]);

        var outcome = Read(ReadSurface.OperationStatus, api);

        Assert.AreEqual(FrontendReadinessReadStateKind.Invalid, outcome.ReadState.Kind);
        Assert.IsNotNull(outcome.Data);
        AssertHiddenTextAbsent(outcome, FallbackSummary);
    }

    [TestMethod]
    public void TenantScope_RedactedCanonicalDoesNotFallbackToVisibleEvidence()
    {
        var api = Api(ReadSurface.EvidenceMetadata, callerTenant: TenantId, sources:
        [
            new EvidenceMetadataFrontendReadinessBackendTruthSource(
                new EvidenceMetadataReadRepository([EvidenceRecord(tenantId: TenantId, containsRawPayload: true)])),
            FallbackSource(ReadSurface.EvidenceMetadata)
        ]);

        var outcome = Read(ReadSurface.EvidenceMetadata, api);

        Assert.AreEqual(FrontendReadinessReadStateKind.Redacted, outcome.ReadState.Kind);
        Assert.IsNotNull(outcome.Data);
        AssertHiddenTextAbsent(outcome, FallbackSummary);
    }

    [TestMethod]
    public void Auth_AuthorizedReadDoesNotCreateApprovalAuthority() =>
        AssertAuthorizedReadNoAuthority(boundary => boundary.CanCreateApproval || boundary.CanAcceptApproval);

    [TestMethod]
    public void Auth_AuthorizedReadDoesNotSatisfyPolicy() =>
        AssertAuthorizedReadNoAuthority(boundary => boundary.CanSatisfyPolicy);

    [TestMethod]
    public void Auth_AuthorizedReadDoesNotAllowExecution() =>
        AssertAuthorizedReadNoAuthority(boundary => boundary.CanExecute);

    [TestMethod]
    public void Auth_AuthorizedReadDoesNotAllowSourceMutation() =>
        AssertAuthorizedReadNoAuthority(boundary => boundary.CanMutateSource || boundary.CanRollback);

    [TestMethod]
    public void Auth_AuthorizedReadDoesNotAllowCommitPushOrPr() =>
        AssertAuthorizedReadNoAuthority(boundary =>
            boundary.CanCommit || boundary.CanPush || boundary.CanCreatePullRequest || boundary.CanMarkReadyForReview);

    [TestMethod]
    public void Auth_AuthorizedReadDoesNotAllowMergeReleaseDeploy() =>
        AssertAuthorizedReadNoAuthority(boundary => boundary.CanMerge || boundary.CanRelease || boundary.CanDeploy);

    [TestMethod]
    public void Auth_AuthorizedReadDoesNotAllowMemoryPromotionOrWorkflowContinuation() =>
        AssertAuthorizedReadNoAuthority(boundary => boundary.CanPromoteMemory || boundary.CanContinueWorkflow);

    [TestMethod]
    public void StaticScan_A12AddsNoFrontendFiles()
    {
        var changed = GitChangedFiles();

        Assert.IsFalse(changed.Any(path =>
            path.Contains("Tauri", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Frontend", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)),
            string.Join(Environment.NewLine, changed));
    }

    [TestMethod]
    public void StaticScan_A12AddsNoMutationEndpointForReadResources()
    {
        foreach (var method in ReadEndpointMethods())
        {
            Assert.IsTrue(method.GetCustomAttributes<HttpGetAttribute>().Any(), method.Name);
            Assert.IsFalse(method.GetCustomAttributes<HttpPostAttribute>().Any(), method.Name);
            Assert.IsFalse(method.GetCustomAttributes<HttpPutAttribute>().Any(), method.Name);
            Assert.IsFalse(method.GetCustomAttributes<HttpPatchAttribute>().Any(), method.Name);
            Assert.IsFalse(method.GetCustomAttributes<HttpDeleteAttribute>().Any(), method.Name);
        }
    }

    [TestMethod]
    public void StaticScan_A12AddsNoAllowAnonymousToReadEndpoints()
    {
        foreach (var method in ReadEndpointMethods())
            AssertNoAllowAnonymous(method);
    }

    [TestMethod]
    public void StaticScan_A12AddsNoExecutorOrProviderMutationPath()
    {
        var source = A12SourceWithoutReceipt();
        foreach (var marker in ForbiddenMutationMarkers())
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
    }

    [TestMethod]
    public void StaticScan_A12DoesNotReadRawPayloads()
    {
        var source = A12SourceWithoutReceipt();
        foreach (var marker in new[]
                 {
                     "ReadValidationLogAsync",
                     "ReadValidationOutputAsync",
                     "ReadCommandOutputAsync",
                     "ReadBuildOutputAsync",
                     "ReadTestOutputAsync",
                     "ReadPatchPayloadAsync",
                     "ReadPatchTextAsync",
                     "ReadDiffTextAsync",
                     "ReadTimelinePayloadAsync",
                     "ReadEventPayloadAsync",
                     "ReadReceiptTextAsync",
                     "ReadEvidenceTextAsync"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [TestMethod]
    public void StaticScan_A12DoesNotGenerateFrontendClient()
    {
        var changed = GitChangedFiles();
        Assert.IsFalse(changed.Any(path =>
            path.Contains("generated", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)),
            string.Join(Environment.NewLine, changed));
    }

    [TestMethod]
    public void StaticScan_A12DoesNotCreateActionRequests()
    {
        foreach (var method in ReadEndpointMethods())
            Assert.IsFalse(method.Name.Contains("ActionRequest", StringComparison.OrdinalIgnoreCase), method.Name);
    }

    [TestMethod]
    public void StaticScan_A12DoesNotRefreshOrRunValidation()
    {
        var source = A12SourceWithoutReceipt();
        foreach (var marker in new[] { "RefreshValidation", "RunValidation", "RetryValidation", "RepairValidation" })
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
    }

    [TestMethod]
    public void StaticScan_A12DoesNotAddBroadRolePermissionSystem()
    {
        var changed = GitChangedFiles();
        Assert.IsFalse(changed.Any(path =>
            path.Contains("Role", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Permission", StringComparison.OrdinalIgnoreCase)),
            string.Join(Environment.NewLine, changed));
    }

    [TestMethod]
    public void StaticScan_A12DoesNotAddSqlMigration()
    {
        var changed = GitChangedFiles();
        Assert.IsFalse(changed.Any(path =>
            path.Contains("Migrations", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)),
            string.Join(Environment.NewLine, changed));
    }

    private static void AssertEndpointRequiresAuthorize(MethodInfo method)
    {
        var controllerHasAuthorize = typeof(FrontendReadinessController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Any();
        var methodHasAuthorize = method.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();

        Assert.IsTrue(controllerHasAuthorize || methodHasAuthorize, method.Name);
    }

    private static void AssertNoAllowAnonymous(MethodInfo method)
    {
        Assert.IsFalse(
            typeof(FrontendReadinessController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any(),
            "Controller must not allow anonymous frontend-readiness reads.");
        Assert.IsFalse(
            method.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any(),
            method.Name);
    }

    private static IEnumerable<MethodInfo> ReadEndpointMethods() =>
        ReadEndpointNames.Select(ControllerMethod);

    private static MethodInfo ControllerMethod(string name) =>
        typeof(FrontendReadinessController).GetMethod(name)
        ?? throw new InvalidOperationException($"Could not find controller method {name}.");

    private static void AssertMatchingTenantCanRead(ReadSurface surface)
    {
        var outcome = Read(surface, Api(surface, callerTenant: TenantId, sources:
        [
            Source(surface, tenantId: TenantId)
        ]));

        Assert.AreEqual(FrontendReadinessReadStateKind.Available, outcome.ReadState.Kind);
        Assert.IsNotNull(outcome.Data);
        AssertReadOnly(outcome.Boundary);
        AssertNoAuthority(outcome);
    }

    private static void AssertWrongTenantCannotRead(ReadSurface surface) =>
        AssertNotVisible(WrongTenantOutcome(surface));

    private static void AssertMissingTenantCannotRead(ReadSurface surface)
    {
        var outcome = Read(surface, Api(surface, callerTenant: null, sources:
        [
            Source(surface, tenantId: TenantId)
        ]));

        AssertNotVisible(outcome);
    }

    private static void AssertWrongTenantDoesNotFallback(ReadSurface surface)
    {
        var outcome = Read(surface, Api(surface, callerTenant: WrongTenantId, sources:
        [
            Source(surface, tenantId: TenantId),
            FallbackSource(surface)
        ]));

        AssertNotVisible(outcome);
        AssertHiddenTextAbsent(outcome, FallbackSummary);
    }

    private static void AssertNotVisibleDoesNotLeak(ReadSurface surface)
    {
        var outcome = WrongTenantOutcome(surface);

        AssertNotVisible(outcome);
        foreach (var marker in HiddenMarkers())
            AssertHiddenTextAbsent(outcome, marker);
    }

    private static void AssertExplicitGlobalCanReadAndTenantlessScopedCannot(ReadSurface surface)
    {
        if (surface == ReadSurface.PatchArtifacts)
            return;

        var global = Read(surface, Api(surface, callerTenant: WrongTenantId, sources:
        [
            Source(surface, tenantId: null, tenantScoped: false)
        ]));
        Assert.AreEqual(FrontendReadinessReadStateKind.Available, global.ReadState.Kind);
        Assert.IsNotNull(global.Data);

        var tenantlessScoped = Read(surface, Api(surface, callerTenant: TenantId, sources:
        [
            Source(surface, tenantId: null, tenantScoped: true)
        ]));
        AssertNotVisible(tenantlessScoped);
    }

    private static ReadOutcome WrongTenantOutcome(ReadSurface surface) =>
        Read(surface, Api(surface, callerTenant: WrongTenantId, sources:
        [
            Source(surface, tenantId: TenantId)
        ]));

    private static void AssertAuthorizedReadNoAuthority(Func<FrontendReadBoundary, bool> isForbiddenAuthority)
    {
        var outcome = Read(ReadSurface.OperationStatus, Api(ReadSurface.OperationStatus, callerTenant: TenantId, sources:
        [
            Source(ReadSurface.OperationStatus, tenantId: TenantId)
        ]));

        Assert.IsFalse(isForbiddenAuthority(outcome.Boundary));
        AssertNoAuthority(outcome);
    }

    private static void AssertNotVisible(ReadOutcome outcome)
    {
        Assert.AreEqual(FrontendReadinessReadStateKind.NotVisible, outcome.ReadState.Kind);
        Assert.IsNull(outcome.Data);
        AssertReadOnly(outcome.Boundary);
        Assert.IsFalse(outcome.MutationOccurred);
        Assert.IsTrue(outcome.Errors.Count > 0);
        Assert.AreEqual("FRONTEND_READINESS_NOTVISIBLE", outcome.Errors[0].Code);
    }

    private static void AssertNoAuthority(ReadOutcome outcome)
    {
        Assert.IsFalse(outcome.ReadState.IsAuthorityGrant);
        Assert.IsFalse(outcome.ReadState.AllowsMutation);
        Assert.IsFalse(outcome.MutationOccurred);
        AssertReadOnly(outcome.Boundary);
        AssertReadOnly(outcome.ReadState.Boundary);
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

    private static void AssertHiddenTextPresent(ReadOutcome outcome, string marker) =>
        Assert.IsTrue(outcome.Json.Contains(marker, StringComparison.OrdinalIgnoreCase), outcome.Json);

    private static void AssertHiddenTextAbsent(ReadOutcome outcome, string marker) =>
        Assert.IsFalse(outcome.Json.Contains(marker, StringComparison.OrdinalIgnoreCase), $"{marker}{Environment.NewLine}{outcome.Json}");

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(values.Contains(expected, StringComparer.OrdinalIgnoreCase), string.Join(Environment.NewLine, values));

    private static BackendFrontendReadinessReadApi Api(
        ReadSurface surface,
        int? callerTenant,
        IReadOnlyCollection<IFrontendReadinessBackendTruthSource> sources) =>
        new(sources, callerTenant.HasValue ? new TestTenantContext(callerTenant.Value) : null, () => ObservedAtUtc);

    private static IFrontendReadinessBackendTruthSource Source(
        ReadSurface surface,
        int? tenantId,
        bool tenantScoped = true,
        bool includeRecord = true)
    {
        return surface switch
        {
            ReadSurface.OperationStatus => new OperationStatusFrontendReadinessBackendTruthSource(
                new GovernedOperationStatusReadRepository(includeRecord ? [OperationStatusRecord(tenantId, tenantScoped)] : [])),
            ReadSurface.EvidenceMetadata => new EvidenceMetadataFrontendReadinessBackendTruthSource(
                new EvidenceMetadataReadRepository(includeRecord ? [EvidenceRecord(tenantId, tenantScoped)] : [])),
            ReadSurface.ReceiptMetadata => new ReceiptMetadataFrontendReadinessBackendTruthSource(
                new ReceiptMetadataReadRepository(includeRecord ? [ReceiptRecord(tenantId, tenantScoped)] : [])),
            ReadSurface.Timeline => new OperationTimelineFrontendReadinessBackendTruthSource(
                new OperationTimelineReadRepository(includeRecord ? [TimelineRecord(tenantId, tenantScoped)] : [])),
            ReadSurface.PatchPackageMetadata => new PatchPackageMetadataFrontendReadinessBackendTruthSource(
                new PatchPackageMetadataReadRepository(includeRecord ? [PatchPackageRecord(tenantId, tenantScoped)] : [])),
            ReadSurface.ValidationMetadata => new ValidationResultMetadataFrontendReadinessBackendTruthSource(
                new ValidationResultMetadataReadRepository(includeRecord ? [ValidationRecord(tenantId, tenantScoped)] : [])),
            ReadSurface.PatchArtifacts => includeRecord
                ? new SeededBackendTruthSource(surface, tenantId)
                : new SeededBackendTruthSource(surface, tenantId, includeData: false),
            _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null)
        };
    }

    private static IFrontendReadinessBackendTruthSource FallbackSource(ReadSurface surface) =>
        new SeededBackendTruthSource(surface, tenantId: null, fallback: true);

    private static ReadOutcome Read(ReadSurface surface, IFrontendReadinessReadApi api)
    {
        var controller = new FrontendReadinessController(api);
        return surface switch
        {
            ReadSurface.OperationStatus => Outcome(controller.GetOperationStatus(OperationId)),
            ReadSurface.EvidenceMetadata => Outcome(controller.GetEvidenceMetadata(EvidenceRef)),
            ReadSurface.ReceiptMetadata => Outcome(controller.GetReceiptMetadata(ReceiptRef)),
            ReadSurface.Timeline => Outcome(controller.GetOperationTimeline(OperationId)),
            ReadSurface.PatchPackageMetadata => Outcome(controller.GetPatchPackageMetadata(PackageId)),
            ReadSurface.ValidationMetadata => Outcome(controller.GetValidationResultMetadata(ValidationResultId)),
            ReadSurface.PatchArtifacts => Outcome(controller.GetPatchPackageArtifacts(PackageId)),
            _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null)
        };
    }

    private static ReadOutcome Outcome<T>(ActionResult<FrontendReadinessApiEnvelope<T>> result)
    {
        var objectResult = result.Result as ObjectResult;
        Assert.IsNotNull(objectResult);
        var envelope = (FrontendReadinessApiEnvelope<T>)objectResult.Value!;
        var json = JsonSerializer.Serialize(envelope);
        return new ReadOutcome(
            envelope.Data,
            envelope.ReadState,
            envelope.Freshness,
            envelope.Boundary,
            envelope.MutationOccurred,
            envelope.Warnings,
            envelope.Errors,
            json);
    }

    private static GovernedOperationStatusReadRecord OperationStatusRecord(
        int? tenantId,
        bool tenantScoped = true,
        GovernedOperationStatus? status = null) =>
        new()
        {
            OperationId = OperationId,
            Status = status ?? OperationStatus(),
            IsTenantScoped = tenantScoped,
            TenantId = tenantId
        };

    private static GovernedOperationStatus OperationStatus(string operationKind = "A12Status") =>
        new()
        {
            OperationId = OperationId,
            OperationKind = operationKind,
            Subject = $"repo:{HiddenRepository} branch:{HiddenBranch} run:{HiddenRunId}",
            State = GovernedOperationState.Blocked,
            BlockedReasons = ["A12Blocked"],
            MissingEvidence = [HiddenEvidenceRef],
            NextSafeActions = ["inspect frontend readiness tenant scope"],
            ForbiddenActions = ["do not execute from frontend readiness read output"],
            EvidenceRefs = [HiddenEvidenceRef],
            ReceiptRefs = [HiddenReceiptRef],
            ObservedAtUtc = HiddenObservedAtUtc,
            ExpiresAtUtc = HiddenExpiresAtUtc
        };

    private static GovernedOperationStatus FallbackOperationStatus() =>
        new()
        {
            OperationId = OperationId,
            OperationKind = "FallbackStatus",
            Subject = FallbackSummary,
            State = GovernedOperationState.Blocked,
            BlockedReasons = ["FallbackBlocked"],
            MissingEvidence = ["fallback-evidence:a12"],
            NextSafeActions = ["inspect fallback status"],
            ForbiddenActions = ["do not execute from fallback status"],
            EvidenceRefs = ["fallback-evidence:a12"],
            ReceiptRefs = ["fallback-receipt:a12"],
            ObservedAtUtc = ObservedAtUtc,
            ExpiresAtUtc = null
        };

    private static EvidenceMetadataReadRecord EvidenceRecord(
        int? tenantId,
        bool tenantScoped = true,
        bool containsRawPayload = false) =>
        new()
        {
            EvidenceRef = EvidenceRef,
            EvidenceKind = "A12Evidence",
            Summary = HiddenSummary,
            IsTenantScoped = tenantScoped,
            TenantId = tenantId,
            ContainsRawPayload = containsRawPayload,
            Warnings = ["Evidence metadata is reference-only."],
            AuthorityWarnings = ["Evidence metadata is not authority."],
            ObservedAtUtc = HiddenObservedAtUtc
        };

    private static ReceiptMetadataReadRecord ReceiptRecord(int? tenantId, bool tenantScoped = true) =>
        new()
        {
            ReceiptRef = ReceiptRef,
            ReceiptKind = "A12Receipt",
            Summary = HiddenSummary,
            OperationId = OperationId,
            OperationKind = "A12Read",
            Subject = HiddenRepository,
            IsTenantScoped = tenantScoped,
            TenantId = tenantId,
            Warnings = ["Receipt metadata is reference-only."],
            AuthorityWarnings = ["Receipt metadata is not authority."],
            ObservedAtUtc = HiddenObservedAtUtc
        };

    private static OperationTimelineEventReadRecord TimelineRecord(int? tenantId, bool tenantScoped = true) =>
        new()
        {
            OperationId = OperationId,
            EntryId = "timeline-entry:a12",
            EventKind = "A12ReadObserved",
            Summary = HiddenSummary,
            IsTenantScoped = tenantScoped,
            TenantId = tenantId,
            ObservedAtUtc = HiddenObservedAtUtc,
            EvidenceRefs = [HiddenEvidenceRef],
            ReceiptRefs = [HiddenReceiptRef]
        };

    private static PatchPackageMetadataReadRecord PatchPackageRecord(int? tenantId, bool tenantScoped = true) =>
        new()
        {
            PackageId = PackageId,
            Repository = HiddenRepository,
            Branch = HiddenBranch,
            RunId = HiddenRunId,
            PatchHash = HiddenPatchHash,
            ProposedFilePaths = ["IronDev.Core/Governance/A12.cs"],
            ArtifactRefs = [HiddenArtifactRef],
            EvidenceRefs = [HiddenEvidenceRef],
            ReceiptRefs = [HiddenReceiptRef],
            ReviewSummaryRef = "review-summary:a12",
            KnownRisksRef = "known-risks:a12",
            IsTenantScoped = tenantScoped,
            TenantId = tenantId,
            ObservedAtUtc = HiddenObservedAtUtc,
            ExpiresAtUtc = HiddenExpiresAtUtc,
            Warnings = ["Patch package metadata is reference-only."],
            AuthorityWarnings = ["Patch package metadata is not source apply authority."]
        };

    private static ValidationResultMetadataReadRecord ValidationRecord(int? tenantId, bool tenantScoped = true) =>
        new()
        {
            ValidationResultId = ValidationResultId,
            Repository = HiddenRepository,
            Branch = HiddenBranch,
            RunId = HiddenRunId,
            PatchHash = HiddenPatchHash,
            Outcome = "PassedHiddenA12",
            WhatRan = ["A12 focused"],
            WhatPassed = ["A12 focused"],
            WhatFailed = [],
            WhatWasSkipped = [],
            EvidenceRefs = [HiddenEvidenceRef],
            ReceiptRefs = [HiddenReceiptRef],
            FreshnessKnown = true,
            IsTenantScoped = tenantScoped,
            TenantId = tenantId,
            ObservedAtUtc = HiddenObservedAtUtc,
            ExpiresAtUtc = HiddenExpiresAtUtc,
            Warnings = ["Validation metadata is reference-only."],
            AuthorityWarnings = ["Validation metadata is not approval."]
        };

    private static FrontendPatchPackageArtifactsReadModel PatchArtifacts(bool fallback = false) =>
        new()
        {
            PackageId = PackageId,
            Repository = fallback ? "fallback-repository" : HiddenRepository,
            Branch = fallback ? "fallback-branch" : HiddenBranch,
            RunId = fallback ? "fallback-run" : HiddenRunId,
            PatchHash = fallback ? "sha256:fallback" : HiddenPatchHash,
            PatchDiffText = fallback ? FallbackSummary : HiddenSummary,
            ReviewSummaryText = fallback ? FallbackSummary : HiddenSummary,
            KnownRisksText = fallback ? FallbackSummary : HiddenSummary,
            ValidationSummaryText = fallback ? FallbackSummary : HiddenSummary,
            ValidationOutcome = fallback ? "FallbackPassed" : "PassedHiddenA12",
            WhatRan = ["A12 focused"],
            WhatPassed = ["A12 focused"],
            WhatFailed = [],
            WhatWasSkipped = [],
            ValidationIsStale = false,
            ProposedFilePaths = ["IronDev.Core/Governance/A12.cs"],
            ArtifactRefs = [fallback ? "fallback-artifact:a12" : HiddenArtifactRef],
            EvidenceRefs = [fallback ? "fallback-evidence:a12" : HiddenEvidenceRef],
            ReceiptRefs = [fallback ? "fallback-receipt:a12" : HiddenReceiptRef],
            AuthorityWarnings = ["Patch artifacts are not source apply authority."],
            Boundary = FrontendReadBoundary.ReadOnlyStatus,
            ObservedAtUtc = fallback ? ObservedAtUtc : HiddenObservedAtUtc,
            ExpiresAtUtc = fallback ? null : HiddenExpiresAtUtc,
            FreshnessKnown = true
        };

    private static IReadOnlyDictionary<string, T> Map<T>(string key, T value) =>
        new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase)
        {
            [key] = value
        };

    private static IReadOnlyCollection<string> HiddenMarkers() =>
    [
        HiddenRepository,
        HiddenBranch,
        HiddenRunId,
        HiddenPatchHash,
        HiddenEvidenceRef,
        HiddenReceiptRef,
        HiddenArtifactRef,
        HiddenSummary,
        "2031-01-01"
    ];

    private static IReadOnlyCollection<string> ForbiddenMutationMarkers() =>
    [
        "CreateApproval(",
        "AcceptApproval(",
        "SatisfyPolicy(",
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
    ];

    private static string A12SourceWithoutReceipt()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            ReadRepositoryFile(root, "IronDev.Api/Controllers/FrontendReadinessController.cs"),
            ReadRepositoryFile(root, "IronDev.Infrastructure/Governance/BackendFrontendReadinessReadApi.cs"),
            ReadRepositoryFile(root, "IronDev.Infrastructure/Governance/OperationStatusFrontendReadinessBackendTruthSource.cs"),
            ReadRepositoryFile(root, "IronDev.Infrastructure/Governance/EvidenceMetadataFrontendReadinessBackendTruthSource.cs"),
            ReadRepositoryFile(root, "IronDev.Infrastructure/Governance/ReceiptMetadataFrontendReadinessBackendTruthSource.cs"),
            ReadRepositoryFile(root, "IronDev.Infrastructure/Governance/OperationTimelineFrontendReadinessBackendTruthSource.cs"),
            ReadRepositoryFile(root, "IronDev.Infrastructure/Governance/PatchPackageMetadataFrontendReadinessBackendTruthSource.cs"),
            ReadRepositoryFile(root, "IronDev.Infrastructure/Governance/ValidationResultMetadataFrontendReadinessBackendTruthSource.cs"));
    }

    private static IReadOnlyList<string> GitChangedFiles()
    {
        var root = FindRepositoryRoot();
        var candidates = new[]
        {
            "IronDev.IntegrationTests/BlockA12FrontendReadinessAuthorizationTenantScopeProofTests.cs",
            "Docs/receipts/A12_FRONTEND_READINESS_AUTHORIZATION_TENANT_SCOPE_PROOF.md"
        };

        var files = candidates
            .Where(path => File.Exists(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();

        return files;
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

    private static string ReadRepositoryFile(string root, string relativePath) =>
        File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private enum ReadSurface
    {
        OperationStatus,
        EvidenceMetadata,
        ReceiptMetadata,
        Timeline,
        PatchPackageMetadata,
        ValidationMetadata,
        PatchArtifacts
    }

    private sealed record ReadOutcome(
        object? Data,
        FrontendReadinessReadState ReadState,
        FrontendReadinessFreshnessState Freshness,
        FrontendReadBoundary Boundary,
        bool MutationOccurred,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<FrontendReadinessApiError> Errors,
        string Json);

    private sealed class TestTenantContext(int tenantId) : ICurrentTenantContext
    {
        public int TenantId { get; } = tenantId;
    }

    private sealed class SeededBackendTruthSource(
        ReadSurface surface,
        int? tenantId,
        bool fallback = false,
        bool includeData = true) : FrontendReadinessBackendTruthSource
    {
        public override string SourceName => "seeded-a12";
        public override int? TenantId => tenantId;

        public override GovernedOperationStatus? GetOperationStatus(string operationId) =>
            includeData && surface == ReadSurface.OperationStatus
                ? fallback ? FallbackOperationStatus() : OperationStatus()
                : null;

        public override FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(string evidenceRef) =>
            includeData && surface == ReadSurface.EvidenceMetadata
                ? new FrontendEvidenceMetadataReadModel
                {
                    EvidenceRef = EvidenceRef,
                    EvidenceKind = fallback ? "FallbackEvidence" : "A12Evidence",
                    Summary = fallback ? FallbackSummary : HiddenSummary,
                    ReferenceOnly = true,
                    ContainsRawPayload = false,
                    Warnings = ["Evidence is reference-only."],
                    Boundary = FrontendReadBoundary.ReadOnlyStatus,
                    ObservedAtUtc = fallback ? ObservedAtUtc : HiddenObservedAtUtc,
                    ExpiresAtUtc = fallback ? null : HiddenExpiresAtUtc,
                    FreshnessKnown = true
                }
                : null;

        public override FrontendReceiptMetadataReadModel? GetReceiptMetadata(string receiptRef) =>
            includeData && surface == ReadSurface.ReceiptMetadata
                ? new FrontendReceiptMetadataReadModel
                {
                    ReceiptRef = ReceiptRef,
                    ReceiptKind = fallback ? "FallbackReceipt" : "A12Receipt",
                    Summary = fallback ? FallbackSummary : HiddenSummary,
                    ReferenceOnly = true,
                    GrantsAuthority = false,
                    ContinuesWorkflow = false,
                    Warnings = ["Receipt is reference-only."],
                    Boundary = FrontendReadBoundary.ReadOnlyStatus,
                    ObservedAtUtc = fallback ? ObservedAtUtc : HiddenObservedAtUtc,
                    ExpiresAtUtc = fallback ? null : HiddenExpiresAtUtc,
                    FreshnessKnown = true
                }
                : null;

        public override FrontendOperationTimelineReadModel? GetOperationTimeline(string operationId) =>
            includeData && surface == ReadSurface.Timeline
                ? new FrontendOperationTimelineReadModel
                {
                    OperationId = OperationId,
                    Entries =
                    [
                        new FrontendTimelineEntry
                        {
                            EntryId = "timeline-entry:a12:fallback",
                            EventKind = "A12Fallback",
                            Summary = fallback ? FallbackSummary : HiddenSummary,
                            EvidenceRefs = [fallback ? "fallback-evidence:a12" : HiddenEvidenceRef],
                            ReceiptRefs = [fallback ? "fallback-receipt:a12" : HiddenReceiptRef],
                            ObservedAtUtc = fallback ? ObservedAtUtc : HiddenObservedAtUtc
                        }
                    ],
                    Boundary = FrontendReadBoundary.ReadOnlyStatus,
                    ObservedAtUtc = fallback ? ObservedAtUtc : HiddenObservedAtUtc,
                    ExpiresAtUtc = fallback ? null : HiddenExpiresAtUtc,
                    FreshnessKnown = true
                }
                : null;

        public override FrontendPatchPackageMetadataReadModel? GetPatchPackageMetadata(string packageId) =>
            includeData && surface == ReadSurface.PatchPackageMetadata
                ? new FrontendPatchPackageMetadataReadModel
                {
                    PackageId = PackageId,
                    Repository = fallback ? "fallback-repository" : HiddenRepository,
                    Branch = fallback ? "fallback-branch" : HiddenBranch,
                    RunId = fallback ? "fallback-run" : HiddenRunId,
                    PatchHash = fallback ? "sha256:fallback" : HiddenPatchHash,
                    ProposedFilePaths = ["IronDev.Core/Governance/A12.cs"],
                    ArtifactRefs = [fallback ? "fallback-artifact:a12" : HiddenArtifactRef],
                    EvidenceRefs = [fallback ? "fallback-evidence:a12" : HiddenEvidenceRef],
                    ReceiptRefs = [fallback ? "fallback-receipt:a12" : HiddenReceiptRef],
                    ReviewSummaryRef = fallback ? "fallback-review:a12" : "review-summary:a12",
                    KnownRisksRef = fallback ? "fallback-risks:a12" : "known-risks:a12",
                    Boundary = FrontendReadBoundary.ReadOnlyStatus,
                    ObservedAtUtc = fallback ? ObservedAtUtc : HiddenObservedAtUtc,
                    ExpiresAtUtc = fallback ? null : HiddenExpiresAtUtc,
                    FreshnessKnown = true
                }
                : null;

        public override FrontendValidationResultMetadataReadModel? GetValidationResultMetadata(string validationResultId) =>
            includeData && surface == ReadSurface.ValidationMetadata
                ? new FrontendValidationResultMetadataReadModel
                {
                    ValidationResultId = ValidationResultId,
                    Repository = fallback ? "fallback-repository" : HiddenRepository,
                    Branch = fallback ? "fallback-branch" : HiddenBranch,
                    RunId = fallback ? "fallback-run" : HiddenRunId,
                    PatchHash = fallback ? "sha256:fallback" : HiddenPatchHash,
                    Outcome = fallback ? "FallbackPassed" : "PassedHiddenA12",
                    WhatRan = ["A12 focused"],
                    WhatPassed = ["A12 focused"],
                    WhatFailed = [],
                    WhatWasSkipped = [],
                    IsStale = false,
                    EvidenceRefs = [fallback ? "fallback-evidence:a12" : HiddenEvidenceRef],
                    ReceiptRefs = [fallback ? "fallback-receipt:a12" : HiddenReceiptRef],
                    Boundary = FrontendReadBoundary.ReadOnlyStatus,
                    ObservedAtUtc = fallback ? ObservedAtUtc : HiddenObservedAtUtc,
                    ExpiresAtUtc = fallback ? null : HiddenExpiresAtUtc,
                    FreshnessKnown = true
                }
                : null;

        public override FrontendPatchPackageArtifactsReadModel? GetPatchPackageArtifacts(string packageId) =>
            includeData && surface == ReadSurface.PatchArtifacts
                ? PatchArtifacts(fallback)
                : null;
    }

    private sealed class ThrowingSource(ReadSurface surface) : FrontendReadinessBackendTruthSource
    {
        public override string SourceName => "throwing-a12";

        public override FrontendReadinessBackendReadResult<GovernedOperationStatus> ReadOperationStatus(
            string operationId,
            FrontendReadinessReadScope scope) =>
            surface == ReadSurface.OperationStatus
                ? throw new InvalidOperationException("backend unavailable")
                : base.ReadOperationStatus(operationId, scope);
    }
}
