using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

public sealed partial class IronDevCliTests
{
    [TestMethod]
    public async Task WorkspaceCopyOnlyApply_EndToEnd_SucceedsAndReportsSourceChanges()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-copy-apply-e2e");
        try
        {
            const string runId = "copy-apply-e2e";
            var sourceRepo = await CreateCopyOnlyApplyTinyDotnetRepositoryAsync(testRoot);
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            using var checkDoc = await RunWorkspaceCheckAsync(runId, sourceRepo, workspaceRoot, expectedExitCode: 0);
            Assert.AreEqual("succeeded", checkDoc.RootElement.GetProperty("status").GetString());

            using var prepareDoc = await RunWorkspacePrepareAsync(runId, sourceRepo, workspaceRoot, expectedExitCode: 0);
            var workspacePath = prepareDoc.RootElement.GetProperty("data").GetProperty("workspacePath").GetString()!;
            var workspaceProgramPath = Path.Combine(workspacePath, "Program.cs");
            var workspaceFeaturePath = Path.Combine(workspacePath, "Feature.cs");

            await File.WriteAllTextAsync(
                workspaceProgramPath,
                """
                Console.WriteLine("hello from governed copy-only apply");
                Console.WriteLine(Feature.Message);
                """);
            await File.WriteAllTextAsync(
                workspaceFeaturePath,
                """
                public static class Feature
                {
                    public static string Message => "added by workspace apply proof";
                }
                """);

            using var validateDoc = await RunWorkspaceValidateAsync(runId, workspacePath, "dotnet-build-test", expectedExitCode: 0);
            Assert.AreEqual("succeeded", validateDoc.RootElement.GetProperty("status").GetString());

            using var diffDoc = await RunWorkspaceDiffAsync(runId, workspacePath, expectedExitCode: 0);
            var diffData = diffDoc.RootElement.GetProperty("data");
            Assert.IsTrue(diffData.GetProperty("changed").GetBoolean());
            Assert.AreEqual(1, diffData.GetProperty("addedFiles").GetArrayLength());
            Assert.AreEqual(1, diffData.GetProperty("modifiedFiles").GetArrayLength());
            Assert.AreEqual(0, diffData.GetProperty("deletedFiles").GetArrayLength());

            using var promotionPackageDoc = await RunWorkspacePromotionPackageAsync(runId, workspacePath, expectedExitCode: 0);
            Assert.AreEqual("succeeded", promotionPackageDoc.RootElement.GetProperty("status").GetString());

            using var approvalDoc = await RunWorkspacePromotionApprovalAsync(
                runId,
                workspacePath,
                "approved",
                "IronDev E2E Proof",
                "Approved deterministic copy-only apply proof.",
                expectedExitCode: 0);
            Assert.AreEqual("succeeded", approvalDoc.RootElement.GetProperty("status").GetString());

            using var preflightDoc = await RunWorkspaceApplyPreflightAsync(runId, workspacePath, expectedExitCode: 0);
            Assert.AreEqual("succeeded", preflightDoc.RootElement.GetProperty("status").GetString());

            using var dryRunDoc = await RunWorkspaceApplyDryRunForProofAsync(runId, workspacePath, expectedExitCode: 0);
            Assert.AreEqual("succeeded", dryRunDoc.RootElement.GetProperty("status").GetString());
            Assert.AreEqual(1, dryRunDoc.RootElement.GetProperty("data").GetProperty("addCount").GetInt32());
            Assert.AreEqual(1, dryRunDoc.RootElement.GetProperty("data").GetProperty("modifyCount").GetInt32());
            Assert.AreEqual(0, dryRunDoc.RootElement.GetProperty("data").GetProperty("deleteCount").GetInt32());

            using var applyCopyDoc = await RunWorkspaceApplyCopyAsync(runId, workspacePath, expectedExitCode: 0);
            Assert.AreEqual("succeeded", applyCopyDoc.RootElement.GetProperty("status").GetString());

            using var applyVerifyDoc = await RunWorkspaceApplyVerifyAsync(runId, workspacePath, expectedExitCode: 0);
            Assert.AreEqual("succeeded", applyVerifyDoc.RootElement.GetProperty("status").GetString());

            using var postApplyValidateDoc = await RunWorkspacePostApplyValidateAsync(runId, workspacePath, "dotnet-build-test", expectedExitCode: 0);
            Assert.AreEqual("succeeded", postApplyValidateDoc.RootElement.GetProperty("status").GetString());

            using var sourceReportDoc = await RunWorkspaceSourceReportAsync(runId, workspacePath, expectedExitCode: 0);
            var sourceReport = sourceReportDoc.RootElement.GetProperty("data");

            Assert.AreEqual("succeeded", sourceReportDoc.RootElement.GetProperty("status").GetString());
            Assert.IsTrue(sourceReport.GetProperty("sourceRepoMutated").GetBoolean());
            Assert.IsTrue(sourceReport.GetProperty("applyVerified").GetBoolean());
            Assert.IsTrue(sourceReport.GetProperty("sourceMatchesWorkspace").GetBoolean());
            Assert.IsTrue(sourceReport.GetProperty("postApplyValidationSucceeded").GetBoolean());
            Assert.AreEqual("ready_for_human_review_or_commit", sourceReport.GetProperty("recommendation").GetString());
            Assert.AreEqual(1, sourceReport.GetProperty("addCount").GetInt32());
            Assert.AreEqual(1, sourceReport.GetProperty("modifyCount").GetInt32());
            Assert.AreEqual(0, sourceReport.GetProperty("deleteCount").GetInt32());

            var sourceFeaturePath = Path.Combine(sourceRepo, "Feature.cs");
            var sourceProgramPath = Path.Combine(sourceRepo, "Program.cs");
            Assert.IsTrue(File.Exists(sourceFeaturePath));
            Assert.AreEqual(await File.ReadAllTextAsync(workspaceFeaturePath), await File.ReadAllTextAsync(sourceFeaturePath));
            Assert.AreEqual(await File.ReadAllTextAsync(workspaceProgramPath), await File.ReadAllTextAsync(sourceProgramPath));
            Assert.IsFalse(Directory.Exists(Path.Combine(sourceRepo, ".irondev")));

            var runDirectory = Path.Combine(workspacePath, ".irondev", "runs", runId);
            AssertExpectedEvidenceExists(
                runDirectory,
                [
                    "diff.json",
                    "promotion-package.json",
                    "promotion-approval.json",
                    "apply-preflight.json",
                    "apply-dry-run.json",
                    "apply-copy.json",
                    "apply-verify.json",
                    "post-apply-validation.json",
                    "source-report.json"
                ]);
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    private static async Task<string> CreateCopyOnlyApplyTinyDotnetRepositoryAsync(string testRoot)
    {
        var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
        await File.WriteAllTextAsync(
            Path.Combine(sourceRepo, ".gitignore"),
            """
            bin/
            obj/
            """);
        await File.WriteAllTextAsync(
            Path.Combine(sourceRepo, "Directory.Build.props"),
            """
            <Project>
              <PropertyGroup>
                <MSBuildProjectExtensionsPath>.assets/</MSBuildProjectExtensionsPath>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(sourceRepo, "TinyApp.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(sourceRepo, "Program.cs"),
            """
            Console.WriteLine("hello from source");
            """);

        await RunDotnetForTestAsync(sourceRepo, "restore", "--nologo");
        await RunGitForTestAsync(sourceRepo, "config", "user.email", "irondev-tests@example.local");
        await RunGitForTestAsync(sourceRepo, "config", "user.name", "IronDev Tests");
        await RunGitForTestAsync(sourceRepo, "add", ".");
        await RunGitForTestAsync(sourceRepo, "commit", "-m", "initial", "-q");
        return sourceRepo;
    }

    private static void AssertExpectedEvidenceExists(string runDirectory, IReadOnlyList<string> fileNames)
    {
        foreach (var fileName in fileNames)
            Assert.IsTrue(File.Exists(Path.Combine(runDirectory, fileName)), $"Expected evidence file '{fileName}' to exist.");
    }

    private static async Task<JsonDocument> RunWorkspaceApplyDryRunForProofAsync(
        string runId,
        string workspacePath,
        int expectedExitCode)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var result = await IronDev.Cli.IronDevCli.RunAsync(
            [
                "workspace", "apply-dry-run",
                "--run-id", runId,
                "--workspace-path", workspacePath,
                "--json"
            ],
            output,
            error,
            handler: null,
            CancellationToken.None);

        Assert.AreEqual(expectedExitCode, result, $"stderr: {error}\nstdout: {output}");
        AssertJsonWasWritten(output);
        return JsonDocument.Parse(output.ToString());
    }
}
