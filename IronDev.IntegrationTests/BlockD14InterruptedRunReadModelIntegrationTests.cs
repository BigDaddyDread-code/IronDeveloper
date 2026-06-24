using System.Reflection;
using System.Text.RegularExpressions;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockD14InterruptedRunReadModelIntegrationTests
{
    private const string TenantId = "tenant-d14";
    private const string ProjectId = "project-d14";
    private const string OperationId = "op_0000000000000014";
    private const string CorrelationId = "corr_4123456789abcdef";
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-24T11:00:00Z");
    private static readonly DateTimeOffset RecordedAtUtc = DateTimeOffset.Parse("2026-06-24T11:01:00Z");

    [TestMethod]
    public void ValidRequestWithNoCheckpoints_ReturnsNoCheckpoints()
    {
        var result = Assemble();

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(InterruptedRunReadModelStatus.NoCheckpoints, result.ResolutionStatus);
        Assert.IsNull(result.Assessment);
        AssertContains(result.ForbiddenAuthorityImplications, "interrupted-run read model is read-only");
    }

    [TestMethod]
    public void CompletedCheckpoint_ReturnsNoInterruptionObserved()
    {
        var result = Assemble(Checkpoint(InterruptedRunCheckpointKind.Completed));

        AssertState(result, InterruptedRunReadModelStatus.NoInterruptionObserved, InterruptedRunStateKind.NoInterruptionObserved, InterruptedRunGapKind.NoneObserved);
        AssertContains(result.Warnings, "no interruption observed is not action allowed");
    }

    [DataTestMethod]
    [DataRow(InterruptedRunCheckpointKind.WorkspaceCreated, InterruptedRunGapKind.WorkspaceCreatedNoPatch)]
    [DataRow(InterruptedRunCheckpointKind.PatchArtifactCreated, InterruptedRunGapKind.PatchCreatedNoValidation)]
    [DataRow(InterruptedRunCheckpointKind.ValidationFailed, InterruptedRunGapKind.ValidationFailed)]
    [DataRow(InterruptedRunCheckpointKind.SourceApplyStarted, InterruptedRunGapKind.ApplyStartedNotCompleted)]
    [DataRow(InterruptedRunCheckpointKind.CommitPackageCreated, InterruptedRunGapKind.CommitPackageCreatedNoCommit)]
    [DataRow(InterruptedRunCheckpointKind.CommitCreated, InterruptedRunGapKind.CommitCreatedNoPush)]
    [DataRow(InterruptedRunCheckpointKind.PushCompleted, InterruptedRunGapKind.PushCompletedNoPullRequest)]
    public void InterruptedCheckpointGaps_AreExplained(
        InterruptedRunCheckpointKind checkpointKind,
        InterruptedRunGapKind expectedGap)
    {
        var checkpoints = ChainFor(checkpointKind);
        var result = Assemble(checkpoints);

        AssertState(result, InterruptedRunReadModelStatus.Interrupted, InterruptedRunStateKind.Interrupted, expectedGap);
    }

    [TestMethod]
    public void ExplicitFailedCheckpoint_ReturnsFailed()
    {
        var result = Assemble(Checkpoint(InterruptedRunCheckpointKind.Failed));

        AssertState(result, InterruptedRunReadModelStatus.Failed, InterruptedRunStateKind.Failed, InterruptedRunGapKind.Failed);
        AssertContains(result.ForbiddenAuthorityImplications, "failed state is not recovery permission");
    }

    [TestMethod]
    public void ExplicitCancelledCheckpoint_ReturnsCancelled()
    {
        var result = Assemble(Checkpoint(InterruptedRunCheckpointKind.Cancelled));

        AssertState(result, InterruptedRunReadModelStatus.Cancelled, InterruptedRunStateKind.Cancelled, InterruptedRunGapKind.Cancelled);
        AssertContains(result.ForbiddenAuthorityImplications, "cancelled state is not resume permission");
    }

    [TestMethod]
    public void SourceApplyCompletedPlusCommitPackageNoCommit_UsesLaterGap()
    {
        var result = Assemble(
            Chain(
                InterruptedRunCheckpointKind.WorkspaceCreated,
                InterruptedRunCheckpointKind.PatchArtifactCreated,
                InterruptedRunCheckpointKind.ValidationPassed,
                InterruptedRunCheckpointKind.SourceApplyStarted,
                InterruptedRunCheckpointKind.SourceApplyCompleted,
                InterruptedRunCheckpointKind.CommitPackageCreated));

        AssertState(result, InterruptedRunReadModelStatus.Interrupted, InterruptedRunStateKind.Interrupted, InterruptedRunGapKind.CommitPackageCreatedNoCommit);
    }

    [TestMethod]
    public void DuplicateCheckpointIds_ReturnAmbiguous()
    {
        var result = Assemble(
            Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated, id: "checkpoint-dup", appendPosition: 1),
            Checkpoint(InterruptedRunCheckpointKind.PatchArtifactCreated, id: "checkpoint-dup", appendPosition: 2));

        AssertAmbiguous(result, "DuplicateInterruptedRunCheckpointId:checkpoint-dup");
    }

    [TestMethod]
    public void DuplicateAppendPositions_ReturnAmbiguous()
    {
        var result = Assemble(
            Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated, id: "checkpoint-a", appendPosition: 7),
            Checkpoint(InterruptedRunCheckpointKind.PatchArtifactCreated, id: "checkpoint-b", appendPosition: 7));

        AssertAmbiguous(result, "DuplicateInterruptedRunCheckpointAppendPosition:7");
    }

    [TestMethod]
    public void ConflictingCheckpointMetadata_ReturnsAmbiguous()
    {
        var result = Assemble(
            Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated, id: "checkpoint-same", appendPosition: 1),
            Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated, id: "checkpoint-same", appendPosition: 2) with
            {
                SurfaceId = "different-surface"
            });

        AssertAmbiguous(result, "ConflictingInterruptedRunCheckpointMetadata:checkpoint-same");
    }

    [DataTestMethod]
    [DataRow("completed-failed")]
    [DataRow("completed-incomplete-apply")]
    [DataRow("validation-failed-downstream")]
    [DataRow("commit-without-package")]
    [DataRow("push-without-commit")]
    [DataRow("pr-without-push")]
    public void ContradictoryCheckpointCombinations_ReturnAmbiguous(string scenario)
    {
        var checkpoints = scenario switch
        {
            "completed-failed" => Chain(InterruptedRunCheckpointKind.Completed, InterruptedRunCheckpointKind.Failed),
            "completed-incomplete-apply" => Chain(InterruptedRunCheckpointKind.SourceApplyStarted, InterruptedRunCheckpointKind.Completed),
            "validation-failed-downstream" => Chain(InterruptedRunCheckpointKind.ValidationFailed, InterruptedRunCheckpointKind.SourceApplyStarted),
            "commit-without-package" => Chain(InterruptedRunCheckpointKind.CommitCreated),
            "push-without-commit" => Chain(InterruptedRunCheckpointKind.PushCompleted),
            "pr-without-push" => Chain(InterruptedRunCheckpointKind.PullRequestCreated),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };

        var result = Assemble(checkpoints);

        Assert.AreEqual(InterruptedRunReadModelStatus.AmbiguousCheckpoints, result.ResolutionStatus);
        Assert.AreEqual(InterruptedRunStateKind.Ambiguous, result.Assessment?.StateKind);
        Assert.AreEqual(InterruptedRunGapKind.Ambiguous, result.Assessment?.GapKind);
    }

    [TestMethod]
    public void AmbiguityDoesNotChooseWinnerAndSortsDeterministically()
    {
        var result = Assemble(
            Checkpoint(InterruptedRunCheckpointKind.Failed, id: "z-terminal", appendPosition: 2),
            Checkpoint(InterruptedRunCheckpointKind.Completed, id: "a-terminal", appendPosition: 1),
            Checkpoint(InterruptedRunCheckpointKind.Cancelled, id: "m-terminal", appendPosition: 3));

        Assert.AreEqual(InterruptedRunReadModelStatus.AmbiguousCheckpoints, result.ResolutionStatus);
        Assert.AreEqual(InterruptedRunCheckpointKind.Cancelled, result.Assessment?.LastCheckpointKind);
        CollectionAssert.AreEqual(new[] { "a-terminal", "m-terminal", "z-terminal" }, result.CheckpointIds.ToArray());
        CollectionAssert.AreEqual(
            result.AmbiguousCheckpoints.OrderBy(static item => item, StringComparer.Ordinal).ToArray(),
            result.AmbiguousCheckpoints.ToArray());
    }

    [TestMethod]
    public void DiagnosticSnapshot_IsCarriedAsMetadataOnly()
    {
        var result = Assemble(
            [.. ChainFor(InterruptedRunCheckpointKind.CommitCreated)],
            Diagnostic());

        AssertState(result, InterruptedRunReadModelStatus.Interrupted, InterruptedRunStateKind.Interrupted, InterruptedRunGapKind.CommitCreatedNoPush);
        StringAssert.Contains(result.Assessment?.DiagnosticSummary ?? string.Empty, "missingEvidence=Complete");
        StringAssert.Contains(result.Assessment?.DiagnosticSummary ?? string.Empty, "forbiddenActions=NoForbiddenFactsObserved");
        AssertContains(result.Warnings, "interrupted state is not retry permission");
        AssertContains(result.ForbiddenAuthorityImplications, "interrupted-run read model is not source apply");
        AssertContains(result.ForbiddenAuthorityImplications, "interrupted-run read model is not workflow continuation");
    }

    [DataTestMethod]
    [DataRow("tenant", "InterruptedRunTenantIdRequired")]
    [DataRow("project", "InterruptedRunProjectIdRequired")]
    [DataRow("operation", "OperationIdRequired")]
    [DataRow("operation-invalid", "OperationIdMustBeBackendMintedCanonicalId")]
    [DataRow("asof", "InterruptedRunAsOfUtcRequired")]
    [DataRow("checkpoints-null", "InterruptedRunCheckpointsRequired")]
    public void RequestValidation_FailsClosed(string field, string expectedIssue)
    {
        var request = Request([Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated)]) with
        {
            TenantId = field == "tenant" ? "" : TenantId,
            ProjectId = field == "project" ? "" : ProjectId,
            OperationId = field == "operation" ? "" : field == "operation-invalid" ? "not canonical" : OperationId,
            AsOfUtc = field == "asof" ? default : AsOfUtc,
            Checkpoints = field == "checkpoints-null" ? null! : [Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated)]
        };

        var result = InterruptedRunReadModelAssembler.Assemble(request);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(InterruptedRunReadModelStatus.InvalidRequest, result.ResolutionStatus);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("checkpoint-id", "InterruptedRunCheckpointIdRequired")]
    [DataRow("checkpoint-id-unsafe", "InterruptedRunCheckpointIdInvalid")]
    [DataRow("kind", "InterruptedRunCheckpointKindRequired")]
    [DataRow("append", "InterruptedRunCheckpointAppendPositionInvalid")]
    [DataRow("correlation", "InterruptedRunCheckpointCorrelationIdInvalid")]
    [DataRow("observed", "InterruptedRunCheckpointObservedAtRequired")]
    [DataRow("recorded", "InterruptedRunCheckpointRecordedAtRequired")]
    [DataRow("recorded-before", "InterruptedRunCheckpointRecordedBeforeObserved")]
    [DataRow("surface-kind", "InterruptedRunCheckpointSurfaceKindRequired")]
    [DataRow("surface-id", "InterruptedRunCheckpointSurfaceIdRequired")]
    [DataRow("surface-id-unsafe", "InterruptedRunCheckpointSurfaceIdInvalid")]
    [DataRow("ref-kind-without-id", "InterruptedRunCheckpointReferenceIdRequired")]
    [DataRow("ref-id-without-kind", "InterruptedRunCheckpointReferenceKindRequired")]
    [DataRow("ref-id-unsafe", "InterruptedRunCheckpointReferenceIdInvalid")]
    [DataRow("source", "InterruptedRunCheckpointSourceRequired")]
    [DataRow("source-unsafe", "InterruptedRunCheckpointSourceInvalid")]
    [DataRow("redaction-missing", "InterruptedRunCheckpointRedactionReasonRequired")]
    [DataRow("redaction-unsafe", "InterruptedRunCheckpointRedactionReasonInvalid")]
    public void CheckpointValidation_FailsClosed(string field, string expectedIssue)
    {
        var checkpoint = field switch
        {
            "checkpoint-id" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { CheckpointId = "" },
            "checkpoint-id-unsafe" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { CheckpointId = "https://bad" },
            "kind" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { CheckpointKind = InterruptedRunCheckpointKind.Unknown },
            "append" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { AppendPosition = -1 },
            "correlation" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { CorrelationId = "bad-correlation" },
            "observed" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { ObservedAtUtc = default },
            "recorded" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { RecordedAtUtc = default },
            "recorded-before" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { RecordedAtUtc = ObservedAtUtc.AddMinutes(-1) },
            "surface-kind" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { SurfaceKind = OperationCorrelationSurfaceKind.Unknown },
            "surface-id" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { SurfaceId = "" },
            "surface-id-unsafe" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { SurfaceId = "raw patch" },
            "ref-kind-without-id" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { ReferenceKind = OperationReferenceKind.RunId, ReferenceId = null },
            "ref-id-without-kind" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { ReferenceKind = OperationReferenceKind.Unknown, ReferenceId = "ref-123" },
            "ref-id-unsafe" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { ReferenceKind = OperationReferenceKind.RunId, ReferenceId = "raw diff" },
            "source" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { Source = "" },
            "source-unsafe" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { Source = "policy satisfied" },
            "redaction-missing" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { IsRedacted = true, RedactionReason = null },
            "redaction-unsafe" => Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with { IsRedacted = true, RedactionReason = "secret token" },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
        };

        var result = Assemble(checkpoint);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("tenant", "InterruptedRunCheckpointTenantMismatch")]
    [DataRow("project", "InterruptedRunCheckpointProjectMismatch")]
    [DataRow("operation", "InterruptedRunCheckpointOperationMismatch")]
    public void CrossScopeCheckpoint_FailsClosed(string field, string expectedIssue)
    {
        var checkpoint = Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated) with
        {
            TenantId = field == "tenant" ? "other-tenant" : TenantId,
            ProjectId = field == "project" ? "other-project" : ProjectId,
            OperationId = field == "operation" ? "op_0000000000000099" : OperationId
        };

        var result = Assemble(checkpoint);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("tenant", "InterruptedRunDiagnosticTenantMismatch")]
    [DataRow("project", "InterruptedRunDiagnosticProjectMismatch")]
    [DataRow("operation", "InterruptedRunDiagnosticOperationMismatch")]
    public void CrossScopeDiagnosticSnapshot_FailsClosed(string field, string expectedIssue)
    {
        var snapshot = Diagnostic() with
        {
            TenantId = field == "tenant" ? "other-tenant" : TenantId,
            ProjectId = field == "project" ? "other-project" : ProjectId,
            OperationId = field == "operation" ? "op_0000000000000099" : OperationId
        };

        var result = Assemble([Checkpoint(InterruptedRunCheckpointKind.WorkspaceCreated)], snapshot);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void ReadModelExposesNoAuthorityShapedProperties()
    {
        var propertyNames = typeof(InterruptedRunReadModel)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Concat(typeof(InterruptedRunAssessment).GetProperties(BindingFlags.Instance | BindingFlags.Public))
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
            "CanRetry",
            "CanResume",
            "CanRecover",
            "CanContinue",
            "ApprovalStatus",
            "PolicySatisfied",
            "NextSafeAction",
            "AuthorityGranted",
            "ActionAllowed",
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
    public void StaticScan_D14CoreAddsNoMutationOrUpstreamResolverSurface()
    {
        var source = D14CoreSourceWithoutStrings();
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
            "ContinueWorkflow"
        };

        foreach (var marker in forbiddenMarkers)
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), marker);
        }
    }

    [TestMethod]
    public void ReceiptRecordsInterruptedRunBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "Docs",
            "receipts",
            "D14_INTERRUPTED_RUN_READ_MODEL.md"));

        StringAssert.Contains(receipt, "The interrupted-run read model explains where a governed operation appears to have stopped using supplied checkpoint metadata and supplied diagnostic summaries only.");
        StringAssert.Contains(receipt, "Interrupted state is not retry permission.");
        StringAssert.Contains(receipt, "No interruption observed is not action allowed.");
    }

    private static InterruptedRunReadModel Assemble(params InterruptedRunCheckpointObservation[] checkpoints) =>
        InterruptedRunReadModelAssembler.Assemble(Request(checkpoints));

    private static InterruptedRunReadModel Assemble(
        IReadOnlyList<InterruptedRunCheckpointObservation> checkpoints,
        InterruptedRunDiagnosticSnapshot? snapshot) =>
        InterruptedRunReadModelAssembler.Assemble(Request(checkpoints, snapshot));

    private static InterruptedRunReadModelRequest Request(
        IReadOnlyList<InterruptedRunCheckpointObservation> checkpoints,
        InterruptedRunDiagnosticSnapshot? snapshot = null) =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            AsOfUtc = AsOfUtc,
            Checkpoints = checkpoints,
            DiagnosticSnapshot = snapshot
        };

    private static InterruptedRunCheckpointObservation[] ChainFor(InterruptedRunCheckpointKind checkpointKind) =>
        checkpointKind switch
        {
            InterruptedRunCheckpointKind.WorkspaceCreated => Chain(InterruptedRunCheckpointKind.WorkspaceCreated),
            InterruptedRunCheckpointKind.PatchArtifactCreated => Chain(InterruptedRunCheckpointKind.WorkspaceCreated, InterruptedRunCheckpointKind.PatchArtifactCreated),
            InterruptedRunCheckpointKind.ValidationFailed => Chain(InterruptedRunCheckpointKind.WorkspaceCreated, InterruptedRunCheckpointKind.PatchArtifactCreated, InterruptedRunCheckpointKind.ValidationFailed),
            InterruptedRunCheckpointKind.SourceApplyStarted => Chain(InterruptedRunCheckpointKind.WorkspaceCreated, InterruptedRunCheckpointKind.PatchArtifactCreated, InterruptedRunCheckpointKind.ValidationPassed, InterruptedRunCheckpointKind.SourceApplyStarted),
            InterruptedRunCheckpointKind.CommitPackageCreated => Chain(InterruptedRunCheckpointKind.WorkspaceCreated, InterruptedRunCheckpointKind.PatchArtifactCreated, InterruptedRunCheckpointKind.ValidationPassed, InterruptedRunCheckpointKind.SourceApplyStarted, InterruptedRunCheckpointKind.SourceApplyCompleted, InterruptedRunCheckpointKind.CommitPackageCreated),
            InterruptedRunCheckpointKind.CommitCreated => Chain(InterruptedRunCheckpointKind.WorkspaceCreated, InterruptedRunCheckpointKind.PatchArtifactCreated, InterruptedRunCheckpointKind.ValidationPassed, InterruptedRunCheckpointKind.SourceApplyStarted, InterruptedRunCheckpointKind.SourceApplyCompleted, InterruptedRunCheckpointKind.CommitPackageCreated, InterruptedRunCheckpointKind.CommitCreated),
            InterruptedRunCheckpointKind.PushCompleted => Chain(InterruptedRunCheckpointKind.WorkspaceCreated, InterruptedRunCheckpointKind.PatchArtifactCreated, InterruptedRunCheckpointKind.ValidationPassed, InterruptedRunCheckpointKind.SourceApplyStarted, InterruptedRunCheckpointKind.SourceApplyCompleted, InterruptedRunCheckpointKind.CommitPackageCreated, InterruptedRunCheckpointKind.CommitCreated, InterruptedRunCheckpointKind.PushCompleted),
            _ => [Checkpoint(checkpointKind)]
        };

    private static InterruptedRunCheckpointObservation[] Chain(params InterruptedRunCheckpointKind[] kinds) =>
        kinds.Select((kind, index) => Checkpoint(kind, appendPosition: index + 1)).ToArray();

    private static InterruptedRunCheckpointObservation Checkpoint(
        InterruptedRunCheckpointKind kind,
        string? id = null,
        long appendPosition = 1) =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            CheckpointId = id ?? $"checkpoint-{appendPosition:D2}-{kind}",
            CheckpointKind = kind,
            AppendPosition = appendPosition,
            ObservedAtUtc = ObservedAtUtc.AddMinutes(appendPosition),
            RecordedAtUtc = RecordedAtUtc.AddMinutes(appendPosition),
            SurfaceKind = OperationCorrelationSurfaceKind.TimelineEvent,
            SurfaceId = $"timeline-{appendPosition:D2}",
            ReferenceKind = OperationReferenceKind.TimelineEventId,
            ReferenceId = $"timeline-ref-{appendPosition:D2}",
            Source = "d14-test",
            IsRedacted = false,
            RedactionReason = null
        };

    private static InterruptedRunDiagnosticSnapshot Diagnostic() =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            ProjectedStatusKind = GovernedOperationState.Blocked,
            MissingEvidenceStatus = MissingEvidenceResolutionStatus.Complete,
            ForbiddenActionStatus = ForbiddenActionResolutionStatus.NoForbiddenFactsObserved,
            ReceiptResolutionStatus = ReceiptReferenceResolutionStatus.Resolved,
            EvidenceResolutionStatus = EvidenceResolutionStatus.Resolved,
            ValidationStalenessStatus = ValidationStalenessResolutionStatus.Assessed,
            PatchBaseFreshnessStatus = PatchBaseFreshnessResolutionStatus.Assessed,
            WorktreeBaseHeadFreshnessStatus = WorktreeBaseHeadFreshnessResolutionStatus.Assessed,
            Source = "d14-test",
            RecordedAtUtc = RecordedAtUtc
        };

    private static void AssertState(
        InterruptedRunReadModel result,
        InterruptedRunReadModelStatus expectedStatus,
        InterruptedRunStateKind expectedState,
        InterruptedRunGapKind expectedGap)
    {
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(expectedStatus, result.ResolutionStatus);
        Assert.IsNotNull(result.Assessment);
        Assert.AreEqual(expectedState, result.Assessment.StateKind);
        Assert.AreEqual(expectedGap, result.Assessment.GapKind);
    }

    private static void AssertAmbiguous(InterruptedRunReadModel result, string expectedAmbiguity)
    {
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(InterruptedRunReadModelStatus.AmbiguousCheckpoints, result.ResolutionStatus);
        Assert.AreEqual(InterruptedRunStateKind.Ambiguous, result.Assessment?.StateKind);
        AssertContains(result.AmbiguousCheckpoints, expectedAmbiguity);
    }

    private static void AssertContains(IEnumerable<string> values, string expected)
    {
        if (!values.Contains(expected, StringComparer.Ordinal))
        {
            Assert.Fail($"Expected '{expected}' in: {string.Join(", ", values)}");
        }
    }

    private static string D14CoreSourceWithoutStrings()
    {
        var root = RepoRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "InterruptedRunReadModelModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "InterruptedRunReadModelValidator.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "InterruptedRunReadModelAssembler.cs")
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
