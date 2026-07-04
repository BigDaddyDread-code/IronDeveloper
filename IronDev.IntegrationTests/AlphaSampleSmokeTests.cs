using System.Diagnostics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// D-1 — the demo target is executable proof, not theatre. This suite is the
/// build-path guarantee behind the alpha demo:
///  - Samples/BookSeller compiles and its own tests pass AS-IS (the fixture
///    tickets describe features deliberately not implemented — the Builder
///    earns the demo by solving them live; nothing here pre-bakes answers);
///  - TestFixtures/BookSeller/tickets.json is complete, its dependency chain
///    resolves and yields more than one wave (so the batch demo sequences),
///    and it names no approvals, capabilities, or gates;
///  - nothing in the sample or fixtures points at machine-specific paths — a
///    clean machine can run the demo without hidden local knowledge.
/// </summary>
[TestClass]
[TestCategory("LongRunning")]
[TestCategory("SkeletonRun")]
public sealed class AlphaSampleSmokeTests
{
    [TestMethod]
    public void Sample_CompilesGreen_AsIs()
    {
        var sampleRoot = Path.Combine(RepoRoot(), "Samples", "BookSeller");
        var (exitCode, output) = RunDotnet("build BookSeller.slnx", sampleRoot);

        Assert.AreEqual(0, exitCode,
            $"Samples/BookSeller must compile as-is — a demo that does not build is theatre.{Environment.NewLine}{Tail(output)}");
    }

    [TestMethod]
    public void SampleTests_PassGreen_AsIs()
    {
        var sampleRoot = Path.Combine(RepoRoot(), "Samples", "BookSeller");
        var (exitCode, output) = RunDotnet("test BookSeller.slnx", sampleRoot);

        Assert.AreEqual(0, exitCode,
            $"The sample's own tests must pass as-is — the loop's workspace runs them, so a red baseline poisons every run.{Environment.NewLine}{Tail(output)}");
    }

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
                     @"C:\Users", @"C:\\Users", "%USERPROFILE%", "(localdb)", "SQLEXPRESS", "DESKTOP-"
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
        var domainDir = Path.Combine(RepoRoot(), "Samples", "BookSeller", "src", "BookSeller.Domain");
        var book = File.ReadAllText(Path.Combine(domainDir, "Book.cs"));
        var catalog = File.ReadAllText(Path.Combine(domainDir, "Catalog.cs"));
        var pricing = File.ReadAllText(Path.Combine(domainDir, "PricingService.cs"));

        // Implementation-shaped markers only — the sources may (and do) carry
        // comments pointing at the tickets; a comment implements nothing.
        Assert.IsFalse(book.Contains("throw", StringComparison.Ordinal),
            "Book.cs must not pre-implement validation — the validate-book ticket is solved live.");
        Assert.IsFalse(catalog.Contains("book.Author", StringComparison.Ordinal) || catalog.Contains("ByAuthor", StringComparison.Ordinal),
            "Catalog.cs must not pre-implement author search — the search-by-author ticket is solved live.");
        Assert.IsFalse(pricing.Contains("0.9", StringComparison.Ordinal) || pricing.Contains("Math.Round", StringComparison.Ordinal),
            "PricingService.cs must not pre-implement discounting — the bulk-discount ticket is solved live.");
    }

    // ── fixture model + helpers ────────────────────────────────────────────

    private sealed record FixtureFile(FixtureProject Project, List<FixtureTicket> Tickets);

    private sealed record FixtureProject(
        string Name, string Description, string RelativePath, string BuildCommand, string TestCommand);

    private sealed record FixtureTicket(
        string Key, string Title, string Summary, string AcceptanceCriteria, string TechnicalNotes, List<string> BlockedBy);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static FixtureFile LoadFixture()
    {
        var fixture = JsonSerializer.Deserialize<FixtureFile>(File.ReadAllText(FixturePath()), JsonOptions);
        Assert.IsNotNull(fixture, "tickets.json must deserialize.");
        return fixture;
    }

    private static string FixturePath() =>
        Path.Combine(RepoRoot(), "TestFixtures", "BookSeller", "tickets.json");

    private static (int ExitCode, string Output) RunDotnet(string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.IsTrue(process.WaitForExit(TimeSpan.FromMinutes(5)), $"dotnet {arguments} timed out.");
        return (process.ExitCode, stdout + stderr);
    }

    private static string Tail(string output)
    {
        var lines = output.Split('\n');
        return string.Join('\n', lines.Skip(Math.Max(0, lines.Length - 25)));
    }

    private static string RepoRoot()
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
