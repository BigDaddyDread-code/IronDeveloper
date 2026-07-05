using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// D-1 — static guards on the demo target's honesty. These are file/JSON reads
/// only (fast, no build), so they ride the governance-boundary lane:
///  - the ticket fixtures are complete enough for the readiness gate;
///  - the dependency chain resolves and yields more than one wave, so the batch
///    demo actually sequences;
///  - the fixtures name no approvals/capabilities/gates and know no machine;
///  - the sample does NOT already implement the ticket features — if someone
///    pre-bakes an answer, this goes red. A demo that only works because the
///    machine already knows the trick is a lie with screenshots.
/// The slow half (actually building and testing the sample) lives in
/// AlphaSampleBuildSmokeTests under the LongRunning lane.
/// </summary>
[TestClass]
[TestCategory("StaticBoundary")]
public sealed class AlphaSampleFixtureBoundaryTests
{
    [TestMethod]
    public void Fixtures_AreCompleteEnoughToPassTheReadinessGate()
    {
        var fixture = LoadFixture();

        Assert.IsFalse(string.IsNullOrWhiteSpace(fixture.Project.Name), "The fixture project needs a name.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(fixture.Project.RelativePath), "The fixture project needs a repo-relative path.");
        Assert.IsTrue(Directory.Exists(Path.Combine(RepoRoot(), fixture.Project.RelativePath)),
            "The fixture project path must point at a directory that exists in the repo.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(fixture.Project.BuildCommand), "The readiness gate requires a build command.");

        Assert.AreEqual(3, fixture.Tickets.Count, "The alpha demo is a 3-ticket batch.");
        foreach (var ticket in fixture.Tickets)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(ticket.Key), "Every ticket needs a key.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(ticket.Title), $"Ticket '{ticket.Key}' needs a title.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(ticket.Summary),
                $"Ticket '{ticket.Key}' needs a summary — the readiness gate blocks unclear scope.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(ticket.AcceptanceCriteria),
                $"Ticket '{ticket.Key}' needs acceptance criteria — the tester authors tests from them, blind to the diff.");
        }
    }

    [TestMethod]
    public void Fixtures_DependencyChain_ResolvesAndYieldsWaves()
    {
        var fixture = LoadFixture();
        var keys = fixture.Tickets.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var ticket in fixture.Tickets)
        {
            foreach (var dependency in ticket.BlockedBy)
            {
                Assert.IsTrue(keys.Contains(dependency),
                    $"Ticket '{ticket.Key}' is blocked by unknown ticket '{dependency}'.");
                Assert.AreNotEqual(ticket.Key, dependency, $"Ticket '{ticket.Key}' cannot block itself.");
            }
        }

        var independent = fixture.Tickets.Count(t => t.BlockedBy.Count == 0);
        var dependent = fixture.Tickets.Count(t => t.BlockedBy.Count > 0);
        Assert.IsTrue(independent >= 2, "The first wave needs at least two independent tickets to demonstrate parallel sequencing.");
        Assert.IsTrue(dependent >= 1, "At least one ticket must depend on another so the demo exercises wave ordering.");
    }

    [TestMethod]
    public void Fixtures_GrantNothing_AndKnowNoMachine()
    {
        var fixtureText = File.ReadAllText(FixturePath());

        // A ticket fixture describes work; it must never smuggle authority
        // language into the loop or point at a particular machine.
        foreach (var forbidden in new[]
                 {
                     "approval", "capability", "authority", "grant", "gate",
                     "AcceptedApproval", "skeleton-run.continue",
                     string.Concat(@"C:\", "Users"),
                     string.Concat(@"C:\\", "Users"),
                     string.Concat("%USER", "PROFILE%"),
                     string.Concat("(local", "db)"),
                     string.Concat("SQL", "EXPRESS"),
                     string.Concat("DESKTOP", "-")
                 })
        {
            Assert.IsFalse(fixtureText.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"tickets.json must not contain '{forbidden}' — fixtures describe work, never authority or a local machine.");
        }
    }

    [TestMethod]
    public void Sample_DescribedFeatures_AreDeliberatelyUnimplemented()
    {
        // The demo is earned live. If the sample already implements the ticket
        // features, the demo proves nothing — this pins the "unsolved" baseline.
        // Implementation-shaped markers only; a comment pointing at a ticket
        // implements nothing.
        var domainDir = Path.Combine(RepoRoot(), "Samples", "BookSeller", "src", "BookSeller.Domain");
        var book = File.ReadAllText(Path.Combine(domainDir, "Book.cs"));
        var catalog = File.ReadAllText(Path.Combine(domainDir, "Catalog.cs"));
        var pricing = File.ReadAllText(Path.Combine(domainDir, "PricingService.cs"));

        Assert.IsFalse(book.Contains("throw", StringComparison.Ordinal),
            "Book.cs must not pre-implement validation — the validate-book ticket is solved live.");
        Assert.IsFalse(catalog.Contains("book.Author", StringComparison.Ordinal) || catalog.Contains("ByAuthor", StringComparison.Ordinal),
            "Catalog.cs must not pre-implement author search — the search-by-author ticket is solved live.");
        Assert.IsFalse(pricing.Contains("0.9", StringComparison.Ordinal) || pricing.Contains("Math.Round", StringComparison.Ordinal),
            "PricingService.cs must not pre-implement discounting — the bulk-discount ticket is solved live.");
    }

    // ── fixture model + helpers ────────────────────────────────────────────

    internal sealed record FixtureFile(FixtureProject Project, List<FixtureTicket> Tickets);

    internal sealed record FixtureProject(
        string Name, string Description, string RelativePath, string BuildCommand, string TestCommand);

    internal sealed record FixtureTicket(
        string Key, string Title, string Summary, string AcceptanceCriteria, string TechnicalNotes, List<string> BlockedBy);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static FixtureFile LoadFixture()
    {
        var fixture = JsonSerializer.Deserialize<FixtureFile>(File.ReadAllText(FixturePath()), JsonOptions);
        Assert.IsNotNull(fixture, "tickets.json must deserialize.");
        return fixture;
    }

    internal static string FixturePath() =>
        Path.Combine(RepoRoot(), "TestFixtures", "BookSeller", "tickets.json");

    internal static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
