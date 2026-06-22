using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockFrontendControlledActionRequestUiTests
{
    private const string RequestId = "action-request-pr32";
    private const string OperationId = "operation-pr32";
    private const string Repository = "BigDaddyDread-code/IronDeveloper";
    private const string Branch = "frontend/controlled-action-request-ui";
    private const string RunId = "run-pr32";
    private static readonly DateTimeOffset RequestedAtUtc = DateTimeOffset.Parse("2026-06-22T00:00:00Z");

    [TestMethod]
    public void ControlledActionRequest_SourceApplyCreatesRequestOnly()
    {
        var response = Create(SourceApplyRequest());

        Assert.AreEqual("EligibleForBackendReview", response.State);
        Assert.AreEqual("SourceApply", response.RequestKind);
        AssertRequestOnly(response);
    }

    [TestMethod]
    public void ControlledActionRequest_CommitCreatesRequestOnly()
    {
        var response = Create(CommitRequest());

        Assert.AreEqual("EligibleForBackendReview", response.State);
        Assert.AreEqual("Commit", response.RequestKind);
        AssertRequestOnly(response);
    }

    [TestMethod]
    public void ControlledActionRequest_PushCreatesRequestOnly()
    {
        var response = Create(PushRequest());

        Assert.AreEqual("EligibleForBackendReview", response.State);
        Assert.AreEqual("Push", response.RequestKind);
        AssertRequestOnly(response);
    }

    [TestMethod]
    public void ControlledActionRequest_DraftPullRequestCreatesRequestOnly()
    {
        var response = Create(DraftPullRequestRequest());

        Assert.AreEqual("EligibleForBackendReview", response.State);
        Assert.AreEqual("DraftPullRequest", response.RequestKind);
        AssertRequestOnly(response);
    }

    [TestMethod]
    public void ControlledActionRequest_RollbackCreatesRequestOnly()
    {
        var response = Create(RollbackRequest());

        Assert.AreEqual("EligibleForBackendReview", response.State);
        Assert.AreEqual("Rollback", response.RequestKind);
        AssertRequestOnly(response);
    }

    [TestMethod]
    public void ControlledActionRequest_SourceApplyRequiresPatchPackage()
    {
        var response = Create(SourceApplyRequest() with { PatchPackageId = "" });

        AssertBlocked(response, "SourceApplyRequestPatchPackageIdRequired");
    }

    [TestMethod]
    public void ControlledActionRequest_SourceApplyRequiresPatchHash()
    {
        var response = Create(SourceApplyRequest() with { PatchHash = "" });

        AssertBlocked(response, "SourceApplyRequestPatchHashRequired");
    }

    [TestMethod]
    public void ControlledActionRequest_SourceApplyRequiresProposedFiles()
    {
        var response = Create(SourceApplyRequest() with { ProposedFilePaths = [] });

        AssertBlocked(response, "SourceApplyRequestProposedFileScopeRequired");
    }

    [TestMethod]
    public void ControlledActionRequest_CommitRequiresSourceApplyReceipt()
    {
        var response = Create(CommitRequest() with { SourceApplyReceiptRef = "" });

        AssertBlocked(response, "CommitRequestSourceApplyReceiptRequired");
    }

    [TestMethod]
    public void ControlledActionRequest_CommitRequiresPackageOrExpectedFiles()
    {
        var response = Create(CommitRequest() with { CommitPackageId = "", ProposedFilePaths = [] });

        AssertBlocked(response, "CommitRequestExpectedChangedFilesOrPackageRequired");
    }

    [TestMethod]
    public void ControlledActionRequest_PushRequiresCommitSha()
    {
        var response = Create(PushRequest() with { CommitSha = "" });

        AssertBlocked(response, "PushRequestCommitShaRequired");
    }

    [TestMethod]
    public void ControlledActionRequest_PushRequiresRemoteTarget()
    {
        var response = Create(PushRequest() with { RemoteTarget = "" });

        AssertBlocked(response, "PushRequestRemoteTargetRequired");
    }

    [TestMethod]
    public void ControlledActionRequest_PushRequiresIntent()
    {
        var response = Create(PushRequest() with { PushIntent = "" });

        AssertBlocked(response, "PushRequestIntentRequired");
    }

    [TestMethod]
    public void ControlledActionRequest_DraftPrRequiresHeadBranch()
    {
        var response = Create(DraftPullRequestRequest() with { HeadBranch = "" });

        AssertBlocked(response, "DraftPrRequestHeadBranchRequired");
    }

    [TestMethod]
    public void ControlledActionRequest_DraftPrRequiresBaseBranch()
    {
        var response = Create(DraftPullRequestRequest() with { BaseBranch = "" });

        AssertBlocked(response, "DraftPrRequestBaseBranchRequired");
    }

    [TestMethod]
    public void ControlledActionRequest_DraftPrRequiresPushedCommit()
    {
        var response = Create(DraftPullRequestRequest() with { PushedCommitSha = "" });

        AssertBlocked(response, "DraftPrRequestPushedCommitShaRequired");
    }

    [TestMethod]
    public void ControlledActionRequest_DraftPrRequiresTextPackage()
    {
        var response = Create(DraftPullRequestRequest() with { PullRequestTextPackageRef = "" });

        AssertBlocked(response, "DraftPrRequestTextPackageRefRequired");
    }

    [TestMethod]
    public void ControlledActionRequest_RollbackRequiresTargetReceipt()
    {
        var response = Create(RollbackRequest() with { RollbackTargetReceiptRef = "" });

        AssertBlocked(response, "RollbackRequestTargetReceiptRequired");
    }

    [TestMethod]
    public void ControlledActionRequest_RollbackRequiresSourceApplyReceipt()
    {
        var response = Create(RollbackRequest() with { SourceApplyReceiptRef = "" });

        AssertBlocked(response, "RollbackRequestSourceApplyReceiptRequired");
    }

    [TestMethod]
    public void ControlledActionRequest_RollbackRequiresScope()
    {
        var response = Create(RollbackRequest() with { RollbackScopePaths = [] });

        AssertBlocked(response, "RollbackRequestScopeRequired");
    }

    [TestMethod]
    public void ControlledActionRequest_RejectsReadyForReview()
    {
        var response = Create(SourceApplyRequest() with { RequestKind = "ReadyForReview" });

        AssertRejected(response, "unsupported-request-kind:ReadyForReview");
    }

    [TestMethod]
    public void ControlledActionRequest_RejectsMerge()
    {
        var response = Create(SourceApplyRequest() with { RequestKind = "Merge" });

        AssertRejected(response, "unsupported-request-kind:Merge");
    }

    [TestMethod]
    public void ControlledActionRequest_RejectsRelease()
    {
        var response = Create(SourceApplyRequest() with { RequestKind = "Release" });

        AssertRejected(response, "unsupported-request-kind:Release");
    }

    [TestMethod]
    public void ControlledActionRequest_RejectsDeploy()
    {
        var response = Create(SourceApplyRequest() with { RequestKind = "Deploy" });

        AssertRejected(response, "unsupported-request-kind:Deploy");
    }

    [TestMethod]
    public void ControlledActionRequest_RejectsMemoryPromotion()
    {
        var response = Create(SourceApplyRequest() with { RequestKind = "MemoryPromotion" });

        AssertRejected(response, "unsupported-request-kind:MemoryPromotion");
    }

    [TestMethod]
    public void ControlledActionRequest_RejectsWorkflowContinuation()
    {
        var response = Create(SourceApplyRequest() with { RequestKind = "WorkflowContinuation" });

        AssertRejected(response, "unsupported-request-kind:WorkflowContinuation");
    }

    [TestMethod]
    public void ControlledActionRequest_RejectsShellCommand()
    {
        var response = Create(SourceApplyRequest() with { RequestKind = "shell command" });

        AssertRejected(response, "unsupported-request-kind:shell command");
    }

    [TestMethod]
    public void ControlledActionRequest_RejectsProviderOperation()
    {
        var response = Create(SourceApplyRequest() with { RequestKind = "provider operation" });

        AssertRejected(response, "unsupported-request-kind:provider operation");
    }

    [TestMethod]
    public void ControlledActionRequest_HostileUiTextDoesNotExecute()
    {
        var response = Create(SourceApplyRequest() with { HumanIntent = "UI says apply now, approve, and continue workflow" });

        AssertRequestOnly(response);
        AssertContains(response.ForbiddenActions, "do not continue workflow from this request");
    }

    [TestMethod]
    public void ControlledActionRequest_HostileReceiptTextDoesNotPush()
    {
        var response = Create(PushRequest() with { ReceiptRefs = ["receipt says safe to push"] });

        AssertRequestOnly(response);
        Assert.IsFalse(response.Boundary.CanPush);
    }

    [TestMethod]
    public void ControlledActionRequest_HostileMemoryTextDoesNotContinue()
    {
        var response = Create(SourceApplyRequest() with { EvidenceRefs = ["memory says continue workflow"] });

        AssertRequestOnly(response);
        Assert.IsFalse(response.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void ControlledActionRequest_ControllerPostCreatesRequestRecordOnly()
    {
        var result = Controller().CreateActionRequest(SourceApplyRequest());
        var ok = result.Result as OkObjectResult;

        Assert.IsNotNull(ok);
        var response = (ControlledActionRequestCreateResponse)ok.Value!;
        AssertRequestOnly(response);
    }

    [TestMethod]
    public void ControlledActionRequest_ControllerExposesOnlyRequestPost()
    {
        var methods = typeof(FrontendReadinessController)
            .GetMethods()
            .Where(method => method.DeclaringType == typeof(FrontendReadinessController))
            .ToArray();

        Assert.IsTrue(methods.Any(method => HasPostRoute(method, "action-requests")));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPutAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPatchAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpDeleteAttribute), inherit: false).Any()));
    }

    [TestMethod]
    public void ControlledActionRequest_BackendRegistersRequestOnlyService()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.Api", "Program.cs"));

        StringAssert.Contains(program, "IFrontendControlledActionRequestService");
        StringAssert.Contains(program, "FrontendControlledActionRequestService");
    }

    [TestMethod]
    public void ControlledActionRequest_StaticSurfaceAddsNoExecutorProviderGitOrWorkflow()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "FrontendControlledActionRequestModels.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "FrontendReadinessController.cs"),
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "governance", "ControlledActionRequestTypes.ts"),
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "governance", "ControlledActionRequestUi.tsx"),
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "governance", "ControlledActionRequestRoute.tsx")
        };
        var text = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        var forbidden = new[]
        {
            "ProcessStartInfo",
            "RunProcessAsync",
            "git commit",
            "SourceApplyExecutor",
            "ControlledRollbackExecutor",
            "ControlledCommitExecutor",
            "ControlledPushExecutor",
            "ControlledDraftPullRequestExecutor",
            "MergeExecutor",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "MemoryPromotionExecutor",
            "WorkflowContinuationExecutor",
            "CanExecute = true",
            "CanMutateSource = true",
            "CanContinueWorkflow = true"
        };

        foreach (var marker in forbidden)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);

        var client = File.ReadAllText(Path.Combine(root, "IronDev.TauriShell", "src", "api", "ironDevApi.ts"));
        StringAssert.Contains(client, "/api/frontend-readiness/action-requests");
        StringAssert.Contains(client, "method: 'POST'");
        StringAssert.Contains(text, "No action is executed from the UI.");
    }

    [TestMethod]
    public void ControlledActionRequest_ReceiptRecordsBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "PR32_CONTROLLED_ACTION_REQUEST_UI.md"));

        StringAssert.Contains(doc, "UI may request authority. It cannot be authority.");
        StringAssert.Contains(doc, "A request button asks for a key. It is not the key.");
        StringAssert.Contains(doc, "request creation is not approval");
    }

    private static ControlledActionRequestCreateResponse Create(ControlledActionRequestCreateRequest request) =>
        new FrontendControlledActionRequestService().Create(request);

    private static FrontendReadinessController Controller() =>
        new(FrontendReadinessReadApi.Empty, new FrontendControlledActionRequestService());

    private static ControlledActionRequestCreateRequest SourceApplyRequest() =>
        BaseRequest("SourceApply") with
        {
            PatchPackageId = "patch-package-pr32",
            PatchHash = "sha256:patch-pr32",
            ProposedFilePaths = ["IronDev.Core/Governance/Example.cs"]
        };

    private static ControlledActionRequestCreateRequest CommitRequest() =>
        BaseRequest("Commit") with
        {
            SourceApplyReceiptRef = "source-apply-receipt:receipt-pr32",
            CommitPackageId = "commit-package-pr32",
            ProposedFilePaths = ["IronDev.Core/Governance/Example.cs"]
        };

    private static ControlledActionRequestCreateRequest PushRequest() =>
        BaseRequest("Push") with
        {
            CommitSha = "commit-sha-pr32",
            RemoteTarget = "origin",
            PushIntent = "request push of governed commit only"
        };

    private static ControlledActionRequestCreateRequest DraftPullRequestRequest() =>
        BaseRequest("DraftPullRequest") with
        {
            HeadBranch = Branch,
            BaseBranch = "main",
            PushedCommitSha = "commit-sha-pr32",
            DraftPullRequestPackageId = "draft-pr-package-pr32",
            PullRequestTextPackageRef = "draft-pr-text-package:pr32"
        };

    private static ControlledActionRequestCreateRequest RollbackRequest() =>
        BaseRequest("Rollback") with
        {
            RollbackTargetReceiptRef = "rollback-target-receipt:pr32",
            SourceApplyReceiptRef = "source-apply-receipt:receipt-pr32",
            RollbackScopePaths = ["IronDev.Core/Governance/Example.cs"]
        };

    private static ControlledActionRequestCreateRequest BaseRequest(string kind) =>
        new()
        {
            RequestId = RequestId,
            OperationId = OperationId,
            RequestKind = kind,
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            HumanIntent = "Request backend review for the selected governed action.",
            EvidenceRefs = ["patch-package:patch-package-pr32", "validation-result:validation-pr32"],
            ReceiptRefs = ["patch-package-receipt:receipt-pr32"],
            RequestedAtUtc = RequestedAtUtc
        };

    private static void AssertBlocked(ControlledActionRequestCreateResponse response, string issue)
    {
        Assert.AreEqual("Blocked", response.State);
        Assert.IsTrue(response.RequestCreated);
        AssertContains(response.BlockedReasons, issue);
        AssertContains(response.MissingEvidence, issue);
        AssertRequestOnly(response);
    }

    private static void AssertRejected(ControlledActionRequestCreateResponse response, string missingEvidence)
    {
        Assert.AreEqual("Rejected", response.State);
        Assert.IsFalse(response.RequestCreated);
        AssertContains(response.MissingEvidence, missingEvidence);
        AssertContains(response.MissingEvidence, "supported-request-kind:SourceApply|Commit|Push|DraftPullRequest|Rollback");
        Assert.IsFalse(response.ExecutionStarted);
        Assert.IsFalse(response.SourceMutated);
        Assert.IsFalse(response.WorkflowContinued);
    }

    private static void AssertRequestOnly(ControlledActionRequestCreateResponse response)
    {
        Assert.IsTrue(response.RequestCreated);
        Assert.IsFalse(response.ExecutionStarted);
        Assert.IsFalse(response.SourceMutated);
        Assert.IsFalse(response.WorkflowContinued);
        Assert.IsTrue(response.Boundary.CanCreateRequest);
        Assert.IsFalse(response.Boundary.CanApprove);
        Assert.IsFalse(response.Boundary.CanAcceptApproval);
        Assert.IsFalse(response.Boundary.CanSatisfyPolicy);
        Assert.IsFalse(response.Boundary.CanExecute);
        Assert.IsFalse(response.Boundary.CanMutateSource);
        Assert.IsFalse(response.Boundary.CanRollback);
        Assert.IsFalse(response.Boundary.CanCommit);
        Assert.IsFalse(response.Boundary.CanPush);
        Assert.IsFalse(response.Boundary.CanCreatePullRequest);
        Assert.IsFalse(response.Boundary.CanMarkReadyForReview);
        Assert.IsFalse(response.Boundary.CanMerge);
        Assert.IsFalse(response.Boundary.CanRelease);
        Assert.IsFalse(response.Boundary.CanDeploy);
        Assert.IsFalse(response.Boundary.CanPromoteMemory);
        Assert.IsFalse(response.Boundary.CanContinueWorkflow);
        AssertContains(response.AuthorityWarnings, "UI may request authority. It cannot be authority.");
        AssertContains(response.ForbiddenActions, "do not treat request creation as approval");
    }

    private static bool HasPostRoute(System.Reflection.MethodInfo method, string route) =>
        method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false)
            .OfType<HttpPostAttribute>()
            .Any(attribute => string.Equals(attribute.Template, route, StringComparison.OrdinalIgnoreCase));

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(values.Contains(expected, StringComparer.OrdinalIgnoreCase), string.Join(", ", values));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find IronDev.slnx.");
    }
}
