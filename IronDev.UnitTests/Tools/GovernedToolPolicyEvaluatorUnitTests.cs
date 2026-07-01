using IronDev.Core.Tools;

namespace IronDev.UnitTests.Tools;

[TestClass]
public sealed class GovernedToolPolicyEvaluatorUnitTests
{
    [TestMethod]
    public void ReadOnlyToolWithMatchingNameAndAllowedCallerIsAllowed()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate();

        GovernedToolPolicyEvaluatorTestFixtures.AssertAllowed(decision);
    }

    [TestMethod]
    public void AllowedDecisionHasStableReason()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate();

        Assert.AreEqual("Governed tool policy allowed this read-only call.", decision.Reason);
    }

    [TestMethod]
    public void ToolNameMatchIsCaseInsensitive()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(name: "Read.Context"),
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(toolName: "read.context"));

        GovernedToolPolicyEvaluatorTestFixtures.AssertAllowed(decision);
    }

    [TestMethod]
    public void AllowedCallerMatchIsCaseInsensitive()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowedCallers: ["Planner"]),
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(requestedBy: "planner"));

        GovernedToolPolicyEvaluatorTestFixtures.AssertAllowed(decision);
    }

    [TestMethod]
    public void AllowedNestedDepthPassesWhenDefinitionAllowsNestedCalls()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsNestedCalls: true),
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(nestedCallDepth: 1));

        GovernedToolPolicyEvaluatorTestFixtures.AssertAllowed(decision);
    }

    [TestMethod]
    public void AllowedParentRequestPassesWhenDefinitionAllowsNestedCalls()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsNestedCalls: true),
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(parentRequestId: "parent-request"));

        GovernedToolPolicyEvaluatorTestFixtures.AssertAllowed(decision);
    }

    [TestMethod]
    public void AllowedNestedDepthAndParentRequestPassWhenDefinitionAllowsNestedCalls()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsNestedCalls: true),
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(parentRequestId: "parent-request", nestedCallDepth: 2));

        GovernedToolPolicyEvaluatorTestFixtures.AssertAllowed(decision);
    }

    [TestMethod]
    public void ToolNameMismatchIsRejected()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(name: GovernedToolPolicyEvaluatorTestFixtures.RegisteredToolName),
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(toolName: GovernedToolPolicyEvaluatorTestFixtures.RequestedToolName));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.ToolNameMismatchReason());
    }

    [TestMethod]
    public void ToolNameMismatchReasonNamesRequestAndRegisteredTool()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(name: "registered.audit"),
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(toolName: "requested.audit"));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            "Request tool 'requested.audit' does not match registered tool 'registered.audit'.");
    }

    [TestMethod]
    public void ToolNameMismatchIsCheckedBeforeDangerousCapabilities()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(
                name: GovernedToolPolicyEvaluatorTestFixtures.RegisteredToolName,
                mutatesState: true,
                allowsFileWrites: true,
                allowsProcessExecution: true,
                allowsNetworkAccess: true,
                allowsWorkspaceMutation: true),
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(toolName: GovernedToolPolicyEvaluatorTestFixtures.RequestedToolName));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.ToolNameMismatchReason());
    }

    [TestMethod]
    public void ToolNameMismatchIsCheckedBeforeCallerPolicy()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(name: GovernedToolPolicyEvaluatorTestFixtures.RegisteredToolName),
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(
                toolName: GovernedToolPolicyEvaluatorTestFixtures.RequestedToolName,
                requestedBy: GovernedToolPolicyEvaluatorTestFixtures.DisallowedCaller));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.ToolNameMismatchReason());
    }

    [TestMethod]
    public void ToolNameMismatchIsCheckedBeforeNestedCallPolicy()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(name: GovernedToolPolicyEvaluatorTestFixtures.RegisteredToolName),
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(
                toolName: GovernedToolPolicyEvaluatorTestFixtures.RequestedToolName,
                parentRequestId: "parent-request",
                nestedCallDepth: 1));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.ToolNameMismatchReason());
    }

    [TestMethod]
    public void MutationCapableToolIsRejected()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(mutatesState: true));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.MutationReason());
    }

    [TestMethod]
    public void FileWriteCapableToolIsRejected()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsFileWrites: true));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.FileWriteReason());
    }

    [TestMethod]
    public void ProcessExecutionCapableToolIsRejected()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsProcessExecution: true));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.ProcessReason());
    }

    [TestMethod]
    public void NetworkAccessCapableToolIsRejected()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsNetworkAccess: true));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.NetworkReason());
    }

    [TestMethod]
    public void WorkspaceMutationCapableToolIsRejected()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsWorkspaceMutation: true));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.WorkspaceReason());
    }

    [TestMethod]
    public void EachCapabilityBlockUsesGovernedReadOnlyPathReason()
    {
        var decisions = new[]
        {
            GovernedToolPolicyEvaluatorTestFixtures.Evaluate(definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(mutatesState: true)),
            GovernedToolPolicyEvaluatorTestFixtures.Evaluate(definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsFileWrites: true)),
            GovernedToolPolicyEvaluatorTestFixtures.Evaluate(definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsProcessExecution: true)),
            GovernedToolPolicyEvaluatorTestFixtures.Evaluate(definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsNetworkAccess: true)),
            GovernedToolPolicyEvaluatorTestFixtures.Evaluate(definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsWorkspaceMutation: true))
        };

        Assert.IsTrue(decisions.All(decision => !decision.IsAllowed));
        Assert.IsTrue(decisions.All(decision => decision.Reason.Contains("governed read-only tool path", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void MultipleDangerousCapabilitiesRejectByFirstObservableCapability()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(
                mutatesState: true,
                allowsFileWrites: true,
                allowsProcessExecution: true,
                allowsNetworkAccess: true,
                allowsWorkspaceMutation: true));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.MutationReason());
    }

    [TestMethod]
    public void MutationCapabilityIsCheckedBeforeFileWriteCapability()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(mutatesState: true, allowsFileWrites: true));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.MutationReason());
    }

    [TestMethod]
    public void FileWriteCapabilityIsCheckedBeforeProcessExecutionCapability()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsFileWrites: true, allowsProcessExecution: true));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.FileWriteReason());
    }

    [TestMethod]
    public void ProcessExecutionCapabilityIsCheckedBeforeNetworkAccessCapability()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsProcessExecution: true, allowsNetworkAccess: true));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.ProcessReason());
    }

    [TestMethod]
    public void NetworkAccessCapabilityIsCheckedBeforeWorkspaceMutationCapability()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsNetworkAccess: true, allowsWorkspaceMutation: true));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.NetworkReason());
    }

    [TestMethod]
    public void WorkspaceMutationCapabilityIsCheckedBeforeCallerPolicy()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsWorkspaceMutation: true),
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(requestedBy: GovernedToolPolicyEvaluatorTestFixtures.DisallowedCaller));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.WorkspaceReason());
    }

    [TestMethod]
    public void DisallowedCallerIsRejected()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(requestedBy: GovernedToolPolicyEvaluatorTestFixtures.DisallowedCaller));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.CallerReason());
    }

    [TestMethod]
    public void EmptyAllowedCallersRejectsAllCallers()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowedCallers: []));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.CallerReason(GovernedToolPolicyEvaluatorTestFixtures.AllowedCaller));
    }

    [TestMethod]
    public void AllowedCallerListIsCaseInsensitive()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowedCallers: ["PLANNER", "Reviewer"]));

        GovernedToolPolicyEvaluatorTestFixtures.AssertAllowed(decision);
    }

    [TestMethod]
    public void AllowedCallerListCanContainMultipleCallers()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(
                allowedCallers:
                [
                    "planner",
                    GovernedToolPolicyEvaluatorTestFixtures.OtherAllowedCaller
                ]),
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(requestedBy: GovernedToolPolicyEvaluatorTestFixtures.OtherAllowedCaller));

        GovernedToolPolicyEvaluatorTestFixtures.AssertAllowed(decision);
    }

    [TestMethod]
    public void DisallowedCallerReasonNamesRequestedCallerAndTool()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(requestedBy: "unknown-caller"));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            "Caller 'unknown-caller' is not allowed to run governed tool 'read.context'.");
    }

    [TestMethod]
    public void CallerPolicyIsCheckedBeforeNestedCallPolicy()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(
                requestedBy: GovernedToolPolicyEvaluatorTestFixtures.DisallowedCaller,
                parentRequestId: "parent-request",
                nestedCallDepth: 1));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.CallerReason());
    }

    [TestMethod]
    public void NestedDepthRejectedWhenNestedCallsDisabled()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(nestedCallDepth: 1));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.NestedReason());
    }

    [TestMethod]
    public void ParentRequestRejectedWhenNestedCallsDisabled()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(parentRequestId: "parent-request"));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.NestedReason());
    }

    [TestMethod]
    public void NestedDepthAndParentRequestRejectedWhenNestedCallsDisabled()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(parentRequestId: "parent-request", nestedCallDepth: 1));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.NestedReason());
    }

    [TestMethod]
    public void NestedDepthAllowedWhenNestedCallsEnabled()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsNestedCalls: true),
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(nestedCallDepth: 1));

        GovernedToolPolicyEvaluatorTestFixtures.AssertAllowed(decision);
    }

    [TestMethod]
    public void ParentRequestAllowedWhenNestedCallsEnabled()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsNestedCalls: true),
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(parentRequestId: "parent-request"));

        GovernedToolPolicyEvaluatorTestFixtures.AssertAllowed(decision);
    }

    [TestMethod]
    public void NestedCallReasonNamesTool()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(parentRequestId: "parent-request"));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            "Nested governed tool call 'read.context' was rejected.");
    }

    [TestMethod]
    public void ZeroDepthWithoutParentIsNotNested()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(parentRequestId: null, nestedCallDepth: 0));

        GovernedToolPolicyEvaluatorTestFixtures.AssertAllowed(decision);
    }

    [TestMethod]
    public void WhitespaceParentRequestIdIsNotNested()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(parentRequestId: "   ", nestedCallDepth: 0));

        GovernedToolPolicyEvaluatorTestFixtures.AssertAllowed(decision);
    }

    [TestMethod]
    public void AllowedDecisionIsAllowedAndHasReason()
    {
        var decision = GovernedToolPolicyDecision.Allowed();

        Assert.IsTrue(decision.IsAllowed);
        Assert.AreEqual(GovernedToolPolicyEvaluatorTestFixtures.AllowedReason(), decision.Reason);
    }

    [TestMethod]
    public void RejectedDecisionIsNotAllowedAndHasReason()
    {
        var decision = GovernedToolPolicyDecision.Rejected("blocked by policy");

        Assert.IsFalse(decision.IsAllowed);
        Assert.AreEqual("blocked by policy", decision.Reason);
    }

    [TestMethod]
    public void RejectedDecisionContainsNoExecutionSignal()
    {
        var decision = GovernedToolPolicyDecision.Rejected("blocked by policy");

        AssertDecisionHasOnlyPolicyShape(decision);
    }

    [TestMethod]
    public void AllowedDecisionContainsNoExecutionSignal()
    {
        var decision = GovernedToolPolicyDecision.Allowed();

        AssertDecisionHasOnlyPolicyShape(decision);
    }

    [TestMethod]
    public void DecisionDoesNotContainApprovalMutationRetryOrRepairFlags()
    {
        var properties = typeof(GovernedToolPolicyDecision)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { "IsAllowed", "Reason" }, properties);
        Assert.IsFalse(properties.Any(name => name.Contains("Approval", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(properties.Any(name => name.Contains("Mutation", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(properties.Any(name => name.Contains("Retry", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(properties.Any(name => name.Contains("Repair", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(properties.Any(name => name.Contains("Execute", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void AllowedPolicyDecisionDoesNotExecuteTool()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate();

        AssertDecisionHasOnlyPolicyShape(decision);
    }

    [TestMethod]
    public void RejectedPolicyDecisionDoesNotExecuteTool()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(mutatesState: true));

        AssertDecisionHasOnlyPolicyShape(decision);
    }

    [TestMethod]
    public void RejectedPolicyDecisionDoesNotWriteFilesRunProcessesAccessNetworkOrMutateWorkspace()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsWorkspaceMutation: true));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.WorkspaceReason());
        AssertDecisionHasOnlyPolicyShape(decision);
    }

    [TestMethod]
    public void RejectedPolicyDecisionDoesNotGrantAuthorityOrAuthorizeFallbackExecutionOrRepair()
    {
        var decision = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(requestedBy: GovernedToolPolicyEvaluatorTestFixtures.DisallowedCaller));

        GovernedToolPolicyEvaluatorTestFixtures.AssertRejected(
            decision,
            GovernedToolPolicyEvaluatorTestFixtures.CallerReason());
        AssertDecisionHasOnlyPolicyShape(decision);
    }

    [TestMethod]
    public void SameDefinitionAndRequestProduceSameDecision()
    {
        var definition = GovernedToolPolicyEvaluatorTestFixtures.Definition();
        var request = GovernedToolPolicyEvaluatorTestFixtures.Request();

        var first = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(definition, request);
        var second = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(definition, request);

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void EquivalentCaseInsensitiveInputsProduceSameAllowedDecision()
    {
        var lower = GovernedToolPolicyEvaluatorTestFixtures.Evaluate();
        var mixed = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(name: "READ.CONTEXT", allowedCallers: ["PLANNER"]),
            request: GovernedToolPolicyEvaluatorTestFixtures.Request(toolName: "read.context", requestedBy: "planner"));

        Assert.AreEqual(lower, mixed);
    }

    [TestMethod]
    public void EquivalentRejectedInputsProduceSameRejectionReason()
    {
        var first = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsNetworkAccess: true));
        var second = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            definition: GovernedToolPolicyEvaluatorTestFixtures.Definition(allowsNetworkAccess: true));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void EvaluatorDoesNotDependOnClockEnvironmentOrFilesystem()
    {
        var first = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            request: GovernedToolPolicyEvaluatorTestFixtures.Request() with
            {
                CreatedAtUtc = GovernedToolPolicyEvaluatorTestFixtures.FixedCreatedAtUtc
            });
        var second = GovernedToolPolicyEvaluatorTestFixtures.Evaluate(
            request: GovernedToolPolicyEvaluatorTestFixtures.Request() with
            {
                CreatedAtUtc = GovernedToolPolicyEvaluatorTestFixtures.FixedCreatedAtUtc.AddYears(20)
            });

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void FastUnitProjectStillReferencesOnlyCoreAndMSTest()
    {
        var project = GovernedToolPolicyEvaluatorTestFixtures.UnitTestProject();
        var projectReferences = project
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var packageReferences = project
            .Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { @"..\IronDev.Core\IronDev.Core.csproj" },
            projectReferences);
        CollectionAssert.AreEquivalent(
            new[] { "Microsoft.NET.Test.Sdk", "MSTest.TestAdapter", "MSTest.TestFramework" },
            packageReferences);
    }

    [TestMethod]
    public void G09bSourcesDoNotReferenceRuntimeRegistryOrMutationSurfaces()
    {
        var source = GovernedToolPolicyEvaluatorTestFixtures.SourceText(
            "IronDev.UnitTests/Tools/GovernedToolPolicyEvaluatorTestFixtures.cs",
            "IronDev.UnitTests/Tools/GovernedToolPolicyEvaluatorUnitTests.cs");

        foreach (var forbidden in GovernedToolPolicyEvaluatorTestFixtures.RuntimeAndMutationForbiddenFragments())
        {
            Assert.IsFalse(
                source.Contains(forbidden, StringComparison.Ordinal),
                $"G09b unit tests must not contain runtime or mutation surface '{forbidden}'.");
        }
    }

    private static void AssertDecisionHasOnlyPolicyShape(GovernedToolPolicyDecision decision)
    {
        Assert.IsNotNull(decision.Reason);
        var properties = typeof(GovernedToolPolicyDecision)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { "IsAllowed", "Reason" }, properties);
    }
}
