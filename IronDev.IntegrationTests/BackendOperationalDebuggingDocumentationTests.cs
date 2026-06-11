using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BackendOperationalDebuggingDocumentationTests
{
    private const string GuidePath = "Docs/L4_L5_OPERATIONAL_DEBUGGING.md";

    [TestMethod]
    public void OperationalDebuggingGuide_DocumentsRequiredSectionsAndInvariants()
    {
        var guide = ReadRepositoryFileByRelativePath(GuidePath);

        foreach (var expected in new[]
        {
            "PR 54 is operational debugging documentation, not runtime change.",
            "Documentation-only; no behavior change intended.",
            "No SQL/API/CLI/UI/runtime/persistence/capability changes.",
            "## Purpose",
            "## L4/L5 scope",
            "## Boundary rules",
            "## Common Failure Shapes",
            "## Diagnostic checklist",
            "## Evidence Map",
            "## Log/trace/run report map",
            "## Focused Test Filter Map",
            "## Known Red Lanes / Not This Guide",
            "## What not to infer",
            "## Escalation/split guidance",
            "SQL is source of truth.",
            "Weaviate/vector/index is retrieval only, never truth/authority/approval/promotion.",
            "Proposal is not apply.",
            "Candidate is not memory.",
            "Retrieval match is not memory candidate.",
            "Audit is not approval.",
            "Gate is not executor.",
            "Critic is not governance.",
            "Memory safe is not approval.",
            "Tool request is request form, not execution permission.",
            "Model output is advisory only.",
            "Human review remains required for source apply and memory promotion."
        })
        {
            StringAssert.Contains(guide, expected);
        }
    }

    [TestMethod]
    public void OperationalDebuggingGuide_DocumentsCommonFailureShapes()
    {
        var guide = ReadRepositoryFileByRelativePath(GuidePath);

        foreach (var expected in new[]
        {
            "### Tool Request Failure",
            "### Audit Store Failure",
            "### Proposal Loop Failure",
            "### Repair/Fix Proposal Failure",
            "### Memory Improvement Detection Failure",
            "### Dogfood Harness Failure",
            "### Gate/Critic Failure",
            "### L5 Source Apply / Memory Promotion Failure",
            "Tool request is not execution permission.",
            "Audit is evidence locker, not robot arm.",
            "Repair proposal is not repair execution.",
            "Dogfood receipt is evidence, not the dog.",
            "Human review remains required for source apply and memory promotion."
        })
        {
            StringAssert.Contains(guide, expected);
        }
    }

    [TestMethod]
    public void OperationalDebuggingGuide_DocumentsEvidenceAndDiagnosticMaps()
    {
        var guide = ReadRepositoryFileByRelativePath(GuidePath);

        foreach (var expected in new[]
        {
            "| Tool request | request record, approval evidence if execution is allowed, gate decision, audit record | request alone means permission |",
            "| Proposal review | proposal record, review/critic output, audit/report evidence | proposal means source changed |",
            "| Source apply | scoped human approval evidence, apply report, source diff/result, verification evidence | critic approval is enough |",
            "| Memory promotion | accepted promotion evidence, SQL persisted memory record, promotion audit evidence | safe classification is approval |",
            "| Retrieval | retrieval match/result, query scope, source reference | match is memory candidate |",
            "| Dogfood | receipt/report/logs, source report or failure package where applicable | receipt means release-ready |",
            "1. Identify the flow.",
            "2. Identify the boundary involved.",
            "3. Locate SQL authoritative records.",
            "4. Locate audit/evidence records.",
            "5. Locate run report/traces/logs.",
            "6. Confirm whether human approval was required.",
            "7. Confirm whether human approval was present.",
            "8. Confirm whether source or memory was mutated.",
            "9. Confirm whether mutation was allowed.",
            "10. Confirm no advisory output was treated as authority.",
            "11. Run the focused test filter.",
            "12. Decide whether this is a bug, missing evidence, stale fixture, or invalid expectation."
        })
        {
            StringAssert.Contains(guide, expected);
        }
    }

    [TestMethod]
    public void OperationalDebuggingGuide_DocumentsFocusedTestFiltersAndKnownRedLanes()
    {
        var guide = ReadRepositoryFileByRelativePath(GuidePath);

        foreach (var expected in new[]
        {
            "`ToolExecutionAuditStore`",
            "`ManualTesterAgentToolExecution`",
            "`ManualImplementationPatchProposal`",
            "`MemoryImprovementProposal`",
            "`BackendSqlCleanup`",
            "`InlineSql`",
            "`BackendNamingNormalisation`",
            "`BackendFixtureConsolidation`",
            "`BackendEntityTableInventory`",
            "`BackendArchitecture`",
            "`BackendAdr`",
            "`OperationalDebugging`",
            "Stored manual agent DI construction issue in API lane",
            "StoredManualIndependentCriticAgentService",
            "StoredManualMemoryImprovementAgentService",
            "Broad governance/memory/architecture lanes",
            "Legacy runtime DDL/bootstrap ownership exceptions from PR 51",
            "SQL/entity artifacts marked uncertain in inventories",
            "Intentionally ugly names left from PR 51.5",
            "Full solution broad lanes still failing"
        })
        {
            StringAssert.Contains(guide, expected);
        }
    }

    [TestMethod]
    public void OperationalDebuggingGuide_ReferencesArchitectureAndAdrDocs()
    {
        var guide = ReadRepositoryFileByRelativePath(GuidePath);

        foreach (var expected in new[]
        {
            "Docs/BACKEND_ARCHITECTURE.md",
            "Docs/ADR/README.md",
            "Docs/ADR/ADR-001-SQL-source-of-truth.md",
            "Docs/ADR/ADR-002-retrieval-match-not-memory-candidate.md",
            "Docs/ADR/ADR-003-memory-candidate-proposal-promotion-boundary.md",
            "Docs/ADR/ADR-004-proposal-review-apply-boundary.md",
            "Docs/ADR/ADR-005-tool-request-audit-execution-boundary.md",
            "Docs/ADR/ADR-006-critic-gate-governance-boundary.md",
            "Docs/ADR/ADR-007-human-review-required-for-apply-and-promotion.md"
        })
        {
            StringAssert.Contains(guide, expected);
        }
    }

    [TestMethod]
    public void OperationalDebuggingGuide_DoesNotDescribeAdvisorySignalsAsAuthority()
    {
        var guide = ReadRepositoryFileByRelativePath(GuidePath);

        foreach (var expected in new[]
        {
            "Do not infer approval from audit records.",
            "Do not infer authority from model output.",
            "Do not infer memory from retrieval matches.",
            "Do not infer source mutation from proposals.",
            "Do not infer governance from critic text.",
            "Do not infer execution from a gate decision.",
            "Do not infer promotion from memory safety results."
        })
        {
            StringAssert.Contains(guide, expected);
        }
    }

    [TestMethod]
    public void OperationalDebuggingGuide_DoesNotClaimRuntimeSchemaOrCapabilityChanges()
    {
        var guide = ReadRepositoryFileByRelativePath(GuidePath);
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
            "NOCHECK CONSTRAINT",
            "DISABLE TRIGGER",
            "CREATE OR ALTER PROCEDURE",
            "ALTER TABLE",
            "DROP TABLE",
            "PromoteCollectiveMemory",
            "SubmitReview"
        };

        foreach (var token in forbidden)
        {
            Assert.IsFalse(
                guide.Contains(token, StringComparison.OrdinalIgnoreCase),
                $"Operational debugging documentation must not introduce runtime/schema/capability token: {token}");
        }
    }

    [TestMethod]
    public void OperationalDebuggingGuide_DoesNotContainHiddenOrBidirectionalUnicode()
    {
        var absolutePath = Path.Combine(RepositoryRoot(), GuidePath);

        AssertAsciiBytesAndNoBom(GuidePath, File.ReadAllBytes(absolutePath));
        AssertAsciiAndNoFormatControls(GuidePath, File.ReadAllText(absolutePath));
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
