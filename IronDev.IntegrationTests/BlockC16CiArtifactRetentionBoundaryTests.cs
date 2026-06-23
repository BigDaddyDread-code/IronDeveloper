using System.Diagnostics;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockC16CiArtifactRetentionBoundaryTests
{
    private static readonly string[] WorkflowPaths =
    [
        Path.Combine(".github", "workflows", "governance-boundary-ci.yml"),
        Path.Combine(".github", "workflows", "sql-integration-ci.yml"),
        Path.Combine(".github", "workflows", "frontend-contract-ci.yml")
    ];

    [TestMethod]
    public void BlockC16_WorkflowsUploadBoundedEvidenceArtifacts()
    {
        foreach (var workflowPath in WorkflowPaths)
        {
            var workflow = ReadRepositoryFile(workflowPath);

            StringAssert.Contains(workflow, "uses: actions/upload-artifact@v4");
            StringAssert.Contains(workflow, "if: ${{ always()");
            StringAssert.Contains(workflow, "retention-days: 14");
            StringAssert.Contains(workflow, "if-no-files-found: error");
            StringAssert.Contains(workflow, "path: artifacts/ci/");
            StringAssert.Contains(workflow, "Verify evidence artifacts are sanitized");
            StringAssert.Contains(workflow, "test-ci-evidence-artifact-safety.ps1");
        }
    }

    [TestMethod]
    public void BlockC16_WorkflowsKeepReadOnlyPermissions()
    {
        foreach (var workflowPath in WorkflowPaths)
        {
            var workflow = ReadRepositoryFile(workflowPath);

            StringAssert.Contains(workflow, "permissions:");
            StringAssert.Contains(workflow, "contents: read");
            AssertDoesNotContain(workflow, "contents: write", workflowPath);
            AssertDoesNotContain(workflow, "actions: write", workflowPath);
            AssertDoesNotContain(workflow, "pull-requests: write", workflowPath);
            AssertDoesNotContain(workflow, "issues: write", workflowPath);
            AssertDoesNotContain(workflow, "deployments: write", workflowPath);
            AssertDoesNotContain(workflow, "id-token: write", workflowPath);
        }
    }

    [TestMethod]
    public void BlockC16_WorkflowsDoNotUploadRepositoryOrForbiddenDirectories()
    {
        foreach (var workflowPath in WorkflowPaths)
        {
            var workflow = ReadRepositoryFile(workflowPath);

            AssertDoesNotContain(workflow, "path: .", workflowPath);
            AssertDoesNotContain(workflow, "path: ${{ github.workspace }}", workflowPath);
            AssertDoesNotContain(workflow, ".git", workflowPath);
            AssertDoesNotContain(workflow, "node_modules", workflowPath);
            AssertDoesNotContain(workflow, "bin/", workflowPath);
            AssertDoesNotContain(workflow, "obj/", workflowPath);
            AssertDoesNotContain(workflow, "appsettings.Test.json", workflowPath);
        }
    }

    [TestMethod]
    public void BlockC16_CiScriptsWriteEvidenceSummariesAndBoundedOutputs()
    {
        var governanceScript = ReadRepositoryFile("Scripts", "ci", "run-governance-boundary-ci.ps1");
        var sqlScript = ReadRepositoryFile("Scripts", "ci", "run-sql-integration-ci.ps1");
        var frontendScript = ReadRepositoryFile("Scripts", "ci", "run-frontend-contract-ci.ps1");

        foreach (var script in new[] { governanceScript, sqlScript, frontendScript })
        {
            StringAssert.Contains(script, "artifacts\\ci\\");
            StringAssert.Contains(script, "write-ci-evidence-summary.ps1");
            StringAssert.Contains(script, "test-ci-evidence-artifact-safety.ps1");
            StringAssert.Contains(script, "-ResultStatus \"Started\"");
            StringAssert.Contains(script, "-ResultStatus \"Passed\"");
        }

        StringAssert.Contains(governanceScript, "--logger \"trx;LogFileName=$safeLaneName.trx\"");
        StringAssert.Contains(governanceScript, "--results-directory $script:TestResultsRoot");
        StringAssert.Contains(sqlScript, "--logger \"trx;LogFileName=$safeLaneName.trx\"");
        StringAssert.Contains(sqlScript, "--results-directory $script:TestResultsRoot");
        StringAssert.Contains(frontendScript, "frontend-contract-output.txt");
        StringAssert.Contains(frontendScript, "openapi-drift-summary.txt");
        AssertDoesNotContain(frontendScript, "Copy-Item", "frontend artifact output");
    }

    [TestMethod]
    public void BlockC16_EvidenceSummaryWriterRecordsRequiredFieldsAndBoundary()
    {
        var summaryWriter = ReadRepositoryFile("Scripts", "ci", "write-ci-evidence-summary.ps1");

        foreach (var requiredField in new[]
        {
            "Workflow name",
            "Run id",
            "Run attempt",
            "Commit SHA",
            "Branch/ref",
            "UTC timestamp",
            "Lane name",
            "Command category",
            "Result status"
        })
        {
            StringAssert.Contains(summaryWriter, requiredField);
        }

        StringAssert.Contains(summaryWriter, "Evidence artifact only. Not approval, readiness, authority, or permission.");
        AssertDoesNotContain(summaryWriter, "Get-ChildItem Env:", "evidence summary writer");
        AssertDoesNotContain(summaryWriter, "ConnectionStrings__", "evidence summary writer");
    }

    [TestMethod]
    public void BlockC16_ArtifactSafetyScanRejectsCommonSecretMarkers()
    {
        var safetyScan = ReadRepositoryFile("Scripts", "ci", "test-ci-evidence-artifact-safety.ps1");

        foreach (var marker in new[]
        {
            "Password",
            "Pwd",
            "Bearer ",
            "Authorization:",
            "Jwt:Key",
            "Weaviate:ApiKey",
            "OPENAI_API_KEY=",
            "IRONDEV_JWT_KEY=",
            "IRONDEV_WEAVIATE_API_KEY=",
            "sk-",
            "ghp_",
            "github_pat_",
            "PRIVATE KEY"
        })
        {
            StringAssert.Contains(safetyScan, marker);
        }

        StringAssert.Contains(safetyScan, "/artifacts/ci/");
        StringAssert.Contains(safetyScan, ".git");
        StringAssert.Contains(safetyScan, "node_modules");
        StringAssert.Contains(safetyScan, "bin");
        StringAssert.Contains(safetyScan, "obj");
    }

    [TestMethod]
    public void BlockC16_ArtifactSafetyScan_AllowsSafeBoundedArtifactDirectory()
    {
        using var directory = CreateArtifactDirectory();
        File.WriteAllText(
            Path.Combine(directory.Path, "evidence-summary.md"),
            "Evidence artifact only. Not approval, readiness, authority, or permission.");

        var result = RunArtifactSafetyScan(directory.Path);

        Assert.AreEqual(0, result.ExitCode, result.Output);
    }

    [TestMethod]
    public void BlockC16_ArtifactSafetyScan_RejectsBearerTokenMarker()
    {
        using var directory = CreateArtifactDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "evidence-summary.md"), "Bearer token-value");

        var result = RunArtifactSafetyScan(directory.Path);

        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "Forbidden marker");
    }

    [TestMethod]
    public void BlockC16_ArtifactSafetyScan_RejectsPasswordMarker()
    {
        using var directory = CreateArtifactDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "evidence-summary.md"), string.Concat("Password", "=unsafe"));

        var result = RunArtifactSafetyScan(directory.Path);

        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "Forbidden marker");
    }

    [TestMethod]
    public void BlockC16_ArtifactSafetyScan_RejectsProviderKeyMarker()
    {
        using var directory = CreateArtifactDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "evidence-summary.md"), "sk-unsafe");

        var result = RunArtifactSafetyScan(directory.Path);

        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "Forbidden marker");
    }

    [TestMethod]
    public void BlockC16_ArtifactSafetyScan_RejectsPrivateKeyMarker()
    {
        using var directory = CreateArtifactDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "evidence-summary.md"), string.Concat("BEGIN ", "PRIVATE KEY"));

        var result = RunArtifactSafetyScan(directory.Path);

        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "Forbidden marker");
    }

    [TestMethod]
    public void BlockC16_SecurityBoundaryLaneStillRunsC11ThroughC15()
    {
        var governanceScript = ReadRepositoryFile("Scripts", "ci", "run-governance-boundary-ci.ps1");

        StringAssert.Contains(governanceScript, "FullyQualifiedName~BlockC11SecretScanningRegressionTests");
        StringAssert.Contains(governanceScript, "FullyQualifiedName~BlockC12LocalTestSafetyRegressionTests");
        StringAssert.Contains(governanceScript, "FullyQualifiedName~BlockC13ProductionEnvironmentSafetyRegressionTests");
        StringAssert.Contains(governanceScript, "FullyQualifiedName~BlockC14SensitiveApiRateLimitAuthBoundaryTests");
        StringAssert.Contains(governanceScript, "FullyQualifiedName~BlockC15SecurityAuditLogBoundaryTests");
        StringAssert.Contains(governanceScript, "FullyQualifiedName~BlockC16CiArtifactRetentionBoundaryTests");
    }

    [TestMethod]
    public void BlockC16_CiArtifactRetentionDoesNotTouchAuthorityOrRuntimeSurfaces()
    {
        foreach (var workflowPath in WorkflowPaths)
        {
            var workflow = ReadRepositoryFile(workflowPath);
            AssertDoesNotContain(workflow, "source-apply", workflowPath);
            AssertDoesNotContain(workflow, "git push", workflowPath);
            AssertDoesNotContain(workflow, "gh pr", workflowPath);
            AssertDoesNotContain(workflow, "release", workflowPath);
            AssertDoesNotContain(workflow, "deploy", workflowPath);
            AssertDoesNotContain(workflow, "promote-memory", workflowPath);
            AssertDoesNotContain(workflow, "workflow continuation", workflowPath);
        }
    }

    [TestMethod]
    public void BlockC16_ReceiptRecordsArtifactBoundary()
    {
        var receipt = ReadRepositoryFile("Docs", "receipts", "C16_CI_VALIDATION_ARTIFACT_RETENTION.md");

        StringAssert.Contains(receipt, "CI artifacts preserve validation evidence. They do not grant authority, approval, policy satisfaction, execution permission, merge readiness, release readiness, deployment readiness, or workflow continuation.");
        StringAssert.Contains(receipt, "A retained artifact is a receipt of what CI attempted or observed. It is not a decision to merge, release, deploy, apply source, or continue workflow.");
        StringAssert.Contains(receipt, "artifacts upload the repository root");
        StringAssert.Contains(receipt, "artifact upload runs only on success");
        StringAssert.Contains(receipt, "workflow permissions are widened beyond read-only");
    }

    private static TemporaryArtifactDirectory CreateArtifactDirectory()
    {
        var directory = Path.Combine(
            RepositoryRoot(),
            "artifacts",
            "ci",
            "c16-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new TemporaryArtifactDirectory(directory);
    }

    private static ProcessResult RunArtifactSafetyScan(string artifactDirectory)
    {
        var scriptPath = Path.Combine(RepositoryRoot(), "Scripts", "ci", "test-ci-evidence-artifact-safety.ps1");
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -ArtifactDirectory \"{artifactDirectory}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output + error);
    }

    private static string ReadRepositoryFile(params string[] pathParts) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));

    private static void AssertDoesNotContain(string source, string unexpected, string sourceName)
    {
        Assert.IsFalse(
            source.Contains(unexpected, StringComparison.OrdinalIgnoreCase),
            $"{sourceName} must not contain '{unexpected}'.");
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

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed record ProcessResult(int ExitCode, string Output);

    private sealed class TemporaryArtifactDirectory : IDisposable
    {
        public TemporaryArtifactDirectory(string path) => Path = path;

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
