using IronDev.Api.Controllers;
using IronDev.Core.Auth;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockA02OperationStatusReadRepositoryAdapterTests
{
    private const string OperationId = "operation-a02";
    private const string EvidenceRef = "operation-status-evidence:a02";
    private const string ReceiptRef = "operation-status-receipt:a02";
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-22T01:00:00Z");
    private static readonly DateTimeOffset ExpiresAtUtc = DateTimeOffset.Parse("2026-06-22T02:00:00Z");

    [TestMethod]
    public void OperationStatusRepository_ReturnsCanonicalStatusByOperationId()
    {
        var result = Repository(Record()).GetByOperationId(OperationId, Scope());

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Status);
        Assert.AreEqual(OperationId, result.Status.OperationId);
        Assert.AreEqual("CanonicalStatus", result.Status.OperationKind);
    }

    [TestMethod]
    public void OperationStatusRepository_ReturnsNullForMissingOperation()
    {
        var result = Repository().GetByOperationId("missing-operation", Scope());

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Status);
        AssertContains(result.Issues, "OperationStatusNotFound");
    }

    [TestMethod]
    public void OperationStatusRepository_PreservesBlockedReasons()
    {
        var result = Repository(Record()).GetByOperationId(OperationId, Scope());

        AssertContains(result.Status!.BlockedReasons, "CanonicalStatusBlocked");
    }

    [TestMethod]
    public void OperationStatusRepository_PreservesMissingEvidence()
    {
        var result = Repository(Record()).GetByOperationId(OperationId, Scope());

        AssertContains(result.Status!.MissingEvidence, "canonical-operation-status-record:a02");
    }

    [TestMethod]
    public void OperationStatusRepository_PreservesForbiddenActions()
    {
        var result = Repository(Record()).GetByOperationId(OperationId, Scope());

        AssertContains(result.Status!.ForbiddenActions, "do not execute from operation status");
    }

    [TestMethod]
    public void OperationStatusRepository_PreservesEvidenceAndReceiptRefs()
    {
        var result = Repository(Record()).GetByOperationId(OperationId, Scope());

        AssertContains(result.Status!.EvidenceRefs, EvidenceRef);
        AssertContains(result.Status.ReceiptRefs, ReceiptRef);
        AssertContains(result.EvidenceRefs, EvidenceRef);
    }

    [TestMethod]
    public void OperationStatusRepository_PreservesObservedAndExpiryTimestamps()
    {
        var result = Repository(Record()).GetByOperationId(OperationId, Scope());

        Assert.AreEqual(ObservedAtUtc, result.Status!.ObservedAtUtc);
        Assert.AreEqual(ExpiresAtUtc, result.Status.ExpiresAtUtc);
    }

    [TestMethod]
    public void OperationStatusRepository_InvalidStoredStatusFailsClosed()
    {
        var invalid = Status() with
        {
            BlockedReasons = [],
            MissingEvidence = [],
            NextSafeActions = []
        };

        var result = Repository(Record(status: invalid)).GetByOperationId(OperationId, Scope());

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Status);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
        AssertContains(result.Issues, "StoredOperationStatusInvalid");
        AssertContains(result.Status.BlockedReasons, "StoredOperationStatusInvalid");
        AssertContains(result.Status.MissingEvidence, "valid-governed-operation-status-record");
        AssertContains(result.Status.ForbiddenActions, "do not execute from invalid operation status");
    }

    [TestMethod]
    public void OperationStatusRepository_AllowsMatchingTenant()
    {
        var result = Repository(Record(tenantId: 42)).GetByOperationId(OperationId, Scope(42));

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Status);
    }

    [TestMethod]
    public void OperationStatusRepository_RejectsWrongTenant()
    {
        var result = Repository(Record(tenantId: 42)).GetByOperationId(OperationId, Scope(41));

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Status);
        AssertContains(result.Issues, "OperationStatusTenantMismatch");
    }

    [TestMethod]
    public void OperationStatusRepository_RejectsTenantlessTenantScopedRecord()
    {
        var result = Repository(Record(tenantId: null)).GetByOperationId(OperationId, Scope(42));

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Status);
        AssertContains(result.Issues, "TenantScopedOperationStatusRecordTenantRequired");
    }

    [TestMethod]
    public void OperationStatusRepository_RejectsUnscopedTenantRead()
    {
        var result = Repository(Record(tenantId: 42)).GetByOperationId(OperationId, FrontendReadinessReadScope.Unscoped);

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Status);
        AssertContains(result.Issues, "TenantScopedOperationStatusRequiresTenantScope");
    }

    [TestMethod]
    public void FrontendReadiness_OperationStatusUsesCanonicalRepositoryBeforeRunReport()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.Api", "Program.cs"));
        Assert.IsTrue(
            program.IndexOf("OperationStatusFrontendReadinessBackendTruthSource", StringComparison.Ordinal) <
            program.IndexOf("RunReportFrontendReadinessBackendTruthSource", StringComparison.Ordinal),
            "Operation status source must be registered before run reports.");

        var api = Api(
            Repository(Record(status: Status(operationKind: "CanonicalStatus"))),
            new SeededBackendTruthSource(RunReportStatus()));

        var model = api.GetOperationStatus(OperationId);

        Assert.IsNotNull(model);
        Assert.AreEqual("CanonicalStatus", model.OperationKind);
        Assert.AreEqual("Blocked", model.State);
        AssertContains(model.BlockedReasons, "CanonicalStatusBlocked");
    }

    [TestMethod]
    public void FrontendReadiness_OperationStatusDoesNotInventStatusWhenRepositoryMisses()
    {
        var api = Api(Repository());

        Assert.IsNull(api.GetOperationStatus(OperationId));
    }

    [TestMethod]
    public void FrontendReadiness_OperationStatusKeepsReadOnlyBoundary()
    {
        var model = Api().GetOperationStatus(OperationId)!;

        AssertReadOnly(model.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_OperationStatusCompactModeCannotHideMissingEvidence()
    {
        var envelope = OkEnvelope(Controller().GetOperationStatus(OperationId, compact: true));

        AssertContains(envelope.Warnings, "Compact mode was requested but authority-critical fields are still returned.");
        AssertContains(envelope.Data!.MissingEvidence, "canonical-operation-status-record:a02");
    }

    [TestMethod]
    public void FrontendReadiness_OperationStatusCompactModeCannotHideForbiddenActions()
    {
        var envelope = OkEnvelope(Controller().GetOperationStatus(OperationId, compact: true));

        AssertContains(envelope.Data!.ForbiddenActions, "do not execute from operation status");
        AssertContains(envelope.Data.ForbiddenActions, "do not continue workflow from status, receipt, memory, or UI text");
    }

    [TestMethod]
    public void FrontendReadiness_OperationStatusInvalidRecordFailsClosed()
    {
        var invalid = Status() with
        {
            BlockedReasons = [],
            MissingEvidence = [],
            NextSafeActions = []
        };
        var model = Api(Repository(Record(status: invalid))).GetOperationStatus(OperationId);

        Assert.IsNotNull(model);
        Assert.AreEqual("Blocked", model.State);
        AssertContains(model.BlockedReasons, "StoredOperationStatusInvalid");
        AssertContains(model.MissingEvidence, "valid-governed-operation-status-record");
        AssertContains(model.ForbiddenActions, "do not execute from invalid operation status");
    }

    [TestMethod]
    public void StaticScan_A02AddsNoMutationEndpoint()
    {
        var source = A02Source();

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
    public void StaticScan_A02AddsNoExecutorOrProviderMutationPath()
    {
        var source = A02Source();

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
    public void StaticScan_A02DoesNotExposePrivateMaterial()
    {
        var source = A02Source();

        foreach (var marker in new[]
                 {
                     "raw prompt",
                     "rawPrompt",
                     "raw completion",
                     "rawCompletion",
                     "raw tool output",
                     "rawToolOutput",
                     "chain-of-thought",
                     "scratchpad",
                     "bearer token",
                     "api key",
                     "raw patch payload",
                     "full diff"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    private static IGovernedOperationStatusReadRepository Repository(params GovernedOperationStatusReadRecord[] records) =>
        new GovernedOperationStatusReadRepository(records);

    private static IFrontendReadinessReadApi Api(
        IGovernedOperationStatusReadRepository? repository = null,
        IFrontendReadinessBackendTruthSource? fallback = null)
    {
        var sources = new List<IFrontendReadinessBackendTruthSource>
        {
            new OperationStatusFrontendReadinessBackendTruthSource(repository ?? Repository(Record()))
        };

        if (fallback is not null)
            sources.Add(fallback);

        return new BackendFrontendReadinessReadApi(sources, new TestTenantContext(42));
    }

    private static FrontendReadinessController Controller() =>
        new(Api());

    private static GovernedOperationStatusReadRecord Record(
        GovernedOperationStatus? status = null,
        int? tenantId = 42,
        bool isTenantScoped = true,
        string? operationId = null) =>
        new()
        {
            OperationId = operationId ?? OperationId,
            Status = status ?? Status(),
            IsTenantScoped = isTenantScoped,
            TenantId = tenantId
        };

    private static GovernedOperationStatus Status(
        string operationId = OperationId,
        string operationKind = "CanonicalStatus",
        GovernedOperationState state = GovernedOperationState.Blocked) =>
        new()
        {
            OperationId = operationId,
            OperationKind = operationKind,
            Subject = "repo:BigDaddyDread-code/IronDeveloper branch:frontend/operation-status-read-repository run:run-a02",
            State = state,
            BlockedReasons = state == GovernedOperationState.Blocked ? ["CanonicalStatusBlocked"] : [],
            MissingEvidence = state == GovernedOperationState.Blocked ? ["canonical-operation-status-record:a02"] : [],
            NextSafeActions = ["inspect canonical operation status record"],
            ForbiddenActions =
            [
                "do not execute from operation status",
                "do not treat evidence refs as approval",
                "do not treat receipt refs as workflow continuation"
            ],
            EvidenceRefs = [EvidenceRef],
            ReceiptRefs = [ReceiptRef],
            ObservedAtUtc = ObservedAtUtc,
            ExpiresAtUtc = ExpiresAtUtc
        };

    private static GovernedOperationStatus RunReportStatus() =>
        new()
        {
            OperationId = OperationId,
            OperationKind = "RunReportStatus",
            Subject = "run report fallback",
            State = GovernedOperationState.Completed,
            BlockedReasons = [],
            MissingEvidence = [],
            NextSafeActions = ["inspect run report evidence"],
            ForbiddenActions = ["do not treat run report status as approval"],
            EvidenceRefs = ["run-evidence:a02"],
            ReceiptRefs = ["run-report:a02"],
            ObservedAtUtc = ObservedAtUtc.AddMinutes(5),
            ExpiresAtUtc = null
        };

    private static FrontendReadinessReadScope Scope(int tenantId = 42) => new(tenantId);

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

    private static string A02Source()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "GovernedOperationStatusReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "GovernedOperationStatusReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "OperationStatusFrontendReadinessBackendTruthSource.cs")));
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

    private sealed class SeededBackendTruthSource(GovernedOperationStatus status) : FrontendReadinessBackendTruthSource
    {
        public override string SourceName => "seeded-run-report";

        public override GovernedOperationStatus? GetOperationStatus(string operationId) =>
            string.Equals(operationId, status.OperationId, StringComparison.OrdinalIgnoreCase)
                ? status
                : null;
    }

    private sealed class TestTenantContext(int tenantId) : ICurrentTenantContext
    {
        public int TenantId { get; } = tenantId;
    }
}
