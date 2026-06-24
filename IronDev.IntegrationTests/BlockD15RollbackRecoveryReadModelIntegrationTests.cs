using System.Reflection;
using System.Text.RegularExpressions;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockD15RollbackRecoveryReadModelIntegrationTests
{
    private const string TenantId = "tenant-d15";
    private const string ProjectId = "project-d15";
    private const string OperationId = "op_0000000000000015";
    private const string CorrelationId = "corr_5123456789abcdef";
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-24T11:00:00Z");
    private static readonly DateTimeOffset RecordedAtUtc = DateTimeOffset.Parse("2026-06-24T11:01:00Z");

    [TestMethod]
    public void ValidRequestWithNoMaterial_ReturnsNoMaterial()
    {
        var result = Assemble();

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(RollbackRecoveryReadModelStatus.NoMaterial, result.ResolutionStatus);
        Assert.IsNull(result.Assessment);
        AssertContains(result.ForbiddenAuthorityImplications, "rollback/recovery read model is read-only");
    }

    [TestMethod]
    public void InterruptedDiagnosticWithNoRollbackPlan_ReturnsMissingMaterial()
    {
        var result = Assemble([], Diagnostic());

        AssertState(result, RollbackRecoveryReadModelStatus.MissingMaterial, RollbackRecoveryStateKind.RollbackMaterialMissing, RollbackRecoveryGapKind.InterruptedNoRollbackPlan);
        AssertContains(result.Warnings, "rollback material is not rollback authority");
    }

    [TestMethod]
    public void InterruptedDiagnosticWithNoRecoveryPlan_ReturnsMissingMaterial()
    {
        var result = Assemble(
            Chain(
                RollbackRecoveryMaterialKind.RollbackPlan,
                RollbackRecoveryMaterialKind.RollbackEvidence,
                RollbackRecoveryMaterialKind.RollbackReceipt),
            Diagnostic());

        AssertState(result, RollbackRecoveryReadModelStatus.MissingMaterial, RollbackRecoveryStateKind.RecoveryMaterialMissing, RollbackRecoveryGapKind.InterruptedNoRecoveryPlan);
    }

    [DataTestMethod]
    [DataRow(RollbackRecoveryMaterialKind.RollbackPlan, RollbackRecoveryStateKind.RollbackMaterialMissing, RollbackRecoveryGapKind.RollbackPlanNoEvidence)]
    [DataRow(RollbackRecoveryMaterialKind.RollbackEvidence, RollbackRecoveryStateKind.RollbackMaterialMissing, RollbackRecoveryGapKind.RollbackEvidenceNoReceipt)]
    [DataRow(RollbackRecoveryMaterialKind.RecoveryPlan, RollbackRecoveryStateKind.RecoveryMaterialMissing, RollbackRecoveryGapKind.RecoveryPlanNoEvidence)]
    [DataRow(RollbackRecoveryMaterialKind.RecoveryEvidence, RollbackRecoveryStateKind.RecoveryMaterialMissing, RollbackRecoveryGapKind.RecoveryEvidenceNoReceipt)]
    public void MissingMaterialGaps_AreExplained(
        RollbackRecoveryMaterialKind materialKind,
        RollbackRecoveryStateKind expectedState,
        RollbackRecoveryGapKind expectedGap)
    {
        var result = Assemble(ChainFor(materialKind));

        AssertState(result, RollbackRecoveryReadModelStatus.MissingMaterial, expectedState, expectedGap);
    }

    [DataTestMethod]
    [DataRow(RollbackRecoveryMaterialKind.RollbackExecutionObserved, RollbackRecoveryReadModelStatus.Assessed, RollbackRecoveryStateKind.RollbackObserved, RollbackRecoveryGapKind.NoneObserved)]
    [DataRow(RollbackRecoveryMaterialKind.RollbackExecutionFailed, RollbackRecoveryReadModelStatus.FailureObserved, RollbackRecoveryStateKind.RollbackFailed, RollbackRecoveryGapKind.RollbackFailed)]
    [DataRow(RollbackRecoveryMaterialKind.RecoveryExecutionObserved, RollbackRecoveryReadModelStatus.Assessed, RollbackRecoveryStateKind.RecoveryObserved, RollbackRecoveryGapKind.NoneObserved)]
    [DataRow(RollbackRecoveryMaterialKind.RecoveryExecutionFailed, RollbackRecoveryReadModelStatus.FailureObserved, RollbackRecoveryStateKind.RecoveryFailed, RollbackRecoveryGapKind.RecoveryFailed)]
    public void ExecutionObservedAndFailedStates_AreExplained(
        RollbackRecoveryMaterialKind materialKind,
        RollbackRecoveryReadModelStatus expectedStatus,
        RollbackRecoveryStateKind expectedState,
        RollbackRecoveryGapKind expectedGap)
    {
        var result = Assemble(ChainFor(materialKind));

        AssertState(result, expectedStatus, expectedState, expectedGap);
    }

    [TestMethod]
    public void RollbackAndRecoveryObserved_ReturnsCombinedState()
    {
        var result = Assemble(
            Chain(
                RollbackRecoveryMaterialKind.RollbackPlan,
                RollbackRecoveryMaterialKind.RollbackEvidence,
                RollbackRecoveryMaterialKind.RollbackReceipt,
                RollbackRecoveryMaterialKind.RollbackExecutionObserved,
                RollbackRecoveryMaterialKind.RecoveryPlan,
                RollbackRecoveryMaterialKind.RecoveryEvidence,
                RollbackRecoveryMaterialKind.RecoveryReceipt,
                RollbackRecoveryMaterialKind.RecoveryExecutionObserved));

        AssertState(result, RollbackRecoveryReadModelStatus.Assessed, RollbackRecoveryStateKind.RollbackAndRecoveryObserved, RollbackRecoveryGapKind.NoneObserved);
    }

    [TestMethod]
    public void CompleteRollbackMaterial_ReturnsAvailableButNotAuthority()
    {
        var result = Assemble(
            Chain(
                RollbackRecoveryMaterialKind.RollbackPlan,
                RollbackRecoveryMaterialKind.RollbackEvidence,
                RollbackRecoveryMaterialKind.RollbackReceipt));

        AssertState(result, RollbackRecoveryReadModelStatus.Assessed, RollbackRecoveryStateKind.RollbackMaterialAvailable, RollbackRecoveryGapKind.NoneObserved);
        AssertContains(result.ForbiddenAuthorityImplications, "rollback receipt observed is not rollback permission");
    }

    [TestMethod]
    public void CompleteRecoveryMaterial_ReturnsAvailableButNotAuthority()
    {
        var result = Assemble(
            Chain(
                RollbackRecoveryMaterialKind.RecoveryPlan,
                RollbackRecoveryMaterialKind.RecoveryEvidence,
                RollbackRecoveryMaterialKind.RecoveryReceipt));

        AssertState(result, RollbackRecoveryReadModelStatus.Assessed, RollbackRecoveryStateKind.RecoveryMaterialAvailable, RollbackRecoveryGapKind.NoneObserved);
        AssertContains(result.ForbiddenAuthorityImplications, "recovery receipt observed is not recovery permission");
    }

    [TestMethod]
    public void OperatorNoteOnly_ReturnsNoConcernObserved()
    {
        var result = Assemble(Material(RollbackRecoveryMaterialKind.OperatorNote));

        AssertState(result, RollbackRecoveryReadModelStatus.Assessed, RollbackRecoveryStateKind.NoRollbackOrRecoveryObserved, RollbackRecoveryGapKind.NoneObserved);
        AssertContains(result.Warnings, "no missing material is not action allowed");
    }

    [TestMethod]
    public void DuplicateMaterialIds_ReturnAmbiguous()
    {
        var result = Assemble(
            Material(RollbackRecoveryMaterialKind.RollbackPlan, id: "material-dup", appendPosition: 1),
            Material(RollbackRecoveryMaterialKind.RollbackEvidence, id: "material-dup", appendPosition: 2));

        AssertAmbiguous(result, "DuplicateRollbackRecoveryMaterialId:material-dup");
    }

    [TestMethod]
    public void DuplicateAppendPositions_ReturnAmbiguous()
    {
        var result = Assemble(
            Material(RollbackRecoveryMaterialKind.RollbackPlan, id: "material-a", appendPosition: 7),
            Material(RollbackRecoveryMaterialKind.RollbackEvidence, id: "material-b", appendPosition: 7));

        AssertAmbiguous(result, "DuplicateRollbackRecoveryMaterialAppendPosition:7");
    }

    [TestMethod]
    public void ConflictingMaterialMetadata_ReturnsAmbiguous()
    {
        var result = Assemble(
            Material(RollbackRecoveryMaterialKind.RollbackPlan, id: "material-same", appendPosition: 1),
            Material(RollbackRecoveryMaterialKind.RollbackPlan, id: "material-same", appendPosition: 2) with
            {
                SurfaceId = "different-surface"
            });

        AssertAmbiguous(result, "ConflictingRollbackRecoveryMaterialMetadata:material-same");
    }

    [DataTestMethod]
    [DataRow("rollback-observed-failed")]
    [DataRow("recovery-observed-failed")]
    [DataRow("rollback-observed-without-plan")]
    [DataRow("recovery-observed-without-plan")]
    [DataRow("rollback-receipt-without-evidence")]
    [DataRow("recovery-receipt-without-evidence")]
    public void ContradictoryMaterial_ReturnsAmbiguous(string scenario)
    {
        var materials = scenario switch
        {
            "rollback-observed-failed" => Chain(
                RollbackRecoveryMaterialKind.RollbackPlan,
                RollbackRecoveryMaterialKind.RollbackEvidence,
                RollbackRecoveryMaterialKind.RollbackReceipt,
                RollbackRecoveryMaterialKind.RollbackExecutionObserved,
                RollbackRecoveryMaterialKind.RollbackExecutionFailed),
            "recovery-observed-failed" => Chain(
                RollbackRecoveryMaterialKind.RecoveryPlan,
                RollbackRecoveryMaterialKind.RecoveryEvidence,
                RollbackRecoveryMaterialKind.RecoveryReceipt,
                RollbackRecoveryMaterialKind.RecoveryExecutionObserved,
                RollbackRecoveryMaterialKind.RecoveryExecutionFailed),
            "rollback-observed-without-plan" => Chain(RollbackRecoveryMaterialKind.RollbackExecutionObserved),
            "recovery-observed-without-plan" => Chain(RollbackRecoveryMaterialKind.RecoveryExecutionObserved),
            "rollback-receipt-without-evidence" => Chain(RollbackRecoveryMaterialKind.RollbackReceipt),
            "recovery-receipt-without-evidence" => Chain(RollbackRecoveryMaterialKind.RecoveryReceipt),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };

        var result = Assemble(materials);

        Assert.AreEqual(RollbackRecoveryReadModelStatus.AmbiguousMaterial, result.ResolutionStatus);
        Assert.AreEqual(RollbackRecoveryStateKind.Ambiguous, result.Assessment?.StateKind);
        Assert.AreEqual(RollbackRecoveryGapKind.Ambiguous, result.Assessment?.GapKind);
    }

    [TestMethod]
    public void AmbiguityDoesNotChooseWinnerAndSortsDeterministically()
    {
        var result = Assemble(
            Material(RollbackRecoveryMaterialKind.RecoveryExecutionFailed, id: "z-material", appendPosition: 3),
            Material(RollbackRecoveryMaterialKind.RecoveryExecutionObserved, id: "a-material", appendPosition: 2),
            Material(RollbackRecoveryMaterialKind.RecoveryPlan, id: "m-material", appendPosition: 1));

        Assert.AreEqual(RollbackRecoveryReadModelStatus.AmbiguousMaterial, result.ResolutionStatus);
        Assert.AreEqual(RollbackRecoveryMaterialKind.RecoveryExecutionFailed, result.Assessment?.LastMaterialKind);
        CollectionAssert.AreEqual(new[] { "a-material", "m-material", "z-material" }, result.MaterialIds.ToArray());
        CollectionAssert.AreEqual(
            result.AmbiguousMaterial.OrderBy(static item => item, StringComparer.Ordinal).ToArray(),
            result.AmbiguousMaterial.ToArray());
    }

    [TestMethod]
    public void DiagnosticSnapshot_IsCarriedAsMetadataOnly()
    {
        var result = Assemble(
            [.. Chain(RollbackRecoveryMaterialKind.RollbackPlan)],
            Diagnostic());

        AssertState(result, RollbackRecoveryReadModelStatus.MissingMaterial, RollbackRecoveryStateKind.RollbackMaterialMissing, RollbackRecoveryGapKind.RollbackPlanNoEvidence);
        StringAssert.Contains(result.Assessment?.DiagnosticSummary ?? string.Empty, "interruptedStatus=Interrupted");
        StringAssert.Contains(result.Assessment?.DiagnosticSummary ?? string.Empty, "missingEvidence=Complete");
        StringAssert.Contains(result.Assessment?.DiagnosticSummary ?? string.Empty, "forbiddenActions=NoForbiddenFactsObserved");
        AssertContains(result.ForbiddenAuthorityImplications, "rollback/recovery read model is not rollback execution");
        AssertContains(result.ForbiddenAuthorityImplications, "rollback/recovery read model is not workflow continuation");
    }

    [DataTestMethod]
    [DataRow("tenant", "RollbackRecoveryTenantIdRequired")]
    [DataRow("project", "RollbackRecoveryProjectIdRequired")]
    [DataRow("operation", "OperationIdRequired")]
    [DataRow("operation-invalid", "OperationIdMustBeBackendMintedCanonicalId")]
    [DataRow("asof", "RollbackRecoveryAsOfUtcRequired")]
    [DataRow("materials-null", "RollbackRecoveryMaterialsRequired")]
    public void RequestValidation_FailsClosed(string field, string expectedIssue)
    {
        var request = Request([Material(RollbackRecoveryMaterialKind.RollbackPlan)]) with
        {
            TenantId = field == "tenant" ? "" : TenantId,
            ProjectId = field == "project" ? "" : ProjectId,
            OperationId = field == "operation" ? "" : field == "operation-invalid" ? "not canonical" : OperationId,
            AsOfUtc = field == "asof" ? default : AsOfUtc,
            Materials = field == "materials-null" ? null! : [Material(RollbackRecoveryMaterialKind.RollbackPlan)]
        };

        var result = RollbackRecoveryReadModelAssembler.Assemble(request);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(RollbackRecoveryReadModelStatus.InvalidRequest, result.ResolutionStatus);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("material-id", "RollbackRecoveryMaterialIdRequired")]
    [DataRow("material-id-unsafe", "RollbackRecoveryMaterialIdInvalid")]
    [DataRow("kind", "RollbackRecoveryMaterialKindRequired")]
    [DataRow("append", "RollbackRecoveryMaterialAppendPositionInvalid")]
    [DataRow("correlation", "RollbackRecoveryMaterialCorrelationIdInvalid")]
    [DataRow("observed", "RollbackRecoveryMaterialObservedAtRequired")]
    [DataRow("recorded", "RollbackRecoveryMaterialRecordedAtRequired")]
    [DataRow("recorded-before", "RollbackRecoveryMaterialRecordedBeforeObserved")]
    [DataRow("surface-kind", "RollbackRecoveryMaterialSurfaceKindRequired")]
    [DataRow("surface-id", "RollbackRecoveryMaterialSurfaceIdRequired")]
    [DataRow("surface-id-unsafe", "RollbackRecoveryMaterialSurfaceIdInvalid")]
    [DataRow("ref-kind-without-id", "RollbackRecoveryMaterialReferenceIdRequired")]
    [DataRow("ref-id-without-kind", "RollbackRecoveryMaterialReferenceKindRequired")]
    [DataRow("ref-id-unsafe", "RollbackRecoveryMaterialReferenceIdInvalid")]
    [DataRow("source", "RollbackRecoveryMaterialSourceRequired")]
    [DataRow("source-unsafe", "RollbackRecoveryMaterialSourceInvalid")]
    [DataRow("redaction-missing", "RollbackRecoveryMaterialRedactionReasonRequired")]
    [DataRow("redaction-unsafe", "RollbackRecoveryMaterialRedactionReasonInvalid")]
    public void MaterialValidation_FailsClosed(string field, string expectedIssue)
    {
        var material = field switch
        {
            "material-id" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { MaterialId = "" },
            "material-id-unsafe" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { MaterialId = "https://bad" },
            "kind" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { MaterialKind = RollbackRecoveryMaterialKind.Unknown },
            "append" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { AppendPosition = -1 },
            "correlation" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { CorrelationId = "bad-correlation" },
            "observed" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { ObservedAtUtc = default },
            "recorded" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { RecordedAtUtc = default },
            "recorded-before" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { RecordedAtUtc = ObservedAtUtc.AddMinutes(-1) },
            "surface-kind" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { SurfaceKind = OperationCorrelationSurfaceKind.Unknown },
            "surface-id" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { SurfaceId = "" },
            "surface-id-unsafe" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { SurfaceId = "raw patch" },
            "ref-kind-without-id" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { ReferenceKind = OperationReferenceKind.ReceiptId, ReferenceId = null },
            "ref-id-without-kind" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { ReferenceKind = OperationReferenceKind.Unknown, ReferenceId = "ref-123" },
            "ref-id-unsafe" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { ReferenceKind = OperationReferenceKind.ReceiptId, ReferenceId = "raw diff" },
            "source" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { Source = "" },
            "source-unsafe" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { Source = "policy satisfied" },
            "redaction-missing" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { IsRedacted = true, RedactionReason = null },
            "redaction-unsafe" => Material(RollbackRecoveryMaterialKind.RollbackPlan) with { IsRedacted = true, RedactionReason = "secret token" },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
        };

        var result = Assemble(material);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("tenant", "RollbackRecoveryMaterialTenantMismatch")]
    [DataRow("project", "RollbackRecoveryMaterialProjectMismatch")]
    [DataRow("operation", "RollbackRecoveryMaterialOperationMismatch")]
    public void CrossScopeMaterial_FailsClosed(string field, string expectedIssue)
    {
        var material = Material(RollbackRecoveryMaterialKind.RollbackPlan) with
        {
            TenantId = field == "tenant" ? "other-tenant" : TenantId,
            ProjectId = field == "project" ? "other-project" : ProjectId,
            OperationId = field == "operation" ? "op_0000000000000099" : OperationId
        };

        var result = Assemble(material);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("tenant", "RollbackRecoveryDiagnosticTenantMismatch")]
    [DataRow("project", "RollbackRecoveryDiagnosticProjectMismatch")]
    [DataRow("operation", "RollbackRecoveryDiagnosticOperationMismatch")]
    public void CrossScopeDiagnosticSnapshot_FailsClosed(string field, string expectedIssue)
    {
        var snapshot = Diagnostic() with
        {
            TenantId = field == "tenant" ? "other-tenant" : TenantId,
            ProjectId = field == "project" ? "other-project" : ProjectId,
            OperationId = field == "operation" ? "op_0000000000000099" : OperationId
        };

        var result = Assemble([Material(RollbackRecoveryMaterialKind.RollbackPlan)], snapshot);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void ReadModelExposesNoAuthorityShapedProperties()
    {
        var propertyNames = typeof(RollbackRecoveryReadModel)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Concat(typeof(RollbackRecoveryAssessment).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(static property => property.Name)
            .ToArray();

        var forbidden = new[]
        {
            "CanApply",
            "CanCommit",
            "CanPush",
            "CanCreatePullRequest",
            "CanMerge",
            "CanRelease",
            "CanDeploy",
            "CanRollback",
            "CanRecover",
            "CanRetry",
            "CanResume",
            "CanContinue",
            "ApprovalStatus",
            "PolicySatisfied",
            "NextSafeAction",
            "AuthorityGranted",
            "ActionAllowed",
            "RollbackAllowed",
            "RecoveryAllowed",
            "RetryAllowed",
            "ResumeAllowed",
            "RecoveryReady",
            "RawPatch",
            "RawDiff",
            "RawSourceContent",
            "RawValidationLog",
            "RawEvidencePayload",
            "RawReceiptPayload"
        };

        foreach (var name in forbidden)
        {
            CollectionAssert.DoesNotContain(propertyNames, name);
        }
    }

    [TestMethod]
    public void StaticScan_D15CoreAddsNoMutationOrUpstreamResolverSurface()
    {
        var source = D15CoreSourceWithoutStrings();
        var forbiddenMarkers = new[]
        {
            "Controller",
            "MapGet",
            "MapPost",
            "DbContext",
            "SqlConnection",
            "File.ReadAllText",
            "File.ReadAllBytes",
            "Directory.",
            "Process.Start",
            "ProcessStartInfo",
            "DateTimeOffset.UtcNow",
            "DateTime.UtcNow",
            "LibGit2Sharp",
            "MissingEvidenceResolver.",
            "ForbiddenActionResolver.",
            "ReceiptReferenceResolver.",
            "EvidenceResolver.",
            "ValidationStalenessResolver.",
            "PatchBaseFreshnessResolver.",
            "WorktreeBaseHeadFreshnessReadModelAssembler.",
            "InterruptedRunReadModelAssembler.",
            "GovernedOperationTimelineAssembler.",
            "AppendOnlyEventToStatusProjection.",
            "RunProcessAsync",
            "ExecuteAsync",
            "ApplyAsync",
            "CommitAsync",
            "PushAsync",
            "CreatePullRequestAsync",
            "MergeAsync",
            "ReleaseAsync",
            "DeployAsync",
            "RollbackAsync",
            "RecoverAsync",
            "ContinueWorkflow"
        };

        foreach (var marker in forbiddenMarkers)
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), marker);
        }
    }

    [TestMethod]
    public void ReceiptRecordsRollbackRecoveryBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "Docs",
            "receipts",
            "D15_ROLLBACK_RECOVERY_READ_MODEL.md"));

        StringAssert.Contains(receipt, "The rollback/recovery read model explains supplied rollback and recovery material metadata only.");
        StringAssert.Contains(receipt, "Rollback plan observed is not rollback authority.");
        StringAssert.Contains(receipt, "Recovery plan observed is not recovery authority.");
    }

    private static RollbackRecoveryReadModel Assemble(params RollbackRecoveryMaterialObservation[] materials) =>
        RollbackRecoveryReadModelAssembler.Assemble(Request(materials));

    private static RollbackRecoveryReadModel Assemble(
        IReadOnlyList<RollbackRecoveryMaterialObservation> materials,
        RollbackRecoveryDiagnosticSnapshot? snapshot) =>
        RollbackRecoveryReadModelAssembler.Assemble(Request(materials, snapshot));

    private static RollbackRecoveryReadModelRequest Request(
        IReadOnlyList<RollbackRecoveryMaterialObservation> materials,
        RollbackRecoveryDiagnosticSnapshot? snapshot = null) =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            AsOfUtc = AsOfUtc,
            Materials = materials,
            DiagnosticSnapshot = snapshot
        };

    private static RollbackRecoveryMaterialObservation[] ChainFor(RollbackRecoveryMaterialKind materialKind) =>
        materialKind switch
        {
            RollbackRecoveryMaterialKind.RollbackPlan => Chain(RollbackRecoveryMaterialKind.RollbackPlan),
            RollbackRecoveryMaterialKind.RollbackEvidence => Chain(RollbackRecoveryMaterialKind.RollbackPlan, RollbackRecoveryMaterialKind.RollbackEvidence),
            RollbackRecoveryMaterialKind.RecoveryPlan => Chain(RollbackRecoveryMaterialKind.RecoveryPlan),
            RollbackRecoveryMaterialKind.RecoveryEvidence => Chain(RollbackRecoveryMaterialKind.RecoveryPlan, RollbackRecoveryMaterialKind.RecoveryEvidence),
            RollbackRecoveryMaterialKind.RollbackExecutionObserved => Chain(RollbackRecoveryMaterialKind.RollbackPlan, RollbackRecoveryMaterialKind.RollbackEvidence, RollbackRecoveryMaterialKind.RollbackReceipt, RollbackRecoveryMaterialKind.RollbackExecutionObserved),
            RollbackRecoveryMaterialKind.RollbackExecutionFailed => Chain(RollbackRecoveryMaterialKind.RollbackPlan, RollbackRecoveryMaterialKind.RollbackEvidence, RollbackRecoveryMaterialKind.RollbackReceipt, RollbackRecoveryMaterialKind.RollbackExecutionFailed),
            RollbackRecoveryMaterialKind.RecoveryExecutionObserved => Chain(RollbackRecoveryMaterialKind.RecoveryPlan, RollbackRecoveryMaterialKind.RecoveryEvidence, RollbackRecoveryMaterialKind.RecoveryReceipt, RollbackRecoveryMaterialKind.RecoveryExecutionObserved),
            RollbackRecoveryMaterialKind.RecoveryExecutionFailed => Chain(RollbackRecoveryMaterialKind.RecoveryPlan, RollbackRecoveryMaterialKind.RecoveryEvidence, RollbackRecoveryMaterialKind.RecoveryReceipt, RollbackRecoveryMaterialKind.RecoveryExecutionFailed),
            _ => [Material(materialKind)]
        };

    private static RollbackRecoveryMaterialObservation[] Chain(params RollbackRecoveryMaterialKind[] kinds) =>
        kinds.Select((kind, index) => Material(kind, appendPosition: index + 1)).ToArray();

    private static RollbackRecoveryMaterialObservation Material(
        RollbackRecoveryMaterialKind kind,
        string? id = null,
        long appendPosition = 1) =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            MaterialId = id ?? $"material-{appendPosition:D2}-{kind}",
            MaterialKind = kind,
            AppendPosition = appendPosition,
            ObservedAtUtc = ObservedAtUtc.AddMinutes(appendPosition),
            RecordedAtUtc = RecordedAtUtc.AddMinutes(appendPosition),
            SurfaceKind = OperationCorrelationSurfaceKind.TimelineEvent,
            SurfaceId = $"timeline-{appendPosition:D2}",
            ReferenceKind = OperationReferenceKind.ReceiptId,
            ReferenceId = $"receipt-ref-{appendPosition:D2}",
            Source = "d15-test",
            IsRedacted = false,
            RedactionReason = null
        };

    private static RollbackRecoveryDiagnosticSnapshot Diagnostic() =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            InterruptedRunStatus = InterruptedRunReadModelStatus.Interrupted,
            InterruptedRunState = InterruptedRunStateKind.Interrupted,
            InterruptedRunGap = InterruptedRunGapKind.ApplyStartedNotCompleted,
            ProjectedStatusKind = GovernedOperationState.Blocked,
            MissingEvidenceStatus = MissingEvidenceResolutionStatus.Complete,
            ForbiddenActionStatus = ForbiddenActionResolutionStatus.NoForbiddenFactsObserved,
            ReceiptResolutionStatus = ReceiptReferenceResolutionStatus.Resolved,
            EvidenceResolutionStatus = EvidenceResolutionStatus.Resolved,
            ValidationStalenessStatus = ValidationStalenessResolutionStatus.Assessed,
            PatchBaseFreshnessStatus = PatchBaseFreshnessResolutionStatus.Assessed,
            WorktreeBaseHeadFreshnessStatus = WorktreeBaseHeadFreshnessResolutionStatus.Assessed,
            Source = "d15-test",
            RecordedAtUtc = RecordedAtUtc
        };

    private static void AssertState(
        RollbackRecoveryReadModel result,
        RollbackRecoveryReadModelStatus expectedStatus,
        RollbackRecoveryStateKind expectedState,
        RollbackRecoveryGapKind expectedGap)
    {
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(expectedStatus, result.ResolutionStatus);
        Assert.IsNotNull(result.Assessment);
        Assert.AreEqual(expectedState, result.Assessment.StateKind);
        Assert.AreEqual(expectedGap, result.Assessment.GapKind);
    }

    private static void AssertAmbiguous(RollbackRecoveryReadModel result, string expectedAmbiguity)
    {
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(RollbackRecoveryReadModelStatus.AmbiguousMaterial, result.ResolutionStatus);
        Assert.AreEqual(RollbackRecoveryStateKind.Ambiguous, result.Assessment?.StateKind);
        AssertContains(result.AmbiguousMaterial, expectedAmbiguity);
    }

    private static void AssertContains(IEnumerable<string> values, string expected)
    {
        if (!values.Contains(expected, StringComparer.Ordinal))
        {
            Assert.Fail($"Expected '{expected}' in: {string.Join(", ", values)}");
        }
    }

    private static string D15CoreSourceWithoutStrings()
    {
        var root = RepoRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackRecoveryReadModelModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackRecoveryReadModelValidator.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackRecoveryReadModelAssembler.cs")
        };

        return StripStrings(string.Join(Environment.NewLine, files.Select(File.ReadAllText)));
    }

    private static string StripStrings(string source)
    {
        source = Regex.Replace(source, "@\"(?:[^\"]|\"\")*\"", "\"\"", RegexOptions.Singleline);
        source = Regex.Replace(source, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");
        return source;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repo root.");
    }
}
