using System.Reflection;
using IronDev.Core.Governance;
using IronDev.Core.Governance.CommitExecution;
using BvCommitMessageEvidence = IronDev.Core.Governance.Commit.CommitMessageEvidence;
using BvCommitOperationAuthorityEvidence = IronDev.Core.Governance.Commit.CommitOperationAuthorityEvidence;
using BvCommitPackageBuilder = IronDev.Core.Governance.Commit.CommitPackageBuilder;
using BvCommitPackageRequest = IronDev.Core.Governance.Commit.CommitPackageRequest;
using BvCommitValidationRequirementEvidence = IronDev.Core.Governance.Commit.CommitValidationRequirementEvidence;
using BvExpectedDiffEvidence = IronDev.Core.Governance.Commit.ExpectedDiffEvidence;
using BvSourceApplyReceiptEvidence = IronDev.Core.Governance.Commit.SourceApplyReceiptEvidence;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBWControlledCommitExecutorTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 21, 1, 0, 0, TimeSpan.Zero);
    private const string Repository = "BigDaddyDread-code/IronDeveloper";
    private const string Branch = "commit/controlled-commit-executor";
    private const string RunId = "run-bw-001";
    private const string PatchHash = "sha256:abcdef1234567890";
    private const string DiffHash = "sha256:fedcba0987654321";
    private const string WorktreeRoot = "C:/work/irondev";
    private const string FilePath = "IronDev.Core/Governance/CommitExecution/ControlledCommitExecutor.cs";
    private const string ParentCommitId = "parent-bw-001";
    private const string CommitId = "commit-bw-001";

    [TestMethod]
    public async Task BlockBW_Executor_CreatesExactlyOneCommitAfterAuthorityAndSourceStateChecks()
    {
        var request = ValidExecutionRequest();
        var inspector = new FakeCommitWorktreeInspector
        {
            PreObservations = [GoodPreObservation()],
            PostObservations = [GoodPostObservation()]
        };
        var gateway = new FakeControlledCommitGateway();

        var result = await ControlledCommitExecutor.ExecuteAsync(request, inspector, gateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledCommitExecutionVerdict.Completed, result.Verdict);
        Assert.AreEqual(ControlledCommitFailureKind.None, result.FailureKind);
        Assert.IsTrue(result.IsCommitExecuted);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(GovernedOperationState.Completed, result.OperationStatus.State);
        AssertContains(result.OperationStatus.ReceiptRefs, "controlled-commit-receipt:commit-bw-001");
        AssertContains(result.OperationStatus.ForbiddenActions, "do not push from commit receipt");
        AssertContains(result.OperationStatus.ForbiddenActions, "do not continue workflow from commit receipt");
        Assert.AreEqual(1, inspector.PreCalls);
        Assert.AreEqual(1, inspector.PostCalls);
        Assert.AreEqual(1, gateway.CommitCalls);
        Assert.IsNotNull(gateway.LastRequest);
        Assert.IsTrue(gateway.LastRequest!.DisableHooks);
        CollectionAssert.AreEquivalent(new[] { FilePath }, gateway.LastRequest.FilePathsToStage.ToArray());
        AssertValid(result);
    }

    [TestMethod]
    public async Task BlockBW_Executor_BlocksPreflightBeforeObservationOrGateway()
    {
        var valid = ValidExecutionRequest();
        var cases = new (string Name, ControlledCommitExecutionRequest Request, string ExpectedIssue)[]
        {
            ("invalid-package", valid with { CommitPackageRequest = ValidPackageRequest() with { CommitAuthority = null } }, "CommitOperationAuthorityRequired"),
            ("manifest-repo", valid with { CommitPackageManifest = valid.CommitPackageManifest! with { Repository = "other/repo" } }, "CommitPackageManifestRepositoryMismatch"),
            ("source-receipt", valid with { CommitPackageRequest = ValidPackageRequest() with { SourceApplyReceipt = null } }, "SourceApplyReceiptRequired"),
            ("dirty-diff", valid with { CommitPackageRequest = ValidPackageRequest() with { ExpectedDiff = ValidExpectedDiff() with { IsCleanExpectedDiff = false } } }, "ExpectedDiffNotClean"),
            ("file-set", valid with { ExpectedFilePaths = ["Docs/unexpected.md"] }, "SourceApplyReceiptFileSetMismatch"),
            ("repo-scope", valid with { Repository = "*" }, "RepositoryMustBeSingleExplicitScope"),
            ("patch-hash", valid with { PatchHash = "latest" }, "PatchHashInvalid"),
            ("forbidden-file", valid with { ExpectedFilePaths = ["IronDev.Core/obj/project.assets.json"] }, "ForbiddenFileObserved:IronDev.Core/obj/project.assets.json")
        };

        foreach (var item in cases)
        {
            var inspector = new FakeCommitWorktreeInspector { PreObservations = [GoodPreObservation()] };
            var gateway = new FakeControlledCommitGateway();

            var result = await ControlledCommitExecutor.ExecuteAsync(item.Request, inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledCommitExecutionVerdict.Blocked, result.Verdict, item.Name);
            Assert.IsFalse(result.IsCommitExecuted, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(0, inspector.PreCalls, item.Name);
            Assert.AreEqual(0, inspector.PostCalls, item.Name);
            Assert.AreEqual(0, gateway.CommitCalls, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockBW_Executor_RequiresCommitOperationAuthoritySpecifically()
    {
        foreach (var operationKind in new[]
        {
            RunAuthorityOperationKind.SourceApply,
            RunAuthorityOperationKind.PatchPackageWrite,
            RunAuthorityOperationKind.Push
        })
        {
            var request = ValidExecutionRequest() with
            {
                CommitPackageRequest = ValidPackageRequest() with
                {
                    CommitAuthority = ValidCommitAuthority(operationKind)
                }
            };
            var inspector = new FakeCommitWorktreeInspector();
            var gateway = new FakeControlledCommitGateway();

            var result = await ControlledCommitExecutor.ExecuteAsync(request, inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledCommitExecutionVerdict.Blocked, result.Verdict, operationKind.ToString());
            AssertHasIssue(result, "CommitOperationAuthorityRequired", operationKind.ToString());
            Assert.AreEqual(0, inspector.PreCalls, operationKind.ToString());
            Assert.AreEqual(0, gateway.CommitCalls, operationKind.ToString());
        }
    }

    [TestMethod]
    public async Task BlockBW_Executor_RequiresInspectorAndGateway()
    {
        var request = ValidExecutionRequest();
        var gateway = new FakeControlledCommitGateway();

        var missingInspector = await ControlledCommitExecutor.ExecuteAsync(request, null!, gateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledCommitExecutionVerdict.Blocked, missingInspector.Verdict);
        Assert.IsFalse(missingInspector.IsCommitExecuted);
        AssertHasIssue(missingInspector, "CommitWorktreeInspectorRequired");
        Assert.AreEqual(0, gateway.CommitCalls);
        AssertValid(missingInspector);

        var inspector = new FakeCommitWorktreeInspector { PreObservations = [GoodPreObservation()] };
        var missingGateway = await ControlledCommitExecutor.ExecuteAsync(request, inspector, null!).ConfigureAwait(false);

        Assert.AreEqual(ControlledCommitExecutionVerdict.Blocked, missingGateway.Verdict);
        Assert.IsFalse(missingGateway.IsCommitExecuted);
        AssertHasIssue(missingGateway, "ControlledCommitGatewayRequired");
        Assert.AreEqual(0, inspector.PreCalls);
        Assert.AreEqual(0, inspector.PostCalls);
        AssertValid(missingGateway);
    }

    [TestMethod]
    public async Task BlockBW_Executor_BlocksPreCommitObservationDriftBeforeGateway()
    {
        var cases = new (string Name, CommitWorktreeObservation Observation, string ExpectedIssue)[]
        {
            ("unreadable", GoodPreObservation() with { IsWorktreeReadable = false }, "CommitWorktreeObservationFailed"),
            ("repo", GoodPreObservation() with { Repository = "other/repo" }, "PreCommitObservationRepositoryMismatch"),
            ("branch", GoodPreObservation() with { Branch = "other-branch" }, "PreCommitObservationBranchMismatch"),
            ("head", GoodPreObservation() with { HeadCommitId = "" }, "PreCommitHeadCommitIdRequired"),
            ("diff", GoodPreObservation() with { CurrentDiffHash = "sha256:other" }, "PreCommitDiffHashMismatch"),
            ("changed", GoodPreObservation() with { ChangedFilePaths = [FilePath, "Docs/unexpected.md"] }, "PreCommitChangedFileSetMismatch"),
            ("null-staged", GoodPreObservation() with { StagedFilePaths = null! }, "PreCommitStagedFilePathsRequired"),
            ("staged", GoodPreObservation() with { StagedFilePaths = ["Docs/staged.md"] }, "PreCommitStagedFilesNotEmpty"),
            ("null-untracked", GoodPreObservation() with { UntrackedFilePaths = null! }, "PreCommitUntrackedFilePathsRequired"),
            ("untracked", GoodPreObservation() with { UntrackedFilePaths = ["Docs/new.md"] }, "PreCommitUntrackedFilesNotEmpty"),
            ("forbidden", GoodPreObservation() with { ChangedFilePaths = [FilePath, "IronDev.Core/obj/project.assets.json"] }, "ForbiddenFileObserved:IronDev.Core/obj/project.assets.json")
        };

        foreach (var item in cases)
        {
            var inspector = new FakeCommitWorktreeInspector { PreObservations = [item.Observation] };
            var gateway = new FakeControlledCommitGateway();

            var result = await ControlledCommitExecutor.ExecuteAsync(ValidExecutionRequest(), inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledCommitExecutionVerdict.Blocked, result.Verdict, item.Name);
            Assert.IsFalse(result.IsCommitExecuted, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(1, inspector.PreCalls, item.Name);
            Assert.AreEqual(0, inspector.PostCalls, item.Name);
            Assert.AreEqual(0, gateway.CommitCalls, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockBW_Executor_FailsWhenGatewayReturnsInvalidReceipt()
    {
        var cases = new (string Name, ControlledCommitReceipt? Receipt, string ExpectedIssue, bool CommitExecuted)[]
        {
            ("missing", null, "ControlledCommitReceiptRequired", false),
            ("receipt-ref", GoodReceipt() with { ReceiptRef = "receipt:wrong" }, "ControlledCommitReceiptRefInvalid", true),
            ("parent", GoodReceipt() with { ParentCommitId = "other-parent" }, "CommitReceiptParentCommitIdMismatch", true),
            ("files", GoodReceipt() with { CommittedFilePaths = [FilePath, "Docs/unexpected.md"] }, "CommitReceiptFileSetMismatch", true),
            ("hooks", GoodReceipt() with { HooksDisabled = false }, "CommitReceiptHooksMustBeDisabled", true),
            ("downstream", GoodReceipt() with { PushAttempted = true }, "CommitReceiptDownstreamAuthorityAttempted", true)
        };

        foreach (var item in cases)
        {
            var inspector = new FakeCommitWorktreeInspector { PreObservations = [GoodPreObservation()] };
            var gateway = new FakeControlledCommitGateway { Receipt = item.Receipt };

            var result = await ControlledCommitExecutor.ExecuteAsync(ValidExecutionRequest(), inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledCommitExecutionVerdict.Failed, result.Verdict, item.Name);
            Assert.AreEqual(item.CommitExecuted, result.IsCommitExecuted, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(1, gateway.CommitCalls, item.Name);
            Assert.AreEqual(0, inspector.PostCalls, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockBW_Executor_FailsWhenPostCommitObservationDoesNotVerifyCleanState()
    {
        var cases = new (string Name, CommitPostStateObservation Observation, string ExpectedIssue)[]
        {
            ("not-observed", GoodPostObservation() with { IsObservedAfterCommit = false }, "PostCommitObservationRequired"),
            ("repo", GoodPostObservation() with { Repository = "other/repo" }, "PostCommitObservationRepositoryMismatch"),
            ("branch", GoodPostObservation() with { Branch = "other-branch" }, "PostCommitObservationBranchMismatch"),
            ("head", GoodPostObservation() with { HeadCommitId = "other-commit" }, "PostCommitHeadCommitIdMismatch"),
            ("null-changed", GoodPostObservation() with { RemainingChangedFilePaths = null! }, "PostCommitRemainingChangedFilePathsRequired"),
            ("changed", GoodPostObservation() with { RemainingChangedFilePaths = ["Docs/dirty.md"] }, "PostCommitRemainingChangedFiles"),
            ("null-staged", GoodPostObservation() with { RemainingStagedFilePaths = null! }, "PostCommitRemainingStagedFilePathsRequired"),
            ("staged", GoodPostObservation() with { RemainingStagedFilePaths = ["Docs/staged.md"] }, "PostCommitRemainingStagedFiles"),
            ("null-untracked", GoodPostObservation() with { RemainingUntrackedFilePaths = null! }, "PostCommitRemainingUntrackedFilePathsRequired"),
            ("untracked", GoodPostObservation() with { RemainingUntrackedFilePaths = ["Docs/untracked.md"] }, "PostCommitRemainingUntrackedFiles")
        };

        foreach (var item in cases)
        {
            var inspector = new FakeCommitWorktreeInspector
            {
                PreObservations = [GoodPreObservation()],
                PostObservations = [item.Observation]
            };
            var gateway = new FakeControlledCommitGateway();

            var result = await ControlledCommitExecutor.ExecuteAsync(ValidExecutionRequest(), inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledCommitExecutionVerdict.Failed, result.Verdict, item.Name);
            Assert.IsTrue(result.IsCommitExecuted, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(1, gateway.CommitCalls, item.Name);
            Assert.AreEqual(1, inspector.PostCalls, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockBW_CommitReceipt_DoesNotImplyDownstreamAuthority()
    {
        var inspector = new FakeCommitWorktreeInspector
        {
            PreObservations = [GoodPreObservation()],
            PostObservations = [GoodPostObservation()]
        };
        var gateway = new FakeControlledCommitGateway();

        var result = await ControlledCommitExecutor.ExecuteAsync(ValidExecutionRequest(), inspector, gateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledCommitExecutionVerdict.Completed, result.Verdict);
        Assert.IsNotNull(result.Receipt);
        Assert.IsFalse(result.Receipt!.PushAttempted);
        Assert.IsFalse(result.Receipt.PullRequestCreationAttempted);
        Assert.IsFalse(result.Receipt.MergeAttempted);
        Assert.IsFalse(result.Receipt.ReleaseAttempted);
        Assert.IsFalse(result.Receipt.DeploymentAttempted);
        Assert.IsFalse(result.Receipt.MemoryWriteAttempted);
        Assert.IsFalse(result.Receipt.ContinuationAttempted);
        foreach (var forbidden in new[]
        {
            "do not push from commit receipt",
            "do not create PR from commit receipt",
            "do not merge from commit receipt",
            "do not release from commit receipt",
            "do not deploy from commit receipt",
            "do not continue workflow from commit receipt",
            "do not promote memory from commit receipt"
        })
        {
            AssertContains(result.OperationStatus.ForbiddenActions, forbidden);
        }
    }

    [TestMethod]
    public void BlockBW_StaticControlledMutationSurface_HasNoDirectProcessOrDownstreamSurface()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "IronDev.Core", "Governance", "CommitExecution"),
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
            "Push execution",
            "PR execution",
            "Merge execution",
            "Release execution",
            "Deploy execution",
            "WorkflowContinuation",
            "MemoryPromotion"
        })
        {
            Assert.IsFalse(ContainsForbiddenSurface(text, forbidden), forbidden);
        }

        StringAssert.Contains(text, "ICommitWorktreeInspector");
        StringAssert.Contains(text, "IControlledCommitGateway");
        StringAssert.Contains(text, "FilePathsToStage");
        StringAssert.Contains(text, "DisableHooks");
    }

    [TestMethod]
    public void BlockBW_Contracts_DoNotUseMisleadingDownstreamAuthorityNames()
    {
        var names = new[]
            {
                typeof(ControlledCommitExecutionRequest),
                typeof(ControlledCommitExecutionResult),
                typeof(ControlledCommitExecutor),
                typeof(ControlledCommitReceipt),
                typeof(CommitWorktreeObservation),
                typeof(CommitPostStateObservation),
                typeof(ICommitWorktreeInspector),
                typeof(IControlledCommitGateway),
                typeof(ControlledCommitExecutionOptions),
                typeof(ControlledCommitGatewayRequest)
            }
            .SelectMany(type => new[] { type.Name }.Concat(type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(member => member.Name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var forbidden in new[]
        {
            "AutoCommit",
            "AutoPush",
            "CanPush",
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
    public void BlockBW_Receipt_RecordsControlledCommitBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "BW_CONTROLLED_COMMIT_EXECUTOR.md"));

        StringAssert.Contains(doc, "Block BW adds a controlled commit executor.");
        StringAssert.Contains(doc, "Commit package is not commit execution.");
        StringAssert.Contains(doc, "Source apply receipt is not commit authority.");
        StringAssert.Contains(doc, "Apply authority is not commit authority.");
        StringAssert.Contains(doc, "It does not push.");
        StringAssert.Contains(doc, "It does not create PRs.");
        StringAssert.Contains(doc, "It does not continue workflow.");
        StringAssert.Contains(doc, "A commit is a durable mutation and must pass the same gate as every other mutation.");
    }

    private static ControlledCommitExecutionRequest ValidExecutionRequest()
    {
        var packageRequest = ValidPackageRequest();
        var package = BvCommitPackageBuilder.Build(packageRequest);
        Assert.IsTrue(package.IsPackageCreated, string.Join(", ", package.Issues));
        return new ControlledCommitExecutionRequest
        {
            ExecutionId = "controlled-commit-exec-bw-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            WorktreeRoot = WorktreeRoot,
            CommitPackageRequest = packageRequest,
            CommitPackageManifest = package.Manifest,
            ExpectedFilePaths = [FilePath],
            ExpectedDiffHash = DiffHash,
            ObservedAtUtc = ObservedAtUtc,
            EvidenceRefs = ["commit-execution-request:bw-001"],
            ReceiptRefs = []
        };
    }

    private static BvCommitPackageRequest ValidPackageRequest() =>
        new()
        {
            PackageId = "commit-package-bw-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            SourceApplyReceipt = ValidSourceApplyReceipt(),
            ExpectedDiff = ValidExpectedDiff(),
            CommitAuthority = ValidCommitAuthority(RunAuthorityOperationKind.Commit),
            MessageEvidence = ValidMessage(),
            ValidationRequirement = ValidValidationRequirement(),
            ObservedAtUtc = ObservedAtUtc,
            EvidenceRefs = ["operation-eligibility-decision:commit-bw-001"],
            ReceiptRefs = []
        };

    private static BvSourceApplyReceiptEvidence ValidSourceApplyReceipt() =>
        new()
        {
            ReceiptRef = "source-apply-receipt:source-apply-bw-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            AppliedFilePaths = [FilePath],
            AppliedAtUtc = ObservedAtUtc.AddMinutes(-20),
            AppliedByAuthorityPath = "BoundedRunAuthority"
        };

    private static BvExpectedDiffEvidence ValidExpectedDiff() =>
        new()
        {
            EvidenceRef = "expected-diff:diff-bw-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            ExpectedDiffHash = DiffHash,
            ExpectedChangedFilePaths = [FilePath],
            IsCleanExpectedDiff = true
        };

    private static BvCommitOperationAuthorityEvidence ValidCommitAuthority(RunAuthorityOperationKind operationKind) =>
        new()
        {
            EvidenceRef = "commit-operation-authority:bw-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            FilePaths = [FilePath],
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

    private static BvCommitMessageEvidence ValidMessage() =>
        new()
        {
            EvidenceRef = "commit-message:message-bw-001",
            Subject = "feat(governance): add controlled commit executor",
            Body = "Controlled commit executor evidence.",
            MessageSource = "HumanProvided"
        };

    private static BvCommitValidationRequirementEvidence ValidValidationRequirement() =>
        new()
        {
            IsSatisfied = true,
            IsExplicitlyBlocked = false,
            ValidationEvidenceRefs = ["validation-result:focused-bw"],
            BlockedReasons = []
        };

    private static CommitWorktreeObservation GoodPreObservation() =>
        new()
        {
            Repository = Repository,
            Branch = Branch,
            WorktreeRoot = WorktreeRoot,
            HeadCommitId = ParentCommitId,
            CurrentDiffHash = DiffHash,
            ChangedFilePaths = [FilePath],
            StagedFilePaths = [],
            UntrackedFilePaths = [],
            IsWorktreeReadable = true
        };

    private static CommitPostStateObservation GoodPostObservation() =>
        new()
        {
            Repository = Repository,
            Branch = Branch,
            HeadCommitId = CommitId,
            RemainingChangedFilePaths = [],
            RemainingStagedFilePaths = [],
            RemainingUntrackedFilePaths = [],
            IsObservedAfterCommit = true
        };

    private static ControlledCommitReceipt GoodReceipt() =>
        new()
        {
            ReceiptRef = "controlled-commit-receipt:commit-bw-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            PackageId = "commit-package-bw-001",
            CommitId = CommitId,
            ParentCommitId = ParentCommitId,
            CommittedFilePaths = [FilePath],
            CommitSubject = "feat(governance): add controlled commit executor",
            CommittedAtUtc = ObservedAtUtc.AddMinutes(1),
            HooksDisabled = true,
            PushAttempted = false,
            PullRequestCreationAttempted = false,
            MergeAttempted = false,
            ReleaseAttempted = false,
            DeploymentAttempted = false,
            MemoryWriteAttempted = false,
            ContinuationAttempted = false
        };

    private static void AssertValid(ControlledCommitExecutionResult result)
    {
        Assert.IsTrue(result.StatusValidation.IsValid, string.Join(", ", result.StatusValidation.Issues.Concat(result.StatusValidation.RedFlags)));
        Assert.IsFalse(result.StatusValidation.Boundary.CanPush);
        Assert.IsFalse(result.StatusValidation.Boundary.CanContinueWorkflow);
        Assert.IsFalse(result.StatusValidation.Boundary.CanMerge);
        Assert.IsFalse(result.StatusValidation.Boundary.CanRelease);
        Assert.IsFalse(result.StatusValidation.Boundary.CanDeploy);
    }

    private static void AssertHasIssue(ControlledCommitExecutionResult result, string expected, string? label = null)
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

    private sealed class FakeCommitWorktreeInspector : ICommitWorktreeInspector
    {
        public CommitWorktreeObservation[] PreObservations { get; init; } = [];
        public CommitPostStateObservation[] PostObservations { get; init; } = [];
        public int PreCalls { get; private set; }
        public int PostCalls { get; private set; }

        public Task<CommitWorktreeObservation> ObservePreCommitAsync(
            ControlledCommitExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(PreCalls, Math.Max(PreObservations.Length - 1, 0));
            PreCalls++;
            return Task.FromResult(PreObservations.Length == 0 ? GoodPreObservation() : PreObservations[index]);
        }

        public Task<CommitPostStateObservation> ObservePostCommitAsync(
            ControlledCommitExecutionRequest request,
            ControlledCommitReceipt receipt,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(PostCalls, Math.Max(PostObservations.Length - 1, 0));
            PostCalls++;
            return Task.FromResult(PostObservations.Length == 0 ? GoodPostObservation() : PostObservations[index]);
        }
    }

    private sealed class FakeControlledCommitGateway : IControlledCommitGateway
    {
        public ControlledCommitReceipt? Receipt { get; init; } = GoodReceipt();
        public int CommitCalls { get; private set; }
        public ControlledCommitGatewayRequest? LastRequest { get; private set; }

        public Task<ControlledCommitReceipt?> CommitAsync(
            ControlledCommitGatewayRequest request,
            CancellationToken cancellationToken)
        {
            CommitCalls++;
            LastRequest = request;
            return Task.FromResult(Receipt);
        }
    }
}
