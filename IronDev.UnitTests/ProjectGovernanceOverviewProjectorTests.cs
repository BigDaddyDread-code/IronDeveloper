using IronDev.Core.Board;
using IronDev.Core.Governance;
using IronDev.Core.Provisioning;
using IronDev.Core.WorkItems;
using IronDev.Data.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.UnitTests;

[TestClass]
public sealed class ProjectGovernanceOverviewProjectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 3, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Build_InterruptedApplyWinsPrimaryActionAndTargetsTheWorkItem()
    {
        var board = Board(Item(101), Item(104));
        var findings = WorkItem(101) with
        {
            LatestRun = Run("PausedForApproval", unresolvedFindings: 3)
        };
        var interruptedApply = WorkItem(104) with
        {
            ApplyRecovery = new ProjectWorkItemApplyRecoveryReadModel
            {
                Required = true,
                PartialMutationPossible = true,
                Reason = "Source mutation may have occurred before failure.",
                NextSafeAction = "Inspect receipts and choose a recovery action."
            }
        };

        var model = ProjectGovernanceOverviewProjector.Build(board, [findings, interruptedApply], false, generatedUtc: Now);

        Assert.AreEqual(ProjectGovernanceOverallStatuses.Degraded, model.OverallStatus);
        Assert.AreEqual(ProjectGovernanceAttentionKinds.InterruptedApply, model.PrimaryAction.Kind);
        Assert.AreEqual(104, model.PrimaryAction.WorkItemId);
        Assert.AreEqual("/projects/7/work-items/104", model.PrimaryAction.TargetRoute);
        Assert.AreEqual("Critical", model.AttentionItems[0].Severity);
        Assert.AreEqual(ProjectGovernanceAttentionKinds.FindingsAwaitingDisposition, model.AttentionItems[1].Kind);
    }

    [TestMethod]
    public void Build_UsesTypedApplyActionInsteadOfInventingAContinuationDecision()
    {
        var workItem = WorkItem(42) with
        {
            LatestRun = Run("Completed"),
            PrimaryAction = new ProjectWorkItemActionReadModel
            {
                Kind = ProjectWorkItemActionKinds.Apply,
                Label = "Review controlled apply",
                Allowed = true,
                Reason = "Apply remains a separate governed action."
            }
        };

        var model = ProjectGovernanceOverviewProjector.Build(Board(Item(42)), [workItem], false, generatedUtc: Now);

        Assert.AreEqual(ProjectGovernanceAttentionKinds.ControlledApplyReview, model.PrimaryAction.Kind);
        Assert.AreEqual("Review controlled apply", model.PrimaryAction.Label);
    }

    [TestMethod]
    public void Build_ReadyProjectWithoutAttentionReportsControlsActiveWithoutSafetyClaim()
    {
        var model = ProjectGovernanceOverviewProjector.Build(Board(), [], false, generatedUtc: Now);

        Assert.AreEqual(ProjectGovernanceOverallStatuses.ControlsActive, model.OverallStatus);
        Assert.AreEqual("No action required", model.PrimaryAction.Label);
        Assert.AreEqual(0, model.AttentionItems.Count);
        Assert.IsFalse(model.StatusSummary.Contains("safe", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(ProjectGovernanceOverview.BoundaryText, model.Boundary);
        CollectionAssert.AreEquivalent(
            new[] { "IronDev invariant", "Tenant policy" },
            model.Controls.Select(control => control.Source).Distinct().ToArray());
    }

    [TestMethod]
    public void Build_ReadinessOrSectionFailureIsExplicitlyDegraded()
    {
        var board = Board() with
        {
            Readiness = new ProjectProvisioningReadiness
            {
                ProjectId = 7,
                IsReady = false,
                BlockedCount = 1,
                NextAction = new ProvisioningNextAction { NextSafeAction = "Confirm the project profile." }
            }
        };
        var issue = new ProjectGovernanceSectionIssue { Section = "WorkItem:5", Summary = "Evidence unavailable." };

        var model = ProjectGovernanceOverviewProjector.Build(board, [], false, [issue], Now);

        Assert.AreEqual(ProjectGovernanceOverallStatuses.Degraded, model.OverallStatus);
        Assert.AreEqual("ProjectReadinessDegraded", model.Exceptions.Single().Category);
        Assert.AreEqual("/projects/7/library/provisioning", model.Exceptions.Single().TargetRoute);
        Assert.AreEqual(1, model.SectionIssues.Count);
    }

    [TestMethod]
    public void Build_RejectsCrossProjectWorkItemEvidence()
    {
        var foreign = WorkItem(42, projectId: 8) with
        {
            ApplyRecovery = new ProjectWorkItemApplyRecoveryReadModel
            {
                Required = true,
                PartialMutationPossible = true,
                Reason = "Foreign project evidence.",
                NextSafeAction = "Do not expose."
            },
            Collaboration = Collaboration(new ProjectWorkItemActivityReadModel
            {
                TimestampUtc = Now,
                Kind = "SkeletonApplied",
                Summary = "Foreign decision."
            })
        };

        var model = ProjectGovernanceOverviewProjector.Build(Board(Item(42)), [foreign], false, generatedUtc: Now);

        Assert.AreEqual(0, model.Exceptions.Count);
        Assert.AreEqual(0, model.RecentDecisions.Count);
        Assert.AreEqual(ProjectGovernanceAttentionKinds.Blocked, model.PrimaryAction.Kind);
        Assert.AreEqual("/projects/7/work-items/42", model.PrimaryAction.TargetRoute);
    }

    [TestMethod]
    public void Build_ProjectsOnlyKnownConsequentialEventsAndPreservesActorAttribution()
    {
        var decision = new ProjectWorkItemActivityReadModel
        {
            TimestampUtc = Now,
            Kind = "SkeletonFindingDispositionRecorded",
            Summary = "Finding F-1 was accepted.",
            Actor = new ProjectWorkItemActorReadModel { Kind = "Human", UserId = 9, DisplayName = "Alice Reviewer" }
        };
        var noise = new ProjectWorkItemActivityReadModel
        {
            TimestampUtc = Now.AddMinutes(-1),
            Kind = "SkeletonEvidencePackaged",
            Summary = "Package created."
        };
        var workItem = WorkItem(42) with { Collaboration = Collaboration(decision, noise) };

        var model = ProjectGovernanceOverviewProjector.Build(Board(), [workItem], false, generatedUtc: Now);

        Assert.AreEqual(1, model.RecentDecisions.Count);
        Assert.AreEqual("Alice Reviewer", model.RecentDecisions.Single().ActorDisplayName);
        Assert.AreEqual("/projects/7/work-items/42", model.RecentDecisions.Single().TargetRoute);
    }

    private static ProjectBoardReadModel Board(params ProjectBoardItemReadModel[] items) => new()
    {
        ProjectId = 7,
        ProjectName = "IronDev Local Test Project",
        GeneratedUtc = Now,
        Readiness = new ProjectProvisioningReadiness { ProjectId = 7, IsReady = true },
        Items = items
    };

    private static ProjectBoardItemReadModel Item(long id) => new()
    {
        WorkItemId = id,
        Title = $"Work item {id}",
        NeedsAttention = true,
        AttentionReason = "Human review is required.",
        NextSafeAction = "Open the Work Item.",
        WaitingOn = new ProjectBoardWaitingOnReadModel { Kind = "Human", Label = "Reviewer" },
        LastMeaningfulEventUtc = Now.AddMinutes(-id)
    };

    private static ProjectWorkItemReadModel WorkItem(long id, int projectId = 7) => new()
    {
        ProjectId = projectId,
        WorkItemId = id,
        Title = $"Work item {id}",
        StatusSummary = "Review is required.",
        LastMeaningfulEventUtc = Now.AddMinutes(-id),
        Ticket = new ProjectTicket { Id = id, ProjectId = projectId, Title = $"Work item {id}" },
        Contract = new ProjectWorkItemContractReadModel(),
        Collaboration = Collaboration(),
        Authority = new ProjectWorkItemAuthorityReadModel(),
        Gate = new ProjectWorkItemGateReadModel { NextSafeAction = "Review backend evidence." },
        PrimaryAction = new ProjectWorkItemActionReadModel(),
        ApplyRecovery = new ProjectWorkItemApplyRecoveryReadModel(),
        ExecutionProof = new ProjectWorkItemExecutionProofReadModel(),
        EvidenceLinks = new ProjectWorkItemEvidenceLinksReadModel()
    };

    private static ProjectWorkItemRunReadModel Run(string status, int unresolvedFindings = 0) => new()
    {
        RunId = "run-42",
        Status = status,
        UnresolvedFindingCount = unresolvedFindings
    };

    private static ProjectWorkItemCollaborationReadModel Collaboration(params ProjectWorkItemActivityReadModel[] activity) => new()
    {
        WaitingOn = new ProjectWorkItemActorReadModel { Kind = "Role", DisplayName = "Eligible reviewer" },
        RecentActivity = activity
    };
}
