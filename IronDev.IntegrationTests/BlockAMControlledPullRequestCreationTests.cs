using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockAMControlledPullRequestCreationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public async Task BlockAM_Cli_WritesRequestTextAndStatusWithoutCreatingPullRequest()
    {
        var root = CreateTempRoot();
        try
        {
            var runPath = Path.Combine(root, "run-am");
            var fixture = WriteCommitPackageArtifacts(runPath);
            var result = await RunCliAsync("pull-request", "request", "--run", runPath, "--repo", "owner/repo", "--base", "main", "--head", "feature/am", "--expected-head", fixture.HeadSha, "--json").ConfigureAwait(false);
            Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
            Assert.AreEqual(0, (await RunCliAsync("pull-request", "text", "--run", runPath, "--json").ConfigureAwait(false)).ExitCode);
            Assert.AreEqual(0, (await RunCliAsync("pull-request", "status", "--run", runPath, "--json").ConfigureAwait(false)).ExitCode);

            foreach (var artifact in new[]
                     {
                         "pull-request-creation-request.json",
                         "pull-request-creation-request.md",
                         "pull-request-text-proposal.json",
                         "pull-request-text-proposal.md",
                         "pull-request-creation-bypass-report.json",
                         "pull-request-creation-bypass-report.md",
                         "governance-events.jsonl"
                     })
            {
                Assert.IsTrue(File.Exists(Path.Combine(runPath, artifact)), artifact);
            }

            Assert.IsFalse(File.Exists(Path.Combine(runPath, "pull-request-created-receipt.json")));
            var request = ReadJson<PullRequestCreationRequest>(Path.Combine(runPath, "pull-request-creation-request.json"));
            Assert.AreEqual("owner/repo", request!.RepositoryFullName);
            Assert.AreEqual("main", request.BaseBranch);
            Assert.AreEqual("feature/am", request.HeadBranch);
            Assert.AreEqual(fixture.HeadSha, request.ExpectedHeadSha);
            Assert.IsTrue(request.DraftRequired);
            Assert.AreEqual(fixture.CommitPackageRequestId, request.CommitPackageRequestId);
            Assert.AreEqual(fixture.CommitReadinessReviewId, request.CommitReadinessReviewId);
            AssertBoundary(request.Boundary);

            var proposal = ReadJson<PullRequestTextProposal>(Path.Combine(runPath, "pull-request-text-proposal.json"));
            StringAssert.Contains(proposal!.ProposedBody, fixture.CommitPackageRequestId);
            StringAssert.Contains(proposal.ProposedBody, "Block AM creates a controlled draft pull request only.");
            Assert.IsFalse(PullRequestTextProposalBuilder.ContainsForbiddenPhrase(proposal.ProposedTitle));
            Assert.IsFalse(PullRequestTextProposalBuilder.ContainsForbiddenPhrase(proposal.ProposedBody));
            AssertBoundary(proposal.Boundary);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public void BlockAM_BranchAndEvidenceValidation_BlockUnsafeOrStaleInputs()
    {
        var fixture = CreateCoreFixture();
        var validBranch = PullRequestBranchValidator.Validate(
            fixture.Request,
            new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.HeadBranch, Exists = true, HeadSha = fixture.Request.ExpectedHeadSha },
            new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.BaseBranch, Exists = true, HeadSha = new string('b', 40) },
            new ExistingPullRequestState());
        Assert.IsTrue(validBranch.Passed, string.Join(",", validBranch.Issues));
        AssertBoundary(validBranch.Boundary);

        CollectionAssert.Contains(PullRequestBranchValidator.Validate(fixture.Request, new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.HeadBranch, Exists = false }, new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.BaseBranch, Exists = true }, new ExistingPullRequestState()).Issues, "RemoteHeadBranchMissing");
        CollectionAssert.Contains(PullRequestBranchValidator.Validate(fixture.Request, new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.HeadBranch, Exists = true, HeadSha = new string('c', 40) }, new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.BaseBranch, Exists = true }, new ExistingPullRequestState()).Issues, "ExpectedHeadShaMismatch");
        CollectionAssert.Contains(PullRequestBranchValidator.Validate(fixture.Request, new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.HeadBranch, Exists = true, HeadSha = fixture.Request.ExpectedHeadSha }, new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.BaseBranch, Exists = false }, new ExistingPullRequestState()).Issues, "BaseBranchMissing");
        CollectionAssert.Contains(PullRequestBranchValidator.Validate(fixture.Request, new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.HeadBranch, Exists = true, HeadSha = fixture.Request.ExpectedHeadSha }, new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.BaseBranch, Exists = true }, new ExistingPullRequestState { Exists = true, PullRequestNumber = 7, Url = "https://example.invalid/pr/7", Draft = true }).Issues, "ExistingOpenPullRequestForHeadBranch");

        var evidence = PullRequestEvidenceValidator.Validate(fixture.Request, fixture.CommitReview, fixture.Bundle, fixture.CommitBoundary, [], []);
        Assert.IsTrue(evidence.Passed, string.Join(",", evidence.Issues));
        AssertBoundary(evidence.Boundary);

        CollectionAssert.Contains(PullRequestEvidenceValidator.Validate(fixture.Request, null, fixture.Bundle, fixture.CommitBoundary, [], []).Issues, "MissingCommitReadinessReview");
        CollectionAssert.Contains(PullRequestEvidenceValidator.Validate(fixture.Request, fixture.CommitReview with { Decision = CommitReadinessDecision.NeedsMoreEvidence }, fixture.Bundle, fixture.CommitBoundary, [], []).Issues, "CommitReadinessReviewNotReady");
        CollectionAssert.Contains(PullRequestEvidenceValidator.Validate(fixture.Request, fixture.CommitReview, fixture.Bundle with { MissingEvidence = ["TestsOrExplicitGap"] }, fixture.CommitBoundary, [], []).Issues, "CommitEvidenceHasBlockingGaps");
        CollectionAssert.Contains(PullRequestEvidenceValidator.Validate(fixture.Request, fixture.CommitReview, fixture.Bundle, fixture.CommitBoundary, ["secret finding"], []).Issues, "UnsafeMaterialFinding");
        CollectionAssert.Contains(PullRequestEvidenceValidator.Validate(fixture.Request, fixture.CommitReview, fixture.Bundle, fixture.CommitBoundary, [], ["artifact mismatch"]).Issues, "ArtifactConsistencyBlocker");
        CollectionAssert.Contains(PullRequestEvidenceValidator.Validate(fixture.Request, fixture.CommitReview, fixture.Bundle, fixture.CommitBoundary with { Boundary = new CommitPackageBoundary { CanCreatePullRequest = true } }, [], []).Issues, "CommitPackageBoundaryClaimsAuthority");
    }

    [TestMethod]
    public void BlockAM_TextProposalAndGate_BlockAuthorityLanguageAndNonDraftRequests()
    {
        var fixture = CreateCoreFixture();
        var proposal = PullRequestTextProposalBuilder.Build(fixture.Request with { Reason = "approved and ready to merge deploy now" }, fixture.Manifest, fixture.Bundle, fixture.CommitReview, ["Risk remains bounded."]);
        Assert.IsFalse(PullRequestTextProposalBuilder.ContainsForbiddenPhrase(proposal.ProposedTitle));
        Assert.IsFalse(PullRequestTextProposalBuilder.ContainsForbiddenPhrase(proposal.ProposedBody));
        Assert.IsTrue(proposal.HumanEditRequired);
        AssertBoundary(proposal.Boundary);

        var branch = PullRequestBranchValidator.Validate(
            fixture.Request,
            new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.HeadBranch, Exists = true, HeadSha = fixture.Request.ExpectedHeadSha },
            new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.BaseBranch, Exists = true, HeadSha = new string('b', 40) },
            new ExistingPullRequestState());
        var evidence = PullRequestEvidenceValidator.Validate(fixture.Request, fixture.CommitReview, fixture.Bundle, fixture.CommitBoundary, [], []);
        var conscience = CreateConscience(fixture.Request, ConscienceDecisionOutcome.Allow);
        var gate = PullRequestCreationGateBuilder.Build(fixture.Request, branch, evidence, proposal, fixture.CommitReview, conscience, "thought-ledger:am");
        Assert.AreEqual(PullRequestCreationGateDecision.CreateDraftPullRequest, gate.Decision);
        Assert.AreEqual(nameof(PullRequestCreationGateDecision.CreateDraftPullRequest), gate.AllowedOperation);
        AssertBoundary(gate.Boundary);

        CollectionAssert.Contains(PullRequestCreationGateBuilder.Build(fixture.Request, branch, evidence, proposal, fixture.CommitReview, null, "thought-ledger:am").Reasons, "MissingConscienceDecision");
        CollectionAssert.Contains(PullRequestCreationGateBuilder.Build(fixture.Request, branch, evidence, proposal, fixture.CommitReview, conscience, "").Reasons, "MissingThoughtLedgerRef");
        CollectionAssert.Contains(PullRequestCreationGateBuilder.Build(fixture.Request with { DraftRequired = false }, branch, evidence, proposal, fixture.CommitReview, conscience, "thought-ledger:am").Reasons, "DraftRequiredMissing");
        CollectionAssert.Contains(PullRequestCreationGateBuilder.Build(fixture.Request with { RequestsReviewers = true }, branch, evidence, proposal, fixture.CommitReview, conscience, "thought-ledger:am").Reasons, "ReviewerRequestForbidden");
        CollectionAssert.Contains(PullRequestCreationGateBuilder.Build(fixture.Request with { RequestsReadyForReview = true }, branch, evidence, proposal, fixture.CommitReview, conscience, "thought-ledger:am").Reasons, "ReadyForReviewRequestForbidden");
        CollectionAssert.Contains(PullRequestCreationGateBuilder.Build(fixture.Request with { RequestsMerge = true }, branch, evidence, proposal, fixture.CommitReview, conscience, "thought-ledger:am").Reasons, "MergeRequestForbidden");
        CollectionAssert.Contains(PullRequestCreationGateBuilder.Build(fixture.Request with { RequestsRelease = true }, branch, evidence, proposal, fixture.CommitReview, conscience, "thought-ledger:am").Reasons, "ReleaseRequestForbidden");
        CollectionAssert.Contains(PullRequestCreationGateBuilder.Build(fixture.Request with { RequestsDeploy = true }, branch, evidence, proposal, fixture.CommitReview, conscience, "thought-ledger:am").Reasons, "DeployRequestForbidden");
        CollectionAssert.Contains(PullRequestCreationGateBuilder.Build(fixture.Request, branch with { Passed = false, Issues = ["ExpectedHeadShaMismatch"] }, evidence, proposal, fixture.CommitReview, conscience, "thought-ledger:am").Reasons, "ExpectedHeadShaMismatch");
    }

    [TestMethod]
    public async Task BlockAM_Executor_CreatesDraftOnlyAfterGateAndRechecksHead()
    {
        var fixture = CreateCoreFixture();
        var branch = PullRequestBranchValidator.Validate(
            fixture.Request,
            new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.HeadBranch, Exists = true, HeadSha = fixture.Request.ExpectedHeadSha },
            new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.BaseBranch, Exists = true, HeadSha = new string('b', 40) },
            new ExistingPullRequestState());
        var evidence = PullRequestEvidenceValidator.Validate(fixture.Request, fixture.CommitReview, fixture.Bundle, fixture.CommitBoundary, [], []);
        var proposal = PullRequestTextProposalBuilder.Build(fixture.Request, fixture.Manifest, fixture.Bundle, fixture.CommitReview);
        var gate = PullRequestCreationGateBuilder.Build(fixture.Request, branch, evidence, proposal, fixture.CommitReview, CreateConscience(fixture.Request, ConscienceDecisionOutcome.Allow), "thought-ledger:am");
        var fake = new FakeDraftPullRequestCreator();

        var result = await DraftPullRequestExecutor.CreateDraftAsync(
            fixture.Request,
            gate,
            proposal,
            new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.HeadBranch, Exists = true, HeadSha = fixture.Request.ExpectedHeadSha },
            new ExistingPullRequestState(),
            fake,
            "tester",
            CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(PullRequestCreationExecutionStatus.Created, result.Status);
        Assert.AreEqual(1, fake.Commands.Count);
        Assert.IsTrue(fake.Commands.Single().Draft);
        Assert.AreEqual("main", fake.Commands.Single().BaseBranch);
        Assert.AreEqual("feature/am", fake.Commands.Single().HeadBranch);
        Assert.IsTrue(result.Receipt!.Draft);
        Assert.AreEqual(123, result.Receipt.PullRequestNumber);
        Assert.IsFalse(result.StatusReport!.MarkedReadyForReview);
        Assert.IsFalse(result.StatusReport.ReviewersRequested);
        Assert.IsFalse(result.StatusReport.Merged);
        Assert.IsFalse(result.StatusReport.Released);
        Assert.IsFalse(result.StatusReport.Deployed);
        Assert.IsFalse(result.StatusReport.WorkflowContinued);
        Assert.IsTrue(result.Boundary.CanCreateDraftPullRequest);
        AssertBoundary(result.Receipt.Boundary, receipt: true);

        var drift = await DraftPullRequestExecutor.CreateDraftAsync(fixture.Request, gate, proposal, new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.HeadBranch, Exists = true, HeadSha = new string('d', 40) }, new ExistingPullRequestState(), new FakeDraftPullRequestCreator(), "tester", CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(PullRequestCreationExecutionStatus.Blocked, drift.Status);
        CollectionAssert.Contains(drift.Issues, "ExpectedHeadShaMismatch");

        var duplicate = await DraftPullRequestExecutor.CreateDraftAsync(fixture.Request, gate, proposal, new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.HeadBranch, Exists = true, HeadSha = fixture.Request.ExpectedHeadSha }, new ExistingPullRequestState { Exists = true, PullRequestNumber = 9 }, new FakeDraftPullRequestCreator(), "tester", CancellationToken.None).ConfigureAwait(false);
        CollectionAssert.Contains(duplicate.Issues, "ExistingOpenPullRequestForHeadBranch");

        var nonDraft = await DraftPullRequestExecutor.CreateDraftAsync(fixture.Request, gate, proposal, new RemoteBranchState { RepositoryFullName = fixture.Request.RepositoryFullName, BranchName = fixture.Request.HeadBranch, Exists = true, HeadSha = fixture.Request.ExpectedHeadSha }, new ExistingPullRequestState(), new FakeDraftPullRequestCreator { ReturnDraft = false }, "tester", CancellationToken.None).ConfigureAwait(false);
        CollectionAssert.Contains(nonDraft.Issues, "DraftPullRequestCreatorReturnedNonDraftPullRequest");
    }

    [TestMethod]
    public async Task BlockAM_Cli_BlocksAuthorityShapedSubcommands()
    {
        foreach (var forbidden in new[] { "create", "create-ready", "ready", "request-reviewers", "approve", "merge", "release", "deploy", "push", "commit", "continue" })
        {
            var result = await RunCliAsync("pull-request", forbidden, "--run", "run-am").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockAM_BypassAndStaticBoundary_ProveEvidenceCannotBecomePrAuthority()
    {
        var bypass = PullRequestCreationBypassEvaluator.Evaluate("run-am", ["commit package request", "commit readiness review", "PR text proposal", "branch validation", "test success", "build success", "artifact consistency report", "release readiness report", "chat text", "AI review text", "memory plan text", "human-looking approval text"]);
        Assert.IsFalse(bypass.PullRequestCreated);
        Assert.IsFalse(bypass.NonDraftPullRequestCreated);
        Assert.IsFalse(bypass.CommitCreated);
        Assert.IsFalse(bypass.PushPerformed);
        Assert.IsFalse(bypass.ReadyForReviewMarked);
        Assert.IsFalse(bypass.ReviewersRequested);
        Assert.IsFalse(bypass.Merged);
        Assert.IsFalse(bypass.Released);
        Assert.IsFalse(bypass.Deployed);
        Assert.IsFalse(bypass.WorkflowContinued);
        Assert.IsFalse(PullRequestCreationBypassEvaluator.CanCreatePullRequest(new object()));
        AssertBoundary(bypass.Boundary);

        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliPullRequest.cs"));
        Assert.IsTrue(cli.Contains("\"pr\", \"create\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("--reviewer", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("ready", StringComparison.OrdinalIgnoreCase) && cli.Contains("RunProcessAsync(\"gh\", [\"pr\", \"ready\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("\"pr\", \"merge\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("\"git\", [\"push\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("\"git\", [\"commit\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("\"git\", [\"add\"", StringComparison.OrdinalIgnoreCase));

        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "PR315_318_CONTROLLED_PULL_REQUEST_CREATION.md"));
        StringAssert.Contains(receipt, "AM1 pull request creation request.");
        StringAssert.Contains(receipt, "AM2 branch and commit evidence validation.");
        StringAssert.Contains(receipt, "AM3 pull request title/body proposal.");
        StringAssert.Contains(receipt, "AM4 draft PR creation gate.");
        StringAssert.Contains(receipt, "AM5 draft PR creation executor and receipt.");
        StringAssert.Contains(receipt, "AM6 bypass tests and receipt.");
        StringAssert.Contains(receipt, "It does not commit.");
        StringAssert.Contains(receipt, "It does not push.");
        StringAssert.Contains(receipt, "It does not create branches.");
        StringAssert.Contains(receipt, "It does not create non-draft PRs.");
        StringAssert.Contains(receipt, "It does not mark ready for review.");
        StringAssert.Contains(receipt, "It does not request reviewers.");
        StringAssert.Contains(receipt, "It does not merge.");
        StringAssert.Contains(receipt, "It does not release.");
        StringAssert.Contains(receipt, "It does not deploy.");
        StringAssert.Contains(receipt, "It does not continue workflow.");
    }

    private static CoreFixture CreateCoreFixture()
    {
        var runId = "run-am-core";
        var headSha = new string('a', 40);
        var commitPackage = BuildCommitPackage(runId, headSha);
        var request = PullRequestCreationRequestWriter.Create(new PullRequestCreationRequestInput
        {
            RunId = runId,
            ProjectId = "project-am",
            RepositoryFullName = "owner/repo",
            BaseBranch = "main",
            HeadBranch = "feature/am",
            ExpectedHeadSha = headSha,
            CommitPackageRequestId = commitPackage.Request.CommitPackageRequestId,
            CommitReadinessReviewId = commitPackage.Review.CommitReadinessReviewId,
            RequestedBy = "tester",
            Reason = "docs/test(governance): add AM controlled pull request creation",
            EvidenceRefs = ["commit-package-request.json", "commit-readiness-review.json"]
        });

        return new CoreFixture(request, commitPackage.Manifest, commitPackage.Bundle, commitPackage.Review, commitPackage.Boundary);
    }

    private static WrittenFixture WriteCommitPackageArtifacts(string runPath)
    {
        Directory.CreateDirectory(runPath);
        var headSha = new string('a', 40);
        var package = BuildCommitPackage(Path.GetFileName(runPath), headSha);
        WriteJson(Path.Combine(runPath, "commit-package-request.json"), package.Request);
        WriteJson(Path.Combine(runPath, "commit-file-manifest.json"), package.Manifest);
        WriteJson(Path.Combine(runPath, "commit-staging-plan.json"), package.StagingPlan);
        WriteJson(Path.Combine(runPath, "commit-evidence-bundle.json"), package.Bundle);
        WriteJson(Path.Combine(runPath, "commit-message-proposal.json"), package.Message);
        WriteJson(Path.Combine(runPath, "commit-readiness-review.json"), package.Review);
        WriteJson(Path.Combine(runPath, "commit-package-boundary-report.json"), package.Boundary);
        WriteJson(Path.Combine(runPath, "commit-package-bypass-report.json"), CommitPackageBypassEvaluator.Evaluate(package.Request.RunId, ["commit package"]));
        File.WriteAllText(Path.Combine(runPath, "known-risks.md"), "Draft PR creation must recheck branch head.");
        File.WriteAllText(Path.Combine(runPath, "unsafe-material-report.json"), "{\"findings\":[]}");
        File.WriteAllText(Path.Combine(runPath, "artifact-consistency-report.json"), "{\"outcome\":\"Pass\",\"issues\":[]}");
        new FileBackedGovernanceEventStore(runPath).Append(
            package.Request.RunId,
            package.Request.CommitPackageRequestId,
            GovernanceKernelEventKind.CommitPackageRequestCreated,
            "CommitPackageRequest",
            package.Request.CommitPackageRequestId,
            "Commit package request was created.",
            ["commit-package-request.json"]);
        return new WrittenFixture(headSha, package.Request.CommitPackageRequestId, package.Review.CommitReadinessReviewId);
    }

    private static CommitPackageFixture BuildCommitPackage(string runId, string headSha)
    {
        var request = CommitPackageRequestWriter.Create(new CommitPackageRequestInput
        {
            RunId = runId,
            ProjectId = "project-am",
            SourceRepoIdentity = "owner/repo",
            SourceRepoPath = "repo",
            BaseCommit = new string('b', 40),
            CurrentHeadCommit = headSha,
            SourceApplyReceiptId = "source-apply-receipt-am",
            PatchHash = "patch-hash",
            PostApplyDiffHash = "diff-hash",
            RequestedBy = "tester",
            Reason = "docs/test(governance): add AM controlled pull request creation",
            EvidenceRefs = ["patch.diff", "changed-files.txt"]
        });
        var manifest = CommitFileManifestBuilder.Build(new CommitFileManifestInput
        {
            RunId = runId,
            SourceRepoIdentity = request.SourceRepoIdentity,
            SourceRepoPath = request.SourceRepoPath,
            BaseCommit = request.BaseCommit,
            CurrentHeadCommit = headSha,
            KnownChangedFiles = ["README.md"],
            ActualChangedFiles = ["README.md"],
            FileHashes = [new CommitFileHash { Path = "README.md", ContentHash = new string('c', 64) }],
            DiffHash = "diff-hash"
        });
        var staging = CommitFileManifestBuilder.BuildStagingPlan(manifest);
        var bundle = CommitEvidenceBundleBuilder.Build(new CommitEvidenceBundleInput
        {
            RunId = runId,
            CommitPackageRequestId = request.CommitPackageRequestId,
            CommitFileManifestId = manifest.CommitFileManifestId,
            AvailableArtifactNames = ["patch.diff", "changed-files.txt", "test-results.txt", "build-results.txt", "artifact-consistency-report.json", "unsafe-material-report.json", "source-post-apply-state.json", "governance-events.jsonl"]
        });
        var message = CommitMessageProposalBuilder.Build(bundle, manifest, request.Reason);
        var review = CommitReadinessReviewer.Review(request, manifest, staging, bundle, message, []);
        var boundary = CommitReadinessReviewer.BuildBoundaryReport(runId);
        return new CommitPackageFixture(request, manifest, staging, bundle, message, review, boundary);
    }

    private static ConscienceDecision CreateConscience(PullRequestCreationRequest request, ConscienceDecisionOutcome outcome)
    {
        var draft = new ConscienceDecision
        {
            DecisionId = $"conscience_{Guid.NewGuid():N}",
            ActionId = $"action_{Guid.NewGuid():N}",
            ActionKind = GovernedActionKind.DraftPullRequestCreation,
            SubjectKind = "PullRequestCreationRequest",
            SubjectId = request.PullRequestCreationRequestId,
            RequestedBy = "tester",
            EvidenceRefs = [new ConscienceDecisionEvidenceRef { RefId = "pr-gate", EvidenceKind = "PullRequestCreationGate", SafeSummary = "Draft PR creation reviewed for AM." }],
            PolicyRefs = ["human-review"],
            RiskLevel = ConscienceDecisionRiskLevel.High,
            Decision = outcome,
            BlockReasons = outcome == ConscienceDecisionOutcome.Allow ? [] : ["blocked"],
            RequiredHumanReview = true,
            ThoughtLedgerRef = "thought-ledger:am",
            DecisionHash = string.Empty,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        return draft with { DecisionHash = ConscienceDecisionHash.Compute(draft) };
    }

    private static void AssertBoundary(PullRequestBoundary boundary, bool receipt = false)
    {
        if (!receipt)
            Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsFalse(boundary.CanCreateNonDraftPullRequest);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanStageFiles);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanForcePush);
        Assert.IsFalse(boundary.CanCreateBranch);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanMarkReadyForReview);
        Assert.IsFalse(boundary.CanRequestReviewers);
        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanContinueWorkflow);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanMutateWorkspace);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
    }

    private static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));

    private static T? ReadJson<T>(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : default;

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await IronDevCli.RunAsync(args, output, error, CancellationToken.None).ConfigureAwait(false);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "irondev-am-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for Windows file handles.
        }
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

    private sealed class FakeDraftPullRequestCreator : IDraftPullRequestCreator
    {
        public List<PullRequestDraftCreateCommand> Commands { get; } = [];
        public bool ReturnDraft { get; init; } = true;

        public Task<PullRequestCreatedResult> CreateDraftPullRequestAsync(PullRequestDraftCreateCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(new PullRequestCreatedResult
            {
                Number = 123,
                Url = "https://github.com/owner/repo/pull/123",
                Draft = ReturnDraft
            });
        }
    }

    private sealed record WrittenFixture(string HeadSha, string CommitPackageRequestId, string CommitReadinessReviewId);
    private sealed record CoreFixture(PullRequestCreationRequest Request, CommitFileManifest Manifest, CommitEvidenceBundle Bundle, CommitReadinessReview CommitReview, CommitPackageBoundaryReport CommitBoundary);
    private sealed record CommitPackageFixture(CommitPackageRequest Request, CommitFileManifest Manifest, CommitStagingPlan StagingPlan, CommitEvidenceBundle Bundle, CommitMessageProposal Message, CommitReadinessReview Review, CommitPackageBoundaryReport Boundary);
}
