namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockC01GitHubActionsBoundaryCiTests
{
    private static readonly string[] BSeriesBoundaryClasses =
    [
        "BlockB01AuthorityProfileKindUnificationTests",
        "BlockB03AuthorityProfileVocabularyDriftTests",
        "BlockB04AskBeforeMutationRunProfileTests",
        "BlockB05BoundedRunAuthorityProfileTests",
        "BlockB06AuthorityProfileStatusCanonicalModelTests",
        "BlockB07ProposalOnlyMutationStatusProofTests",
        "BlockB08AskBeforeMutationBoundaryProofTests",
        "BlockB09BoundedRunAuthorityDownstreamProofTests",
        "BlockB10CanonicalAuthorityGlossaryTests",
        "BlockB11StatusAuthorityGlossaryAdoptionTests",
        "BlockB12HostileProfileTextEligibilityProofTests"
    ];

    private static readonly string[] CompatibilityBoundaryClasses =
    [
        "BlockBQRunAuthorityProfileContractTests",
        "BlockBRBoundedRunAuthorityGrantTests",
        "BlockBSOperationEligibilityEvaluatorTests",
        "BlockBTAuthorityProfileStatusMappingTests",
        "BlockBUSourceApplyConsumesBoundedAuthorityTests"
    ];

    [TestMethod]
    public void BlockC01_Workflow_ExistsAndIsPullRequestScoped()
    {
        var workflow = WorkflowText();

        StringAssert.Contains(workflow, "name: governance-boundary-ci");
        StringAssert.Contains(workflow, "pull_request:");
        StringAssert.Contains(workflow, "branches:");
        StringAssert.Contains(workflow, "- main");
        StringAssert.Contains(workflow, "workflow_dispatch:");
    }

    [TestMethod]
    public void BlockC01_Workflow_UsesReadOnlyRepositoryPermissions()
    {
        var workflow = WorkflowText();

        StringAssert.Contains(workflow, "permissions:");
        StringAssert.Contains(workflow, "contents: read");
        AssertDoesNotContain(workflow, "write-all");
        AssertDoesNotContain(workflow, "contents: write");

        foreach (var permission in new[]
        {
            "pull-requests",
            "issues",
            "checks",
            "statuses",
            "deployments",
            "packages",
            "actions",
            "id-token"
        })
        {
            AssertDoesNotContain(workflow, $"{permission}: write");
        }
    }

    [TestMethod]
    public void BlockC01_Workflow_DoesNotContainAuthorityMutationSteps()
    {
        var workflow = WorkflowText();
        var forbidden = new[]
        {
            "git push",
            "gh pr",
            "gh issue",
            "gh release",
            "gh workflow",
            "actions/upload-artifact",
            "dotnet nuget push",
            "docker",
            "kubectl",
            "az deployment",
            "aws deploy",
            "gcloud deploy",
            "softprops/action-gh-release"
        };

        foreach (var marker in forbidden)
            AssertDoesNotContain(workflow, marker);
    }

    [TestMethod]
    public void BlockC01_Workflow_RestoresBuildsAndRunsBoundaryScript()
    {
        var workflow = WorkflowText();

        StringAssert.Contains(workflow, "actions/checkout@v4");
        StringAssert.Contains(workflow, "actions/setup-dotnet@v4");
        StringAssert.Contains(workflow, "dotnet-version: '10.0.x'");
        StringAssert.Contains(workflow, "dotnet restore IronDev.slnx");
        StringAssert.Contains(workflow, "dotnet build IronDev.slnx --no-restore");
        StringAssert.Contains(workflow, "shell: pwsh");
        StringAssert.Contains(workflow, "./Scripts/ci/run-governance-boundary-ci.ps1");
    }

    [TestMethod]
    public void BlockC01_Script_DoesNotRunBroadBlockSweep()
    {
        var script = ScriptText();
        var forbidden = new[]
        {
            "FullyQualifiedName~Block\"",
            "FullyQualifiedName~Block'",
            "FullyQualifiedName~Block ",
            "--filter Block",
            "Block*",
            "*Block*"
        };

        foreach (var marker in forbidden)
            AssertDoesNotContain(script, marker);
    }

    [TestMethod]
    public void BlockC01_Script_IncludesBSeriesAndCompatibilityBoundaryTests()
    {
        var script = ScriptText();

        foreach (var className in BSeriesBoundaryClasses.Concat(CompatibilityBoundaryClasses))
            StringAssert.Contains(script, $"FullyQualifiedName~{className}");

        StringAssert.Contains(script, "dotnet test $script:Project");
        StringAssert.Contains(script, "--no-restore");
        StringAssert.Contains(script, "--no-build");
        StringAssert.Contains(script, "--logger \"console;verbosity=minimal\"");
    }

    [TestMethod]
    public void BlockC01_Script_DoesNotRequireExternalServicesOrSecrets()
    {
        var script = ScriptText();
        var forbidden = new[]
        {
            "docker",
            "docker compose",
            "sqlcmd",
            "(localdb)",
            "MSSQL",
            "OpenAI",
            "OLLAMA",
            "WEAVIATE",
            "ApiKey",
            "secrets."
        };

        foreach (var marker in forbidden)
            AssertDoesNotContain(script, marker);
    }

    [TestMethod]
    public void BlockC01_Receipt_RecordsCiBoundary()
    {
        var doc = ReceiptText();

        foreach (var section in new[]
        {
            "## Summary",
            "## Boundary",
            "## Workflow Scope",
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
            "GitHub Actions CI reports evidence only.",
            "CI is not approval.",
            "CI is not merge readiness.",
            "CI is not release readiness.",
            "CI is not deployment readiness.",
            "CI is not policy satisfaction.",
            "CI is not execution permission.",
            "The workflow uses read-only repository permissions.",
            "The workflow does not mutate source, PRs, issues, labels, releases, deployments, memory, receipts, or workflow state.",
            "The workflow restores packages.",
            "The workflow builds IronDev.slnx.",
            "The workflow runs explicit governance boundary tests.",
            "The workflow does not run a broad Block sweep.",
            "The workflow does not require SQL Server, Docker, external AI providers, or secrets.",
            "No executor, mutation, approval, policy, UI, API, CLI, SQL, durable store, generated client, release, or deployment path was added.",
            "A green check is evidence, not permission."
        })
        {
            StringAssert.Contains(doc, requiredLine);
        }
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
            "C01_GITHUB_ACTIONS_BUILD_BOUNDARY_CI.md"));

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
