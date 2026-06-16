using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("SourceApplyThreatBoundary")]
public sealed class SourceApplyThreatBoundaryReceiptTests
{
    [TestMethod]
    public void SourceApplyThreatBoundary_ReceiptExists()
    {
        Assert.IsTrue(File.Exists(ReceiptPath()), ReceiptPath());
    }

    [TestMethod]
    public void SourceApplyThreatBoundary_ReceiptStatesDocsOnly()
    {
        var receipt = ReceiptText();

        foreach (var statement in new[]
        {
            "docs/receipt/test only",
            "does not add source apply",
            "does not mutate source",
            "does not execute rollback",
            "does not continue workflow",
            "does not approve release",
            "does not add API",
            "does not add CLI",
            "does not add UI"
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    [TestMethod]
    public void SourceApplyThreatBoundary_NamesAllFootguns()
    {
        var receipt = ReceiptText();

        foreach (var footgun in new[]
        {
            "wrong branch",
            "dirty worktree",
            "stale base",
            "missing rollback",
            "partial apply",
            "silent mutation",
            "validation bypass",
            "approval/policy drift"
        })
        {
            StringAssert.Contains(receipt, footgun);
        }
    }

    [TestMethod]
    public void SourceApplyThreatBoundary_StatesPatchArtifactIsNotMutation()
    {
        var receipt = ReceiptText();

        foreach (var statement in new[]
        {
            "Source apply is mutation.",
            "Patch artifact is not mutation.",
            "Patch artifact read is not mutation.",
            "Patch artifact creation is not mutation.",
            "Patch base/hash validation is not mutation."
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    [TestMethod]
    public void SourceApplyThreatBoundary_StatesFutureSourceApplyRequirements()
    {
        var receipt = ReceiptText();

        foreach (var requirement in new[]
        {
            "clean source state",
            "baseline match",
            "rollback plan",
            "validation proof",
            "durable apply evidence"
        })
        {
            StringAssert.Contains(receipt, requirement);
        }

        StringAssert.Contains(receipt, "Source apply must require explicit authority, clean source state, baseline match, rollback plan, validation proof, and durable apply evidence.");
    }

    [TestMethod]
    public void SourceApplyThreatBoundary_IncludesFullAuthorityChain()
    {
        StringAssert.Contains(
            ReceiptText(),
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate");
    }

    [TestMethod]
    public void SourceApplyThreatBoundary_DoesNotAddProductionCode()
    {
        CollectionAssert.AreEquivalent(new[]
        {
            "Docs/receipts/PR193_SOURCE_APPLY_THREAT_MODEL_AND_BOUNDARY.md",
            "IronDev.IntegrationTests/Governance/SourceApplyThreatBoundaryReceiptTests.cs"
        }, Pr193ChangedFiles());
    }

    [TestMethod]
    public void SourceApplyThreatBoundary_DoesNotAddSourceApplyImplementation()
    {
        AssertChangedFilesDoNotContain(ForbiddenImplementationTokens());
    }

    [TestMethod]
    public void SourceApplyThreatBoundary_DoesNotAddApiCliUiRuntime()
    {
        foreach (var file in Pr193ChangedFiles())
        foreach (var token in new[] { "Controller", "Program.cs", "Cli", "Tauri", "UI" })
        {
            Assert.IsFalse(file.Contains(token, StringComparison.OrdinalIgnoreCase), $"PR193 must not add {token}: {file}");
        }

        AssertChangedFilesDoNotContain([
            "I" + "HostedService",
            "Background" + "Service",
            "Sched" + "uler"
        ]);
    }

    [TestMethod]
    public void SourceApplyThreatBoundary_DoesNotCallModelsAgentsMemoryRetrieval()
    {
        AssertChangedFilesDoNotContain([
            "L" + "LM",
            "model" + " call",
            "Agent" + "Dispatch",
            "Tool" + "Execution",
            "Promote" + "Memory",
            "Activate" + "Retrieval",
            "Vec" + "tor",
            "Embed" + "ding",
            "Wea" + "viate"
        ]);
    }

    [TestMethod]
    public void SourceApplyThreatBoundary_ReviewLineNamesButDoesNotFightDragon()
    {
        StringAssert.Contains(ReceiptText(), "PR193 names the dragon. It does not fight it.");
    }

    private static string[] ForbiddenImplementationTokens() =>
    [
        "Apply" + "SourceAsync",
        "Source" + "ApplyService",
        "Controlled" + "SourceApply",
        "Source" + "ApplyExecutor",
        "Rollback" + "Executor",
        "Continue" + "WorkflowAsync",
        "Approve" + "ReleaseAsync",
        "Release" + "Ready = true",
        "Can" + "ApplySource = true"
    ];

    private static void AssertChangedFilesDoNotContain(IReadOnlyList<string> tokens)
    {
        foreach (var file in Pr193ChangedFiles())
        {
            var text = ReadRepoText(file);
            foreach (var token in tokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Unexpected token {token} in {file}.");
            }
        }
    }

    private static string[] Pr193ChangedFiles() =>
    [
        "Docs/receipts/PR193_SOURCE_APPLY_THREAT_MODEL_AND_BOUNDARY.md",
        "IronDev.IntegrationTests/Governance/SourceApplyThreatBoundaryReceiptTests.cs"
    ];

    private static string ReceiptText() => File.ReadAllText(ReceiptPath());

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR193_SOURCE_APPLY_THREAT_MODEL_AND_BOUNDARY.md");

    private static string ReadRepoText(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
