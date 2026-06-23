namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockC03GitHubActionsApiBoundaryCiTests
{
    private static readonly string[] ApiBoundaryClasses =
    [
        "BlockOOperationalReadinessApiSurfaceTests",
        "OperationalDebuggingApiContractTests",
        "RunsEndpointContractTests",
        "WorkflowContinuationApiRegressionTests"
    ];

    [TestMethod]
    public void BlockC03_GovernanceBoundaryScript_AddsApiProjectLane()
    {
        var script = ScriptText();

        StringAssert.Contains(script, "$script:ApiProject = \"IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj\"");
        StringAssert.Contains(script, "function Invoke-ApiBoundaryTestLane");
        StringAssert.Contains(script, "dotnet test $script:ApiProject");
        StringAssert.Contains(script, "-Name \"API boundary tests\"");
        StringAssert.Contains(script, "--filter $Filter");
        StringAssert.Contains(script, "--no-restore");
        StringAssert.Contains(script, "--no-build");
        StringAssert.Contains(script, "--logger \"console;verbosity=minimal\"");
    }

    [TestMethod]
    public void BlockC03_ApiLane_UsesExplicitApiBoundaryFilters()
    {
        var script = ScriptText();

        foreach (var className in ApiBoundaryClasses)
            StringAssert.Contains(script, $"FullyQualifiedName~{className}");

        foreach (var marker in new[]
        {
            "dotnet test IronDev.slnx",
            "dotnet test IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj",
            "--filter \"FullyQualifiedName~\"",
            "--filter 'FullyQualifiedName~'",
            "--filter Block",
            "FullyQualifiedName~Api",
            "*Api*"
        })
        {
            AssertDoesNotContain(script, marker);
        }
    }

    [TestMethod]
    public void BlockC03_ApiLane_UsesNoSqlApiProjectTests()
    {
        foreach (var className in ApiBoundaryClasses)
        {
            var source = ApiTestSource(className);

            StringAssert.Contains(source, $"class {className}");
            AssertDoesNotContain(source, ": ApiTestBase");
            AssertDoesNotContain(source, "SqlConnection");
            AssertDoesNotContain(source, "ConnectionStrings__IronDeveloperDb");
            AssertDoesNotContain(source, "appsettings.Test.json");
        }
    }

    [TestMethod]
    public void BlockC03_ApiLane_DoesNotRequireSqlDockerSecretsOrExternalProviders()
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
            "dotnet run"
        })
        {
            AssertDoesNotContain(script, marker);
        }
    }

    [TestMethod]
    public void BlockC03_Workflow_RemainsReadOnlyAndUsesBoundaryScript()
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
    public void BlockC03_Workflow_DoesNotMutateGithubReleaseDeploymentOrPackages()
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
    public void BlockC03_Receipt_RecordsApiBoundaryCi()
    {
        var doc = ReceiptText();

        foreach (var section in new[]
        {
            "## Summary",
            "## Boundary",
            "## Workflow Scope",
            "## API Scope",
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
            "API CI proves the API boundary is checked.",
            "API CI is not approval.",
            "API CI is not API authority.",
            "API CI is not policy satisfaction.",
            "API CI is not execution permission.",
            "The workflow uses read-only repository permissions.",
            "The workflow does not mutate source, PRs, issues, labels, releases, deployments, memory, receipts, or workflow state.",
            "The API lane runs the API test project directly.",
            "The API lane uses explicit API boundary class filters.",
            "The API lane does not run a broad API project sweep.",
            "The API lane does not require SQL Server, Docker, external AI providers, secrets, external HTTP, or a live hosted API process.",
            "No API behavior, endpoint, auth, client generation, SQL, executor, approval, policy, source-apply, commit, push, PR, release, deploy, memory, or workflow-continuation path was added.",
            "A passing API test is evidence, not permission to call, mutate, approve, or bypass the backend gate."
        })
        {
            StringAssert.Contains(doc, requiredLine);
        }
    }

    private static string ApiTestSource(string className)
    {
        var root = FindRepositoryRoot();
        var path = Directory.GetFiles(Path.Combine(root, "IronDev.IntegrationTests.Api"), "*.cs")
            .SingleOrDefault(file => File.ReadAllText(file).Contains($"class {className}", StringComparison.Ordinal));

        return path is null
            ? throw new FileNotFoundException($"Could not find API test class {className}.")
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
            "C03_GITHUB_ACTIONS_API_BOUNDARY_CI.md"));

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
