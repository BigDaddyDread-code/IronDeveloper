namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockC05GitHubActionsFrontendContractCiTests
{
    [TestMethod]
    public void BlockC05_Discovery_UsesExistingTauriOpenApiAndLockfile()
    {
        var root = FindRepositoryRoot();
        var packageJson = File.ReadAllText(Path.Combine(root, "IronDev.TauriShell", "package.json"));

        Assert.IsTrue(Directory.Exists(Path.Combine(root, "IronDev.TauriShell")));
        Assert.IsTrue(File.Exists(Path.Combine(root, "IronDev.TauriShell", "package-lock.json")));
        Assert.IsTrue(File.Exists(Path.Combine(root, "IronDev.TauriShell", "tsconfig.json")));
        Assert.IsTrue(File.Exists(Path.Combine(root, "IronDev.TauriShell", "src-tauri", "tauri.conf.json")));
        Assert.IsTrue(File.Exists(Path.Combine(root, "IronDev.TauriShell", "openapi", "irondev-api.openapi.json")));
        Assert.IsTrue(File.Exists(Path.Combine(root, "IronDev.TauriShell", "src", "api", "generated", "ironDevApiTypes.ts")));

        StringAssert.Contains(packageJson, "\"openapi-typescript\"");
        StringAssert.Contains(packageJson, "\"build\": \"tsc --noEmit && vite build\"");
    }

    [TestMethod]
    public void BlockC05_Workflow_IsSeparateReadOnlyFrontendContractCi()
    {
        var workflow = WorkflowText();

        StringAssert.Contains(workflow, "name: frontend-contract-ci");
        StringAssert.Contains(workflow, "pull_request:");
        StringAssert.Contains(workflow, "branches:");
        StringAssert.Contains(workflow, "- main");
        StringAssert.Contains(workflow, "workflow_dispatch:");
        StringAssert.Contains(workflow, "permissions:");
        StringAssert.Contains(workflow, "contents: read");
        StringAssert.Contains(workflow, "name: Tauri type-check and OpenAPI drift");
        StringAssert.Contains(workflow, "runs-on: windows-latest");
        StringAssert.Contains(workflow, "uses: actions/setup-node@v4");
        StringAssert.Contains(workflow, "cache-dependency-path: IronDev.TauriShell/package-lock.json");
        StringAssert.Contains(workflow, "./Scripts/ci/run-frontend-contract-ci.ps1");

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
    public void BlockC05_Workflow_DoesNotMutatePublishReleaseDeployOrUploadArtifacts()
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
            "npm publish",
            "pnpm publish",
            "dotnet nuget push",
            "tauri build",
            "notarize",
            "signing",
            "installer",
            "kubectl",
            "az deployment",
            "aws deploy",
            "gcloud deploy"
        })
        {
            AssertDoesNotContain(workflow, marker);
        }
    }

    [TestMethod]
    public void BlockC05_Script_RunsTypeCheckAndOpenApiDriftCheckOnly()
    {
        var script = ScriptText();

        StringAssert.Contains(script, "$script:FrontendRoot = Join-Path $script:RepoRoot \"IronDev.TauriShell\"");
        StringAssert.Contains(script, "$script:OpenApiSnapshot = Join-Path $script:FrontendRoot \"openapi\\irondev-api.openapi.json\"");
        StringAssert.Contains(script, "$script:GeneratedClient = Join-Path $script:FrontendRoot \"src\\api\\generated\\ironDevApiTypes.ts\"");
        StringAssert.Contains(script, "npm ci");
        StringAssert.Contains(script, "npx tsc --noEmit");
        StringAssert.Contains(script, "npx openapi-typescript openapi/irondev-api.openapi.json -o $tempGeneratedClient");
        StringAssert.Contains(script, "[System.IO.Path]::GetTempPath()");
        StringAssert.Contains(script, "Remove-Item -LiteralPath $tempRoot -Recurse -Force");
        StringAssert.Contains(script, "-replace \"`r`n\", \"`n\" -replace \"`r\", \"`n\"");
        StringAssert.Contains(script, "$committedClient -ne $generatedClient");
        StringAssert.Contains(script, "OpenAPI/client drift detected. This is evidence only. Update the API contract/client in a separate reviewed PR.");
    }

    [TestMethod]
    public void BlockC05_Script_DoesNotFetchLiveApiWriteGeneratedSourceOrCallMutationTools()
    {
        var script = ScriptText();

        foreach (var marker in new[]
        {
            "api:fetch-openapi",
            "api:generate",
            "fetch-openapi-spec",
            "IRONDEV_API_BASE_URL",
            "VITE_IRONDEV_API_BASE_URL",
            "swagger/v1/swagger.json",
            "http://",
            "https://",
            "dotnet run",
            "Start-Process",
            "git add",
            "git commit",
            "git push",
            "gh pr",
            "gh release",
            "npm publish",
            "pnpm publish",
            "tauri build",
            "actions/upload-artifact",
            "sqlcmd",
            "docker",
            "ConnectionStrings__IronDeveloperDb",
            "OpenAI",
            "OLLAMA",
            "WEAVIATE",
            "ApiKey",
            "secrets."
        })
        {
            AssertDoesNotContain(script, marker);
        }
    }

    [TestMethod]
    public void BlockC05_CSeriesSplit_RemainsNoSqlExceptC02()
    {
        var governanceWorkflow = GovernanceBoundaryWorkflowText();
        var sqlWorkflow = SqlIntegrationWorkflowText();
        var frontendWorkflow = WorkflowText();

        StringAssert.Contains(governanceWorkflow, "name: governance-boundary-ci");
        StringAssert.Contains(governanceWorkflow, "contents: read");
        StringAssert.Contains(governanceWorkflow, "./Scripts/ci/run-governance-boundary-ci.ps1");

        StringAssert.Contains(sqlWorkflow, "name: sql-integration-ci");
        StringAssert.Contains(sqlWorkflow, "services:");
        StringAssert.Contains(sqlWorkflow, "sqlserver:");
        StringAssert.Contains(sqlWorkflow, "./Scripts/ci/run-sql-integration-ci.ps1");

        StringAssert.Contains(frontendWorkflow, "name: frontend-contract-ci");
        AssertDoesNotContain(frontendWorkflow, "sqlserver:");
        AssertDoesNotContain(frontendWorkflow, "ConnectionStrings__IronDeveloperDb");
        AssertDoesNotContain(frontendWorkflow, "run-governance-boundary-ci.ps1");
        AssertDoesNotContain(frontendWorkflow, "run-sql-integration-ci.ps1");
    }

    [TestMethod]
    public void BlockC05_Receipt_RecordsFrontendContractBoundary()
    {
        var doc = ReceiptText();

        foreach (var section in new[]
        {
            "## Summary",
            "## Boundary",
            "## Workflow Scope",
            "## Frontend/Tauri Scope",
            "## OpenAPI Drift Scope",
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
            "Frontend contract CI proves the Tauri/API contract still checks.",
            "Frontend contract CI reports evidence only.",
            "Frontend contract CI is not frontend authority.",
            "Frontend contract CI is not API authority.",
            "Frontend contract CI is not generated-client approval.",
            "Frontend contract CI is not policy satisfaction.",
            "Frontend contract CI is not execution permission.",
            "Frontend contract CI is not package publication.",
            "Frontend contract CI is not workflow continuation.",
            "Frontend contract CI reports evidence only. It is not frontend authority, API authority, generated-client approval, merge readiness, release readiness, deployment readiness, policy satisfaction, execution permission, package publication, or workflow continuation.",
            "OpenAPI drift success means the committed contract and generated/client surface match. It does not approve API shape or client changes.",
            "The drift check is check-only. It does not fetch a live API, silently update snapshots, write generated clients into source, commit generated files, upload generated files, or publish packages.",
            "A clean type-check and OpenAPI drift check is evidence, not permission to change API shape, generate clients, publish artifacts, release, deploy, or bypass backend authority."
        })
        {
            StringAssert.Contains(doc, requiredLine);
        }
    }

    [TestMethod]
    public void BlockC05_AllowedFilesOnly()
    {
        var root = FindRepositoryRoot();

        Assert.IsTrue(File.Exists(Path.Combine(root, ".github", "workflows", "frontend-contract-ci.yml")));
        Assert.IsTrue(File.Exists(Path.Combine(root, "Scripts", "ci", "run-frontend-contract-ci.ps1")));
        Assert.IsTrue(File.Exists(Path.Combine(root, "IronDev.IntegrationTests", "BlockC05GitHubActionsFrontendContractCiTests.cs")));
        Assert.IsTrue(File.Exists(Path.Combine(root, "Docs", "receipts", "C05_GITHUB_ACTIONS_FRONTEND_CONTRACT_CI.md")));
    }

    private static string WorkflowText() =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "frontend-contract-ci.yml"));

    private static string GovernanceBoundaryWorkflowText() =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "governance-boundary-ci.yml"));

    private static string SqlIntegrationWorkflowText() =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "sql-integration-ci.yml"));

    private static string ScriptText() =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Scripts",
            "ci",
            "run-frontend-contract-ci.ps1"));

    private static string ReceiptText() =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "C05_GITHUB_ACTIONS_FRONTEND_CONTRACT_CI.md"));

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
