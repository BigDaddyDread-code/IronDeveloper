using System.Reflection;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("GovernanceTraceExplorer")]
public sealed class GovernanceTraceExplorerBoundaryTests
{
    [TestMethod] public void TraceSummary_IsReadOnlyTrace() => Assert.IsTrue(Summary().IsReadOnlyTrace);
    [TestMethod] public void TraceSummary_IsNotApproval() => Assert.IsFalse(Summary().IsApproval);
    [TestMethod] public void TraceSummary_IsNotPolicySatisfaction() => Assert.IsFalse(Summary().IsPolicySatisfaction);
    [TestMethod] public void TraceSummary_IsNotWorkflowTransition() => Assert.IsFalse(Summary().IsWorkflowTransition);
    [TestMethod] public void TraceSummary_CannotApprove() => Assert.IsFalse(Summary().CanApprove);
    [TestMethod] public void TraceSummary_CannotReject() => Assert.IsFalse(Summary().CanReject);
    [TestMethod] public void TraceSummary_CannotSatisfyPolicy() => Assert.IsFalse(Summary().CanSatisfyPolicy);
    [TestMethod] public void TraceSummary_CannotTransitionWorkflow() => Assert.IsFalse(Summary().CanTransitionWorkflow);
    [TestMethod] public void TraceSummary_CannotInvokeTool() => Assert.IsFalse(Summary().CanInvokeTool);
    [TestMethod] public void TraceSummary_CannotDispatchAgent() => Assert.IsFalse(Summary().CanDispatchAgent);
    [TestMethod] public void TraceSummary_CannotCallModel() => Assert.IsFalse(Summary().CanCallModel);
    [TestMethod] public void TraceSummary_CannotPromoteMemory() => Assert.IsFalse(Summary().CanPromoteMemory);
    [TestMethod] public void TraceSummary_CannotApplySource() => Assert.IsFalse(Summary().CanApplySource);

    [TestMethod]
    public void TraceDetail_TimelineItemsAreSafeSummariesOnly()
    {
        var properties = typeof(GovernanceTraceTimelineItem).GetProperties().Select(property => property.Name).ToArray();
        CollectionAssert.Contains(properties, nameof(GovernanceTraceTimelineItem.SafeSummary));
        AssertNoForbiddenNames(properties, "PayloadJson", "RawPayload", "RawPrompt", "RawCompletion", "RawToolOutput", "RawCommandOutput", "SourceContent", "PatchPayload", "PrivateReasoning", "HiddenReasoning", "ChainOfThought");
    }

    [TestMethod]
    public void TraceDetail_RelatedReferencesAreReferencesOnly()
    {
        var properties = typeof(GovernanceTraceRelatedReference).GetProperties().Select(property => property.Name).ToArray();
        CollectionAssert.AreEquivalent(new[] { "ReferenceKind", "ReferenceId", "SafeSummary" }, properties);
    }

    [TestMethod]
    public void TraceDetail_BoundaryWarningsSayTraceIsNotAuthority()
    {
        var warnings = string.Join("\n", GovernanceTraceExplorerBoundaries.Warnings);
        StringAssert.Contains(warnings, "Governance trace explorer is read-only.");
        StringAssert.Contains(warnings, "Trace output is not approval.");
        StringAssert.Contains(warnings, "Trace output is not policy satisfaction.");
        StringAssert.Contains(warnings, "Trace output is not workflow transition.");
    }

    [TestMethod] public void TraceSearch_DoesNotCreateGovernanceEvents() => AssertNoServiceMethod("CreateEventAsync", "AppendEventAsync");
    [TestMethod] public void TraceSearch_DoesNotCreateApprovalDecision() => AssertNoServiceMethod("CreateApprovalDecisionAsync", "ApproveAsync");
    [TestMethod] public void TraceSearch_DoesNotCreatePolicyDecision() => AssertNoServiceMethod("CreatePolicyDecisionAsync", "SatisfyPolicyAsync");
    [TestMethod] public void TraceSearch_DoesNotCreateToolRequest() => AssertNoServiceMethod("CreateToolRequestAsync", "InvokeToolAsync");
    [TestMethod] public void TraceSearch_DoesNotCreateWorkflowTransition() => AssertNoServiceMethod("TransitionWorkflowAsync", "ContinueWorkflowAsync");
    [TestMethod] public void TraceSearch_DoesNotCreateMemoryProposal() => AssertNoServiceMethod("CreateMemoryProposalAsync", "PromoteMemoryAsync");
    [TestMethod] public void TraceSearch_DoesNotCreateA2aHandoff() => AssertNoServiceMethod("DispatchAgentAsync", "SendHandoffAsync");
    [TestMethod] public void TraceSearch_DoesNotCallModel() => AssertNoServiceMethod("CallModelAsync", "PromptAsync");
    [TestMethod] public void TraceSearch_DoesNotInvokeTool() => AssertNoServiceMethod("InvokeToolAsync", "ExecuteToolAsync");
    [TestMethod] public void TraceSearch_DoesNotDispatchAgent() => AssertNoServiceMethod("DispatchAgentAsync", "RunAgentAsync");
    [TestMethod] public void TraceSearch_DoesNotPromoteMemory() => AssertNoServiceMethod("PromoteMemoryAsync", "ActivateRetrievalAsync");
    [TestMethod] public void TraceSearch_DoesNotApplySource() => AssertNoServiceMethod("ApplySourceAsync", "ApplyPatchAsync");

    [TestMethod]
    public void TraceSearch_DoesNotExposePayloadJson()
    {
        AssertNoForbiddenNames(AllTracePropertyNames(), "PayloadJson", "RawPayload", "Payload", "FullPayload");
    }

    [TestMethod]
    public void TraceSearch_DoesNotExposeHiddenPrivateReasoning()
    {
        AssertNoForbiddenNames(AllTracePropertyNames(), "RawPrompt", "RawCompletion", "RawToolOutput", "RawCommandOutput", "PrivateReasoning", "HiddenReasoning", "ChainOfThought", "Scratchpad", "SourceContent", "PatchPayload");
    }

    private static GovernanceTraceSummary Summary() =>
        new()
        {
            TraceId = "trace-1",
            ProjectReferenceId = "project-1",
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            CorrelationId = "correlation-1",
            CausationId = "causation-1",
            SubjectReferenceId = "subject-1",
            EventKind = "governance.event.recorded",
            SourceComponent = "governance-test",
            SafeSummary = "Governance trace summary only.",
            RecordedUtc = DateTimeOffset.UtcNow,
            IsReadOnlyTrace = true,
            IsAuthorityDecision = false,
            IsApproval = false,
            IsPolicySatisfaction = false,
            IsWorkflowTransition = false,
            CanApprove = false,
            CanReject = false,
            CanSatisfyPolicy = false,
            CanTransitionWorkflow = false,
            CanInvokeTool = false,
            CanDispatchAgent = false,
            CanCallModel = false,
            CanPromoteMemory = false,
            CanApplySource = false
        };

    private static void AssertNoServiceMethod(params string[] forbidden)
    {
        var methods = typeof(IGovernanceTraceExplorerService).GetMethods().Select(method => method.Name).ToArray();
        AssertNoForbiddenNames(methods, forbidden);
    }

    private static string[] AllTracePropertyNames() =>
    [
        .. typeof(GovernanceTraceSummary).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name),
        .. typeof(GovernanceTraceDetail).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name),
        .. typeof(GovernanceTraceTimelineItem).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name),
        .. typeof(GovernanceTraceRelatedReference).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name),
        .. typeof(GovernanceTraceListResponse).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name),
        .. typeof(GovernanceTraceDetailResponse).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name)
    ];

    private static void AssertNoForbiddenNames(IEnumerable<string> values, params string[] forbidden)
    {
        var set = new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var token in forbidden)
            Assert.IsFalse(set.Contains(token), $"Unexpected trace authority/payload member found: {token}");
    }
}
