using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("OverallMemorySystemDiscussion")]
public sealed class OverallMemorySystemDiscussionDocumentationTests
{
    private const string DiscussionPath = "Docs/architecture/OVERALL_MEMORY_SYSTEM_DISCUSSION.md";

    [TestMethod]
    public void OverallMemorySystemDiscussion_DocumentExistsAndStatesFutureParkingLotStatus()
    {
        var discussion = ReadRepositoryFileByRelativePath(DiscussionPath);

        foreach (var expected in new[]
        {
            "# Overall Memory System Discussion",
            "## Status",
            "Future discussion / parking-lot architecture note.",
            "Not part of the current active implementation blocks.",
            "Documentation-only. No production code, database schema, API, CLI, runtime behaviour, agent behaviour, memory write path, background job, LangGraph orchestration, Weaviate change, Hopfield implementation, tool permission, or ConscienceAgent behaviour is introduced by this document."
        })
        {
            StringAssert.Contains(discussion, expected);
        }
    }

    [TestMethod]
    public void OverallMemorySystemDiscussion_DocumentsProblemAndCoreIdea()
    {
        var discussion = ReadRepositoryFileByRelativePath(DiscussionPath);

        foreach (var expected in new[]
        {
            "IronDev needs durable project-level learning without turning agent memory into hidden authority.",
            "Agents can learn independently, but the project learns globally.",
            "Agents can learn privately.",
            "The system can learn globally.",
            "Only governed evidence becomes project truth.",
            "Project-level learning should happen only through governed promotion."
        })
        {
            StringAssert.Contains(discussion, expected);
        }
    }

    [TestMethod]
    public void OverallMemorySystemDiscussion_ListsRequiredMemoryLayers()
    {
        var discussion = ReadRepositoryFileByRelativePath(DiscussionPath);

        foreach (var expected in new[]
        {
            "## Memory Layers",
            "### Project Canon",
            "### Operational Memory",
            "### Failure Mode Registry",
            "### Agent Memory",
            "### Session Memory",
            "### Raw Evidence Store"
        })
        {
            StringAssert.Contains(discussion, expected);
        }
    }

    [TestMethod]
    public void OverallMemorySystemDiscussion_DefinesConscienceAgentAsGovernorNotMemory()
    {
        var discussion = ReadRepositoryFileByRelativePath(DiscussionPath);

        foreach (var expected in new[]
        {
            "## ConscienceAgent Role",
            "The ConscienceAgent is not the memory.",
            "It is the memory integrity governor.",
            "ConscienceAgent should not be a hidden memory writer, hidden policy engine, approval shortcut, source mutation path, memory promotion bypass, or runtime executor."
        })
        {
            StringAssert.Contains(discussion, expected);
        }
    }

    [TestMethod]
    public void OverallMemorySystemDiscussion_DocumentsEvidenceProposalGovernanceChain()
    {
        var discussion = ReadRepositoryFileByRelativePath(DiscussionPath);

        foreach (var expected in new[]
        {
            "Evidence -> Proposal -> Governance -> Approved Memory -> Future Behaviour.",
            "Anything that skips this chain is not trusted project learning.",
            "Only governed, evidence-backed memory can become trusted project truth.",
            "Raw evidence can inform.",
            "Agent memory can propose.",
            "Retrieval matches can surface.",
            "Model output can draft."
        })
        {
            StringAssert.Contains(discussion, expected);
        }
    }

    [TestMethod]
    public void OverallMemorySystemDiscussion_DocumentsAssociativeMemoryAsSuggestionOnly()
    {
        var discussion = ReadRepositoryFileByRelativePath(DiscussionPath);

        foreach (var expected in new[]
        {
            "Future Hopfield-style or graph-style overlap may be useful for pattern recall.",
            "Associative memory may suggest.",
            "It must not decide.",
            "similarity is retrieval support",
            "retrieval support is not authority",
            "authority comes only from governed, evidence-backed memory"
        })
        {
            StringAssert.Contains(discussion, expected);
        }
    }

    [TestMethod]
    public void OverallMemorySystemDiscussion_ListsNonGoalsPreventingRuntimeAndProductionChanges()
    {
        var discussion = ReadRepositoryFileByRelativePath(DiscussionPath);

        foreach (var expected in new[]
        {
            "## Non-Goals",
            "This document does not implement memory compilation.",
            "This document does not implement memory proposal storage.",
            "This document does not implement Hopfield recall.",
            "This document does not implement agent memory mutation.",
            "This document does not implement Project Canon writes.",
            "This document does not add new production code.",
            "This document does not add new database migrations.",
            "This document does not add new APIs.",
            "This document does not add new agent runtime behaviour.",
            "This document does not add new memory write paths.",
            "This document does not add new background jobs.",
            "This document does not add new LangGraph orchestration.",
            "This document does not add new Weaviate changes.",
            "This document does not add a Hopfield implementation.",
            "This document does not add new tool permissions.",
            "This document does not add new ConscienceAgent behaviour.",
            "This document does not change the current governed memory, tool-request, or event-store spine."
        })
        {
            StringAssert.Contains(discussion, expected);
        }
    }

    [TestMethod]
    public void OverallMemorySystemDiscussion_ListsFutureWorkWithoutClaimingImplementation()
    {
        var discussion = ReadRepositoryFileByRelativePath(DiscussionPath);

        foreach (var expected in new[]
        {
            "## Future Work",
            "Possible future PRs may introduce:",
            "MemoryProposal model",
            "MemoryProposalStore",
            "MemoryCompiler",
            "ConscienceAgent memory validation path",
            "memory authority levels",
            "stale/superseded memory checks",
            "evidence-linked promotion workflow",
            "associative-memory retrieval contract",
            "Hopfield-style pattern recall experiment"
        })
        {
            StringAssert.Contains(discussion, expected);
        }
    }

    [TestMethod]
    public void OverallMemorySystemDiscussion_DoesNotContainHiddenOrBidirectionalUnicode()
    {
        var absolutePath = Path.Combine(RepositoryRoot(), DiscussionPath);

        AssertAsciiBytesAndNoBom(DiscussionPath, File.ReadAllBytes(absolutePath));
        AssertAsciiAndNoFormatControls(DiscussionPath, File.ReadAllText(absolutePath));
    }

    [TestMethod]
    public void OverallMemorySystemDiscussion_DoesNotIntroduceRuntimeSchemaOrCapabilityTokens()
    {
        var discussion = ReadRepositoryFileByRelativePath(DiscussionPath);
        var forbidden = new[]
        {
            "HttpPost",
            "ControllerBase",
            "WebApplication",
            "AddScoped<",
            "IHostedService",
            "BackgroundService",
            "ProcessStartInfo",
            "File.WriteAllText",
            "CREATE OR ALTER PROCEDURE",
            "ALTER TABLE",
            "DROP TABLE",
            "DbContext",
            "SqlConnection",
            "WeaviateClient",
            "LangGraphRuntime",
            "HopfieldMemoryStore",
            "PromoteCollectiveMemory"
        };

        foreach (var token in forbidden)
        {
            Assert.IsFalse(
                discussion.Contains(token, StringComparison.OrdinalIgnoreCase),
                $"Overall memory discussion must not introduce runtime/schema/capability token: {token}");
        }
    }

    private static string ReadRepositoryFileByRelativePath(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static void AssertAsciiAndNoFormatControls(string path, string source)
    {
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(current);
            if (current > 127 || category == System.Globalization.UnicodeCategory.Format)
                Assert.Fail($"{path} contains hidden or non-ASCII Unicode at index {index}: U+{(int)current:X4}.");
        }
    }

    private static void AssertAsciiBytesAndNoBom(string path, byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            Assert.Fail($"{path} must not contain a UTF-8 byte-order mark.");

        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] > 127)
                Assert.Fail($"{path} contains non-ASCII byte at offset {index}: 0x{bytes[index]:X2}.");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new InvalidOperationException("Could not locate repository root.");

        return directory.FullName;
    }
}
