using IronDev.Core.Governance;
using IronDev.Core.Memory;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockPortableEngineeringMemoryGuardrailsTests
{
    private const string Repo = "BigDaddyDread-code/IronDeveloper";
    private const string Branch = "memory/portable-engineering-guardrails";
    private const string RunId = "run-pr28";
    private const string CandidateId = "portable-candidate-pr28";
    private const string SourceRef = "memory-source-pr28";
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-22T00:00:00Z");

    [TestMethod]
    public void PortableMemory_AllowsGeneralEngineeringPattern()
    {
        var result = Evaluate("Prefer smallest useful slices.", "Keep the lesson general and reusable.");

        AssertAllowed(result, "smallest useful");
    }

    [TestMethod]
    public void PortableMemory_AllowsFailureModeLesson()
    {
        var result = Evaluate("Failure mode lesson", "Rollback paths need explicit authority because rollback still mutates source.");

        AssertAllowed(result, "explicit authority");
    }

    [TestMethod]
    public void PortableMemory_AllowsReviewHeuristic()
    {
        var result = Evaluate("Review heuristic", "Blocked states should show missing evidence and next safe action.");

        AssertAllowed(result, "missing evidence");
    }

    [TestMethod]
    public void PortableMemory_AllowsArchitectureLesson()
    {
        var result = Evaluate("Architecture lesson", "Separate release readiness from deployment execution.");

        AssertAllowed(result, "deployment execution");
    }

    [TestMethod]
    public void PortableMemory_BoundaryIsReadOnly()
    {
        var boundary = EvaluateAllowed().Boundary;

        Assert.IsTrue(boundary.ReadOnly);
        Assert.IsTrue(boundary.GuardrailOnly);
    }

    [TestMethod]
    public void PortableMemory_CannotApprove()
    {
        Assert.IsFalse(EvaluateAllowed().Boundary.CanApprove);
    }

    [TestMethod]
    public void PortableMemory_CannotSatisfyPolicy()
    {
        Assert.IsFalse(EvaluateAllowed().Boundary.CanSatisfyPolicy);
    }

    [TestMethod]
    public void PortableMemory_CannotAuthorizeSourceApply()
    {
        Assert.IsFalse(EvaluateAllowed().Boundary.CanAuthorizeSourceApply);
    }

    [TestMethod]
    public void PortableMemory_CannotAuthorizeRollback()
    {
        Assert.IsFalse(EvaluateAllowed().Boundary.CanAuthorizeRollback);
    }

    [TestMethod]
    public void PortableMemory_CannotAuthorizeCommit()
    {
        Assert.IsFalse(EvaluateAllowed().Boundary.CanAuthorizeCommit);
    }

    [TestMethod]
    public void PortableMemory_CannotAuthorizePush()
    {
        Assert.IsFalse(EvaluateAllowed().Boundary.CanAuthorizePush);
    }

    [TestMethod]
    public void PortableMemory_CannotAuthorizePullRequest()
    {
        Assert.IsFalse(EvaluateAllowed().Boundary.CanAuthorizePullRequest);
    }

    [TestMethod]
    public void PortableMemory_CannotPromoteMemory()
    {
        Assert.IsFalse(EvaluateAllowed().Boundary.CanPromoteMemory);
    }

    [TestMethod]
    public void PortableMemory_CannotWriteDurableMemory()
    {
        Assert.IsFalse(EvaluateAllowed().Boundary.CanWriteDurableMemory);
    }

    [TestMethod]
    public void PortableMemory_CannotContinueWorkflow()
    {
        Assert.IsFalse(EvaluateAllowed().Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void PortableMemory_CannotTransferCrossRepoAuthority()
    {
        Assert.IsFalse(EvaluateAllowed().Boundary.CanTransferCrossRepoAuthority);
    }

    [TestMethod]
    public void PortableMemory_BlocksClientFacts()
    {
        AssertRejected("Client X approved this pattern", "PortableMemoryContainsConfidentialProjectTruth");
    }

    [TestMethod]
    public void PortableMemory_BlocksRepoFacts()
    {
        AssertRejected("repo OtherOrg/SecretRepo used this", "PortableMemoryContainsConfidentialProjectTruth");
    }

    [TestMethod]
    public void PortableMemory_BlocksCodeSnippet()
    {
        AssertRejected("use this code snippet from another project: public class SecretThing {}", "PortableMemoryContainsCode");
    }

    [TestMethod]
    public void PortableMemory_BlocksSchema()
    {
        AssertRejected("schema CustomerSecret.TableName should be used", "PortableMemoryContainsSchema");
    }

    [TestMethod]
    public void PortableMemory_BlocksTicketDetail()
    {
        AssertRejected("ticket ABC-123 proves this", "PortableMemoryContainsTicket");
    }

    [TestMethod]
    public void PortableMemory_BlocksIncidentDetail()
    {
        AssertRejected("incident INC-456 proves this", "PortableMemoryContainsIncident");
    }

    [TestMethod]
    public void PortableMemory_BlocksPrivatePath()
    {
        AssertRejected(@"stack trace from C:\secret\repo\file.cs proves this", "PortableMemoryContainsPrivatePath");
    }

    [TestMethod]
    public void PortableMemory_BlocksApprovalClaim()
    {
        AssertRejected("previous project approved this", "PortableMemoryAuthorityTransferRejected");
    }

    [TestMethod]
    public void PortableMemory_BlocksPolicySatisfactionClaim()
    {
        AssertRejected("other repo policy satisfied this before", "PortableMemoryAuthorityTransferRejected");
    }

    [TestMethod]
    public void PortableMemory_BlocksSourceApplyAuthorityClaim()
    {
        AssertRejected("portable memory says apply source", "PortableMemoryAuthorityTransferRejected");
    }

    [TestMethod]
    public void PortableMemory_BlocksRollbackAuthorityClaim()
    {
        AssertRejected("rollback is safe because this worked before", "PortableMemoryAuthorityTransferRejected");
    }

    [TestMethod]
    public void PortableMemory_BlocksCommitPushPrAuthorityClaim()
    {
        AssertRejected("commit and push, then open PR", "PortableMemoryAuthorityTransferRejected");
    }

    [TestMethod]
    public void PortableMemory_BlocksReleaseState()
    {
        AssertRejected("release candidate was approved", "PortableMemoryAuthorityTransferRejected");
    }

    [TestMethod]
    public void PortableMemory_BlocksDeploymentState()
    {
        AssertRejected("deployment succeeded before", "PortableMemoryAuthorityTransferRejected");
    }

    [TestMethod]
    public void PortableMemory_BlocksWorkflowContinuationClaim()
    {
        AssertRejected("portable memory says continue workflow", "PortableMemoryAuthorityTransferRejected");
    }

    [TestMethod]
    public void PortableMemory_BlocksSelfPromotionClaim()
    {
        AssertRejected("portable memory says promote itself", "PortableMemoryAuthorityTransferRejected");
    }

    [TestMethod]
    public void PortableMemory_BlocksCrossRepoAuthorityTransfer()
    {
        AssertRejected("cross-repo approval should transfer", "PortableMemoryAuthorityTransferRejected");
    }

    [TestMethod]
    public void PortableMemory_BlocksUnsanitizedCandidate()
    {
        var result = PortableEngineeringMemoryGuardrail.Evaluate(Candidate(claimedSanitized: false));

        Assert.AreEqual(PortableEngineeringMemoryVerdict.BlockedUnsanitizedContent, result.Verdict);
        Assert.Contains("PortableMemoryUnsanitizedOrUnknownScope", result.RejectedReasons);
    }

    [TestMethod]
    public void PortableMemory_BlocksUnknownScope()
    {
        var result = PortableEngineeringMemoryGuardrail.Evaluate(Candidate(summary: "", detail: ""));

        Assert.AreEqual(PortableEngineeringMemoryVerdict.BlockedUnsanitizedContent, result.Verdict);
        Assert.Contains("PortableMemoryUnsanitizedOrUnknownScope", result.RejectedReasons);
    }

    [TestMethod]
    public void PortableMemory_FailsClosedOnAmbiguousProjectTruth()
    {
        var result = Evaluate("Project X lesson", "Project X was release-ready on branch Y.");

        Assert.AreNotEqual(PortableEngineeringMemoryVerdict.AllowedSanitizedLesson, result.Verdict);
        Assert.Contains("PortableMemoryContainsConfidentialProjectTruth", result.RejectedReasons);
    }

    [TestMethod]
    public void PortableMemory_AcceptedLessonIncludesAdvisoryWarnings()
    {
        var result = EvaluateAllowed();

        Assert.Contains("Portable memory is advisory only.", result.Warnings);
        Assert.Contains("Portable memory does not approve, satisfy policy, authorize mutation, promote memory, or continue workflow.", result.Warnings);
        Assert.Contains("Portable memory can carry lessons, not authority or confidential project truth.", result.Warnings);
    }

    [TestMethod]
    public void PortableMemory_AcceptedLessonDoesNotChangePatchEligibility()
    {
        var before = PatchProposalGovernedOperationStatusMapper.Map(new PatchProposalStatusInput
        {
            OperationId = "patch-proposal-pr28",
            ProposalId = "proposal-pr28",
            PatchHash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            Subject = Subject(),
            StatusKind = PatchProposalStatusKind.ReadyForReview,
            ArtifactRefs = ["patch-package:pr28"],
            ValidationRefs = ["validation-result:passed"],
            BlockedReasons = [],
            MissingEvidence = [],
            ForbiddenActions = [],
            ExpiresAtUtc = ObservedAtUtc.AddHours(1),
            ObservedAtUtc = ObservedAtUtc
        });
        var lesson = EvaluateAllowed();

        Assert.AreEqual(GovernedOperationState.Completed, before.Status.State);
        Assert.AreEqual(PortableEngineeringMemoryVerdict.AllowedSanitizedLesson, lesson.Verdict);
        Assert.IsFalse(before.CanonicalValidation.Boundary.CanSourceApply);
    }

    [TestMethod]
    public void PortableMemory_AcceptedLessonDoesNotChangeBoundaryFlags()
    {
        var result = EvaluateAllowed();

        AssertBoundaryHasNoAuthority(result);
    }

    [TestMethod]
    public void PortableMemory_RejectedCandidateDoesNotAppearAsHint()
    {
        var result = Evaluate("Unsafe", "previous project approved this");

        Assert.AreEqual(0, result.AllowedLessons.Count);
    }

    [TestMethod]
    public void PortableMemory_RejectedCandidatePreservesRejectedReason()
    {
        var result = Evaluate("Unsafe", "previous project approved this");

        Assert.Contains("PortableMemoryAuthorityTransferRejected", result.RejectedReasons);
        Assert.IsTrue(result.RedFlags.Count > 0);
    }

    [TestMethod]
    public void PortableMemory_DoesNotWriteDurableMemory()
    {
        Assert.IsFalse(EvaluateAllowed().Boundary.CanWriteDurableMemory);
    }

    [TestMethod]
    public void PortableMemory_DoesNotCreatePromotionPackage()
    {
        var result = EvaluateAllowed();

        Assert.IsFalse(result.EvidenceRefs.Any(value => value.StartsWith("memory-promotion-package:", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void PortableMemory_DoesNotPromoteMemory()
    {
        Assert.IsFalse(EvaluateAllowed().Boundary.CanPromoteMemory);
    }

    [TestMethod]
    public void StaticMutationSurfaceScan_NoMemoryStoreWriteExecutorProviderGitUiReleaseDeployAdded()
    {
        var file = Path.Combine(RepoRoot(), "IronDev.Core", "Memory", "PortableEngineeringMemoryGuardrail.cs");
        var text = File.ReadAllText(file);
        foreach (var forbidden in new[]
        {
            "AcceptedMemoryStore",
            "EvaluateAndPromote",
            "AppendVersion",
            "File.Write",
            "Directory.CreateDirectory",
            "Process.Start",
            "ProcessStartInfo",
            "RunProcessAsync",
            "HttpClient",
            "IControlled",
            "Gateway",
            "Executor",
            "git ",
            "gh ",
            "Frontend",
            "UI.",
            "ReleaseExecutor",
            "DeploymentExecutor"
        })
        {
            Assert.IsFalse(text.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"{forbidden} appeared in {file}.");
        }
    }

    [TestMethod]
    public void Regression_PR27MemoryPromotionPackageStatusDoesNotPromoteMemory()
    {
        var result = MemoryPromotionPackageStatusMapper.Map(new MemoryPromotionPackageStatusInput
        {
            PromotionPackageId = "memory-promotion-package-pr28",
            Repository = Repo,
            Branch = Branch,
            RunId = RunId,
            Candidate = new MemoryPromotionCandidate
            {
                CandidateId = "candidate-pr28",
                Scope = MemoryPromotionScope.PortableEngineering,
                Kind = MemoryPromotionKind.SanitizedEngineeringHeuristic,
                Summary = "Prefer small slices",
                Detail = "Keep evidence separate from authority.",
                SourceRepository = "OtherOrg/OtherRepo",
                SourceProjectId = "project-pr28",
                IsSanitized = true,
                IsProjectLocal = false,
                IsPortableEngineeringMemory = true,
                SourceEvidenceRefs = ["portable-memory-source:pr28"]
            },
            StatusKind = MemoryPromotionStatusKind.CandidateCreated,
            EvidenceRefs = [],
            ReceiptRefs = [],
            BlockedReasons = [],
            MissingEvidence = [],
            ForbiddenActions = [],
            ObservedAtUtc = ObservedAtUtc
        });

        Assert.IsFalse(result.Boundary.CanPromoteMemory);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanPromoteMemory);
    }

    [TestMethod]
    public void Regression_PR26MemoryContextRemainsAdvisoryOnly()
    {
        var context = PatchRunMemoryContextBuilder.Build(new PatchRunMemoryContextRequest
        {
            RequestId = "pr28-regression-pr26",
            Repository = Repo,
            Branch = Branch,
            RunId = RunId,
            PatchIntent = "regression check",
            CandidateFilePaths = ["IronDev.Core/Memory/PortableEngineeringMemoryGuardrail.cs"],
            EvidenceRefs = ["portable-memory-guardrail:pr28"],
            ObservedAtUtc = ObservedAtUtc
        },
        [
            new PatchRunMemoryContextSource
            {
                SourceRef = "accepted-memory:pr26",
                MemoryScope = MemoryScope.Project,
                MemoryKind = MemoryKind.ProjectConvention,
                SourceRepository = Repo,
                Summary = "Project memory remains advisory.",
                Detail = "Memory context can improve proposal wording only.",
                IsSanitized = true,
                IsAcceptedMemory = true
            }
        ]);

        Assert.IsTrue(context.Boundary.ReadOnly);
        Assert.IsFalse(context.Boundary.CanPromoteMemory);
        Assert.IsFalse(context.Boundary.CanAuthorizeSourceApply);
    }

    [TestMethod]
    public void Regression_PR25FormatterRemainsPresentationOnly()
    {
        var status = new GovernedOperationStatus
        {
            OperationId = "portable-status-pr28",
            OperationKind = "MemoryReadOnlyStatus",
            Subject = Subject(),
            State = GovernedOperationState.Blocked,
            BlockedReasons = ["Portable memory is advisory only."],
            MissingEvidence = ["portable-memory-sanitization-review:pr28"],
            NextSafeActions = ["review portable engineering lesson before promotion"],
            ForbiddenActions = ["do not promote memory from portable lesson"],
            EvidenceRefs = ["portable-memory-candidate:pr28"],
            ReceiptRefs = [],
            ObservedAtUtc = ObservedAtUtc
        };

        var message = GovernedStatusUserMessageFormatter.Format(status);

        Assert.IsFalse(message.CanApprove);
        Assert.IsFalse(message.CanExecute);
        Assert.IsFalse(message.CanMutateSource);
        Assert.IsFalse(message.CanContinueWorkflow);
    }

    [TestMethod]
    public void Regression_PR24BoundedAuthorityLaneStillStopsBeforeDownstreamAuthority()
    {
        var doc = Receipt("PR24_BOUNDED_AUTHORITY_DOGFOOD_LANE.md");

        StringAssert.Contains(doc, "ready-for-review");
        StringAssert.Contains(doc, "merge");
        StringAssert.Contains(doc, "release");
        StringAssert.Contains(doc, "deployment");
        StringAssert.Contains(doc, "memory promotion");
        StringAssert.Contains(doc, "workflow continuation");
    }

    [TestMethod]
    public void Regression_PR23AskBeforeLaneStillBlocksSourceApplyWithoutExplicitSourceApplyAuthority()
    {
        var doc = Receipt("PR23_ASK_BEFORE_MUTATION_DOGFOOD_LANE.md");

        StringAssert.Contains(doc, "Validation result is not source apply authority.");
        StringAssert.Contains(doc, "The lane produces a blocked `SourceApply` status.");
    }

    [TestMethod]
    public void Regression_PR22NoApprovalLaneStillProducesEvidenceOnly()
    {
        var doc = Receipt("PR22_NO_APPROVAL_DOGFOOD_LANE.md");

        StringAssert.Contains(doc, "Useful evidence is not mutation permission.");
        StringAssert.Contains(doc, "Status is not authority.");
    }

    [TestMethod]
    public void Regression_PR21FreshnessGuardRemainsExplanationOnly()
    {
        var doc = Receipt("PR21_REPO_STATE_FRESHNESS_GUARD.md");

        StringAssert.Contains(doc, "The freshness guard is read-only.");
        StringAssert.Contains(doc, "Stale evidence cannot authorize current mutation.");
    }

    [TestMethod]
    public void Regression_PR20RecoveryRemainsReadOnly()
    {
        var doc = Receipt("PR20_INTERRUPTED_RUN_RECOVERY.md");

        StringAssert.Contains(doc, "Recovery diagnosis is read-only");
        StringAssert.Contains(doc, "It must not execute the next action");
    }

    [TestMethod]
    public void Regression_CARollbackExecutorStillRequiresSeparateRollbackAuthority()
    {
        var doc = Receipt("CA_CONTROLLED_ROLLBACK_EXECUTOR.md");

        StringAssert.Contains(doc, "Rollback plan is not rollback execution");
        StringAssert.Contains(doc, "source-apply receipt");
    }

    [TestMethod]
    public void Regression_ProposalOnlyStillForbidsMutation()
    {
        var result = ProposalOnlyRunProfileEvaluator.Evaluate(new ProposalOnlyRunProfileEvaluationRequest
        {
            OperationId = "proposal-only-portable-memory-pr28",
            OperationKind = ProposalOnlyOperationKinds.SourceApply,
            Subject = Subject(),
            RepoId = Repo,
            Branch = Branch,
            EvidenceRefs = ["portable-memory-guardrail:pr28"],
            ArtifactRefs = [],
            RequestedPaths = ["IronDev.Core/Memory/PortableEngineeringMemoryGuardrail.cs"],
            ExpiresAtUtc = ObservedAtUtc.AddHours(1),
            ObservedAtUtc = ObservedAtUtc
        });

        Assert.IsFalse(result.IsAllowed);
        Assert.IsTrue(result.Status.ForbiddenActions.Any(action => action.Contains("durable source", StringComparison.OrdinalIgnoreCase)));
    }

    private static PortableEngineeringMemoryGuardrailResult EvaluateAllowed() =>
        Evaluate("General lesson", "Keep evidence and authority separate.");

    private static PortableEngineeringMemoryGuardrailResult Evaluate(string summary, string detail) =>
        PortableEngineeringMemoryGuardrail.Evaluate(Candidate(summary: summary, detail: detail));

    private static void AssertAllowed(PortableEngineeringMemoryGuardrailResult result, string expectedText)
    {
        Assert.AreEqual(PortableEngineeringMemoryVerdict.AllowedSanitizedLesson, result.Verdict);
        Assert.IsTrue(result.AllowedLessons.Any(lesson => lesson.Contains(expectedText, StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(0, result.RejectedReasons.Count);
        AssertBoundaryHasNoAuthority(result);
    }

    private static void AssertRejected(string detail, string expectedReason)
    {
        var result = Evaluate("Unsafe portable lesson", detail);

        Assert.AreNotEqual(PortableEngineeringMemoryVerdict.AllowedSanitizedLesson, result.Verdict);
        Assert.Contains(expectedReason, result.RejectedReasons);
        Assert.AreEqual(0, result.AllowedLessons.Count);
        AssertBoundaryHasNoAuthority(result);
    }

    private static void AssertBoundaryHasNoAuthority(PortableEngineeringMemoryGuardrailResult result)
    {
        Assert.IsTrue(result.Boundary.ReadOnly);
        Assert.IsTrue(result.Boundary.GuardrailOnly);
        Assert.IsFalse(result.Boundary.CanApprove);
        Assert.IsFalse(result.Boundary.CanSatisfyPolicy);
        Assert.IsFalse(result.Boundary.CanAuthorizeSourceApply);
        Assert.IsFalse(result.Boundary.CanAuthorizeRollback);
        Assert.IsFalse(result.Boundary.CanAuthorizeCommit);
        Assert.IsFalse(result.Boundary.CanAuthorizePush);
        Assert.IsFalse(result.Boundary.CanAuthorizePullRequest);
        Assert.IsFalse(result.Boundary.CanPromoteMemory);
        Assert.IsFalse(result.Boundary.CanWriteDurableMemory);
        Assert.IsFalse(result.Boundary.CanContinueWorkflow);
        Assert.IsFalse(result.Boundary.CanTransferCrossRepoAuthority);
    }

    private static PortableEngineeringMemoryCandidate Candidate(
        string summary = "General engineering lesson",
        string detail = "Keep evidence and authority separate.",
        bool claimedSanitized = true) => new()
    {
        CandidateId = CandidateId,
        SourceRef = SourceRef,
        Summary = summary,
        Detail = detail,
        SourceRepository = "OtherOrg/OtherRepo",
        SourceProjectId = "portable-source-pr28",
        ClaimedSanitized = claimedSanitized,
        EvidenceRefs = ["portable-memory-source:pr28"],
        ObservedAtUtc = ObservedAtUtc
    };

    private static string Subject() =>
        $"repo:{Repo} branch:{Branch} run:{RunId} patch:sha256-pr28 scope:IronDev.Core/Memory/PortableEngineeringMemoryGuardrail.cs";

    private static string Receipt(string name) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", name));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
