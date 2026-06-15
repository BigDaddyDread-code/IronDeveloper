using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("HumanApprovedApply")]
public sealed class HumanApprovedApplyApiCliBoundaryTests
{
    [TestMethod]
    public void HumanApprovedApply_ApplyPreviewControllerExposesOnlyGetPreview()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "ApplyPreviewController.cs"));

        StringAssert.Contains(text, "IApplyPreviewService");
        StringAssert.Contains(text, "GetPreviewAsync");
        AssertDoesNotContainAny(
            text,
            "[HttpPost",
            "[HttpPut",
            "[HttpPatch",
            "[HttpDelete",
            "IApplyDryRunStore.CreateAsync",
            "ApplySource",
            "ApplyPatch",
            "SatisfyApproval",
            "SatisfyPolicy",
            "ContinueWorkflow",
            "PromoteMemory");
    }

    [TestMethod]
    public void HumanApprovedApply_ApplyPreviewCliExposesInspectionOnlyCommand()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "IronDev.Cli", "CliApplyPreview.cs"));

        StringAssert.Contains(text, "apply-preview");
        StringAssert.Contains(text, "GetApplyPreviewAsync");
        AssertDoesNotContainAny(
            text,
            "apply-source",
            "apply-patch",
            "execute-dry-run",
            "run-dry-run",
            "approve-apply",
            "satisfy-approval",
            "satisfy-policy",
            "continue-workflow",
            "promote-memory",
            "PostAsJson",
            "PutAsJson",
            "PatchAsync",
            "DeleteAsync");
    }

    [TestMethod]
    public void HumanApprovedApply_ClientSurfaceKeepsApplyPreviewReadOnly()
    {
        var root = RepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "IronDev.Client"), "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("ApplyPreview", StringComparison.Ordinal))
            .ToArray();

        Assert.IsTrue(files.Length > 0, "Expected client apply preview files.");
        var text = string.Join("\n", files.Select(File.ReadAllText));

        StringAssert.Contains(text, "GetApplyPreviewAsync");
        AssertDoesNotContainAny(
            text,
            "CreateApplyPreview",
            "PostApplyPreview",
            "ExecuteApplyPreview",
            "ApplySource",
            "ApplyPatch",
            "SatisfyApproval",
            "SatisfyPolicy",
            "ContinueWorkflow",
            "PromoteMemory");
    }

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected API/CLI boundary token: {marker}");
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

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
