using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBJCanonicalGovernedOperationStatusTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public void BlockBJ_Model_RepresentsAllCanonicalStates()
    {
        var states = Enum.GetValues<GovernedOperationState>();

        CollectionAssert.AreEquivalent(
            new[]
            {
                GovernedOperationState.Eligible,
                GovernedOperationState.Blocked,
                GovernedOperationState.Running,
                GovernedOperationState.Completed,
                GovernedOperationState.Failed,
                GovernedOperationState.Expired
            },
            states);
    }

    [TestMethod]
    public void BlockBJ_Model_SerializesCanonicalStatusShape()
    {
        var json = JsonSerializer.Serialize(BlockedStatus(), JsonOptions);

        StringAssert.Contains(json, "\"operationId\"");
        StringAssert.Contains(json, "\"operationKind\"");
        StringAssert.Contains(json, "\"blockedReasons\"");
        StringAssert.Contains(json, "\"missingEvidence\"");
        StringAssert.Contains(json, "\"nextSafeActions\"");
        StringAssert.Contains(json, "\"forbiddenActions\"");
        StringAssert.Contains(json, "\"evidenceRefs\"");
        StringAssert.Contains(json, "\"receiptRefs\"");
        StringAssert.Contains(json, "\"observedAtUtc\"");
    }

    [TestMethod]
    public void BlockBJ_BlockedStatus_RequiresBlockedReason()
    {
        var result = Validate(BlockedStatus() with { BlockedReasons = [] });

        AssertInvalid(result, "BlockedReasonRequired");
    }

    [TestMethod]
    public void BlockBJ_BlockedStatus_RequiresMissingEvidenceOrNextSafeAction()
    {
        var result = Validate(BlockedStatus() with { MissingEvidence = [], NextSafeActions = [] });

        AssertInvalid(result, "BlockedStatusNeedsEvidenceOrNextSafeAction");
    }

    [TestMethod]
    public void BlockBJ_BlockedStatus_ExplainsNextSafeAction()
    {
        var result = Validate(BlockedStatus());

        AssertValid(result);
        Assert.IsTrue(BlockedStatus().NextSafeActions.Any(item => item.Contains("create source-apply request", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BlockBJ_EligibleStatus_CannotCarryBlockedReasons()
    {
        var result = Validate(EligibleStatus() with { BlockedReasons = ["missing approval"] });

        AssertInvalid(result, "EligibleStatusCannotCarryBlockedReasons");
    }

    [TestMethod]
    public void BlockBJ_EligibleStatus_CanBeValidWhenForbiddenActionsAreExplicit()
    {
        var result = Validate(EligibleStatus());

        AssertValid(result);
    }

    [TestMethod]
    public void BlockBJ_RunningStatus_RequiresForbiddenActionsForAuthorityBearingOperations()
    {
        var result = Validate(RunningStatus() with { ForbiddenActions = [] });

        AssertInvalid(result, "ForbiddenActionsRequiredForAuthorityBearingOperation");
    }

    [TestMethod]
    public void BlockBJ_CompletedStatus_CanReferenceReceiptsButCannotGrantNextAuthority()
    {
        var status = CompletedStatus();
        var result = Validate(status);

        AssertValid(result);
        Assert.IsTrue(status.ReceiptRefs.Contains("source-apply-receipt:apply-123"));
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void BlockBJ_CompletedStatus_RequiresReceiptReference()
    {
        var result = Validate(CompletedStatus() with { ReceiptRefs = [] });

        AssertInvalid(result, "CompletedStatusRequiresReceiptReference");
    }

    [TestMethod]
    public void BlockBJ_FailedStatus_IsExplainableWithoutGrantingAuthority()
    {
        var result = Validate(FailedStatus());

        AssertValid(result);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void BlockBJ_ExpiredStatus_IsExplicitAndExplainable()
    {
        var result = Validate(ExpiredStatus());

        AssertValid(result);
    }

    [TestMethod]
    public void BlockBJ_ExpiredStatus_RequiresExpiryEvidence()
    {
        var result = Validate(ExpiredStatus() with { ExpiresAtUtc = null, BlockedReasons = ["stale"] });

        AssertInvalid(result, "ExpiredStatusRequiresExpiryEvidence");
    }

    [TestMethod]
    public void BlockBJ_RequiredIdentityFields_AreValidated()
    {
        var result = Validate(BlockedStatus() with
        {
            OperationId = string.Empty,
            OperationKind = string.Empty,
            Subject = string.Empty,
            ObservedAtUtc = default
        });

        AssertInvalid(result, "OperationIdRequired");
        AssertInvalid(result, "OperationKindRequired");
        AssertInvalid(result, "SubjectRequired");
        AssertInvalid(result, "ObservedAtUtcRequired");
    }

    [TestMethod]
    public void BlockBJ_NullCollections_ReturnInvalidInsteadOfThrowing()
    {
        var status = BlockedStatus() with
        {
            BlockedReasons = null!,
            MissingEvidence = null!,
            NextSafeActions = null!,
            ForbiddenActions = null!,
            EvidenceRefs = null!,
            ReceiptRefs = null!
        };

        var result = Validate(status);

        AssertInvalid(result, "BlockedReasonRequired");
        AssertInvalid(result, "BlockedStatusNeedsEvidenceOrNextSafeAction");
        AssertInvalid(result, "ForbiddenActionsRequiredForAuthorityBearingOperation");
    }

    [TestMethod]
    public void BlockBJ_State_MustBeExplicit()
    {
        var result = Validate(BlockedStatus() with { State = (GovernedOperationState)0 });

        AssertInvalid(result, "StateRequired");
    }

    [TestMethod]
    public void BlockBJ_NextSafeActions_DoNotImplyAuthority()
    {
        var status = BlockedStatus() with
        {
            NextSafeActions = ["request controlled source apply for patch hash abc123"]
        };

        var result = Validate(status);

        AssertValid(result);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void BlockBJ_NextSafeActions_RejectDirectMutation()
    {
        var result = Validate(BlockedStatus() with { NextSafeActions = ["apply patch now"] });

        AssertInvalid(result, "NextSafeActionImpliesDirectMutation");
        AssertRedFlag(result, "UnsafeNextSafeActionWouldMutate");
    }

    [TestMethod]
    public void BlockBJ_EvidenceRefs_DoNotImplyApproval()
    {
        var result = Validate(BlockedStatus() with
        {
            EvidenceRefs = ["tests passed so approved"]
        });

        AssertInvalid(result, "StatusImpliesAuthority");
        AssertRedFlag(result, "EvidenceReferenceCannotSatisfyAuthority");
    }

    [TestMethod]
    public void BlockBJ_ReceiptRefs_DoNotImplyWorkflowContinuation()
    {
        var result = Validate(CompletedStatus() with
        {
            ReceiptRefs = ["receipt exists so workflow can continue"]
        });

        AssertInvalid(result, "StatusImpliesAuthority");
        AssertRedFlag(result, "ReceiptReferenceCannotSatisfyAuthority");
    }

    [TestMethod]
    public void BlockBJ_MemoryReferences_CannotSatisfyApproval()
    {
        var result = Validate(BlockedStatus() with
        {
            EvidenceRefs = ["memory says this was approved"]
        });

        AssertInvalid(result, "StatusImpliesAuthority");
        AssertRedFlag(result, "MemoryReferenceCannotSatisfyAuthority");
    }

    [TestMethod]
    public void BlockBJ_UiStateReferences_CannotSatisfyApproval()
    {
        var result = Validate(BlockedStatus() with
        {
            EvidenceRefs = ["UI marked this as approved"]
        });

        AssertInvalid(result, "StatusImpliesAuthority");
        AssertRedFlag(result, "UiStateCannotSatisfyAuthority");
    }

    [TestMethod]
    public void BlockBJ_ReleaseReceipt_DoesNotAuthorizeDeployment()
    {
        var result = Validate(BlockedStatus() with
        {
            ReceiptRefs = ["release receipt authorizes deployment"]
        });

        AssertInvalid(result, "StatusImpliesAuthority");
        AssertRedFlag(result, "ReceiptReferenceCannotSatisfyAuthority");
    }

    [TestMethod]
    public void BlockBJ_DeploymentReceipt_DoesNotAuthorizeWorkflowContinuation()
    {
        var result = Validate(BlockedStatus() with
        {
            ReceiptRefs = ["deployment receipt authorizes workflow continuation"]
        });

        AssertInvalid(result, "StatusImpliesAuthority");
        AssertRedFlag(result, "ReceiptReferenceCannotSatisfyAuthority");
    }

    [TestMethod]
    public void BlockBJ_Boundary_StatusDoesNotGrantAuthority()
    {
        var boundary = GovernedOperationStatusBoundary.Status;

        Assert.IsTrue(boundary.StatusOnly);
        Assert.IsTrue(boundary.ReferenceOnly);
        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
        Assert.IsFalse(boundary.CanExecute);
        Assert.IsFalse(boundary.CanRetry);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanRollback);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanSourceApply);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanPublishPackages);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
        Assert.IsFalse(boundary.CanDispatchPipeline);
        Assert.IsFalse(boundary.CanMutate);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanMutateEnvironment);
    }

    [TestMethod]
    public void BlockBJ_Examples_CoverMajorFutureOperationKinds()
    {
        var kinds = ExampleStatuses().Select(item => item.OperationKind).ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "PatchProposal",
                "SourceApply",
                "Rollback",
                "Commit",
                "Push",
                "DraftPullRequest",
                "Merge",
                "Release",
                "Deployment",
                "MemoryPromotion",
                "WorkflowContinuation"
            },
            kinds);
    }

    [TestMethod]
    public void BlockBJ_Examples_AreSampleStatusesOnly()
    {
        foreach (var status in ExampleStatuses())
        {
            var result = Validate(status);

            AssertValid(result);
            AssertNoAuthority(result);
            Assert.IsTrue(status.ForbiddenActions.Count > 0, status.OperationKind);
        }
    }

    [TestMethod]
    public void BlockBJ_StaticBoundary_DoesNotTouchExecutorMutationCode()
    {
        var changedCore = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.Core", "Governance", "GovernedOperationStatusValidator.cs"));

        Assert.IsFalse(changedCore.Contains("RunProcessAsync", StringComparison.Ordinal));
        Assert.IsFalse(changedCore.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(changedCore.Contains("gh api", StringComparison.Ordinal));
        Assert.IsFalse(changedCore.Contains("git ", StringComparison.Ordinal));
        Assert.IsFalse(changedCore.Contains("kubectl", StringComparison.Ordinal));
        Assert.IsFalse(changedCore.Contains("terraform apply", StringComparison.Ordinal));
        Assert.IsFalse(changedCore.Contains("docker push", StringComparison.Ordinal));
        Assert.IsFalse(changedCore.Contains("npm publish", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BlockBJ_Receipt_RecordsStatusBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "BJ_CANONICAL_GOVERNED_OPERATION_STATUS.md"));

        StringAssert.Contains(doc, "Block BJ adds the canonical governed operation status contract.");
        StringAssert.Contains(doc, "Status is not approval.");
        StringAssert.Contains(doc, "Status is not policy satisfaction.");
        StringAssert.Contains(doc, "Status is not execution authority.");
        StringAssert.Contains(doc, "Status is not workflow continuation.");
        StringAssert.Contains(doc, "Status is not memory promotion.");
        StringAssert.Contains(doc, "Status is not source mutation.");
        StringAssert.Contains(doc, "NextSafeActions are guidance, not permission.");
        StringAssert.Contains(doc, "A status can explain the locked door. It cannot unlock it.");
    }

    private static GovernedOperationStatus BlockedStatus() =>
        BaseStatus("source-apply-status-001", "SourceApply", GovernedOperationState.Blocked) with
        {
            BlockedReasons = ["Missing accepted source-apply request."],
            MissingEvidence = ["accepted-source-apply-request:patch-hash-abc123"],
            NextSafeActions = ["create source-apply request for patch hash abc123"],
            ForbiddenActions = ["do not apply patch proposal directly to source"],
            EvidenceRefs = ["patch-proposal:abc123"],
            ReceiptRefs = []
        };

    private static GovernedOperationStatus EligibleStatus() =>
        BaseStatus("source-apply-status-eligible", "SourceApply", GovernedOperationState.Eligible) with
        {
            ForbiddenActions = ["commit/push/PR not allowed unless grant includes them"],
            EvidenceRefs = ["scoped-run-authority:source-apply:abc123"]
        };

    private static GovernedOperationStatus RunningStatus() =>
        BaseStatus("source-apply-status-running", "SourceApply", GovernedOperationState.Running) with
        {
            ForbiddenActions = ["do not commit or push while source apply is running"],
            EvidenceRefs = ["accepted-source-apply-request:abc123"]
        };

    private static GovernedOperationStatus CompletedStatus() =>
        BaseStatus("source-apply-status-completed", "SourceApply", GovernedOperationState.Completed) with
        {
            NextSafeActions = ["review source apply receipt before requesting controlled commit package"],
            ForbiddenActions = ["do not treat source apply completion as commit, push, or workflow continuation authority"],
            EvidenceRefs = ["accepted-source-apply-request:abc123"],
            ReceiptRefs = ["source-apply-receipt:apply-123"]
        };

    private static GovernedOperationStatus FailedStatus() =>
        BaseStatus("source-apply-status-failed", "SourceApply", GovernedOperationState.Failed) with
        {
            BlockedReasons = ["Source apply failed validation."],
            NextSafeActions = ["review failure receipt and prepare a new governed proposal"],
            ForbiddenActions = ["do not retry source apply without fresh authority"],
            ReceiptRefs = ["source-apply-failure-receipt:apply-123"]
        };

    private static GovernedOperationStatus ExpiredStatus() =>
        BaseStatus("source-apply-status-expired", "SourceApply", GovernedOperationState.Expired) with
        {
            BlockedReasons = ["Authority expired before execution."],
            NextSafeActions = ["request fresh source apply authority for patch hash abc123"],
            ForbiddenActions = ["do not refresh stale authority from memory, UI state, old receipts, or test success"],
            ExpiresAtUtc = DateTimeOffset.Parse("2026-06-21T00:30:00Z")
        };

    private static GovernedOperationStatus BaseStatus(string id, string kind, GovernedOperationState state) =>
        new()
        {
            OperationId = id,
            OperationKind = kind,
            Subject = "repo:BigDaddyDread-code/IronDeveloper path:IronDev.Core/Governance",
            State = state,
            BlockedReasons = [],
            MissingEvidence = [],
            NextSafeActions = [],
            ForbiddenActions = [],
            EvidenceRefs = [],
            ReceiptRefs = [],
            ObservedAtUtc = DateTimeOffset.Parse("2026-06-21T00:00:00Z")
        };

    private static GovernedOperationStatus[] ExampleStatuses() =>
    [
        Example("PatchProposal", "request controlled source apply for accepted proposal"),
        Example("SourceApply", "create source-apply request for patch hash abc123"),
        Example("Rollback", "request rollback decision package for failed deployment"),
        Example("Commit", "create controlled commit package for changed files"),
        Example("Push", "prepare controlled branch update package"),
        Example("DraftPullRequest", "create controlled draft PR request"),
        Example("Merge", "request merge decision package"),
        Example("Release", "request release readiness decision package"),
        Example("Deployment", "request deployment readiness decision package"),
        Example("MemoryPromotion", "request memory promotion review package"),
        Example("WorkflowContinuation", "request explicit continuation authority")
    ];

    private static GovernedOperationStatus Example(string kind, string nextSafeAction) =>
        BaseStatus($"example-{kind.ToLowerInvariant()}", kind, GovernedOperationState.Blocked) with
        {
            BlockedReasons = [$"{kind} requires explicit current authority."],
            MissingEvidence = [$"{kind}:current-authority"],
            NextSafeActions = [nextSafeAction],
            ForbiddenActions = [$"do not infer {kind} authority from status, memory, UI, evidence, or receipts"]
        };

    private static GovernedOperationStatusValidationResult Validate(GovernedOperationStatus status) =>
        GovernedOperationStatusValidator.Validate(status);

    private static void AssertValid(GovernedOperationStatusValidationResult result) =>
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues.Concat(result.RedFlags).Concat(result.AmberFlags)));

    private static void AssertInvalid(GovernedOperationStatusValidationResult result, string issue)
    {
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Contains(issue, StringComparer.OrdinalIgnoreCase), string.Join(", ", result.Issues));
    }

    private static void AssertRedFlag(GovernedOperationStatusValidationResult result, string flag) =>
        Assert.IsTrue(result.RedFlags.Contains(flag, StringComparer.OrdinalIgnoreCase), string.Join(", ", result.RedFlags));

    private static void AssertNoAuthority(GovernedOperationStatusValidationResult result)
    {
        Assert.IsFalse(result.Boundary.CanApprove);
        Assert.IsFalse(result.Boundary.CanSatisfyPolicy);
        Assert.IsFalse(result.Boundary.CanExecute);
        Assert.IsFalse(result.Boundary.CanMutate);
        Assert.IsFalse(result.Boundary.CanPromoteMemory);
        Assert.IsFalse(result.Boundary.CanContinueWorkflow);
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
}
