using IronDev.Core.Audit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.UnitTests;

[TestClass]
public sealed class ProjectAuditExportProjectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 5, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Build_ProducesStableHashForSameOrderedItems()
    {
        var first = ProjectAuditExportProjector.Build(7, "IronDeveloper", Filters(), Ledger(Item("event-1")), Now);
        var second = ProjectAuditExportProjector.Build(7, "IronDeveloper", Filters(), Ledger(Item("event-1")), Now.AddMinutes(1));

        Assert.AreEqual(first.ItemsSha256, second.ItemsSha256);
        Assert.AreEqual(64, first.ItemsSha256.Length);
    }

    [TestMethod]
    public void Build_HashChangesWhenOrderedEvidenceChanges()
    {
        var first = ProjectAuditExportProjector.Build(7, "IronDeveloper", Filters(), Ledger(Item("event-1")), Now);
        var changed = ProjectAuditExportProjector.Build(7, "IronDeveloper", Filters(), Ledger(Item("event-1") with { Outcome = "Refused" }), Now);

        Assert.AreNotEqual(first.ItemsSha256, changed.ItemsSha256);
    }

    [TestMethod]
    public void Build_DropsCrossProjectAndExternalEvidenceLinks()
    {
        var item = Item("event-1") with
        {
            EvidenceLinks =
            [
                new AuditLedgerEvidenceLink { Label = "Current project", Href = "/projects/7/work-items/42" },
                new AuditLedgerEvidenceLink { Label = "Other project", Href = "/projects/8/work-items/42" },
                new AuditLedgerEvidenceLink { Label = "External", Href = "https://example.test/evidence" },
                new AuditLedgerEvidenceLink { Label = "Timeline", Href = "/governance/timeline" }
            ]
        };

        var export = ProjectAuditExportProjector.Build(7, "IronDeveloper", Filters(), Ledger(item), Now);

        CollectionAssert.AreEqual(
            new[] { "/projects/7/work-items/42", "/governance/timeline" },
            export.Items.Single().EvidenceLinks.Select(link => link.Href).ToArray());
    }

    [TestMethod]
    public void AuditEvidenceLinkSafety_IsTheSingleFailClosedProjectLinkRule()
    {
        Assert.IsTrue(AuditEvidenceLinkSafety.IsSafeForProject(7, "/projects/7/work-items/42"));
        Assert.IsTrue(AuditEvidenceLinkSafety.IsSafeForProject(7, "/governance/timeline"));
        Assert.IsFalse(AuditEvidenceLinkSafety.IsSafeForProject(7, "/projects/8/work-items/42"));
        Assert.IsFalse(AuditEvidenceLinkSafety.IsSafeForProject(7, "//example.test/evidence"));
        Assert.IsFalse(AuditEvidenceLinkSafety.IsSafeForProject(7, "https://example.test/evidence"));
        Assert.IsFalse(AuditEvidenceLinkSafety.IsSafeForProject(0, "/governance/timeline"));
        Assert.IsFalse(AuditEvidenceLinkSafety.IsSafeForProject(7, "/projects/7/../8/work-items/42"));
        Assert.IsFalse(AuditEvidenceLinkSafety.IsSafeForProject(7, "/governance/../projects/8/work-items/42"));
        Assert.IsFalse(AuditEvidenceLinkSafety.IsSafeForProject(7, "/projects/7/%2e%2e/8/work-items/42"));
        Assert.IsFalse(AuditEvidenceLinkSafety.IsSafeForProject(7, "/projects/+7/work-items/42"));
    }

    [TestMethod]
    public void Build_RedactsSecretLookingSummaryAndExcludesForeignRows()
    {
        var secret = Item("event-1") with { Summary = "Bearer abc123 was supplied." };
        var foreign = Item("event-2") with { ProjectId = 8 };

        var export = ProjectAuditExportProjector.Build(7, "IronDeveloper", Filters(), Ledger(secret, foreign), Now);

        Assert.AreEqual(1, export.ReturnedCount);
        Assert.AreEqual("[redacted audit summary]", export.Items.Single().Summary);
        Assert.IsFalse(export.Items.Single().Summary.Contains("abc123", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Build_ReportsBoundAndTruncationWithoutCompletenessClaim()
    {
        var export = ProjectAuditExportProjector.Build(7, "IronDeveloper", Filters(2), Ledger(Item("one"), Item("two")), Now);

        Assert.AreEqual(2, export.Take);
        Assert.AreEqual(2, export.ReturnedCount);
        Assert.IsTrue(export.Truncated);
        StringAssert.Contains(export.Warnings[0], "may be truncated");
        Assert.IsFalse(export.Boundary.GrantsAuthority);
        Assert.IsFalse(export.Boundary.ExposesRawPayloadJson);
    }

    private static ProjectAuditExportFilters Filters(int take = 250) => new() { Take = take };

    private static AuditLedgerResponse Ledger(params AuditLedgerItem[] items) => new()
    {
        Items = items,
        ReturnedCount = items.Length,
        Take = items.Length
    };

    private static AuditLedgerItem Item(string id) => new()
    {
        LedgerId = id,
        TimeUtc = Now,
        ProjectId = 7,
        ProjectName = "IronDeveloper",
        WorkItemId = 42,
        WorkItemTitle = "Audit export",
        Source = "RunEvent",
        ActorId = "9",
        ActorDisplayName = "Alice Reviewer",
        Action = "AcceptedApprovalRecorded",
        Outcome = "Recorded",
        Summary = "Approval evidence was recorded.",
        CorrelationId = "run-42",
        EvidenceLinks = [new AuditLedgerEvidenceLink { Label = "Work Item", Href = "/projects/7/work-items/42" }]
    };
}
