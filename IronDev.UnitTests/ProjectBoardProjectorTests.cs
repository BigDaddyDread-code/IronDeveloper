using IronDev.Core.Board;
using IronDev.Core.Provisioning;
using IronDev.Core.Runs;
using IronDev.Data.Models;
using IronDev.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.UnitTests;

[TestClass]
public sealed class ProjectBoardProjectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 1, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Build_UsesLatestRunToOwnStageAttentionAndWaitingOnTruth()
    {
        var project = Project();
        var ticket = Ticket(42, "Review this run", "Ready");
        var older = Run(ticket.Id, "older", RunLifecycleState.Running, Now.AddMinutes(-5));
        var latest = Run(ticket.Id, "latest", RunLifecycleState.PausedForApproval, Now, "Waiting for approval");

        var model = ProjectBoardProjector.Build(project, Ready(), [ticket], [older, latest], Now);

        var item = model.Items.Single();
        Assert.AreEqual(ProjectBoardStages.Review, item.Stage);
        Assert.IsTrue(item.NeedsAttention);
        Assert.AreEqual("Waiting for approval", item.AttentionReason);
        Assert.AreEqual(ProjectBoardWaitingOnKinds.Human, item.WaitingOn?.Kind);
        Assert.AreEqual("latest", item.LatestRun?.RunId);
        Assert.AreEqual(Now, item.LastMeaningfulEventUtc);
    }

    [TestMethod]
    public void Build_AppliedRunIsDoneAndDoesNotNeedAttention()
    {
        var project = Project();
        var ticket = Ticket(43, "Applied work", "Build");

        var model = ProjectBoardProjector.Build(
            project,
            Ready(),
            [ticket],
            [Run(ticket.Id, "applied", RunLifecycleState.Applied, Now)],
            Now);

        var item = model.Items.Single();
        Assert.AreEqual(ProjectBoardStages.Done, item.Stage);
        Assert.IsFalse(item.NeedsAttention);
        Assert.IsNull(item.WaitingOn);
        Assert.AreEqual("Inspect the applied outcome and its receipts.", item.NextSafeAction);
    }

    [TestMethod]
    public void Build_BlockedDependencyIsNamedWithoutInventingAnAssignee()
    {
        var project = Project();
        var ticket = Ticket(44, "Dependent work", "Ready");
        ticket.BlockedByTicketIds = "12, 13";

        var model = ProjectBoardProjector.Build(project, Ready(), [ticket], [], Now);

        var item = model.Items.Single();
        Assert.IsTrue(item.NeedsAttention);
        StringAssert.Contains(item.AttentionReason, "12, 13");
        Assert.AreEqual(ProjectBoardWaitingOnKinds.Dependency, item.WaitingOn?.Kind);
        Assert.IsNull(item.Assignee);
    }

    [TestMethod]
    public void Build_ExcludesOtherProjectsAndDeletedTickets()
    {
        var project = Project();
        var other = Ticket(45, "Other", "Ready");
        other.ProjectId = 999;
        var deleted = Ticket(46, "Deleted", "Ready");
        deleted.IsDeleted = true;

        var model = ProjectBoardProjector.Build(project, Ready(), [other, deleted], [], Now);

        Assert.AreEqual(0, model.Items.Count);
    }

    [TestMethod]
    public void Build_UsesPersistedAssigneeAndWaitingOnInsteadOfInventingActors()
    {
        var ticket = Ticket(47, "Owned work", "Ready");
        var collaboration = new Dictionary<long, ProjectWorkItemCollaborationSnapshot>
        {
            [ticket.Id] = new()
            {
                WorkItemId = ticket.Id,
                Assignee = new("Human", 8, "Alice Reviewer"),
                WaitingOn = new("Role", null, "Approver")
            }
        };

        var model = ProjectBoardProjector.Build(Project(), Ready(), [ticket], [], Now, collaboration);

        Assert.AreEqual("Alice Reviewer", model.Items.Single().Assignee?.DisplayName);
        Assert.AreEqual("Approver", model.Items.Single().WaitingOn?.Label);
    }

    private static Project Project() => new() { Id = 7, Name = "Board test" };

    private static ProjectTicket Ticket(long id, string title, string status) => new()
    {
        Id = id,
        ProjectId = 7,
        Title = title,
        Status = status,
        Priority = "Medium",
        AcceptanceCriteria = "Given backend truth, when read, then display it.",
        CreatedDate = Now.UtcDateTime.AddHours(-1)
    };

    private static RunRecord Run(long ticketId, string runId, RunLifecycleState state, DateTimeOffset updated, string summary = "") => new()
    {
        RunId = runId,
        ProjectId = 7,
        TicketId = ticketId,
        State = state,
        Summary = summary,
        CreatedUtc = updated.AddMinutes(-1),
        UpdatedUtc = updated
    };

    private static ProjectProvisioningReadiness Ready() => new()
    {
        ProjectId = 7,
        IsReady = true,
        BlockedCount = 0
    };
}
