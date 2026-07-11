using IronDev.Core.Builder;
using IronDev.Core.Models;
using IronDev.Core.Runs;
using IronDev.Core.WorkItems;
using IronDev.Data.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.UnitTests;

[TestClass]
public sealed class ProjectWorkItemProjectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 2, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Build_ReadyTicketExposesStartRunWithoutInventingCollaboration()
    {
        var model = Build(Ticket(), null, null, Ready());

        Assert.AreEqual(ProjectWorkItemStages.Ticket, model.Stage);
        Assert.AreEqual(ProjectWorkItemActionKinds.StartRun, model.PrimaryAction.Kind);
        Assert.IsTrue(model.PrimaryAction.Allowed);
        Assert.AreEqual("Open", model.Gate.State);
        Assert.IsNull(model.Collaboration.Assignee);
        Assert.AreEqual(0, model.Collaboration.Followers.Count);
        Assert.IsNull(model.Collaboration.WaitingOn);
    }

    [TestMethod]
    public void Build_BlockedReadinessNamesReasonAndSafeAction()
    {
        var readiness = new BuildReadinessResult
        {
            Status = BuildReadinessStatus.NeedsClarification,
            Message = "Acceptance criteria are required.",
            BlockingIssues = ["Ticket has no acceptance criteria."]
        };
        var ticket = Ticket();
        ticket.AcceptanceCriteria = null;

        var model = Build(ticket, null, null, readiness);

        Assert.AreEqual("Blocked", model.Gate.State);
        Assert.AreEqual(ProjectWorkItemActionKinds.ResolveReadiness, model.PrimaryAction.Kind);
        Assert.IsFalse(model.PrimaryAction.Allowed);
        CollectionAssert.Contains(model.Gate.TechnicalDetails.ToList(), "Ticket has no acceptance criteria.");
        Assert.AreEqual("Project team", model.Collaboration.WaitingOn?.DisplayName);
    }

    [TestMethod]
    public void Build_PausedRunProjectsReviewFindingsAndActivity()
    {
        var run = Run(RunLifecycleState.PausedForApproval);
        var report = Report() with
        {
            CriticReviews =
            [
                new SkeletonRunCriticReviewTrace
                {
                    FindingIds = ["F-1", "F-2"],
                    FindingCount = 2
                }
            ],
            FindingDispositions =
            [
                new SkeletonRunFindingDispositionTrace { FindingId = "F-1" }
            ],
            Timeline =
            [
                new SkeletonRunTimelineEntry { TimestampUtc = Now.AddMinutes(-2), EventType = "ApprovalRequired", Message = "Human review required." }
            ]
        };

        var model = Build(Ticket(), run, report, Ready());

        Assert.AreEqual(ProjectWorkItemStages.Review, model.Stage);
        Assert.AreEqual(ProjectWorkItemActionKinds.Review, model.PrimaryAction.Kind);
        Assert.AreEqual(2, model.LatestRun?.FindingCount);
        Assert.AreEqual(1, model.LatestRun?.UnresolvedFindingCount);
        Assert.AreEqual("Eligible reviewer", model.Collaboration.WaitingOn?.DisplayName);
        Assert.AreEqual("ApprovalRequired", model.Collaboration.RecentActivity.Single().Kind);
        Assert.IsNull(model.Collaboration.RecentActivity.Single().Actor);
    }

    [TestMethod]
    public void Build_CompletedRunOffersApplyButDoesNotClaimDone()
    {
        var report = Report() with
        {
            Approval = new SkeletonRunApprovalTrace { HaltObserved = true, ContinuationUnblocked = true }
        };
        var model = Build(Ticket(), Run(RunLifecycleState.Completed), report, Ready());

        Assert.AreEqual(ProjectWorkItemStages.Review, model.Stage);
        Assert.AreEqual(ProjectWorkItemActionKinds.Apply, model.PrimaryAction.Kind);
        Assert.AreEqual("Open", model.Gate.State);
        Assert.IsFalse(model.LatestRun?.Applied);
    }

    [TestMethod]
    public void Build_AppliedRunReportsOutcomeAndEvidenceGapsHonestly()
    {
        var report = Report() with
        {
            Apply = new SkeletonRunApplyTrace { Applied = true },
            LoopComplete = false,
            Gaps = ["Receipt missing"]
        };
        var model = Build(Ticket(), Run(RunLifecycleState.Applied), report, Ready());

        Assert.AreEqual(ProjectWorkItemStages.Done, model.Stage);
        Assert.AreEqual(ProjectWorkItemActionKinds.ViewOutcome, model.PrimaryAction.Kind);
        Assert.AreEqual("Satisfied", model.Gate.State);
        Assert.AreEqual(1, model.LatestRun?.EvidenceGapCount);
        StringAssert.Contains(model.Gate.Reason, "evidence gaps");
        Assert.IsNotNull(model.EvidenceLinks.RunReportApiPath);
        Assert.IsFalse(model.EvidenceLinks.RunReportApiPath!.Contains("artifacts", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(ProjectWorkItemApplyRecoveryStatuses.Applied, model.ApplyRecovery.Status);
        Assert.IsFalse(model.ApplyRecovery.Required);
        Assert.IsFalse(model.ApplyRecovery.RetryAllowed);
    }

    [TestMethod]
    public void Build_ApplyRefusalDoesNotPretendARecoveryCampaignIsRequired()
    {
        var report = Report() with
        {
            Apply = new SkeletonRunApplyTrace
            {
                Applied = false,
                RefusedReason = "Apply preflight refused a dirty source workspace."
            }
        };

        var model = Build(Ticket(), Run(RunLifecycleState.Completed), report, Ready());

        Assert.AreEqual(ProjectWorkItemApplyRecoveryStatuses.ApplyRefused, model.ApplyRecovery.Status);
        Assert.IsFalse(model.ApplyRecovery.Required);
        Assert.IsFalse(model.ApplyRecovery.ApplyAttemptObserved);
        Assert.IsFalse(model.ApplyRecovery.RetryAllowed);
        StringAssert.Contains(model.ApplyRecovery.Reason, "dirty source workspace");
    }

    [TestMethod]
    public void Build_FailedPartialApplyRequiresRecoveryEvidenceAndHumanReview()
    {
        var report = Report() with
        {
            Apply = new SkeletonRunApplyTrace
            {
                Applied = false,
                Stages =
                [
                    new SkeletonRunApplyStageTrace { Stage = "Copy", Succeeded = true },
                    new SkeletonRunApplyStageTrace { Stage = "PostApplyValidation", Succeeded = false, Errors = "Tests failed." }
                ],
                Receipts =
                [
                    new SkeletonRunReceiptRef { Name = "copy-receipt", ExistsOnDisk = true },
                    new SkeletonRunReceiptRef { Name = "validation-receipt", ExistsOnDisk = false }
                ]
            }
        };

        var model = Build(Ticket(), Run(RunLifecycleState.Failed), report, Ready());

        Assert.AreEqual(ProjectWorkItemApplyRecoveryStatuses.RecoveryEvidenceMissing, model.ApplyRecovery.Status);
        Assert.IsTrue(model.ApplyRecovery.Required);
        Assert.IsTrue(model.ApplyRecovery.ApplyAttemptObserved);
        Assert.IsTrue(model.ApplyRecovery.PartialMutationPossible);
        Assert.AreEqual(1, model.ApplyRecovery.SucceededStageCount);
        Assert.AreEqual(1, model.ApplyRecovery.FailedStageCount);
        Assert.AreEqual(1, model.ApplyRecovery.ExistingReceiptCount);
        Assert.AreEqual(1, model.ApplyRecovery.MissingReceiptCount);
        Assert.IsFalse(model.ApplyRecovery.RetryAllowed);
        Assert.IsTrue(model.ApplyRecovery.HumanReviewRequired);
        CollectionAssert.Contains(model.ApplyRecovery.FailedStages.ToList(), "PostApplyValidation");
        CollectionAssert.Contains(model.ApplyRecovery.TechnicalDetails.ToList(), "Tests failed.");
    }

    private static ProjectWorkItemReadModel Build(
        ProjectTicket ticket,
        RunRecord? run,
        SkeletonRunReport? report,
        BuildReadinessResult readiness) =>
        ProjectWorkItemProjector.Build(ticket, run, report, readiness, Now);

    private static ProjectTicket Ticket() => new()
    {
        Id = 42,
        ProjectId = 7,
        Title = "Work Item projection",
        Status = "Ready",
        Summary = "Backend-owned lifecycle truth.",
        AcceptanceCriteria = "- First criterion\n- Second criterion",
        LinkedFilePaths = "src/One.cs;src/Two.cs",
        SourceChatSessionId = 4001,
        CreatedDate = Now.AddHours(-1).UtcDateTime
    };

    private static RunRecord Run(RunLifecycleState state) => new()
    {
        RunId = "run-42",
        ProjectId = 7,
        TicketId = 42,
        State = state,
        Summary = state.ToString(),
        CreatedUtc = Now.AddMinutes(-10),
        UpdatedUtc = Now
    };

    private static SkeletonRunReport Report() => new()
    {
        RunId = "run-42",
        ProjectId = 7,
        TicketId = 42,
        Status = "PausedForApproval"
    };

    private static BuildReadinessResult Ready() => new()
    {
        Status = BuildReadinessStatus.ReadyToBuild,
        Message = "Ready to build."
    };
}
