using System.IO;
using System.Text.RegularExpressions;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ApiBoundaryTests
{
    [TestMethod]
    public void ForwardProductProjects_MustNotReferenceInfrastructure()
    {
        var root = FindRepositoryRoot();

        AssertProjectDoesNotReference(root, "IronDev.Client", "IronDev.Infrastructure");
        AssertProjectDoesNotReference(root, Path.Combine("tools", "IronDev.Cli"), "IronDev.Infrastructure");
    }

    [TestMethod]
    public void RetiredWpfProject_MustNotBeInProductBuild()
    {
        var root = FindRepositoryRoot();
        var retiredRoot = Path.Combine(root, "IronDeveloper");

        Assert.IsFalse(
            File.Exists(Path.Combine(retiredRoot, "IronDev.Agent.csproj")),
            "WPF was retired; IronDeveloper/IronDev.Agent.csproj must not be restored as a product project.");

        if (Directory.Exists(retiredRoot))
        {
            var sourceFiles = Directory.EnumerateFiles(retiredRoot, "*.*", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.vs{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.GetRelativePath(root, path))
                .ToArray();

            Assert.AreEqual(
                0,
                sourceFiles.Length,
                "Retired WPF source files must not exist under IronDeveloper. Found: " + string.Join(", ", sourceFiles));
        }

        var solution = File.ReadAllText(Path.Combine(root, "IronDev.slnx"));
        Assert.IsFalse(
            solution.Contains("IronDeveloper", StringComparison.Ordinal),
            "IronDev.slnx must not reference the retired WPF project.");
    }

    [TestMethod]
    public void ProductCli_MustUseIronDevClientInsteadOfDirectHttp()
    {
        var root = FindRepositoryRoot();
        var cliRoot = Path.Combine(root, "tools", "IronDev.Cli");
        var project = File.ReadAllText(Path.Combine(cliRoot, "IronDev.Cli.csproj"));
        StringAssert.Contains(project, "IronDev.Client");

        var forbidden = new[]
        {
            "new HttpClient",
            ".GetAsync(",
            ".PostAsJsonAsync(",
            ".PutAsJsonAsync(",
            ".DeleteAsync(",
            "ReadFromJsonAsync",
            "System.Net.Http.Json"
        };

        foreach (var source in Directory.EnumerateFiles(cliRoot, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(source);
            foreach (var token in forbidden)
            {
                Assert.IsFalse(
                    text.Contains(token, StringComparison.Ordinal),
                    $"Product CLI must use IronDev.Client instead of direct HTTP. Found '{token}' in {source}.");
            }
        }
    }

    [TestMethod]
    public void TauriShellSource_MustNotReferenceInfrastructureStorageDetails()
    {
        var root = FindRepositoryRoot();
        var shellRoot = Path.Combine(root, "IronDev.TauriShell");
        var forbidden = new Regex(@"IronDev\.Infrastructure|SqlConnection|Dapper|Weaviate", RegexOptions.Compiled);

        foreach (var source in Directory.EnumerateFiles(shellRoot, "*.*", SearchOption.AllDirectories)
                     .Where(path => path.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                                    path.Contains($"{Path.DirectorySeparatorChar}src-tauri{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                     .Where(path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith(".rs", StringComparison.OrdinalIgnoreCase)))
        {
            var text = File.ReadAllText(source);
            Assert.IsFalse(forbidden.IsMatch(text), $"Tauri shell must not reference infrastructure storage/provider details. Violation in {source}.");
        }
    }

    [TestMethod]
    public void BackendSpineBoundaries_MustKeepStageOwnershipInChatDiscussionProposalBuildRunFlow()
    {
        var root = FindRepositoryRoot();

        var architecture = File.ReadAllText(Path.Combine(root, "Docs", "ARCHITECTURE.md"));
        var requiredMatrixTokens = new[]
        {
            "## Backend Spine Boundary Matrix",
            "| Discussion |",
            "| Chat |",
            "| Proposal |",
            "| Build |",
            "| Run |"
        };

        foreach (var token in requiredMatrixTokens)
        {
            Assert.IsTrue(
                architecture.Contains(token, StringComparison.Ordinal),
                $"Architecture boundary matrix must include required section content: {token}");
        }

        var chatController = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "ChatController.cs"));
        var discussionController = File.ReadAllText(
            Path.Combine(root, "IronDev.Api", "Controllers", "DiscussionCodeLoopController.cs"));
        var ticketsController = File.ReadAllText(
            Path.Combine(root, "IronDev.Api", "Controllers", "TicketsController.cs"));

        var chatForbidden = new[]
        {
            "ITicketBuildOrchestrator",
            "ITicketBuildRunService",
            "IBuilderProposalService",
            "IDisposableCodeRunService",
            "IRunReviewPackageService",
            "IBuilderReadinessService"
        };

        foreach (var token in chatForbidden)
        {
            Assert.IsFalse(
                chatController.Contains(token, StringComparison.Ordinal),
                $"ChatController must not own build/proposal/run services directly. Found '{token}'.");
        }

        var discussionRequired = new[]
        {
            "IDiscussionDocumentService",
            "ITicketFromDocumentService",
            "ITicketReviewService",
            "IDisposableCodeRunService",
            "IRunReviewPackageService"
        };

        foreach (var token in discussionRequired)
        {
            Assert.IsTrue(
                discussionController.Contains(token, StringComparison.Ordinal),
                $"DiscussionCodeLoopController must include '{token}' for Discussion->Review->Run stage ownership.");
        }

        var discussionForbidden = new[]
        {
            "ITicketBuildOrchestrator",
            "ITicketBuildRunService",
            "IBuilderProposalService",
            "IBuilderReadinessService"
        };

        foreach (var token in discussionForbidden)
        {
            Assert.IsFalse(
                discussionController.Contains(token, StringComparison.Ordinal),
                $"DiscussionCodeLoopController must not bypass the ticket/build seam. Found forbidden '{token}'.");
        }

        var ticketsRequired = new[]
        {
            "IBuilderProposalService",
            "ITicketBuildOrchestrator",
            "ITicketBuildRunService",
            "ITicketRunReviewService"
        };

        foreach (var token in ticketsRequired)
        {
            Assert.IsTrue(
                ticketsController.Contains(token, StringComparison.Ordinal),
                $"TicketsController should own the proposal/build/run interfaces. Missing '{token}'.");
        }
    }

    private static void AssertProjectDoesNotReference(string root, string projectDirectory, string forbiddenReference)
    {
        var projectRoot = Path.Combine(root, projectDirectory);
        foreach (var projectFile in Directory.EnumerateFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly)
                     .Where(path => !Path.GetFileName(path).Contains("_wpftmp", StringComparison.OrdinalIgnoreCase)))
        {
            var text = File.ReadAllText(projectFile);
            Assert.IsFalse(
                text.Contains(forbiddenReference, StringComparison.Ordinal),
                $"{projectFile} must not reference {forbiddenReference}.");
        }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "IronDev.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
