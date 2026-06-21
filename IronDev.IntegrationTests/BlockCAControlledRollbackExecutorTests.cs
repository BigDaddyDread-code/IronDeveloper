using System.Reflection;
using IronDev.Core.Governance;
using IronDev.Core.Governance.RollbackExecution;
using ControlledRollbackExecutionRequest = IronDev.Core.Governance.RollbackExecution.ControlledRollbackExecutionRequest;
using ControlledRollbackExecutionResult = IronDev.Core.Governance.RollbackExecution.ControlledRollbackExecutionResult;
using ControlledRollbackExecutor = IronDev.Core.Governance.RollbackExecution.ControlledRollbackExecutor;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockCAControlledRollbackExecutorTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 22, 4, 0, 0, TimeSpan.Zero);
    private const string Repository = "BigDaddyDread-code/IronDeveloper";
    private const string Branch = "rollback/controlled-rollback-executor";
    private const string RunId = "run-ca-001";
    private const string PatchHash = "sha256:abcdef1234567890";
    private const string SourceApplyReceiptRef = "source-apply-receipt:apply-ca-001";
    private const string TargetId = "rollback-target-ca-001";
    private const string FilePath = "IronDev.Core/Governance/RollbackExecution/ControlledRollbackExecutor.cs";
    private const string PreHash = "sha256:pre-ca-001";
    private const string PostHash = "sha256:post-ca-001";

    [TestMethod]
    public async Task BlockCA_Executor_CompletesRollbackOnlyAfterAuthorityPreStateReceiptAndPostState()
    {
        var inspector = new FakeRollbackWorktreeInspector
        {
            PreStates = [GoodPreState()],
            PostStates = [GoodPostState()]
        };
        var gateway = new FakeControlledRollbackGateway();

        var result = await ControlledRollbackExecutor.ExecuteAsync(ValidRequest(), inspector, gateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledRollbackExecutionVerdict.Completed, result.Verdict);
        Assert.AreEqual(ControlledRollbackFailureKind.None, result.FailureKind);
        Assert.IsTrue(result.IsRollbackExecuted);
        Assert.AreEqual(GovernedOperationState.Completed, result.OperationStatus.State);
        AssertContains(result.OperationStatus.ReceiptRefs, "controlled-rollback-receipt:rollback-ca-001");
        Assert.AreEqual(1, inspector.PreCalls);
        Assert.AreEqual(1, gateway.RollbackCalls);
        Assert.AreEqual(1, inspector.PostCalls);
        Assert.IsNotNull(gateway.LastRequest);
        CollectionAssert.AreEquivalent(new[] { FilePath }, gateway.LastRequest!.ExpectedFiles.Select(file => file.Path).ToArray());
        Assert.IsTrue(gateway.LastRequest.CompleteRollbackOnly);
        Assert.IsTrue(gateway.LastRequest.PartialRollbackDisabled);
        Assert.IsTrue(gateway.LastRequest.CommitDisabled);
        Assert.IsTrue(gateway.LastRequest.PushDisabled);
        Assert.IsTrue(gateway.LastRequest.PullRequestDisabled);
        Assert.IsTrue(gateway.LastRequest.WorkflowContinuationDisabled);
        AssertValid(result);
    }

    [TestMethod]
    public async Task BlockCA_Executor_CompletesRollbackThroughNarrowPolicyApprovedPath()
    {
        var request = ValidRequest() with
        {
            Authority = null,
            PolicyApprovedPath = ValidPolicyPath()
        };
        var inspector = new FakeRollbackWorktreeInspector
        {
            PreStates = [GoodPreState()],
            PostStates = [GoodPostState()]
        };
        var gateway = new FakeControlledRollbackGateway();

        var result = await ControlledRollbackExecutor.ExecuteAsync(request, inspector, gateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledRollbackExecutionVerdict.Completed, result.Verdict);
        Assert.IsTrue(result.IsRollbackExecuted);
        Assert.AreEqual(1, gateway.RollbackCalls);
        Assert.AreEqual(TargetId, gateway.LastRequest!.RollbackTargetId);
        Assert.IsTrue(gateway.LastRequest.CompleteRollbackOnly);
        Assert.IsTrue(gateway.LastRequest.PartialRollbackDisabled);
        AssertValid(result);
    }

    [TestMethod]
    public async Task BlockCA_Executor_BlocksWithoutAuthorityOrPolicyPath()
    {
        var inspector = new FakeRollbackWorktreeInspector { PreStates = [GoodPreState()] };
        var gateway = new FakeControlledRollbackGateway();

        var result = await ControlledRollbackExecutor.ExecuteAsync(
            ValidRequest() with { Authority = null, PolicyApprovedPath = null },
            inspector,
            gateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledRollbackExecutionVerdict.Blocked, result.Verdict);
        Assert.IsFalse(result.IsRollbackExecuted);
        AssertHasIssue(result, "RollbackAuthorityOrPolicyPathRequired");
        Assert.AreEqual(0, inspector.PreCalls);
        Assert.AreEqual(0, gateway.RollbackCalls);
        AssertValid(result);
    }

    [TestMethod]
    public async Task BlockCA_Executor_RejectsInvalidPolicyApprovedPath()
    {
        var cases = new (string Name, RollbackPolicyApprovedPathEvidence Policy, string ExpectedIssue)[]
        {
            ("repo", ValidPolicyPath() with { Repository = "other/repo" }, "RollbackPolicyPathRepositoryMismatch"),
            ("branch", ValidPolicyPath() with { Branch = "other-branch" }, "RollbackPolicyPathBranchMismatch"),
            ("run", ValidPolicyPath() with { RunId = "other-run" }, "RollbackPolicyPathRunIdMismatch"),
            ("patch", ValidPolicyPath() with { PatchHash = "sha256:other" }, "RollbackPolicyPathPatchHashMismatch"),
            ("receipt", ValidPolicyPath() with { SourceApplyReceiptRef = "source-apply-receipt:other" }, "RollbackApplyReceiptMismatch"),
            ("target", ValidPolicyPath() with { RollbackTargetId = "other-target" }, "RollbackPolicyPathTargetMismatch"),
            ("expired", ValidPolicyPath() with { ExpiresAtUtc = ObservedAtUtc }, "RollbackPolicyPathExpired"),
            ("partial", ValidPolicyPath() with { AllowsPartialRollback = true }, "PartialRollbackRisk"),
            ("downstream", ValidPolicyPath() with { AllowsDownstreamMutation = true }, "RollbackPolicyPathAllowsDownstreamMutation"),
            ("not-complete", ValidPolicyPath() with { AllowsOnlyCompleteRollback = false }, "RollbackPolicyPathDoesNotRequireCompleteRollback"),
            ("not-bound", ValidPolicyPath() with { IsBoundToFailedOrReversibleSourceApply = false }, "RollbackPolicyPathNotBoundToFailedOrReversibleSourceApply")
        };

        foreach (var item in cases)
        {
            var inspector = new FakeRollbackWorktreeInspector { PreStates = [GoodPreState()] };
            var gateway = new FakeControlledRollbackGateway();
            var result = await ControlledRollbackExecutor.ExecuteAsync(
                ValidRequest() with { Authority = null, PolicyApprovedPath = item.Policy },
                inspector,
                gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledRollbackExecutionVerdict.Blocked, result.Verdict, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(0, inspector.PreCalls, item.Name);
            Assert.AreEqual(0, gateway.RollbackCalls, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockCA_Executor_BlocksWrongApplyReceipt()
    {
        var preflightCases = new (string Name, ControlledRollbackExecutionRequest Request)[]
        {
            ("target", ValidRequest() with { Target = ValidTarget() with { SourceApplyReceiptRef = "source-apply-receipt:other" } }),
            ("authority", ValidRequest() with { Authority = ValidAuthority() with { SourceApplyReceiptRef = "source-apply-receipt:other" } }),
            ("policy", ValidRequest() with { Authority = null, PolicyApprovedPath = ValidPolicyPath() with { SourceApplyReceiptRef = "source-apply-receipt:other" } })
        };

        foreach (var item in preflightCases)
        {
            var inspector = new FakeRollbackWorktreeInspector { PreStates = [GoodPreState()] };
            var gateway = new FakeControlledRollbackGateway();
            var result = await ControlledRollbackExecutor.ExecuteAsync(item.Request, inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledRollbackExecutionVerdict.Blocked, result.Verdict, item.Name);
            AssertHasIssue(result, "RollbackApplyReceiptMismatch", item.Name);
            Assert.AreEqual(0, gateway.RollbackCalls, item.Name);
            AssertValid(result);
        }

        var preMismatch = await ControlledRollbackExecutor.ExecuteAsync(
            ValidRequest(),
            new FakeRollbackWorktreeInspector { PreStates = [GoodPreState() with { SourceApplyReceiptRef = "source-apply-receipt:other" }] },
            new FakeControlledRollbackGateway()).ConfigureAwait(false);

        Assert.AreEqual(ControlledRollbackExecutionVerdict.Blocked, preMismatch.Verdict);
        AssertHasIssue(preMismatch, "RollbackApplyReceiptMismatch");

        var receiptMismatch = await ControlledRollbackExecutor.ExecuteAsync(
            ValidRequest(),
            new FakeRollbackWorktreeInspector { PreStates = [GoodPreState()] },
            new FakeControlledRollbackGateway { Receipt = GoodReceipt() with { SourceApplyReceiptRef = "source-apply-receipt:other" } }).ConfigureAwait(false);

        Assert.AreEqual(ControlledRollbackExecutionVerdict.Failed, receiptMismatch.Verdict);
        AssertHasIssue(receiptMismatch, "RollbackApplyReceiptMismatch");

        var postMismatch = await ControlledRollbackExecutor.ExecuteAsync(
            ValidRequest(),
            new FakeRollbackWorktreeInspector
            {
                PreStates = [GoodPreState()],
                PostStates = [GoodPostState() with { SourceApplyReceiptRef = "source-apply-receipt:other" }]
            },
            new FakeControlledRollbackGateway()).ConfigureAwait(false);

        Assert.AreEqual(ControlledRollbackExecutionVerdict.Failed, postMismatch.Verdict);
        Assert.IsTrue(postMismatch.IsRollbackExecuted);
        AssertHasIssue(postMismatch, "RollbackApplyReceiptMismatch");
    }

    [TestMethod]
    public async Task BlockCA_Executor_BlocksWrongRollbackTarget()
    {
        var cases = new (string Name, ControlledRollbackExecutionRequest Request, FakeRollbackWorktreeInspector Inspector, FakeControlledRollbackGateway Gateway, ControlledRollbackExecutionVerdict Verdict)[]
        {
            ("authority", ValidRequest() with { Authority = ValidAuthority() with { RollbackTargetId = "other-target" } }, new FakeRollbackWorktreeInspector(), new FakeControlledRollbackGateway(), ControlledRollbackExecutionVerdict.Blocked),
            ("policy", ValidRequest() with { Authority = null, PolicyApprovedPath = ValidPolicyPath() with { RollbackTargetId = "other-target" } }, new FakeRollbackWorktreeInspector(), new FakeControlledRollbackGateway(), ControlledRollbackExecutionVerdict.Blocked),
            ("pre", ValidRequest(), new FakeRollbackWorktreeInspector { PreStates = [GoodPreState() with { RollbackTargetId = "other-target" }] }, new FakeControlledRollbackGateway(), ControlledRollbackExecutionVerdict.Blocked),
            ("receipt", ValidRequest(), new FakeRollbackWorktreeInspector { PreStates = [GoodPreState()] }, new FakeControlledRollbackGateway { Receipt = GoodReceipt() with { RollbackTargetId = "other-target" } }, ControlledRollbackExecutionVerdict.Failed),
            ("post", ValidRequest(), new FakeRollbackWorktreeInspector { PreStates = [GoodPreState()], PostStates = [GoodPostState() with { RollbackTargetId = "other-target" }] }, new FakeControlledRollbackGateway(), ControlledRollbackExecutionVerdict.Failed)
        };

        foreach (var item in cases)
        {
            var result = await ControlledRollbackExecutor.ExecuteAsync(item.Request, item.Inspector, item.Gateway).ConfigureAwait(false);

            Assert.AreEqual(item.Verdict, result.Verdict, item.Name);
            AssertHasIssue(result, "TargetMismatch", item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockCA_Executor_BlocksExpectedFileMismatchBeforeGateway()
    {
        var cases = new (string Name, ControlledRollbackExecutionRequest Request, RollbackPreStateObservation PreState, string ExpectedIssue)[]
        {
            ("files-null", ValidRequest() with { Target = ValidTarget() with { ExpectedFiles = null! } }, GoodPreState(), "ExpectedFilesRequired"),
            ("files-empty", ValidRequest() with { Target = ValidTarget() with { ExpectedFiles = [] } }, GoodPreState(), "ExpectedFilesRequired"),
            ("duplicate", ValidRequest() with { Target = ValidTarget() with { ExpectedFiles = [ExpectedFile(), ExpectedFile()] } }, GoodPreState(), "DuplicateRollbackPath"),
            ("unsafe", ValidRequest() with { Target = ValidTarget() with { ExpectedFiles = [ExpectedFile() with { Path = "../outside.cs" }] } }, GoodPreState(), "UnsafeRollbackPath"),
            ("missing-observed", ValidRequest(), GoodPreState() with { ObservedFiles = [] }, "RollbackPreStateObservedFileSetMismatch"),
            ("extra-observed", ValidRequest(), GoodPreState() with { ObservedFiles = [ObservedPreFile(), ObservedPreFile() with { Path = "Docs/extra.md" }] }, "RollbackPreStateObservedFileSetMismatch"),
            ("hash", ValidRequest(), GoodPreState() with { ObservedFiles = [ObservedPreFile() with { ContentHash = "sha256:wrong" }] }, "RollbackPreStateFileHashMismatch")
        };

        foreach (var item in cases)
        {
            var inspector = new FakeRollbackWorktreeInspector { PreStates = [item.PreState] };
            var gateway = new FakeControlledRollbackGateway();
            var result = await ControlledRollbackExecutor.ExecuteAsync(item.Request, inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledRollbackExecutionVerdict.Blocked, result.Verdict, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(0, gateway.RollbackCalls, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockCA_Executor_BlocksDirtyPreState()
    {
        var cases = new (string Name, RollbackPreStateObservation PreState, string ExpectedIssue)[]
        {
            ("changed", GoodPreState() with { ChangedFilePaths = [FilePath] }, "DirtyWorktree"),
            ("staged", GoodPreState() with { StagedFilePaths = [FilePath] }, "DirtyWorktree"),
            ("untracked", GoodPreState() with { UntrackedFilePaths = [FilePath] }, "DirtyWorktree"),
            ("changed-null", GoodPreState() with { ChangedFilePaths = null! }, "RollbackPreStateWorktreeCollectionsRequired"),
            ("staged-null", GoodPreState() with { StagedFilePaths = null! }, "RollbackPreStateWorktreeCollectionsRequired"),
            ("untracked-null", GoodPreState() with { UntrackedFilePaths = null! }, "RollbackPreStateWorktreeCollectionsRequired"),
            ("not-immediate", GoodPreState() with { IsObservedImmediatelyBeforeRollback = false }, "RollbackPreStateNotImmediate")
        };

        foreach (var item in cases)
        {
            var inspector = new FakeRollbackWorktreeInspector { PreStates = [item.PreState] };
            var gateway = new FakeControlledRollbackGateway();
            var result = await ControlledRollbackExecutor.ExecuteAsync(ValidRequest(), inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledRollbackExecutionVerdict.Blocked, result.Verdict, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(0, gateway.RollbackCalls, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockCA_Executor_BlocksPartialRollbackRiskBeforeGateway()
    {
        var cases = new (string Name, ControlledRollbackExecutionRequest Request)[]
        {
            ("requires", ValidRequest() with { Target = ValidTarget() with { RequiresPartialRollback = true } }),
            ("risk", ValidRequest() with { Target = ValidTarget() with { HasPartialRollbackRisk = true } }),
            ("not-complete", ValidRequest() with { Target = ValidTarget() with { IsCompleteRollback = false } }),
            ("policy-partial", ValidRequest() with { Authority = null, PolicyApprovedPath = ValidPolicyPath() with { AllowsPartialRollback = true } })
        };

        foreach (var item in cases)
        {
            var gateway = new FakeControlledRollbackGateway();
            var result = await ControlledRollbackExecutor.ExecuteAsync(
                item.Request,
                new FakeRollbackWorktreeInspector { PreStates = [GoodPreState()] },
                gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledRollbackExecutionVerdict.Blocked, result.Verdict, item.Name);
            AssertHasIssue(result, "PartialRollbackRisk", item.Name);
            Assert.AreEqual(0, gateway.RollbackCalls, item.Name);
            AssertContains(result.OperationStatus.ForbiddenActions, "do not treat partial rollback as successful rollback");
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockCA_Executor_FailsPartialRollbackReceipt()
    {
        var cases = new (string Name, ControlledRollbackReceipt Receipt, string ExpectedIssue)[]
        {
            ("not-complete", GoodReceipt() with { CompleteRollbackExecuted = false }, "ControlledRollbackReceiptCompleteRollbackRequired"),
            ("partial-attempt", GoodReceipt() with { PartialRollbackAttempted = true }, "PartialRollbackAttempted"),
            ("partial-failed", GoodReceipt() with { PartialRollbackFailed = true }, "PartialRollbackFailed"),
            ("missing-file", GoodReceipt() with { RolledBackFilePaths = [] }, "ControlledRollbackReceiptFileSetMismatch"),
            ("extra-file", GoodReceipt() with { RolledBackFilePaths = [FilePath, "Docs/extra.md"] }, "ControlledRollbackReceiptFileSetMismatch")
        };

        foreach (var item in cases)
        {
            var inspector = new FakeRollbackWorktreeInspector { PreStates = [GoodPreState()], PostStates = [GoodPostState()] };
            var gateway = new FakeControlledRollbackGateway { Receipt = item.Receipt };
            var result = await ControlledRollbackExecutor.ExecuteAsync(ValidRequest(), inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledRollbackExecutionVerdict.Failed, result.Verdict, item.Name);
            Assert.IsFalse(result.IsRollbackExecuted, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(1, gateway.RollbackCalls, item.Name);
            Assert.AreEqual(0, inspector.PostCalls, item.Name);
            AssertContains(result.OperationStatus.ForbiddenActions, "do not treat partial rollback as successful rollback");
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockCA_Executor_FailsInvalidReceipt()
    {
        var cases = new (string Name, ControlledRollbackReceipt? Receipt, string ExpectedIssue)[]
        {
            ("missing", null, "ControlledRollbackReceiptRequired"),
            ("prefix", GoodReceipt() with { ReceiptRef = "receipt:wrong" }, "ControlledRollbackReceiptRefInvalid"),
            ("repo", GoodReceipt() with { Repository = "other/repo" }, "ControlledRollbackReceiptRepositoryMismatch"),
            ("branch", GoodReceipt() with { Branch = "other-branch" }, "ControlledRollbackReceiptBranchMismatch"),
            ("run", GoodReceipt() with { RunId = "other-run" }, "ControlledRollbackReceiptRunIdMismatch"),
            ("patch", GoodReceipt() with { PatchHash = "sha256:other" }, "ControlledRollbackReceiptPatchHashMismatch"),
            ("time", GoodReceipt() with { ExecutedAtUtc = default }, "ControlledRollbackReceiptExecutedAtRequired"),
            ("commit", GoodReceipt() with { CommitAttempted = true }, "ControlledRollbackReceiptDownstreamAuthorityAttempted"),
            ("push", GoodReceipt() with { PushAttempted = true }, "ControlledRollbackReceiptDownstreamAuthorityAttempted"),
            ("pr", GoodReceipt() with { PullRequestAttempted = true }, "ControlledRollbackReceiptDownstreamAuthorityAttempted"),
            ("merge", GoodReceipt() with { MergeAttempted = true }, "ControlledRollbackReceiptDownstreamAuthorityAttempted"),
            ("release", GoodReceipt() with { ReleaseAttempted = true }, "ControlledRollbackReceiptDownstreamAuthorityAttempted"),
            ("deploy", GoodReceipt() with { DeploymentAttempted = true }, "ControlledRollbackReceiptDownstreamAuthorityAttempted"),
            ("continue", GoodReceipt() with { ContinuationAttempted = true }, "ControlledRollbackReceiptDownstreamAuthorityAttempted")
        };

        foreach (var item in cases)
        {
            var inspector = new FakeRollbackWorktreeInspector { PreStates = [GoodPreState()], PostStates = [GoodPostState()] };
            var gateway = new FakeControlledRollbackGateway { Receipt = item.Receipt };
            var result = await ControlledRollbackExecutor.ExecuteAsync(ValidRequest(), inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledRollbackExecutionVerdict.Failed, result.Verdict, item.Name);
            Assert.IsFalse(result.IsRollbackExecuted, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(0, inspector.PostCalls, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockCA_Executor_FailsPostStateMismatch()
    {
        var cases = new (string Name, RollbackPostStateObservation? PostState, string ExpectedIssue)[]
        {
            ("missing", null, "RollbackPostStateRequired"),
            ("repo", GoodPostState() with { Repository = "other/repo" }, "RollbackPostStateRepositoryMismatch"),
            ("branch", GoodPostState() with { Branch = "other-branch" }, "RollbackPostStateBranchMismatch"),
            ("target", GoodPostState() with { RollbackTargetId = "other-target" }, "RollbackPostStateTargetMismatch"),
            ("files-missing", GoodPostState() with { ObservedFiles = [] }, "RollbackPostStateObservedFileSetMismatch"),
            ("files-extra", GoodPostState() with { ObservedFiles = [ObservedPostFile(), ObservedPostFile() with { Path = "Docs/extra.md" }] }, "RollbackPostStateObservedFileSetMismatch"),
            ("hash", GoodPostState() with { ObservedFiles = [ObservedPostFile() with { ContentHash = "sha256:wrong" }] }, "RollbackPostStateFileHashMismatch"),
            ("remaining-changed", GoodPostState() with { RemainingChangedFilePaths = [FilePath] }, "DirtyWorktree"),
            ("remaining-staged", GoodPostState() with { RemainingStagedFilePaths = [FilePath] }, "DirtyWorktree"),
            ("remaining-untracked", GoodPostState() with { RemainingUntrackedFilePaths = [FilePath] }, "DirtyWorktree"),
            ("not-observed", GoodPostState() with { IsObservedAfterRollback = false }, "RollbackPostStateNotObserved"),
            ("mismatch", GoodPostState() with { MatchesExpectedPostRollbackState = false }, "RollbackPostStateMismatch")
        };

        foreach (var item in cases)
        {
            var inspector = new FakeRollbackWorktreeInspector
            {
                PreStates = [GoodPreState()],
                PostStates = item.PostState is null ? [] : [item.PostState],
                ReturnNullPostState = item.PostState is null
            };
            var gateway = new FakeControlledRollbackGateway();
            var result = await ControlledRollbackExecutor.ExecuteAsync(ValidRequest(), inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledRollbackExecutionVerdict.Failed, result.Verdict, item.Name);
            Assert.IsTrue(result.IsRollbackExecuted, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            AssertContains(result.OperationStatus.ForbiddenActions, "do not continue workflow after rollback mismatch");
            AssertContains(result.OperationStatus.ForbiddenActions, "do not treat failed rollback as stable");
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockCA_RollbackReceiptDoesNotGrantDownstreamAuthority()
    {
        var result = await ControlledRollbackExecutor.ExecuteAsync(
            ValidRequest(),
            new FakeRollbackWorktreeInspector { PreStates = [GoodPreState()], PostStates = [GoodPostState()] },
            new FakeControlledRollbackGateway()).ConfigureAwait(false);

        Assert.AreEqual(ControlledRollbackExecutionVerdict.Completed, result.Verdict);
        Assert.IsFalse(result.StatusValidation.Boundary.CanCommit);
        Assert.IsFalse(result.StatusValidation.Boundary.CanPush);
        Assert.IsFalse(result.StatusValidation.Boundary.CanContinueWorkflow);
        Assert.IsFalse(result.StatusValidation.Boundary.CanMerge);
        Assert.IsFalse(result.StatusValidation.Boundary.CanRelease);
        Assert.IsFalse(result.StatusValidation.Boundary.CanDeploy);
        Assert.IsFalse(result.StatusValidation.Boundary.CanMutateSource);
        AssertContains(result.OperationStatus.ForbiddenActions, "rollback receipt is not commit authority");
        AssertContains(result.OperationStatus.ForbiddenActions, "rollback receipt is not push authority");
        AssertContains(result.OperationStatus.ForbiddenActions, "rollback receipt is not workflow continuation");
        AssertValid(result);
    }

    [TestMethod]
    public void BlockCA_StaticCoreHasNoDirectMutationOrDownstreamSurface()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackExecution"),
            "*.cs",
            SearchOption.TopDirectoryOnly);
        var text = string.Join(Environment.NewLine, files.Select(File.ReadAllText));

        foreach (var forbidden in new[]
        {
            "Process.Start",
            "File.Write",
            "Directory.CreateDirectory",
            "git",
            "checkout",
            "reset",
            "revert",
            "cmd.exe",
            "powershell",
            "bash",
            "HttpClient",
            "Octokit",
            "IGovernanceEventStore",
            "CommitExecution",
            "PushExecution",
            "PullRequestExecution",
            "Merge execution",
            "Release execution",
            "Deploy execution",
            "MemoryPromotion"
        })
        {
            Assert.IsFalse(ContainsForbiddenSurface(text, forbidden), forbidden);
        }

        StringAssert.Contains(text, "IControlledRollbackGateway");
        StringAssert.Contains(text, "ExecuteRollbackAsync");
        StringAssert.Contains(text, "IRollbackWorktreeInspector");
    }

    [TestMethod]
    public void BlockCA_Receipt_RecordsControlledRollbackBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "CA_CONTROLLED_ROLLBACK_EXECUTOR.md"));

        StringAssert.Contains(doc, "Rollback is source mutation.");
        StringAssert.Contains(doc, "Rollback does not get a free pass because it sounds safer.");
        StringAssert.Contains(doc, "Rollback plan is not rollback execution.");
        StringAssert.Contains(doc, "Rollback eligibility is not rollback execution.");
        StringAssert.Contains(doc, "Rollback executes only under explicit rollback authority or a narrow policy-approved rollback path.");
        StringAssert.Contains(doc, "Partial rollback risk blocks rollback.");
        StringAssert.Contains(doc, "Partial rollback failure is not successful rollback.");
        StringAssert.Contains(doc, "Dirty worktree blocks rollback.");
        StringAssert.Contains(doc, "Post-state mismatch fails rollback execution.");
        StringAssert.Contains(doc, "Rollback receipt is not commit authority.");
        StringAssert.Contains(doc, "Rollback receipt is not push authority.");
        StringAssert.Contains(doc, "Rollback receipt is not PR authority.");
        StringAssert.Contains(doc, "It does not continue workflow.");
    }

    private static ControlledRollbackExecutionRequest ValidRequest() =>
        new()
        {
            ExecutionId = "controlled-rollback-exec-ca-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            SourceApplyReceiptRef = SourceApplyReceiptRef,
            Target = ValidTarget(),
            Authority = ValidAuthority(),
            PolicyApprovedPath = null,
            ObservedAtUtc = ObservedAtUtc,
            EvidenceRefs = ["controlled-rollback-execution-request:ca-001"],
            ReceiptRefs = []
        };

    private static RollbackTargetEvidence ValidTarget() =>
        new()
        {
            EvidenceRef = "rollback-target:ca-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            SourceApplyReceiptRef = SourceApplyReceiptRef,
            RollbackTargetId = TargetId,
            IsBoundToSourceApplyReceipt = true,
            IsCompleteRollback = true,
            RequiresPartialRollback = false,
            HasPartialRollbackRisk = false,
            ExpectedFiles = [ExpectedFile()]
        };

    private static RollbackFileExpectation ExpectedFile() =>
        new()
        {
            Path = FilePath,
            ExpectedPreRollbackHash = PreHash,
            ExpectedPostRollbackHash = PostHash,
            ShouldExistBeforeRollback = true,
            ShouldExistAfterRollback = true
        };

    private static RollbackExecutionAuthorityEvidence ValidAuthority() =>
        new()
        {
            EvidenceRef = "rollback-operation-authority:ca-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            SourceApplyReceiptRef = SourceApplyReceiptRef,
            RollbackTargetId = TargetId,
            Decision = EligibleDecision(RunAuthorityOperationKind.Rollback)
        };

    private static RollbackPolicyApprovedPathEvidence ValidPolicyPath() =>
        new()
        {
            EvidenceRef = "rollback-policy-approved-path:ca-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            SourceApplyReceiptRef = SourceApplyReceiptRef,
            RollbackTargetId = TargetId,
            PolicyId = "rollback-policy-ca-001",
            IsPolicyApprovedRollbackPath = true,
            IsBoundToFailedOrReversibleSourceApply = true,
            AllowsOnlyCompleteRollback = true,
            AllowsPartialRollback = false,
            AllowsDownstreamMutation = false,
            ApprovedAtUtc = ObservedAtUtc.AddMinutes(-2),
            ExpiresAtUtc = ObservedAtUtc.AddHours(1)
        };

    private static RollbackPreStateObservation GoodPreState() =>
        new()
        {
            Repository = Repository,
            Branch = Branch,
            HeadCommitId = "head-ca-001",
            SourceApplyReceiptRef = SourceApplyReceiptRef,
            RollbackTargetId = TargetId,
            ObservedFiles = [ObservedPreFile()],
            ChangedFilePaths = [],
            StagedFilePaths = [],
            UntrackedFilePaths = [],
            IsObservedImmediatelyBeforeRollback = true
        };

    private static RollbackPostStateObservation GoodPostState() =>
        new()
        {
            Repository = Repository,
            Branch = Branch,
            SourceApplyReceiptRef = SourceApplyReceiptRef,
            RollbackTargetId = TargetId,
            ObservedFiles = [ObservedPostFile()],
            RemainingChangedFilePaths = [],
            RemainingStagedFilePaths = [],
            RemainingUntrackedFilePaths = [],
            IsObservedAfterRollback = true,
            MatchesExpectedPostRollbackState = true
        };

    private static RollbackObservedFileState ObservedPreFile() =>
        new()
        {
            Path = FilePath,
            Exists = true,
            ContentHash = PreHash
        };

    private static RollbackObservedFileState ObservedPostFile() =>
        new()
        {
            Path = FilePath,
            Exists = true,
            ContentHash = PostHash
        };

    private static ControlledRollbackReceipt GoodReceipt() =>
        new()
        {
            ReceiptRef = "controlled-rollback-receipt:rollback-ca-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            SourceApplyReceiptRef = SourceApplyReceiptRef,
            RollbackTargetId = TargetId,
            RolledBackFilePaths = [FilePath],
            CompleteRollbackExecuted = true,
            PartialRollbackAttempted = false,
            PartialRollbackFailed = false,
            ExecutedAtUtc = ObservedAtUtc,
            CommitAttempted = false,
            PushAttempted = false,
            PullRequestAttempted = false,
            MergeAttempted = false,
            ReleaseAttempted = false,
            DeploymentAttempted = false,
            MemoryWriteAttempted = false,
            ContinuationAttempted = false
        };

    private static OperationEligibilityDecision EligibleDecision(RunAuthorityOperationKind operationKind) =>
        new()
        {
            IsEligibleUnderProfileAndGrant = true,
            OperationKind = operationKind,
            BlockedReasons = [],
            MissingEvidence = [],
            ForbiddenActions =
            [
                "do not treat eligibility as approval",
                "do not treat eligibility as policy satisfaction",
                "do not treat eligibility as execution authority"
            ],
            RequiredIndependentChecks =
            [
                "operation-specific governance still required",
                "profile and grant eligibility is necessary but not sufficient"
            ]
        };

    private static void AssertValid(ControlledRollbackExecutionResult result)
    {
        Assert.IsTrue(result.StatusValidation.IsValid, string.Join(", ", result.StatusValidation.Issues.Concat(result.StatusValidation.RedFlags)));
        Assert.IsFalse(result.StatusValidation.Boundary.CanCommit);
        Assert.IsFalse(result.StatusValidation.Boundary.CanPush);
        Assert.IsFalse(result.StatusValidation.Boundary.CanContinueWorkflow);
        Assert.IsFalse(result.StatusValidation.Boundary.CanMerge);
        Assert.IsFalse(result.StatusValidation.Boundary.CanRelease);
        Assert.IsFalse(result.StatusValidation.Boundary.CanDeploy);
        Assert.IsFalse(result.StatusValidation.Boundary.CanMutateSource);
    }

    private static void AssertHasIssue(ControlledRollbackExecutionResult result, string expected, string? label = null)
    {
        Assert.IsTrue(
            result.Issues.Any(issue => issue.Contains(expected, StringComparison.OrdinalIgnoreCase)),
            $"{label ?? expected}: expected '{expected}' in [{string.Join(", ", result.Issues)}]");
    }

    private static void AssertContains(IEnumerable<string> values, string expected)
    {
        Assert.IsTrue(
            values.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)),
            $"Expected '{expected}' in: {string.Join(", ", values)}");
    }

    private static bool ContainsForbiddenSurface(string text, string forbidden)
    {
        if (forbidden is "git" or "checkout" or "reset" or "revert" or "powershell" or "bash")
        {
            return text.Split(
                    [' ', '\t', '\r', '\n', '"', '\'', '`', '(', ')', '[', ']', '{', '}', ';', ',', '.', ':'],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(token => string.Equals(token, forbidden, StringComparison.OrdinalIgnoreCase));
        }

        return text.Contains(forbidden, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }

    private sealed class FakeRollbackWorktreeInspector : IRollbackWorktreeInspector
    {
        public RollbackPreStateObservation[] PreStates { get; init; } = [];
        public RollbackPostStateObservation[] PostStates { get; init; } = [];
        public bool ReturnNullPostState { get; init; }
        public int PreCalls { get; private set; }
        public int PostCalls { get; private set; }

        public Task<RollbackPreStateObservation?> ObservePreRollbackAsync(
            ControlledRollbackExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(PreCalls, Math.Max(PreStates.Length - 1, 0));
            PreCalls++;
            return Task.FromResult<RollbackPreStateObservation?>(PreStates.Length == 0 ? GoodPreState() : PreStates[index]);
        }

        public Task<RollbackPostStateObservation?> ObservePostRollbackAsync(
            ControlledRollbackExecutionRequest request,
            ControlledRollbackReceipt receipt,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(PostCalls, Math.Max(PostStates.Length - 1, 0));
            PostCalls++;
            if (ReturnNullPostState)
                return Task.FromResult<RollbackPostStateObservation?>(null);

            return Task.FromResult<RollbackPostStateObservation?>(PostStates.Length == 0 ? GoodPostState() : PostStates[index]);
        }
    }

    private sealed class FakeControlledRollbackGateway : IControlledRollbackGateway
    {
        public ControlledRollbackReceipt? Receipt { get; init; } = GoodReceipt();
        public int RollbackCalls { get; private set; }
        public ControlledRollbackGatewayRequest? LastRequest { get; private set; }

        public Task<ControlledRollbackReceipt?> ExecuteRollbackAsync(
            ControlledRollbackGatewayRequest request,
            CancellationToken cancellationToken)
        {
            RollbackCalls++;
            LastRequest = request;
            return Task.FromResult(Receipt);
        }
    }
}
