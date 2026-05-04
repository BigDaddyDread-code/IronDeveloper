using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using IronDev.Core.Builder;
using IronDev.Infrastructure.Builder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// Unit tests for CodePatchService.DryRunValidateAsync.
/// No real project or database required — tests use temp directories.
/// </summary>
[TestClass]
public class CodePatchServiceValidationTests
{
    private string _root = "";
    private CodePatchService _sut = default!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"IronDevPatchTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _sut = new CodePatchService();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string WriteFile(string relativePath, string content)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return relativePath;
    }

    private FileChangeProposal ValidProposal(string relativePath, string before, string after) =>
        new() { FilePath = relativePath, BeforeSnippet = before, AfterSnippet = after, ChangeReason = "test" };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Validate_ValidSingleFileProposal_ShouldPass()
    {
        var path = WriteFile("Foo.cs", "public class Foo { void Bar() { } }");
        var changes = new List<FileChangeProposal>
        {
            ValidProposal(path, "void Bar() { }", "void Bar() { return; }")
        };

        var result = await _sut.DryRunValidateAsync(_root, changes);

        Assert.IsTrue(result.AllValid, result.Summary);
        Assert.AreEqual(1, result.FileResults.Count);
        Assert.IsTrue(result.FileResults[0].IsValid);
    }

    [TestMethod]
    public async Task Validate_NoFileChanges_ShouldFail()
    {
        var result = await _sut.DryRunValidateAsync(_root, new List<FileChangeProposal>());

        Assert.IsFalse(result.AllValid);
        Assert.IsTrue(result.Summary.Contains("no file changes", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Validate_NullFileChanges_ShouldFail()
    {
        var result = await _sut.DryRunValidateAsync(_root, null!);

        Assert.IsFalse(result.AllValid);
        Assert.IsTrue(result.Summary.Contains("no file changes", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Validate_EmptyFilePath_ShouldFail()
    {
        var changes = new List<FileChangeProposal>
        {
            new() { FilePath = "", BeforeSnippet = "x", AfterSnippet = "y" }
        };

        var result = await _sut.DryRunValidateAsync(_root, changes);

        Assert.IsFalse(result.AllValid);
        Assert.IsTrue(result.FileResults[0].Message.Contains("FilePath is empty",
            StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Validate_PathOutsideProjectRoot_ShouldFail()
    {
        var changes = new List<FileChangeProposal>
        {
            new() { FilePath = "../../etc/passwd", BeforeSnippet = "x", AfterSnippet = "y" }
        };

        var result = await _sut.DryRunValidateAsync(_root, changes);

        Assert.IsFalse(result.AllValid);
        Assert.IsTrue(result.FileResults[0].Message.Contains("outside the project root",
            StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Validate_MissingFile_ShouldFail()
    {
        var changes = new List<FileChangeProposal>
        {
            ValidProposal("DoesNotExist.cs", "void Foo() { }", "void Foo() { return; }")
        };

        var result = await _sut.DryRunValidateAsync(_root, changes);

        Assert.IsFalse(result.AllValid);
        Assert.IsTrue(result.FileResults[0].Message.Contains("not found",
            StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Validate_EmptyBeforeSnippet_ShouldFail()
    {
        var path = WriteFile("Bar.cs", "public class Bar { }");
        var changes = new List<FileChangeProposal>
        {
            new() { FilePath = path, BeforeSnippet = "", AfterSnippet = "something" }
        };

        var result = await _sut.DryRunValidateAsync(_root, changes);

        Assert.IsFalse(result.AllValid);
        Assert.IsTrue(result.FileResults[0].Message.Contains("BeforeSnippet is empty",
            StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Validate_BeforeSnippetNotFound_ShouldFail()
    {
        var path = WriteFile("Baz.cs", "public class Baz { }");
        var changes = new List<FileChangeProposal>
        {
            ValidProposal(path, "THIS_DOES_NOT_EXIST", "replacement")
        };

        var result = await _sut.DryRunValidateAsync(_root, changes);

        Assert.IsFalse(result.AllValid);
        Assert.IsTrue(result.FileResults[0].Message.Contains("not found in the file",
            StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Validate_DuplicateBeforeSnippet_ShouldFail()
    {
        var path = WriteFile("Dup.cs",
            "void Foo() { } void Foo() { }");   // same snippet twice
        var changes = new List<FileChangeProposal>
        {
            ValidProposal(path, "void Foo() { }", "void Foo() { return; }")
        };

        var result = await _sut.DryRunValidateAsync(_root, changes);

        Assert.IsFalse(result.AllValid);
        Assert.IsTrue(result.FileResults[0].Message.Contains("appears",
            StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(result.FileResults[0].Message.Contains("exactly once",
            StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Validate_EmptyAfterSnippet_ShouldFail()
    {
        var path = WriteFile("Qux.cs", "public class Qux { void Run() { } }");
        var changes = new List<FileChangeProposal>
        {
            new() { FilePath = path, BeforeSnippet = "void Run() { }", AfterSnippet = "" }
        };

        var result = await _sut.DryRunValidateAsync(_root, changes);

        Assert.IsFalse(result.AllValid);
        Assert.IsTrue(result.FileResults[0].Message.Contains("AfterSnippet is empty",
            StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Validate_MultipleFiles_PartialFailure_ReportsAll()
    {
        var goodPath = WriteFile("Good.cs", "class Good { void A() { } }");
        var changes = new List<FileChangeProposal>
        {
            ValidProposal(goodPath, "void A() { }", "void A() { return; }"),
            new() { FilePath = "Missing.cs", BeforeSnippet = "x", AfterSnippet = "y" }
        };

        var result = await _sut.DryRunValidateAsync(_root, changes);

        Assert.IsFalse(result.AllValid);
        Assert.AreEqual(2, result.FileResults.Count);
        Assert.IsTrue(result.FileResults[0].IsValid,  "Good.cs should pass");
        Assert.IsFalse(result.FileResults[1].IsValid, "Missing.cs should fail");
    }
}
