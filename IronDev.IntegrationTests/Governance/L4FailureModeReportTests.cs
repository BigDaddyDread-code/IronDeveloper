using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("L4FailureModeReport")]
public sealed class L4FailureModeReportTests
{
    private static readonly string[] RequiredFailureModeCodes =
    [
        "AUTHORITY_COLLAPSE",
        "EVIDENCE_COLLAPSE",
        "APPROVAL_THEATRE",
        "POLICY_SATISFACTION_THEATRE",
        "DRY_RUN_THEATRE",
        "PATCH_ARTIFACT_THEATRE",
        "SOURCE_APPLY_ESCAPE",
        "ROLLBACK_THEATRE",
        "WORKFLOW_CONTINUATION_ESCAPE",
        "RELEASE_READINESS_THEATRE",
        "DOGFOOD_CONFUSION",
        "MEMORY_PROMOTION_ESCAPE",
        "UI_AUTHORITY_ESCAPE",
        "TRACE_OBSERVABILITY_CONFUSION",
        "RAW_PRIVATE_PAYLOAD_LEAK",
        "CROSS_PROJECT_CONTAMINATION"
    ];

    [TestMethod]
    public void L4FailureModeReport_ContainsRequiredFailureModes()
    {
        var codes = Catalog().Select(mode => mode.Code).ToArray();

        CollectionAssert.AreEquivalent(RequiredFailureModeCodes, codes);

        var receipt = ReceiptText();
        foreach (var code in RequiredFailureModeCodes)
        {
            StringAssert.Contains(receipt, code);
        }
    }

    [TestMethod]
    public void L4FailureModeReport_EachFailureModeHasSeverityAndGuard()
    {
        foreach (var mode in Catalog())
        {
            AssertHasValue(mode.Code, mode.Title);
            AssertHasValue(mode.Code, mode.Severity);
            AssertHasValue(mode.Code, mode.Description);
            AssertHasValue(mode.Code, mode.Trigger);
            AssertHasValue(mode.Code, mode.FalsePositiveRisk);
            AssertHasValue(mode.Code, mode.RequiredGuard);
            AssertHasValue(mode.Code, mode.DetectionSignal);
            AssertHasValue(mode.Code, mode.BlockedEffect);
            AssertHasValue(mode.Code, mode.RelatedCapabilityCode);
            AssertHasValue(mode.Code, mode.BoundaryMaxim);
        }
    }

    [TestMethod]
    public void L4FailureModeReport_CoversEveryCurrentL4Capability()
    {
        var expectedCapabilities = new L4CapabilityMatrix().List().Select(entry => entry.CapabilityCode).ToArray();
        var coveredCapabilities = Catalog().Select(mode => mode.RelatedCapabilityCode).Distinct().ToArray();

        foreach (var capability in expectedCapabilities)
        {
            CollectionAssert.Contains(coveredCapabilities, capability);
            StringAssert.Contains(ReceiptText(), capability);
        }
    }

    [TestMethod]
    public void L4FailureModeReport_CoversDogfoodAndMemoryConfusion()
    {
        var codes = Catalog().Select(mode => mode.Code).ToArray();

        CollectionAssert.Contains(codes, "DOGFOOD_CONFUSION");
        CollectionAssert.Contains(codes, "MEMORY_PROMOTION_ESCAPE");
        CollectionAssert.Contains(codes, "CROSS_PROJECT_CONTAMINATION");
        StringAssert.Contains(ReceiptText(), "Dogfood campaign is evidence.");
        StringAssert.Contains(ReceiptText(), "Memory proposal is not accepted memory.");
        StringAssert.Contains(ReceiptText(), "Cross-project learning suggestion is not cross-project authority.");
    }

    [TestMethod]
    public void L4FailureModeReport_CoversUiAndTraceConfusion()
    {
        var codes = Catalog().Select(mode => mode.Code).ToArray();

        CollectionAssert.Contains(codes, "UI_AUTHORITY_ESCAPE");
        CollectionAssert.Contains(codes, "TRACE_OBSERVABILITY_CONFUSION");
        CollectionAssert.Contains(codes, "RAW_PRIVATE_PAYLOAD_LEAK");
        StringAssert.Contains(ReceiptText(), "UI is glass, not controls.");
        StringAssert.Contains(ReceiptText(), "Trace output is not approval.");
        StringAssert.Contains(ReceiptText(), "No raw/private payload exposure.");
    }

    [TestMethod]
    public void L4FailureModeReport_DoesNotImplementFixes()
    {
        var files = Pr164Files();
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

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbiddenTokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"{Path.GetFileName(file)} must not reference {token}.");
            }
        }
    }

    [TestMethod]
    public void L4FailureModeReport_ReceiptStatesNonImplementation()
    {
        var receipt = ReceiptText();

        StringAssert.Contains(receipt, "PR164 does not fix these failure modes.");
        StringAssert.Contains(receipt, "PR164 does not implement L4.");
        StringAssert.Contains(receipt, "PR164 does not grant authority.");
        StringAssert.Contains(
            receipt,
            "PR164 does not implement accepted approval records, policy satisfaction records, dry-run execution, patch artifacts, source apply, rollback, workflow continuation, release readiness, memory promotion, retrieval activation, or release approval.");
        StringAssert.Contains(receipt, "Failure mode report is not failure remediation.");
        StringAssert.Contains(receipt, "Failure mode detection is not failure correction.");
        StringAssert.Contains(receipt, "Risk naming is not authority.");
    }

    [TestMethod]
    public void L4FailureModeReport_ReviewLineIsPresent()
    {
        StringAssert.Contains(ReceiptText(), "PR164 names the ways L4 can fail. It does not fix them.");
    }

    private static IReadOnlyList<L4FailureMode> Catalog() =>
    [
        new(
            Code: "AUTHORITY_COLLAPSE",
            Title: "Authority collapse",
            Severity: "Critical",
            Description: "A requirement, trace, UI state, matrix row, or evidence record is treated as backend authority.",
            Trigger: "A caller treats definition, visibility, or evidence shape as permission.",
            FalsePositiveRisk: "Legitimate review evidence may be noisy but still non-authoritative.",
            RequiredGuard: "Backend-owned authority records must be validated separately from definitions, reports, traces, and UI state.",
            DetectionSignal: "Output claims approval, policy satisfaction, workflow continuation, source apply, or release approval without a matching backend authority record.",
            BlockedEffect: "approve; satisfy policy; continue workflow; apply source; release software",
            RelatedCapabilityCode: L4CapabilityCodes.AcceptedApprovalRecord,
            BoundaryMaxim: "Definition is not authority. Matrix row is not permission. Backend authority must be backend-owned."),
        new(
            Code: "EVIDENCE_COLLAPSE",
            Title: "Evidence collapse",
            Severity: "High",
            Description: "Evidence requirement or evidence reference is treated as actual proof.",
            Trigger: "A required evidence reference exists and is mistaken for evidence verification.",
            FalsePositiveRisk: "Evidence references can be valid pointers while still not proving the underlying claim.",
            RequiredGuard: "Evidence references must resolve to governed evidence before they can support a later authority decision.",
            DetectionSignal: "Requirement-only material is used as proof of dry-run acceptance, policy satisfaction, source apply readiness, or release readiness.",
            BlockedEffect: "policy satisfaction; dry-run acceptance; source apply; release readiness",
            RelatedCapabilityCode: L4CapabilityCodes.PolicySatisfactionRecord,
            BoundaryMaxim: "Evidence requirement is not evidence. Evidence reference is not proof."),
        new(
            Code: "APPROVAL_THEATRE",
            Title: "Approval theatre",
            Severity: "Critical",
            Description: "Approval-looking material is treated as accepted approval.",
            Trigger: "Approval package, UI review, requested approval, or approval-shaped text is used as if accepted by the backend.",
            FalsePositiveRisk: "Human review material can look decisive before an accepted approval record exists.",
            RequiredGuard: "Accepted approval must be a backend-owned accepted approval record, not review prose or package readiness.",
            DetectionSignal: "Accepted approval is inferred from required approval, approval package, UI review, or dogfood result.",
            BlockedEffect: "accepted approval; policy satisfaction; source apply; workflow continuation; release readiness",
            RelatedCapabilityCode: L4CapabilityCodes.AcceptedApprovalRecord,
            BoundaryMaxim: "Required approval is not accepted approval. Approval package is not accepted approval. UI review is not decision."),
        new(
            Code: "POLICY_SATISFACTION_THEATRE",
            Title: "Policy satisfaction theatre",
            Severity: "Critical",
            Description: "Policy requirement, rule text, profile, or gate preview is treated as policy satisfaction.",
            Trigger: "A policy rule or gate preview is visible and a caller treats that as a satisfied policy decision.",
            FalsePositiveRisk: "Policy checks can be readable and still not be backend satisfaction records.",
            RequiredGuard: "Policy satisfaction must be recorded as its own backend-owned satisfaction record.",
            DetectionSignal: "Dry-run, source apply, workflow continuation, or release readiness proceeds from policy text or gate preview alone.",
            BlockedEffect: "controlled dry-run; source apply; workflow continuation; release readiness",
            RelatedCapabilityCode: L4CapabilityCodes.PolicySatisfactionRecord,
            BoundaryMaxim: "Required policy is not policy satisfaction. Gate preview is not gate satisfaction."),
        new(
            Code: "DRY_RUN_THEATRE",
            Title: "Dry-run theatre",
            Severity: "High",
            Description: "Dry-run plan, dry-run requirement, preview, or simulated text is treated as real dry-run execution.",
            Trigger: "A dry-run requirement or preview receipt exists without actual controlled dry-run execution proof.",
            FalsePositiveRisk: "Preview text can be useful review material while still not proving execution.",
            RequiredGuard: "Controlled dry-run requires a backend-owned dry-run execution receipt.",
            DetectionSignal: "Patch artifact creation or source apply readiness is claimed from preview-only material.",
            BlockedEffect: "patch artifact creation; source apply; workflow continuation",
            RelatedCapabilityCode: L4CapabilityCodes.ControlledDryRun,
            BoundaryMaxim: "Required dry-run is not dry-run execution. Apply preview is not dry-run execution."),
        new(
            Code: "PATCH_ARTIFACT_THEATRE",
            Title: "Patch artifact theatre",
            Severity: "High",
            Description: "Patch proposal evidence is treated as an immutable patch artifact.",
            Trigger: "Proposal package, patch summary, or candidate repair text is treated as a governed patch artifact.",
            FalsePositiveRisk: "Proposal evidence can be high quality without being an artifact suitable for apply.",
            RequiredGuard: "Patch artifact creation must be separate from proposal evidence.",
            DetectionSignal: "Source apply, commit, push, or workflow continuation is attempted from proposal evidence.",
            BlockedEffect: "source apply; commit; push; workflow continuation",
            RelatedCapabilityCode: L4CapabilityCodes.PatchArtifact,
            BoundaryMaxim: "Required patch artifact is not a patch artifact. Patch proposal evidence package is not a patch."),
        new(
            Code: "SOURCE_APPLY_ESCAPE",
            Title: "Source apply escape",
            Severity: "Critical",
            Description: "A path writes source without accepted approval, policy satisfaction, dry-run proof, patch artifact, and rollback support.",
            Trigger: "File write, patch apply, branch mutation, commit, or push is reachable without the full backend chain.",
            FalsePositiveRisk: "Read-only previews can mention source apply without mutating source.",
            RequiredGuard: "Source apply must be backend-controlled and gated by every prior authority and evidence requirement.",
            DetectionSignal: "Source mutation or git mutation appears without accepted approval, policy satisfaction, dry-run proof, patch artifact, rollback, and source apply approval requirement.",
            BlockedEffect: "file write; patch apply; commit changes; push branch; branch mutation",
            RelatedCapabilityCode: L4CapabilityCodes.ControlledSourceApply,
            BoundaryMaxim: "Required source apply is not source apply. Source apply must be backend controlled."),
        new(
            Code: "ROLLBACK_THEATRE",
            Title: "Rollback theatre",
            Severity: "High",
            Description: "Rollback plan is treated as executable rollback proof.",
            Trigger: "Rollback text or requirement is accepted as proof that rollback can or did execute.",
            FalsePositiveRisk: "Rollback planning is necessary but does not prove runtime rollback capability.",
            RequiredGuard: "Rollback execution or rollback record must be distinct from rollback requirement and plan text.",
            DetectionSignal: "Source apply completion, workflow continuation, or release readiness relies on rollback plan only.",
            BlockedEffect: "source apply completion; workflow continuation; release readiness",
            RelatedCapabilityCode: L4CapabilityCodes.RollbackRecord,
            BoundaryMaxim: "Required rollback is not rollback. Rollback plan is not rollback execution."),
        new(
            Code: "WORKFLOW_CONTINUATION_ESCAPE",
            Title: "Workflow continuation escape",
            Severity: "Critical",
            Description: "Workflow continues because a previous step looks good, not because backend transition authority exists.",
            Trigger: "Review package, UI navigation, trace, or dogfood pass is treated as continuation permission.",
            FalsePositiveRisk: "Read-only navigation can look like progress while no workflow transition occurred.",
            RequiredGuard: "Workflow continuation must require backend-owned transition authority.",
            DetectionSignal: "Workflow transition, retry, repair, or continuation occurs without a workflow transition decision.",
            BlockedEffect: "continue workflow; transition workflow; retry workflow; repair workflow",
            RelatedCapabilityCode: L4CapabilityCodes.WorkflowContinuation,
            BoundaryMaxim: "Required workflow continuation is not workflow continuation. UI navigation is not workflow continuation."),
        new(
            Code: "RELEASE_READINESS_THEATRE",
            Title: "Release readiness theatre",
            Severity: "Critical",
            Description: "Dogfood pass, health check, validation summary, UI review, or correlation report is treated as release readiness.",
            Trigger: "A positive report or passed dogfood campaign is used as release readiness.",
            FalsePositiveRisk: "Positive evidence can be valuable while still not being a release gate.",
            RequiredGuard: "Release readiness must be a backend-owned release readiness gate decision.",
            DetectionSignal: "Release-ready state, tag, deploy, or ship action appears from dogfood, health, validation, UI, or report-only evidence.",
            BlockedEffect: "approve release; mark release ready; tag release; deploy; ship software",
            RelatedCapabilityCode: L4CapabilityCodes.ReleaseReadinessGate,
            BoundaryMaxim: "Required release gate is not release readiness. Dogfood pass is not release readiness. Health check is not release readiness. Validation summary is not release readiness. UI review is not release readiness."),
        new(
            Code: "DOGFOOD_CONFUSION",
            Title: "Dogfood confusion",
            Severity: "High",
            Description: "Dogfood campaign result is treated as approval, release readiness, policy satisfaction, or workflow continuation.",
            Trigger: "Dogfood pass or campaign success is promoted into authority.",
            FalsePositiveRisk: "Dogfood evidence should influence review but not satisfy authority.",
            RequiredGuard: "Dogfood receipts must remain evidence-only until a separate backend authority decision consumes them.",
            DetectionSignal: "Accepted approval, policy satisfaction, release readiness, or workflow continuation is inferred from dogfood result.",
            BlockedEffect: "accepted approval; policy satisfaction; release readiness; workflow continuation",
            RelatedCapabilityCode: L4CapabilityCodes.ReleaseReadinessGate,
            BoundaryMaxim: "Dogfood campaign is evidence. Dogfood pass is not release approval. Campaign success is not workflow continuation."),
        new(
            Code: "MEMORY_PROMOTION_ESCAPE",
            Title: "Memory promotion escape",
            Severity: "Critical",
            Description: "Memory proposal, candidate learning, or campaign observation is treated as accepted memory or portable engineering memory.",
            Trigger: "Memory proposal or dogfood observation is promoted without governed memory promotion.",
            FalsePositiveRisk: "Repeated observations can be strong candidates while still not accepted memory.",
            RequiredGuard: "Memory promotion must require governed review and accepted memory creation.",
            DetectionSignal: "Retrieval activation, accepted memory, or cross-project learning authority appears from proposal or campaign observation.",
            BlockedEffect: "promote memory; activate retrieval; cross-project learning authority",
            RelatedCapabilityCode: L4CapabilityCodes.ReleaseReadinessGate,
            BoundaryMaxim: "Memory proposal is not accepted memory. Campaign observation is not memory promotion. Candidate learning is not portable engineering memory."),
        new(
            Code: "UI_AUTHORITY_ESCAPE",
            Title: "UI authority escape",
            Severity: "Critical",
            Description: "UI route, status chip, copy action, refresh, or button becomes backend authority.",
            Trigger: "Frontend state or user interface affordance is treated as approval, policy, workflow, tool, source, or release authority.",
            FalsePositiveRisk: "UI should expose evidence clearly, which can visually resemble a control surface.",
            RequiredGuard: "UI must call only explicit backend APIs and must not own backend authority.",
            DetectionSignal: "UI state claims approval, policy satisfaction, workflow transition, tool invocation, source apply, or release readiness.",
            BlockedEffect: "approve; satisfy policy; transition workflow; invoke tool; apply source; release software",
            RelatedCapabilityCode: L4CapabilityCodes.WorkflowContinuation,
            BoundaryMaxim: "UI is glass, not controls. UI cannot own L4 authority. UI route is not capability. UI view model is not authority."),
        new(
            Code: "TRACE_OBSERVABILITY_CONFUSION",
            Title: "Trace observability confusion",
            Severity: "High",
            Description: "Trace visibility is treated as replay, approval, or control.",
            Trigger: "Timeline output or trace detail is mistaken for authority to replay, approve, transition, or release.",
            FalsePositiveRisk: "Trace exploration needs rich context without becoming a control plane.",
            RequiredGuard: "Observability surfaces must remain read-only and non-authoritative.",
            DetectionSignal: "Trace output causes replay, transition, approval, or release-readiness effects.",
            BlockedEffect: "governance replay; workflow transition; approval; release readiness",
            RelatedCapabilityCode: L4CapabilityCodes.WorkflowContinuation,
            BoundaryMaxim: "Trace output is not approval. Timeline is not authority. Observability is not mutation permission."),
        new(
            Code: "RAW_PRIVATE_PAYLOAD_LEAK",
            Title: "Raw private payload leak",
            Severity: "Critical",
            Description: "Failure reports expose raw payloads, prompts, completions, private reasoning, secrets, source contents, or patch payloads.",
            Trigger: "Diagnostic report serializes unsafe payload material.",
            FalsePositiveRisk: "Safe summaries may mention payload categories without retaining payload values.",
            RequiredGuard: "Reports must retain safe summaries and references only.",
            DetectionSignal: "Raw prompt, raw completion, raw tool output, hidden/private reasoning, secret, source content, or patch payload appears in report material.",
            BlockedEffect: "payload leak; secret leak; private reasoning leak; source leak",
            RelatedCapabilityCode: L4CapabilityCodes.PatchArtifact,
            BoundaryMaxim: "No raw/private payload exposure. No hidden/private reasoning exposure."),
        new(
            Code: "CROSS_PROJECT_CONTAMINATION",
            Title: "Cross-project contamination",
            Severity: "Critical",
            Description: "Evidence, memory, approval, or authority leaks across project boundaries.",
            Trigger: "Project-scoped truth is reused in another project as authority or confidential evidence.",
            FalsePositiveRisk: "Sanitized engineering learning can be portable only after governed review.",
            RequiredGuard: "Project-scoped evidence and authority must remain isolated unless explicitly sanitized and re-approved.",
            DetectionSignal: "Cross-project authority transfer, confidential detail exposure, or memory contamination appears.",
            BlockedEffect: "cross-project authority transfer; confidential detail exposure; memory contamination",
            RelatedCapabilityCode: L4CapabilityCodes.AcceptedApprovalRecord,
            BoundaryMaxim: "Project-specific truth remains isolated. Portable engineering memory must be sanitized. Cross-project learning suggestion is not cross-project authority.")
    ];

    private static void AssertHasValue(string code, string value) =>
        Assert.IsFalse(string.IsNullOrWhiteSpace(value), $"{code} must have a populated field.");

    private static string ReceiptText() => File.ReadAllText(ReceiptPath());

    private static string ReceiptPath() =>
        Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR164_L4_FAILURE_MODE_REPORT.md");

    private static IReadOnlyList<string> Pr164Files() =>
    [
        ReceiptPath(),
        Path.Combine(RepositoryRoot(), "IronDev.IntegrationTests", "Governance", "L4FailureModeReportTests.cs")
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

    private sealed record L4FailureMode(
        string Code,
        string Title,
        string Severity,
        string Description,
        string Trigger,
        string FalsePositiveRisk,
        string RequiredGuard,
        string DetectionSignal,
        string BlockedEffect,
        string RelatedCapabilityCode,
        string BoundaryMaxim);
}
