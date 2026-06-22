using System.Reflection;
using IronDev.Core.Governance;
using IronDev.Core.Governance.CommitExecution;
using IronDev.Core.Governance.PushExecution;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBXControlledPushExecutorTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 22, 1, 0, 0, TimeSpan.Zero);
    private const string Repository = "BigDaddyDread-code/IronDeveloper";
    private const string Branch = "push/controlled-push-executor";
    private const string RunId = "run-bx-001";
    private const string PatchHash = "sha256:abcdef1234567890";
    private const string RemoteName = "origin";
    private const string RemoteUrl = "https://example.invalid/BigDaddyDread-code/IronDeveloper";
    private const string RemoteBranch = Branch;
    private const string PreviousRemoteHead = "parent-bx-001";
    private const string CommitId = "commit-bx-001";
    private const string FilePath = "IronDev.Core/Governance/PushExecution/ControlledPushExecutor.cs";

    [TestMethod]
    public async Task BlockBX_Executor_PushesExactlyOneExpectedCommitAfterAuthorityAndRemoteStateChecks()
    {
        var inspector = new FakePushRemoteStateInspector
        {
            PreObservations = [GoodPreObservation()],
            PostObservations = [GoodPostObservation()]
        };
        var gateway = new FakeControlledPushGateway();

        var result = await ControlledPushExecutor.ExecuteAsync(ValidExecutionRequest(), inspector, gateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledPushExecutionVerdict.Completed, result.Verdict);
        Assert.AreEqual(ControlledPushFailureKind.None, result.FailureKind);
        Assert.IsTrue(result.IsPushExecuted);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(GovernedOperationState.Completed, result.OperationStatus.State);
        AssertContains(result.OperationStatus.ReceiptRefs, "controlled-push-receipt:push-bx-001");
        AssertContains(result.OperationStatus.ForbiddenActions, "do not create PR from push receipt");
        AssertContains(result.OperationStatus.ForbiddenActions, "do not continue workflow from push receipt");
        Assert.AreEqual(1, inspector.PreCalls);
        Assert.AreEqual(1, inspector.PostCalls);
        Assert.AreEqual(1, gateway.PushCalls);
        Assert.IsNotNull(gateway.LastRequest);
        Assert.AreEqual(RemoteName, gateway.LastRequest!.RemoteName);
        Assert.AreEqual(RemoteUrl, gateway.LastRequest.RemoteUrl);
        Assert.AreEqual(RemoteBranch, gateway.LastRequest.RemoteBranch);
        Assert.AreEqual(CommitId, gateway.LastRequest.ExpectedLocalCommitId);
        Assert.IsTrue(gateway.LastRequest.ForcePushDisabled);
        Assert.IsTrue(gateway.LastRequest.TagsDisabled);
        AssertValid(result);
    }

    [TestMethod]
    public async Task BlockBX_Executor_BlocksMissingRequestInspectorAndGateway()
    {
        var inspector = new FakePushRemoteStateInspector { PreObservations = [GoodPreObservation()] };
        var gateway = new FakeControlledPushGateway();

        var missingRequest = await ControlledPushExecutor.ExecuteAsync(null, inspector, gateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledPushExecutionVerdict.Blocked, missingRequest.Verdict);
        Assert.IsFalse(missingRequest.IsPushExecuted);
        AssertHasIssue(missingRequest, "ControlledPushExecutionRequestRequired");
        Assert.AreEqual(0, inspector.PreCalls);
        Assert.AreEqual(0, gateway.PushCalls);
        AssertValid(missingRequest);

        var request = ValidExecutionRequest();
        var missingInspector = await ControlledPushExecutor.ExecuteAsync(request, null!, gateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledPushExecutionVerdict.Blocked, missingInspector.Verdict);
        Assert.IsFalse(missingInspector.IsPushExecuted);
        AssertHasIssue(missingInspector, "PushRemoteStateInspectorRequired");
        Assert.AreEqual(0, gateway.PushCalls);
        AssertValid(missingInspector);

        var presentInspector = new FakePushRemoteStateInspector { PreObservations = [GoodPreObservation()] };
        var missingGateway = await ControlledPushExecutor.ExecuteAsync(request, presentInspector, null!).ConfigureAwait(false);

        Assert.AreEqual(ControlledPushExecutionVerdict.Blocked, missingGateway.Verdict);
        Assert.IsFalse(missingGateway.IsPushExecuted);
        AssertHasIssue(missingGateway, "ControlledPushGatewayRequired");
        Assert.AreEqual(0, presentInspector.PreCalls);
        Assert.AreEqual(0, presentInspector.PostCalls);
        AssertValid(missingGateway);
    }

    [TestMethod]
    public async Task BlockBX_Executor_BlocksPreflightBeforeRemoteObservationOrGateway()
    {
        var valid = ValidExecutionRequest();
        var cases = new (string Name, ControlledPushExecutionRequest Request, string ExpectedIssue)[]
        {
            ("missing-authority", valid with { PushAuthority = null }, "PushOperationAuthorityRequired"),
            ("commit-authority", valid with { PushAuthority = ValidPushAuthority(RunAuthorityOperationKind.Commit) }, "PushOperationAuthorityRequired"),
            ("source-apply-authority", valid with { PushAuthority = ValidPushAuthority(RunAuthorityOperationKind.SourceApply) }, "PushOperationAuthorityRequired"),
            ("patch-package-authority", valid with { PushAuthority = ValidPushAuthority(RunAuthorityOperationKind.PatchPackageWrite) }, "PushOperationAuthorityRequired"),
            ("missing-receipt", valid with { CommitReceipt = null }, "ControlledCommitReceiptRequired"),
            ("receipt-branch", valid with { CommitReceipt = GoodCommitReceipt() with { Branch = "other-branch" } }, "CommitReceiptBranchMismatch"),
            ("receipt-commit", valid with { CommitReceipt = GoodCommitReceipt() with { CommitId = "other-commit" } }, "CommitReceiptCommitIdMismatch"),
            ("authority-branch", valid with { PushAuthority = ValidPushAuthority() with { Branch = "other-branch" } }, "PushAuthorityBranchMismatch"),
            ("authority-remote-name", valid with { PushAuthority = ValidPushAuthority() with { RemoteName = "upstream" } }, "PushAuthorityRemoteNameMismatch"),
            ("authority-remote-url", valid with { PushAuthority = ValidPushAuthority() with { RemoteUrl = "https://example.invalid/other/repo" } }, "PushAuthorityRemoteUrlMismatch"),
            ("remote-branch", valid with { RemoteBranch = "other-branch" }, "RemoteBranchMustMatchBranch"),
            ("repo-scope", valid with { Repository = "*" }, "RepositoryMustBeSingleExplicitScope"),
            ("patch-hash", valid with { PatchHash = "latest" }, "PatchHashInvalid")
        };

        foreach (var item in cases)
        {
            var inspector = new FakePushRemoteStateInspector { PreObservations = [GoodPreObservation()] };
            var gateway = new FakeControlledPushGateway();

            var result = await ControlledPushExecutor.ExecuteAsync(item.Request, inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledPushExecutionVerdict.Blocked, result.Verdict, item.Name);
            Assert.IsFalse(result.IsPushExecuted, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(0, inspector.PreCalls, item.Name);
            Assert.AreEqual(0, inspector.PostCalls, item.Name);
            Assert.AreEqual(0, gateway.PushCalls, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockBX_Executor_RemoteBranchMismatchBlocksWithNoDisablePath()
    {
        Assert.IsFalse(
            typeof(ControlledPushExecutionOptions)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(property => string.Equals(property.Name, "RequireRemoteBranchMatchesBranch", StringComparison.OrdinalIgnoreCase)));

        var request = ValidExecutionRequest() with
        {
            RemoteBranch = "main",
            PushAuthority = ValidPushAuthority() with { RemoteBranch = "main" }
        };
        var inspector = new FakePushRemoteStateInspector
        {
            PreObservations = [GoodPreObservation() with { RemoteBranch = "main" }]
        };
        var gateway = new FakeControlledPushGateway();

        var result = await ControlledPushExecutor.ExecuteAsync(
            request,
            inspector,
            gateway,
            new ControlledPushExecutionOptions()).ConfigureAwait(false);

        Assert.AreEqual(ControlledPushExecutionVerdict.Blocked, result.Verdict);
        Assert.IsFalse(result.IsPushExecuted);
        AssertHasIssue(result, "RemoteBranchMustMatchBranch");
        Assert.AreEqual(0, inspector.PreCalls);
        Assert.AreEqual(0, inspector.PostCalls);
        Assert.AreEqual(0, gateway.PushCalls);
        AssertValid(result);
    }

    [TestMethod]
    public async Task BlockBX_Executor_BlocksPrePushRemoteStateDriftBeforeGateway()
    {
        var cases = new (string Name, PushRemoteStateObservation Observation, string ExpectedIssue)[]
        {
            ("unreachable", GoodPreObservation() with { IsRemoteReachable = false }, "PushRemoteObservationFailed"),
            ("repo", GoodPreObservation() with { Repository = "other/repo" }, "PrePushObservationRepositoryMismatch"),
            ("branch", GoodPreObservation() with { Branch = "other-branch" }, "PrePushObservationBranchMismatch"),
            ("remote-name", GoodPreObservation() with { RemoteName = "upstream" }, "PrePushObservationRemoteNameMismatch"),
            ("remote-url", GoodPreObservation() with { RemoteUrl = "https://example.invalid/other/repo" }, "PrePushObservationRemoteUrlMismatch"),
            ("remote-branch", GoodPreObservation() with { RemoteBranch = "other-branch" }, "PrePushObservationRemoteBranchMismatch"),
            ("local-head", GoodPreObservation() with { LocalHeadCommitId = "other-commit" }, "PrePushLocalHeadCommitIdMismatch"),
            ("stale-remote", GoodPreObservation() with { RemoteHeadCommitId = "parent-bx-002" }, "PushRemoteHeadStale"),
            ("uncommitted", GoodPreObservation() with { LocalUncommittedFilePaths = [FilePath] }, "LocalUncommittedFilesExist")
        };

        foreach (var item in cases)
        {
            var inspector = new FakePushRemoteStateInspector { PreObservations = [item.Observation] };
            var gateway = new FakeControlledPushGateway();

            var result = await ControlledPushExecutor.ExecuteAsync(ValidExecutionRequest(), inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledPushExecutionVerdict.Blocked, result.Verdict, item.Name);
            Assert.IsFalse(result.IsPushExecuted, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(1, inspector.PreCalls, item.Name);
            Assert.AreEqual(0, inspector.PostCalls, item.Name);
            Assert.AreEqual(0, gateway.PushCalls, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockBX_Executor_FailsClosedWhenInspectorReturnsNullObservations()
    {
        var preInspector = new FakePushRemoteStateInspector { ReturnNullPreObservation = true };
        var preGateway = new FakeControlledPushGateway();

        var preResult = await ControlledPushExecutor.ExecuteAsync(ValidExecutionRequest(), preInspector, preGateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledPushExecutionVerdict.Blocked, preResult.Verdict);
        Assert.IsFalse(preResult.IsPushExecuted);
        AssertHasIssue(preResult, "PrePushObservationRequired");
        Assert.AreEqual(1, preInspector.PreCalls);
        Assert.AreEqual(0, preInspector.PostCalls);
        Assert.AreEqual(0, preGateway.PushCalls);
        AssertValid(preResult);

        var postInspector = new FakePushRemoteStateInspector
        {
            PreObservations = [GoodPreObservation()],
            ReturnNullPostObservation = true
        };
        var postGateway = new FakeControlledPushGateway();

        var postResult = await ControlledPushExecutor.ExecuteAsync(ValidExecutionRequest(), postInspector, postGateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledPushExecutionVerdict.Failed, postResult.Verdict);
        Assert.IsTrue(postResult.IsPushExecuted);
        AssertHasIssue(postResult, "PostPushObservationRequired");
        Assert.AreEqual(1, postInspector.PreCalls);
        Assert.AreEqual(1, postInspector.PostCalls);
        Assert.AreEqual(1, postGateway.PushCalls);
        AssertValid(postResult);
    }

    [TestMethod]
    public async Task BlockBX_Executor_BlocksUnexpectedLocalCommitsBeforeGateway()
    {
        var cases = new (string Name, IReadOnlyCollection<string>? LocalCommits, string ExpectedIssue)[]
        {
            ("empty", [], "UnexpectedLocalCommits"),
            ("extra", [CommitId, "extra-commit"], "UnexpectedLocalCommits"),
            ("wrong", ["other-commit"], "UnexpectedLocalCommits"),
            ("null", null, "LocalUnpushedCommitIdsRequired")
        };

        foreach (var item in cases)
        {
            var inspector = new FakePushRemoteStateInspector
            {
                PreObservations = [GoodPreObservation() with { LocalUnpushedCommitIds = item.LocalCommits! }]
            };
            var gateway = new FakeControlledPushGateway();

            var result = await ControlledPushExecutor.ExecuteAsync(ValidExecutionRequest(), inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledPushExecutionVerdict.Blocked, result.Verdict, item.Name);
            Assert.IsFalse(result.IsPushExecuted, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(0, gateway.PushCalls, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockBX_Executor_FailsWhenGatewayReturnsInvalidReceipt()
    {
        var cases = new (string Name, ControlledPushReceipt? Receipt, string ExpectedIssue, bool PushExecuted)[]
        {
            ("missing", null, "ControlledPushReceiptRequired", false),
            ("receipt-ref", GoodReceipt() with { ReceiptRef = "receipt:wrong" }, "ControlledPushReceiptRefInvalid", true),
            ("remote-name", GoodReceipt() with { RemoteName = "upstream" }, "PushReceiptRemoteNameMismatch", true),
            ("branch", GoodReceipt() with { Branch = "other-branch" }, "PushReceiptBranchMismatch", true),
            ("commit", GoodReceipt() with { PushedCommitId = "other-commit" }, "PushReceiptCommitIdMismatch", true),
            ("previous-head", GoodReceipt() with { PreviousRemoteHeadCommitId = "parent-bx-002" }, "PushReceiptPreviousRemoteHeadMismatch", true),
            ("new-head", GoodReceipt() with { NewRemoteHeadCommitId = "other-commit" }, "PushReceiptNewRemoteHeadMismatch", true),
            ("force", GoodReceipt() with { ForcePushUsed = true }, "PushReceiptForcePushUsed", true),
            ("tags", GoodReceipt() with { TagsPushed = true }, "PushReceiptTagsPushed", true),
            ("pr", GoodReceipt() with { PullRequestCreationAttempted = true }, "PushReceiptDownstreamAuthorityAttempted", true),
            ("continuation", GoodReceipt() with { ContinuationAttempted = true }, "PushReceiptDownstreamAuthorityAttempted", true)
        };

        foreach (var item in cases)
        {
            var inspector = new FakePushRemoteStateInspector { PreObservations = [GoodPreObservation()] };
            var gateway = new FakeControlledPushGateway { Receipt = item.Receipt };

            var result = await ControlledPushExecutor.ExecuteAsync(ValidExecutionRequest(), inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledPushExecutionVerdict.Failed, result.Verdict, item.Name);
            Assert.AreEqual(item.PushExecuted, result.IsPushExecuted, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(1, gateway.PushCalls, item.Name);
            Assert.AreEqual(0, inspector.PostCalls, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockBX_Executor_FailsWhenPostPushObservationDoesNotVerifyRemoteState()
    {
        var cases = new (string Name, PushPostStateObservation Observation, string ExpectedIssue)[]
        {
            ("not-observed", GoodPostObservation() with { IsObservedAfterPush = false }, "PostPushObservationRequired"),
            ("repo", GoodPostObservation() with { Repository = "other/repo" }, "PostPushObservationRepositoryMismatch"),
            ("branch", GoodPostObservation() with { Branch = "other-branch" }, "PostPushObservationBranchMismatch"),
            ("remote-name", GoodPostObservation() with { RemoteName = "upstream" }, "PostPushObservationRemoteNameMismatch"),
            ("remote-url", GoodPostObservation() with { RemoteUrl = "https://example.invalid/other/repo" }, "PostPushObservationRemoteUrlMismatch"),
            ("remote-branch", GoodPostObservation() with { RemoteBranch = "other-branch" }, "PostPushObservationRemoteBranchMismatch"),
            ("head", GoodPostObservation() with { RemoteHeadCommitId = "other-commit" }, "PostPushRemoteHeadCommitIdMismatch"),
            ("remaining", GoodPostObservation() with { RemainingUnpushedCommitIds = ["extra-commit"] }, "PostPushRemainingUnpushedCommits"),
            ("null-remaining", GoodPostObservation() with { RemainingUnpushedCommitIds = null! }, "PostPushRemainingUnpushedCommitIdsRequired")
        };

        foreach (var item in cases)
        {
            var inspector = new FakePushRemoteStateInspector
            {
                PreObservations = [GoodPreObservation()],
                PostObservations = [item.Observation]
            };
            var gateway = new FakeControlledPushGateway();

            var result = await ControlledPushExecutor.ExecuteAsync(ValidExecutionRequest(), inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledPushExecutionVerdict.Failed, result.Verdict, item.Name);
            Assert.IsTrue(result.IsPushExecuted, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(1, gateway.PushCalls, item.Name);
            Assert.AreEqual(1, inspector.PostCalls, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockBX_PushReceipt_DoesNotImplyDownstreamAuthority()
    {
        var inspector = new FakePushRemoteStateInspector
        {
            PreObservations = [GoodPreObservation()],
            PostObservations = [GoodPostObservation()]
        };
        var gateway = new FakeControlledPushGateway();

        var result = await ControlledPushExecutor.ExecuteAsync(ValidExecutionRequest(), inspector, gateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledPushExecutionVerdict.Completed, result.Verdict);
        Assert.IsNotNull(result.Receipt);
        Assert.IsFalse(result.Receipt!.PullRequestCreationAttempted);
        Assert.IsFalse(result.Receipt.MergeAttempted);
        Assert.IsFalse(result.Receipt.ReleaseAttempted);
        Assert.IsFalse(result.Receipt.DeploymentAttempted);
        Assert.IsFalse(result.Receipt.MemoryWriteAttempted);
        Assert.IsFalse(result.Receipt.ContinuationAttempted);
        foreach (var forbidden in new[]
        {
            "do not create PR from push receipt",
            "do not mark ready for review from push receipt",
            "do not request reviewers from push receipt",
            "do not merge from push receipt",
            "do not release from push receipt",
            "do not deploy from push receipt",
            "do not continue workflow from push receipt",
            "do not promote memory from push receipt"
        })
        {
            AssertContains(result.OperationStatus.ForbiddenActions, forbidden);
        }
    }

    [TestMethod]
    public void BlockBX_StaticControlledMutationSurface_HasNoDirectProcessOrDownstreamSurface()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "IronDev.Core", "Governance", "PushExecution"),
            "*.cs",
            SearchOption.TopDirectoryOnly);
        var text = string.Join(Environment.NewLine, files.Select(File.ReadAllText));

        foreach (var forbidden in new[]
        {
            "Process.Start",
            "File.Write",
            "Directory.CreateDirectory",
            "git",
            "dotnet",
            "tf",
            "cmd.exe",
            "powershell",
            "bash",
            "HttpClient",
            "IGovernanceEventStore",
            "PullRequest execution",
            "Merge execution",
            "Release execution",
            "Deploy execution",
            "WorkflowContinuation",
            "MemoryPromotion"
        })
        {
            Assert.IsFalse(ContainsForbiddenSurface(text, forbidden), forbidden);
        }

        StringAssert.Contains(text, "IControlledPushGateway");
        StringAssert.Contains(text, "IPushRemoteStateInspector");
        StringAssert.Contains(text, "PushAsync");
        StringAssert.Contains(text, "ForcePushDisabled");
        StringAssert.Contains(text, "TagsDisabled");
    }

    [TestMethod]
    public void BlockBX_Contracts_DoNotUseMisleadingDownstreamAuthorityNames()
    {
        var names = new[]
            {
                typeof(ControlledPushExecutionRequest),
                typeof(ControlledPushExecutionResult),
                typeof(ControlledPushExecutor),
                typeof(ControlledPushReceipt),
                typeof(PushAuthorityEvidence),
                typeof(PushRemoteStateObservation),
                typeof(PushPostStateObservation),
                typeof(IControlledPushGateway),
                typeof(IPushRemoteStateInspector),
                typeof(ControlledPushExecutionOptions),
                typeof(ControlledPushGatewayRequest)
            }
            .SelectMany(type => new[] { type.Name }.Concat(type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(member => member.Name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var forbidden in new[]
        {
            "AutoPush",
            "CanMerge",
            "CanDeploy",
            "ContinueWorkflow",
            "PolicySatisfied",
            "ApprovalSatisfied"
        })
        {
            Assert.IsFalse(names.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)), forbidden);
        }
    }

    [TestMethod]
    public void BlockBX_Receipt_RecordsControlledPushBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "BX_CONTROLLED_PUSH_EXECUTOR.md"));

        StringAssert.Contains(doc, "Block BX adds a controlled push executor.");
        StringAssert.Contains(doc, "Commit authority is not push authority.");
        StringAssert.Contains(doc, "Commit receipt is not push authority.");
        StringAssert.Contains(doc, "It does not force push.");
        StringAssert.Contains(doc, "It does not push tags.");
        StringAssert.Contains(doc, "It does not create PRs.");
        StringAssert.Contains(doc, "It does not merge.");
        StringAssert.Contains(doc, "It does not continue workflow.");
        StringAssert.Contains(doc, "Push receipt is not PR, merge, release, deployment, or workflow authority.");
    }

    private static ControlledPushExecutionRequest ValidExecutionRequest() =>
        new()
        {
            ExecutionId = "controlled-push-exec-bx-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            RemoteName = RemoteName,
            RemoteUrl = RemoteUrl,
            RemoteBranch = RemoteBranch,
            ExpectedLocalCommitId = CommitId,
            ExpectedRemoteHeadCommitId = PreviousRemoteHead,
            CommitReceipt = GoodCommitReceipt(),
            PushAuthority = ValidPushAuthority(),
            ObservedAtUtc = ObservedAtUtc,
            EvidenceRefs = ["controlled-push-execution-request:bx-001"],
            ReceiptRefs = []
        };

    private static ControlledCommitReceipt GoodCommitReceipt() =>
        new()
        {
            ReceiptRef = "controlled-commit-receipt:commit-bx-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            PackageId = "commit-package-bx-001",
            CommitId = CommitId,
            ParentCommitId = PreviousRemoteHead,
            CommittedFilePaths = [FilePath],
            CommitSubject = "feat(governance): add controlled push executor",
            CommittedAtUtc = ObservedAtUtc.AddMinutes(-5),
            HooksDisabled = true,
            PushAttempted = false,
            PullRequestCreationAttempted = false,
            MergeAttempted = false,
            ReleaseAttempted = false,
            DeploymentAttempted = false,
            MemoryWriteAttempted = false,
            ContinuationAttempted = false
        };

    private static PushAuthorityEvidence ValidPushAuthority(RunAuthorityOperationKind operationKind = RunAuthorityOperationKind.Push) =>
        new()
        {
            EvidenceRef = "push-operation-authority:bx-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            RemoteName = RemoteName,
            RemoteUrl = RemoteUrl,
            RemoteBranch = RemoteBranch,
            CommitId = CommitId,
            ExpectedRemoteHeadCommitId = PreviousRemoteHead,
            Decision = new OperationEligibilityDecision
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
            }
        };

    private static PushRemoteStateObservation GoodPreObservation() =>
        new()
        {
            Repository = Repository,
            Branch = Branch,
            RemoteName = RemoteName,
            RemoteUrl = RemoteUrl,
            RemoteBranch = RemoteBranch,
            LocalHeadCommitId = CommitId,
            RemoteHeadCommitId = PreviousRemoteHead,
            LocalUnpushedCommitIds = [CommitId],
            LocalUncommittedFilePaths = [],
            IsRemoteReachable = true
        };

    private static ControlledPushReceipt GoodReceipt() =>
        new()
        {
            ReceiptRef = "controlled-push-receipt:push-bx-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            RemoteName = RemoteName,
            RemoteUrl = RemoteUrl,
            RemoteBranch = RemoteBranch,
            PushedCommitId = CommitId,
            PreviousRemoteHeadCommitId = PreviousRemoteHead,
            NewRemoteHeadCommitId = CommitId,
            PushedAtUtc = ObservedAtUtc.AddMinutes(1),
            ForcePushUsed = false,
            TagsPushed = false,
            PullRequestCreationAttempted = false,
            MergeAttempted = false,
            ReleaseAttempted = false,
            DeploymentAttempted = false,
            MemoryWriteAttempted = false,
            ContinuationAttempted = false
        };

    private static PushPostStateObservation GoodPostObservation() =>
        new()
        {
            Repository = Repository,
            Branch = Branch,
            RemoteName = RemoteName,
            RemoteUrl = RemoteUrl,
            RemoteBranch = RemoteBranch,
            RemoteHeadCommitId = CommitId,
            RemainingUnpushedCommitIds = [],
            IsObservedAfterPush = true
        };

    private static void AssertValid(ControlledPushExecutionResult result)
    {
        Assert.IsTrue(result.StatusValidation.IsValid, string.Join(", ", result.StatusValidation.Issues.Concat(result.StatusValidation.RedFlags)));
        Assert.IsFalse(result.StatusValidation.Boundary.CanCommit);
        Assert.IsFalse(result.StatusValidation.Boundary.CanPush);
        Assert.IsFalse(result.StatusValidation.Boundary.CanContinueWorkflow);
        Assert.IsFalse(result.StatusValidation.Boundary.CanMerge);
        Assert.IsFalse(result.StatusValidation.Boundary.CanRelease);
        Assert.IsFalse(result.StatusValidation.Boundary.CanDeploy);
    }

    private static void AssertHasIssue(ControlledPushExecutionResult result, string expected, string? label = null)
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
        if (forbidden is "git" or "dotnet" or "tf")
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

    private sealed class FakePushRemoteStateInspector : IPushRemoteStateInspector
    {
        public PushRemoteStateObservation[] PreObservations { get; init; } = [];
        public PushPostStateObservation[] PostObservations { get; init; } = [];
        public bool ReturnNullPreObservation { get; init; }
        public bool ReturnNullPostObservation { get; init; }
        public int PreCalls { get; private set; }
        public int PostCalls { get; private set; }

        public Task<PushRemoteStateObservation> ObservePrePushAsync(
            ControlledPushExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(PreCalls, Math.Max(PreObservations.Length - 1, 0));
            PreCalls++;
            if (ReturnNullPreObservation)
                return Task.FromResult<PushRemoteStateObservation>(null!);
            return Task.FromResult(PreObservations.Length == 0 ? GoodPreObservation() : PreObservations[index]);
        }

        public Task<PushPostStateObservation> ObservePostPushAsync(
            ControlledPushExecutionRequest request,
            ControlledPushReceipt receipt,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(PostCalls, Math.Max(PostObservations.Length - 1, 0));
            PostCalls++;
            if (ReturnNullPostObservation)
                return Task.FromResult<PushPostStateObservation>(null!);
            return Task.FromResult(PostObservations.Length == 0 ? GoodPostObservation() : PostObservations[index]);
        }
    }

    private sealed class FakeControlledPushGateway : IControlledPushGateway
    {
        public ControlledPushReceipt? Receipt { get; init; } = GoodReceipt();
        public int PushCalls { get; private set; }
        public ControlledPushGatewayRequest? LastRequest { get; private set; }

        public Task<ControlledPushReceipt?> PushAsync(
            ControlledPushGatewayRequest request,
            CancellationToken cancellationToken)
        {
            PushCalls++;
            LastRequest = request;
            return Task.FromResult(Receipt);
        }
    }
}
