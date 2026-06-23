namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockC04GitHubActionsCliBoundaryCiTests
{
    private static readonly string[] CliBoundaryCategories =
    [
        "ApiCliContract",
        "ApiCliReleaseGate"
    ];

    private static readonly string[] CliBoundaryClasses =
    [
        "ApiCliBoundaryContractTests",
        "ApiCliCommandMappingTests",
        "ApiCliStaticBoundaryTests",
        "ApiCliReleaseGateReportTests"
    ];

    [TestMethod]
    public void BlockC04_GovernanceBoundaryScript_AddsCliProjectLane()
    {
        var script = ScriptText();

        StringAssert.Contains(script, "$script:CliProject = \"IronDev.IntegrationTests/IronDev.IntegrationTests.csproj\"");
        StringAssert.Contains(script, "function Invoke-CliBoundaryTestLane");
        StringAssert.Contains(script, "dotnet test $script:CliProject");
        StringAssert.Contains(script, "-Name \"CLI boundary tests\"");
        StringAssert.Contains(script, "--filter $Filter");
        StringAssert.Contains(script, "--no-restore");
        StringAssert.Contains(script, "--no-build");
        StringAssert.Contains(script, "--logger \"console;verbosity=minimal\"");
    }

    [TestMethod]
    public void BlockC04_CliLane_UsesExplicitCliBoundaryFilters()
    {
        var script = ScriptText();

        foreach (var category in CliBoundaryCategories)
            StringAssert.Contains(script, $"TestCategory={category}");

        foreach (var marker in new[]
        {
            "dotnet test IronDev.slnx",
            "FullyQualifiedName~ApiCliContract",
            "FullyQualifiedName~Cli",
            "--filter Cli",
            "*Cli*",
            "CliFoundation|CliAgentRuns|CliManualCritic"
        })
        {
            AssertDoesNotContain(script, marker);
        }
    }

    [TestMethod]
    public void BlockC04_CliLane_UsesNoSqlNoLiveServerCliContractTests()
    {
        foreach (var className in CliBoundaryClasses)
        {
            var source = CliTestSource(className);

            StringAssert.Contains(source, $"class {className}");
            AssertDoesNotContain(source, ": ApiTestBase");
            AssertDoesNotContain(source, "using Microsoft.Data.SqlClient");
            AssertDoesNotContain(source, "WebApplicationFactory");
            AssertDoesNotContain(source, "Factory.CreateClient");
            AssertDoesNotContain(source, "Client.GetAsync");
            AssertDoesNotContain(source, "Client.Post");
            AssertDoesNotContain(source, "ProcessStartInfo");
            AssertDoesNotContain(source, "dotnet run");
        }
    }

    [TestMethod]
    public void BlockC04_CliLane_DoesNotRequireSqlDockerSecretsProvidersOrLiveServers()
    {
        var script = ScriptText();

        foreach (var marker in new[]
        {
            "docker",
            "docker compose",
            "sqlcmd",
            "(localdb)",
            "MSSQL",
            "SqlConnection",
            "ConnectionStrings__IronDeveloperDb",
            "appsettings.Test.json",
            "OpenAI",
            "OLLAMA",
            "WEAVIATE",
            "ApiKey",
            "secrets.",
            "curl",
            "Invoke-WebRequest",
            "http://",
            "https://",
            "dotnet run",
            "Start-Process"
        })
        {
            AssertDoesNotContain(script, marker);
        }
    }

    [TestMethod]
    public void BlockC04_CliLane_DoesNotShellIntoLiveMutationCommands()
    {
        var script = ScriptText();

        foreach (var marker in new[]
        {
            "git push",
            "git commit",
            "git tag",
            "gh pr",
            "gh issue",
            "gh release",
            "gh api",
            "gh workflow",
            "dotnet nuget push",
            "kubectl",
            "az deployment",
            "aws deploy",
            "gcloud deploy",
            "release create",
            "create release",
            "package publish",
            "promote-memory",
            "continue workflow"
        })
        {
            AssertDoesNotContain(script, marker);
        }
    }

    [TestMethod]
    public void BlockC04_Workflow_RemainsReadOnlyAndUsesBoundaryScript()
    {
        var workflow = WorkflowText();

        StringAssert.Contains(workflow, "name: governance-boundary-ci");
        StringAssert.Contains(workflow, "pull_request:");
        StringAssert.Contains(workflow, "branches:");
        StringAssert.Contains(workflow, "- main");
        StringAssert.Contains(workflow, "permissions:");
        StringAssert.Contains(workflow, "contents: read");
        StringAssert.Contains(workflow, "./Scripts/ci/run-governance-boundary-ci.ps1");

        foreach (var marker in new[]
        {
            "write-all",
            "contents: write",
            "pull-requests: write",
            "issues: write",
            "checks: write",
            "statuses: write",
            "deployments: write",
            "packages: write",
            "actions: write",
            "id-token: write"
        })
        {
            AssertDoesNotContain(workflow, marker);
        }
    }

    [TestMethod]
    public void BlockC04_Workflow_DoesNotMutateGithubReleaseDeploymentOrPackages()
    {
        var workflow = WorkflowText();

        foreach (var marker in new[]
        {
            "git push",
            "git commit",
            "git tag",
            "gh pr",
            "gh issue",
            "gh release",
            "gh api",
            "gh workflow",
            "actions/upload-artifact",
            "create-pull-request",
            "auto-merge",
            "softprops/action-gh-release",
            "dotnet nuget push",
            "docker",
            "kubectl",
            "az deployment",
            "aws deploy",
            "gcloud deploy",
            "deployment",
            "publish"
        })
        {
            AssertDoesNotContain(workflow, marker);
        }
    }

    [TestMethod]
    public void BlockC04_Receipt_RecordsCliBoundaryCi()
    {
        var doc = ReceiptText();

        foreach (var section in new[]
        {
            "## Summary",
            "## Boundary",
            "## Workflow Scope",
            "## CLI Scope",
            "## CI Lane",
            "## Forbidden Mutation Paths",
            "## Validation",
            "## Review Traps",
            "## Killjoy"
        })
        {
            StringAssert.Contains(doc, section);
        }

        foreach (var requiredLine in new[]
        {
            "CLI CI proves the CLI boundary is checked.",
            "CLI CI reports evidence only.",
            "CLI CI is not approval.",
            "CLI CI is not CLI authority.",
            "CLI CI is not policy satisfaction.",
            "CLI CI is not execution permission.",
            "CLI CI is not workflow continuation.",
            "The workflow uses read-only repository permissions.",
            "The workflow does not mutate source, PRs, issues, labels, releases, deployments, memory, receipts, or workflow state.",
            "The CLI lane runs the integration test project directly.",
            "The CLI lane uses explicit CLI boundary category filters.",
            "The CLI lane does not run a broad CLI sweep.",
            "The CLI lane does not require SQL Server, Docker, external AI providers, secrets, external HTTP, live hosted API processes, or live mutation commands.",
            "No CLI behavior, command, authorization, API behavior, generated client, SQL, executor, approval, policy, source-apply, commit, push, PR, release, deploy, memory, or workflow-continuation path was added.",
            "A passing CLI test is evidence, not permission to execute, mutate, approve, continue workflow, release, deploy, or bypass the backend gate."
        })
        {
            StringAssert.Contains(doc, requiredLine);
        }
    }

    private static string CliTestSource(string className)
    {
        var root = FindRepositoryRoot();
        var path = Directory.GetFiles(Path.Combine(root, "IronDev.IntegrationTests", "ApiCliContract"), "*.cs")
            .SingleOrDefault(file => File.ReadAllText(file).Contains($"class {className}", StringComparison.Ordinal));

        return path is null
            ? throw new FileNotFoundException($"Could not find CLI boundary test class {className}.")
            : File.ReadAllText(path);
    }

    private static string WorkflowText() =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "governance-boundary-ci.yml"));

    private static string ScriptText() =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Scripts",
            "ci",
            "run-governance-boundary-ci.ps1"));

    private static string ReceiptText() =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "C04_GITHUB_ACTIONS_CLI_BOUNDARY_CI.md"));

    private static void AssertDoesNotContain(string text, string marker)
    {
        Assert.IsFalse(
            text.Contains(marker, StringComparison.OrdinalIgnoreCase),
            $"Unexpected marker '{marker}'.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
