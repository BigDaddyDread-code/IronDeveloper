using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("BlockPThinUiReceipt")]
public sealed class BlockPThinUiReceiptTests
{
    [TestMethod]
    public void BlockPThinUiReceipt_Exists()
    {
        Assert.IsTrue(File.Exists(ReceiptPath()), $"Missing receipt at {ReceiptPath()}.");
    }

    [TestMethod]
    public void BlockPThinUiReceipt_StatesThinUiBoundary()
    {
        var receipt = ReceiptText();

        StringAssert.Contains(receipt, "PR160 records the thin UI observability layer and the Block P backend authority entry point.");
        StringAssert.Contains(receipt, "This PR is receipt/test only.");
        StringAssert.Contains(receipt, "The UI is glass, not controls.");
        StringAssert.Contains(receipt, "UI visibility is not backend authority.");
        StringAssert.Contains(receipt, "UI route is not capability.");
        StringAssert.Contains(receipt, "UI view model is not authority.");
        StringAssert.Contains(receipt, "UI refresh is not retry.");
        StringAssert.Contains(receipt, "UI navigation is not workflow continuation.");
        StringAssert.Contains(receipt, "UI copy is not approval.");
        StringAssert.Contains(receipt, "UI status chip is not gate state ownership.");
        StringAssert.Contains(receipt, "UI evidence is not approval.");
        StringAssert.Contains(receipt, "UI review is not decision.");
    }

    [TestMethod]
    public void BlockPThinUiReceipt_StatesBackendAuthorityMustBeBackendOwned()
    {
        var receipt = ReceiptText();

        StringAssert.Contains(receipt, "Backend authority must be backend-owned.");
        StringAssert.Contains(receipt, "Block P must not be implemented in the UI.");
        StringAssert.Contains(receipt, "Accepted approval must be a backend record.");
        StringAssert.Contains(receipt, "Policy satisfaction must be a backend record.");
        StringAssert.Contains(receipt, "Source apply must be backend controlled.");
        StringAssert.Contains(receipt, "Release readiness must be backend decided.");
    }

    [TestMethod]
    public void BlockPThinUiReceipt_StatesNextBackendChain()
    {
        StringAssert.Contains(
            ReceiptText(),
            "The next backend chain is accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate.");
    }

    [TestMethod]
    public void BlockPThinUiReceipt_DoesNotClaimBlockPImplemented()
    {
        var receipt = ReceiptText();

        StringAssert.Contains(
            receipt,
            "PR160 does not implement accepted approval records, policy satisfaction records, dry-run execution, patch artifacts, source apply, rollback, workflow continuation, or release readiness.");
        StringAssert.Contains(receipt, "PR160 does not add backend APIs, CLI commands, SQL migrations, stores, runners, executors, schedulers");
        StringAssert.Contains(receipt, "PR160 does not change the existing UI route behavior.");
    }

    [TestMethod]
    public void BlockPThinUiReceipt_IsTestsAndReceiptOnly()
    {
        var root = RepositoryRoot();
        var forbiddenRoots = new[]
        {
            Path.Combine(root, "IronDev.TauriShell", "src"),
            Path.Combine(root, "IronDev.Api"),
            Path.Combine(root, "IronDev.Core"),
            Path.Combine(root, "IronDev.Infrastructure"),
            Path.Combine(root, "IronDev.Cli"),
            Path.Combine(root, "Database")
        };

        var unexpectedFiles = forbiddenRoots
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => FileNameOrTextContainsPr160Marker(path))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.AreEqual(
            0,
            unexpectedFiles.Length,
            "PR160 markers must stay out of production UI/backend/CLI/database files: " + string.Join(", ", unexpectedFiles));
    }

    [TestMethod]
    public void BlockPThinUiReceipt_ReviewLineIsPresent()
    {
        StringAssert.Contains(ReceiptText(), "PR160 closes the cockpit pass. The engine work resumes in the backend.");
    }

    private static bool FileNameOrTextContainsPr160Marker(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Contains("PR160", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("BlockPThinUi", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var extension = Path.GetExtension(path);
        if (!IsTextExtension(extension))
        {
            return false;
        }

        var text = File.ReadAllText(path);
        return text.Contains("PR160", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("BlockPThinUi", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Block P Thin UI", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextExtension(string extension) =>
        extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".sql", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase);

    private static string ReceiptText() => File.ReadAllText(ReceiptPath());

    private static string ReceiptPath() =>
        Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR160_BLOCK_P_THIN_UI_RECEIPT.md");

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing IronDev.slnx.");
    }
}
