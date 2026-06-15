using System.Reflection;
using IronDev.Core.Governance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("GovernanceTraceExplorer")]
public sealed class GovernanceTraceExplorerStaticBoundaryTests
{
    [TestMethod]
    public void GovernanceTraceExplorerProductionFiles_Exist()
    {
        foreach (var path in ProductionFiles())
            Assert.IsTrue(File.Exists(path), $"Missing governance trace explorer production file: {path}");
    }

    [TestMethod]
    public void GovernanceTraceExplorerReceipt_ExistsAndPreservesBoundaryLanguage()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR145_GOVERNANCE_TRACE_EXPLORER_API.md"));
        StringAssert.Contains(receipt, "PR145 adds the Governance Trace Explorer API.");
        StringAssert.Contains(receipt, "Governance Trace Explorer API is read-only.");
        StringAssert.Contains(receipt, "Traceability is not authority.");
        StringAssert.Contains(receipt, "Trace output is not approval.");
        StringAssert.Contains(receipt, "Trace output is not policy satisfaction.");
        StringAssert.Contains(receipt, "Trace output is not workflow transition.");
        StringAssert.Contains(receipt, "Trace output is not tool invocation.");
        StringAssert.Contains(receipt, "Trace output is not agent dispatch.");
        StringAssert.Contains(receipt, "Trace output is not model execution.");
        StringAssert.Contains(receipt, "Trace output is not memory promotion.");
        StringAssert.Contains(receipt, "Trace output is not source apply.");
        StringAssert.Contains(receipt, "The explorer returns safe summaries and references only.");
        StringAssert.Contains(receipt, "PR145 opens the trace window. It does not move the machinery.");
    }

    [TestMethod]
    public void GovernanceTraceExplorerController_IsGetOnly()
    {
        var methods = typeof(IronDev.Api.Controllers.GovernanceTraceExplorerController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToArray();

        Assert.IsTrue(methods.Any(method => method.GetCustomAttributes<HttpGetAttribute>().Any()), "Controller should expose GET routes.");
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes<HttpPostAttribute>().Any()), "Controller must not expose POST routes.");
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes<HttpPutAttribute>().Any()), "Controller must not expose PUT routes.");
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes<HttpPatchAttribute>().Any()), "Controller must not expose PATCH routes.");
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes<HttpDeleteAttribute>().Any()), "Controller must not expose DELETE routes.");
    }

    [TestMethod]
    public void GovernanceTraceExplorerRoutes_DoNotContainForbiddenFragments()
    {
        var controller = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "GovernanceTraceExplorerController.cs"));
        foreach (var fragment in new[]
        {
            "approve",
            "reject",
            "grant",
            "satisfy",
            "transition",
            "continue",
            "execute",
            "invoke",
            "dispatch",
            "replay",
            "rerun",
            "apply-source",
            "patch-apply",
            "promote-memory",
            "activate-retrieval"
        })
        {
            Assert.IsFalse(controller.Contains($"\"{fragment}", StringComparison.OrdinalIgnoreCase), $"Forbidden route fragment found: {fragment}");
        }

        StringAssert.Contains(controller, "api/v1/governance/traces");
    }

    [TestMethod]
    public void GovernanceTraceExplorerService_DoesNotDeclareForbiddenMethods()
    {
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ApproveAsync",
            "RejectAsync",
            "GrantAsync",
            "SatisfyPolicyAsync",
            "SatisfyApprovalAsync",
            "TransitionWorkflowAsync",
            "ContinueWorkflowAsync",
            "InvokeToolAsync",
            "DispatchAgentAsync",
            "CallModelAsync",
            "ReplayAsync",
            "RerunAsync",
            "PromoteMemoryAsync",
            "ActivateRetrievalAsync",
            "ApplySourceAsync",
            "ApplyPatchAsync",
            "CreateEventAsync",
            "AppendEventAsync",
            "UpdateTraceAsync",
            "DeleteTraceAsync"
        };

        var methodNames = typeof(IGovernanceTraceExplorerService).GetMethods().Select(method => method.Name).ToArray();
        foreach (var methodName in methodNames)
            Assert.IsFalse(forbidden.Contains(methodName), $"Forbidden method declared: {methodName}");
    }

    [TestMethod]
    public void GovernanceTraceExplorerModels_DoNotExposeForbiddenPayloadProperties()
    {
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PayloadJson",
            "RawPayload",
            "RawPrompt",
            "RawCompletion",
            "RawToolOutput",
            "RawCommandOutput",
            "StdOut",
            "StdErr",
            "PrivateReasoning",
            "HiddenReasoning",
            "ChainOfThought",
            "SourceContent",
            "SourceFileContents",
            "PatchPayload",
            "DiffPayload",
            "ApprovalToken",
            "ApprovalSecret",
            "Credential",
            "Secret",
            "ApiKey"
        };

        foreach (var property in TraceTypes().SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)))
            Assert.IsFalse(forbidden.Contains(property.Name), $"Forbidden payload property declared: {property.DeclaringType?.Name}.{property.Name}");
    }

    [TestMethod]
    public void GovernanceTraceExplorerProductionFiles_DoNotContainForbiddenImplementationMarkers()
    {
        var text = string.Join("\n", ProductionFiles().Where(path => !path.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase)).Select(File.ReadAllText));
        foreach (var token in new[]
        {
            "ProcessStartInfo",
            "Process.Start",
            "File.ReadAllText",
            "File.Write",
            "File.Delete",
            "Directory.Enumerate",
            "Directory.GetFiles",
            "ToolInvoker",
            "AgentDispatcher",
            "A2aSender",
            "OpenAI",
            "ChatCompletion",
            "SourceMutation",
            "PatchWriter",
            "DiffBuilder",
            "SourceWriter",
            "RollbackExecutor",
            "ValidationRunner",
            "TestRunner",
            "WorkflowTransitionWriter",
            "ApprovalDecisionWriter",
            "PolicyDecisionWriter",
            "ToolRequestWriter",
            "AppendGovernanceEvent",
            "CreateGovernanceEvent",
            "UpdateGovernanceEvent",
            "DeleteGovernanceEvent"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden implementation marker found: {token}");
        }
    }

    [TestMethod]
    public void GovernanceTraceExplorerProgram_RegistersExplorerOnly()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Program.cs"));
        StringAssert.Contains(program, "IGovernanceTraceExplorerService");
        StringAssert.Contains(program, "GovernanceTraceExplorerService");
    }

    private static IReadOnlyList<string> ProductionFiles()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "IronDev.Core", "Governance", "GovernanceTraceExplorerModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IGovernanceTraceExplorerService.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "GovernanceTraceExplorerService.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "GovernanceTraceExplorerController.cs"),
            Path.Combine(root, "IronDev.Api", "Program.cs")
        ];
    }

    private static IReadOnlyList<Type> TraceTypes() =>
    [
        typeof(GovernanceTraceQuery),
        typeof(GovernanceTraceSummary),
        typeof(GovernanceTraceDetail),
        typeof(GovernanceTraceTimelineItem),
        typeof(GovernanceTraceRelatedReference),
        typeof(GovernanceTraceListResponse),
        typeof(GovernanceTraceDetailResponse),
        typeof(GovernanceTraceExplorerIssue)
    ];

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
