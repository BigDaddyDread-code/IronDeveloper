using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("L4BackendReadinessReport")]
public sealed class L4BackendReadinessReportTests
{
    private static readonly string[] RequiredAreaCodes =
    [
        "L4_CAPABILITY_MATRIX",
        "L4_INVARIANT_GUARDS",
        "L4_FAILURE_MODE_REPORT",
        "DOGFOOD_CAMPAIGN_EVIDENCE",
        "GOVERNANCE_TRACEABILITY",
        "WORKFLOW_READ_VISIBILITY",
        "TOOL_GATE_VISIBILITY",
        "APPROVAL_PACKAGE_VISIBILITY",
        "MEMORY_PROPOSAL_VISIBILITY",
        "UI_AUTHORITY_FIREWALL",
        "ACCEPTED_APPROVAL_RECORD",
        "POLICY_SATISFACTION_RECORD",
        "CONTROLLED_DRY_RUN",
        "PATCH_ARTIFACT",
        "CONTROLLED_SOURCE_APPLY",
        "ROLLBACK_RECORD",
        "WORKFLOW_CONTINUATION",
        "RELEASE_READINESS_GATE"
    ];

    private static readonly string[] NotReadyAuthorityAreaCodes =
    [
        "ACCEPTED_APPROVAL_RECORD",
        "POLICY_SATISFACTION_RECORD",
        "CONTROLLED_DRY_RUN",
        "PATCH_ARTIFACT",
        "CONTROLLED_SOURCE_APPLY",
        "ROLLBACK_RECORD",
        "WORKFLOW_CONTINUATION",
        "RELEASE_READINESS_GATE"
    ];

    private static readonly string[] ReadyEvidenceOnlyAreaCodes =
    [
        "L4_CAPABILITY_MATRIX",
        "L4_INVARIANT_GUARDS",
        "L4_FAILURE_MODE_REPORT",
        "DOGFOOD_CAMPAIGN_EVIDENCE",
        "GOVERNANCE_TRACEABILITY",
        "WORKFLOW_READ_VISIBILITY",
        "TOOL_GATE_VISIBILITY",
        "UI_AUTHORITY_FIREWALL"
    ];

    [TestMethod]
    public void L4BackendReadinessReport_Exists()
    {
        Assert.IsTrue(File.Exists(ReceiptPath()));
        StringAssert.Contains(ReceiptText(), "PR165 adds the L4 backend readiness report.");
    }

    [TestMethod]
    public void L4BackendReadinessReport_ContainsRequiredCategories()
    {
        var areaCodes = Report().Entries.Select(entry => entry.AreaCode).ToArray();

        CollectionAssert.AreEquivalent(RequiredAreaCodes, areaCodes);

        var receipt = ReceiptText();
        foreach (var areaCode in RequiredAreaCodes)
        {
            StringAssert.Contains(receipt, areaCode);
        }
    }

    [TestMethod]
    public void L4BackendReadinessReport_DoesNotOverstateReadiness()
    {
        var entries = Report().Entries.ToDictionary(entry => entry.AreaCode, StringComparer.Ordinal);

        foreach (var areaCode in NotReadyAuthorityAreaCodes)
        {
            Assert.AreEqual("NotReady", entries[areaCode].ReadinessState, areaCode);
            Assert.IsFalse(entries[areaCode].CurrentEvidence.Contains("implemented", StringComparison.OrdinalIgnoreCase), areaCode);
        }
    }

    [TestMethod]
    public void L4BackendReadinessReport_ReadyItemsAreEvidenceOnly()
    {
        var readyEntries = Report().Entries.Where(entry => entry.ReadinessState == "Ready").ToArray();
        var readyCodes = readyEntries.Select(entry => entry.AreaCode).ToArray();

        CollectionAssert.AreEquivalent(ReadyEvidenceOnlyAreaCodes, readyCodes);

        foreach (var entry in readyEntries)
        {
            Assert.DoesNotContain(entry.CurrentEvidence, "authority record", StringComparison.OrdinalIgnoreCase, entry.AreaCode);
            Assert.DoesNotContain(entry.CurrentEvidence, "mutation", StringComparison.OrdinalIgnoreCase, entry.AreaCode);
            Assert.DoesNotContain(entry.CurrentEvidence, "execution authority", StringComparison.OrdinalIgnoreCase, entry.AreaCode);
            Assert.IsTrue(entry.BoundaryMaxim.Contains("not", StringComparison.OrdinalIgnoreCase) || entry.BoundaryMaxim.Contains("glass", StringComparison.OrdinalIgnoreCase));
        }
    }

    [TestMethod]
    public void L4BackendReadinessReport_StatesBackendCanBeginAuthorityImplementationButCannotExecuteL4()
    {
        var receipt = ReceiptText();

        StringAssert.Contains(receipt, "Backend is ready to begin Block P authority implementation.");
        StringAssert.Contains(receipt, "Backend is not ready for L4 execution.");
        StringAssert.Contains(receipt, "Backend is not ready for source apply.");
        StringAssert.Contains(receipt, "Backend is not ready for workflow continuation.");
        StringAssert.Contains(receipt, "Backend is not ready for release readiness.");
    }

    [TestMethod]
    public void L4BackendReadinessReport_StatesAcceptedApprovalRecordIsNext()
    {
        var report = Report();

        Assert.AreEqual("accepted approval record", report.NextBackendAuthorityChain[0]);
        StringAssert.Contains(ReceiptText(), "The next backend implementation target is accepted approval record.");
    }

    [TestMethod]
    public void L4BackendReadinessReport_StatesRequiredBackendChain()
    {
        var chain = "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate";

        CollectionAssert.AreEqual(
            new[]
            {
                "accepted approval record",
                "policy satisfaction record",
                "controlled dry-run",
                "patch artifact",
                "controlled source apply",
                "rollback",
                "workflow continuation",
                "release readiness gate"
            },
            Report().NextBackendAuthorityChain.ToArray());
        StringAssert.Contains(ReceiptText(), chain);
    }

    [TestMethod]
    public void L4BackendReadinessReport_DoesNotClaimAuthorityRecordsExist()
    {
        var receipt = ReceiptText();

        foreach (var claim in new[]
        {
            "accepted approval records are implemented",
            "policy satisfaction records are implemented",
            "controlled dry-run is implemented",
            "patch artifact creation is implemented",
            "source apply is implemented",
            "rollback is implemented",
            "workflow continuation is implemented",
            "release readiness is implemented"
        })
        {
            Assert.IsFalse(receipt.Contains(claim, StringComparison.OrdinalIgnoreCase), $"Receipt must not claim: {claim}");
        }
    }

    [TestMethod]
    public void L4BackendReadinessReport_DoesNotReferenceMutationServices()
    {
        var forbiddenTokens = new[]
        {
            "Apply" + "Source",
            "Apply" + "Patch",
            "Workflow" + "Runner",
            "Workflow" + "Dispatcher",
            "Tool" + "Executor",
            "Tool" + "Invoker",
            "Agent" + "Dispatcher",
            "Release" + "Publisher",
            "Memory" + "Promotion",
            "Retrieval" + "Activation",
            "Sql" + "Connection",
            "Db" + "Command",
            "File." + "Write",
            "File." + "Delete",
            "Process." + "Start",
            "git " + "commit",
            "git " + "push"
        };

        foreach (var file in Pr165Files())
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbiddenTokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"{Path.GetFileName(file)} must not reference {token}.");
            }
        }
    }

    [TestMethod]
    public void L4BackendReadinessReport_ReceiptStatesBoundary()
    {
        var receipt = ReceiptText();

        StringAssert.Contains(receipt, "This PR is tests/receipt only.");
        StringAssert.Contains(receipt, "Readiness report is not readiness.");
        StringAssert.Contains(receipt, "Readiness assessment is not authority.");
        StringAssert.Contains(receipt, "Dogfood campaign evidence is not release readiness.");
        StringAssert.Contains(receipt, "Governance traceability is not authority.");
        StringAssert.Contains(receipt, "UI authority firewall is not backend authority.");
        StringAssert.Contains(receipt, "L4 capability matrix is not L4 execution.");
        StringAssert.Contains(receipt, "L4 invariant guards are not L4 execution.");
        StringAssert.Contains(receipt, "L4 failure mode report is not remediation.");
        StringAssert.Contains(
            receipt,
            "PR165 does not implement accepted approval records, policy satisfaction records, dry-run execution, patch artifacts, source apply, rollback, workflow continuation, release readiness, memory promotion, retrieval activation, or release approval.");
        StringAssert.Contains(receipt, "PR165 checks the engine bay. It does not start the engine.");
    }

    private static L4BackendReadinessReport Report() =>
        new(
            Entries:
            [
                Entry("L4_CAPABILITY_MATRIX", "L4 capability matrix", "Ready", "PR161 defines the L4 capability matrix and ordered backend authority chain.", "None for definition-only planning.", "Use the matrix as a planning map only.", "Capability matrix is not capability execution."),
                Entry("L4_INVARIANT_GUARDS", "L4 invariant guards", "Ready", "PR162 regression tests preserve the L4 invariants.", "None for invariant proof.", "Keep guard tests green while adding authority records.", "L4 invariant guards are not L4 execution."),
                Entry("L4_FAILURE_MODE_REPORT", "L4 failure mode report", "Ready", "PR164 names required failure modes and blocked effects.", "None for failure-mode reporting.", "Use failure modes as review prompts, not fixes.", "L4 failure mode report is not remediation."),
                Entry("DOGFOOD_CAMPAIGN_EVIDENCE", "Dogfood campaign evidence", "Ready", "PR163 proves governed dogfood campaign evidence can be correlated.", "Release readiness gate is missing.", "Treat dogfood as evidence for later backend gates.", "Dogfood campaign evidence is not release readiness."),
                Entry("GOVERNANCE_TRACEABILITY", "Governance traceability", "Ready", "Governance trace and correlation reports can expose read-only evidence.", "Backend authority records are missing.", "Keep traceability read-only.", "Governance traceability is not authority."),
                Entry("WORKFLOW_READ_VISIBILITY", "Workflow read visibility", "Ready", "Workflow run, step, checkpoint, and evidence views are read-only inspection surfaces.", "Workflow transition authority is missing.", "Keep workflow visibility separate from continuation.", "Workflow read visibility is not workflow continuation."),
                Entry("TOOL_GATE_VISIBILITY", "Tool gate visibility", "Ready", "Tool request and gate previews are visible through safe report/API/CLI surfaces.", "Gate satisfaction and tool execution authority are missing.", "Keep gate visibility evidence-only until backend records exist.", "Gate preview is not gate satisfaction."),
                Entry("APPROVAL_PACKAGE_VISIBILITY", "Approval package visibility", "PartiallyReady", "Approval packages can be assembled and reviewed.", "Accepted approval record implementation is missing.", "Build accepted approval record first.", "Approval package visibility is not accepted approval."),
                Entry("MEMORY_PROPOSAL_VISIBILITY", "Memory proposal visibility", "PartiallyReady", "Memory proposals and review surfaces exist.", "Accepted memory and memory promotion authority are missing.", "Keep memory proposals separate from accepted memory.", "Memory proposal visibility is not accepted memory."),
                Entry("UI_AUTHORITY_FIREWALL", "UI authority firewall", "Ready", "Block P thin UI receipt and UI authority tests keep UI observational.", "Backend authority records are missing.", "Keep UI as glass while backend authority is built.", "UI is glass, not controls."),
                Entry("ACCEPTED_APPROVAL_RECORD", "Accepted approval record", "NotReady", "Matrix and tests identify the need.", "Accepted approval storage, validation, and read model are not implemented.", "Build backend-owned accepted approval record.", "Required approval is not accepted approval."),
                Entry("POLICY_SATISFACTION_RECORD", "Policy satisfaction record", "NotReady", "Matrix and tests identify the need.", "Policy satisfaction record is not implemented.", "Build policy satisfaction after accepted approval exists.", "Required policy is not policy satisfaction."),
                Entry("CONTROLLED_DRY_RUN", "Controlled dry-run", "NotReady", "Dry-run requirement and preview concepts exist.", "Controlled dry-run execution record is not implemented.", "Build dry-run only after policy satisfaction exists.", "Required dry-run is not dry-run execution."),
                Entry("PATCH_ARTIFACT", "Patch artifact", "NotReady", "Patch proposal evidence can exist.", "Immutable patch artifact creation is not implemented.", "Build patch artifact after controlled dry-run proof exists.", "Required patch artifact is not a patch artifact."),
                Entry("CONTROLLED_SOURCE_APPLY", "Controlled source apply", "NotReady", "Source apply requirements and preview receipts exist.", "Controlled source apply is not implemented.", "Build source apply only after approval, policy, dry-run, patch artifact, and rollback support exist.", "Required source apply is not source apply."),
                Entry("ROLLBACK_RECORD", "Rollback record", "NotReady", "Rollback requirement is named.", "Rollback record and rollback execution proof are not implemented.", "Build rollback record before workflow continuation or release readiness.", "Required rollback is not rollback."),
                Entry("WORKFLOW_CONTINUATION", "Workflow continuation", "NotReady", "Workflow state and read visibility exist.", "Backend workflow transition authority is not implemented.", "Build workflow continuation only after required authority and evidence records exist.", "Required workflow continuation is not workflow continuation."),
                Entry("RELEASE_READINESS_GATE", "Release readiness gate", "NotReady", "Dogfood, health, trace, and validation evidence can be inspected.", "Backend release readiness gate is not implemented.", "Build release readiness gate last in the chain.", "Required release gate is not release readiness.")
            ],
            BlockingGaps:
            [
                "Accepted approval record is not implemented.",
                "Policy satisfaction record is not implemented.",
                "Controlled dry-run execution is not implemented.",
                "Patch artifact creation is not implemented.",
                "Controlled source apply is not implemented.",
                "Rollback record is not implemented.",
                "Workflow continuation is not implemented.",
                "Release readiness gate is not implemented."
            ],
            NextBackendAuthorityChain:
            [
                "accepted approval record",
                "policy satisfaction record",
                "controlled dry-run",
                "patch artifact",
                "controlled source apply",
                "rollback",
                "workflow continuation",
                "release readiness gate"
            ],
            BoundaryMaxims:
            [
                "Readiness report is not readiness.",
                "Readiness assessment is not authority.",
                "Dogfood campaign evidence is not release readiness.",
                "Governance traceability is not authority.",
                "UI authority firewall is not backend authority.",
                "L4 capability matrix is not L4 execution.",
                "L4 invariant guards are not L4 execution.",
                "L4 failure mode report is not remediation."
            ]);

    private static L4BackendReadinessEntry Entry(
        string areaCode,
        string areaName,
        string readinessState,
        string currentEvidence,
        string missingCapability,
        string requiredNextStep,
        string boundaryMaxim) =>
        new(areaCode, areaName, readinessState, currentEvidence, missingCapability, requiredNextStep, boundaryMaxim);

    private static string ReceiptText() => File.ReadAllText(ReceiptPath());

    private static string ReceiptPath() =>
        Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR165_L4_BACKEND_READINESS_REPORT.md");

    private static IReadOnlyList<string> Pr165Files() =>
    [
        ReceiptPath(),
        Path.Combine(RepositoryRoot(), "IronDev.IntegrationTests", "Governance", "L4BackendReadinessReportTests.cs")
    ];

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

    private sealed record L4BackendReadinessReport(
        IReadOnlyList<L4BackendReadinessEntry> Entries,
        IReadOnlyList<string> BlockingGaps,
        IReadOnlyList<string> NextBackendAuthorityChain,
        IReadOnlyList<string> BoundaryMaxims);

    private sealed record L4BackendReadinessEntry(
        string AreaCode,
        string AreaName,
        string ReadinessState,
        string CurrentEvidence,
        string MissingCapability,
        string RequiredNextStep,
        string BoundaryMaxim);
}
