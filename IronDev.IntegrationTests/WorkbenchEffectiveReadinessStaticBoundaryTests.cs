using System.Text.RegularExpressions;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class WorkbenchEffectiveReadinessStaticBoundaryTests
{
    private static readonly string[] ReadSurfaceFiles =
    [
        "ProjectService.cs",
        "WorkbenchProjectEntryService.cs",
        "WorkbenchProjectUnderstandingService.cs",
        "WorkbenchRepositorySetupService.cs",
        Path.Combine("Sandbox", "WorkbenchSandboxQualificationService.cs"),
        Path.Combine("Sandbox", "WorkbenchSandboxRecoveryService.cs"),
        "WorkbenchTicketProposalCommitService.cs"
    ];

    [TestMethod]
    public void WorkbenchReadSurfaces_UseOnlyTheEffectiveReadinessProjection()
    {
        var services = Path.Combine(RepositoryRoot(), "IronDev.Infrastructure", "Services");
        var staleAssessmentRead = new Regex(
            @"SELECT\s+TOP\s*\(1\)\s+(?:value\.)?ExecutionReadiness(?:\s*,\s*(?:value\.)?ReasonCode)?\s+FROM\s+dbo\.ProjectReadinessAssessments",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (var relativePath in ReadSurfaceFiles)
        {
            var source = File.ReadAllText(Path.Combine(services, relativePath));
            StringAssert.Contains(source, "dbo.vw_WorkbenchEffectiveProjectReadiness", relativePath);
            Assert.IsFalse(staleAssessmentRead.IsMatch(source),
                $"{relativePath} must not expose the latest stored assessment as effective readiness.");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
