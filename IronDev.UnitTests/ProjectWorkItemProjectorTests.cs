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
        Assert.AreEqual(ProjectWorkItemExecutionProofStatuses.NoRun, model.ExecutionProof.Status);
        Assert.IsFalse(model.ExecutionProof.HasRunRecord);
    }

    [TestMethod]
    public void Build_ProjectsDurableOwnershipAndAttributedActivity()
    {
        var collaboration = new ProjectWorkItemCollaborationSnapshot
        {
            WorkItemId = 42,
            Revision = 3,
            Assignee = new("Human", 8, "Alice Reviewer"),
            Followers = [new("Human", 7, "Bob Developer")],
            WaitingOn = new("Role", null, "Approver"),
            RecentActivity =
            [
                new(Now, "CollaborationChanged", "Ownership changed.", new("Human", 7, "Bob Developer"))
            ]
        };

        var model = ProjectWorkItemProjector.Build(Ticket(), null, null, Ready(), Now, collaboration);

        Assert.AreEqual(3, model.Collaboration.Revision);
        Assert.AreEqual("Alice Reviewer", model.Collaboration.Assignee?.DisplayName);
        Assert.AreEqual("Bob Developer", model.Collaboration.Followers.Single().DisplayName);
        Assert.AreEqual("Approver", model.Collaboration.WaitingOn?.DisplayName);
        Assert.AreEqual("Bob Developer", model.Collaboration.RecentActivity.Single().Actor?.DisplayName);
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

    [TestMethod]
    public void Build_InterruptedPreMutationAttemptOffersBackendConstrainedRecovery()
    {
        var report = Report() with
        {
            Apply = new SkeletonRunApplyTrace
            {
                Attempts =
                [
                    new SkeletonRunApplyAttemptTrace
                    {
                        AttemptId = "run-42-apply-001",
                        AttemptNumber = 1,
                        Status = SkeletonApplyAttemptStatuses.Interrupted,
                        MutationState = SkeletonApplyMutationStates.NotObserved,
                        AvailableActions =
                        [
                            SkeletonApplyRecoveryActions.Resume,
                            SkeletonApplyRecoveryActions.Retry,
                            SkeletonApplyRecoveryActions.ManualReview,
                            SkeletonApplyRecoveryActions.Abandon
                        ]
                    }
                ]
            }
        };

        var model = Build(Ticket(), Run(RunLifecycleState.Completed), report, Ready());

        Assert.AreEqual(ProjectWorkItemApplyRecoveryStatuses.Interrupted, model.ApplyRecovery.Status);
        Assert.AreEqual(ProjectWorkItemActionKinds.RecoverApply, model.PrimaryAction.Kind);
        Assert.IsTrue(model.ApplyRecovery.RetryAllowed);
        Assert.AreEqual("run-42-apply-001", model.ApplyRecovery.ApplyAttemptId);
        CollectionAssert.Contains(model.ApplyRecovery.AvailableActions.ToList(), SkeletonApplyRecoveryActions.Resume);
    }

    [TestMethod]
    public void Build_UncertainMutationNeverOffersRetry()
    {
        var report = Report() with
        {
            Apply = new SkeletonRunApplyTrace
            {
                Attempts =
                [
                    new SkeletonRunApplyAttemptTrace
                    {
                        AttemptId = "run-42-apply-001",
                        AttemptNumber = 1,
                        Status = SkeletonApplyAttemptStatuses.Interrupted,
                        MutationState = SkeletonApplyMutationStates.Uncertain,
                        AvailableActions = [SkeletonApplyRecoveryActions.ManualReview, SkeletonApplyRecoveryActions.Abandon]
                    }
                ]
            }
        };

        var model = Build(Ticket(), Run(RunLifecycleState.Completed), report, Ready());

        Assert.AreEqual(ProjectWorkItemApplyRecoveryStatuses.ManualReviewRequired, model.ApplyRecovery.Status);
        Assert.IsFalse(model.ApplyRecovery.RetryAllowed);
        Assert.IsTrue(model.ApplyRecovery.PartialMutationPossible);
        CollectionAssert.DoesNotContain(model.ApplyRecovery.AvailableActions.ToList(), SkeletonApplyRecoveryActions.Retry);
    }

    [TestMethod]
    public void Build_ArtifactsWithoutDurableExecutionEventRemainProofMissing()
    {
        var report = Report() with
        {
            Proposal = new SkeletonRunProposalTrace { ProposalId = "proposal-42", EvidenceExistsOnDisk = true },
            CriticPackage = new SkeletonRunCriticPackageTrace { PackageId = "package-42", ExistsOnDisk = true }
        };

        var model = Build(Ticket(), Run(RunLifecycleState.Completed), report, Ready());

        Assert.AreEqual(ProjectWorkItemExecutionProofStatuses.ProofMissing, model.ExecutionProof.Status);
        Assert.IsTrue(model.ExecutionProof.ArtifactEvidenceObserved);
        Assert.IsFalse(model.ExecutionProof.ArtifactEvidenceProvesExecution);
        Assert.AreEqual(0, model.ExecutionProof.DurableExecutionEventCount);
        StringAssert.Contains(model.ExecutionProof.Reason, "no durable execution event");
    }

    [TestMethod]
    public void Build_DurableExecutionEventIsObservedButNamedGapsPreventLoopVerification()
    {
        var report = Report() with
        {
            Timeline =
            [
                new SkeletonRunTimelineEntry
                {
                    TimestampUtc = Now.AddMinutes(-4),
                    EventType = "SkeletonEvidencePackaged",
                    Message = "Build and test evidence packaged."
                }
            ],
            Gaps = ["Critic package hash could not be verified."]
        };

        var model = Build(Ticket(), Run(RunLifecycleState.PausedForApproval), report, Ready());

        Assert.AreEqual(ProjectWorkItemExecutionProofStatuses.ExecutionObserved, model.ExecutionProof.Status);
        Assert.IsTrue(model.ExecutionProof.ExecutionStarted);
        Assert.IsTrue(model.ExecutionProof.BuildAndTestExecutionObserved);
        Assert.IsFalse(model.ExecutionProof.LoopVerified);
        Assert.AreEqual(1, model.ExecutionProof.DurableExecutionEventCount);
        Assert.AreEqual(1, model.ExecutionProof.Gaps.Count);
        StringAssert.Contains(model.ExecutionProof.NextSafeAction, "evidence gaps");
    }

    [TestMethod]
    public void Build_AppliedLoopRequiresDurableApplyEventAndCompleteReportForVerifiedStatus()
    {
        var report = Report() with
        {
            Timeline =
            [
                new SkeletonRunTimelineEntry { TimestampUtc = Now.AddMinutes(-3), EventType = "SkeletonEvidencePackaged" },
                new SkeletonRunTimelineEntry { TimestampUtc = Now.AddMinutes(-1), EventType = "SkeletonApplied" }
            ],
            Apply = new SkeletonRunApplyTrace { Applied = true },
            LoopComplete = true
        };

        var model = Build(Ticket(), Run(RunLifecycleState.Applied), report, Ready());

        Assert.AreEqual(ProjectWorkItemExecutionProofStatuses.LoopVerified, model.ExecutionProof.Status);
        Assert.IsTrue(model.ExecutionProof.ExecutionCompleted);
        Assert.IsTrue(model.ExecutionProof.ApplyExecutionObserved);
        Assert.IsTrue(model.ExecutionProof.LoopVerified);
        Assert.IsFalse(model.ExecutionProof.ArtifactEvidenceProvesExecution);
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
        StartedUtc = Now.AddMinutes(-9),
        CompletedUtc = state is RunLifecycleState.Failed or RunLifecycleState.Cancelled or RunLifecycleState.Applied
            ? Now
            : null,
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
