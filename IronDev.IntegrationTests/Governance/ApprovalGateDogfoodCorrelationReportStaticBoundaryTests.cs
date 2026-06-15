using System.Reflection;
using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ApprovalGateDogfoodCorrelationReport")]
public sealed class ApprovalGateDogfoodCorrelationReportStaticBoundaryTests
{
    [TestMethod]
    public void ApprovalGateDogfoodCorrelationReportProductionFiles_Exist()
    {
        foreach (var path in ProductionFiles())
            Assert.IsTrue(File.Exists(path), $"Missing approval/gate/dogfood correlation report production file: {path}");
    }

    [TestMethod]
    public void ApprovalGateDogfoodCorrelationReportReceipt_ExistsAndPreservesBoundaryLanguage()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR147_APPROVAL_GATE_DOGFOOD_CORRELATION_REPORT.md"));
        StringAssert.Contains(receipt, "PR147 adds a read-only Approval/Gate/Dogfood Correlation Report surface");
        StringAssert.Contains(receipt, "Correlation is not approval.");
        StringAssert.Contains(receipt, "Correlation is not policy satisfaction.");
        StringAssert.Contains(receipt, "Dogfood receipt evidence is not release approval.");
        StringAssert.Contains(receipt, "Tool gate evidence is not tool execution.");
        StringAssert.Contains(receipt, "does not sign them, reopen the gate, or ship the release");
    }

    [TestMethod]
    public void ApprovalGateDogfoodCorrelationReportController_IsGetOnly()
    {
        var methods = typeof(ApprovalGateDogfoodCorrelationReportController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToArray();

        Assert.IsTrue(methods.Any(method => method.GetCustomAttributes<HttpGetAttribute>().Any()), "Controller should expose GET routes.");
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes<HttpPostAttribute>().Any()), "Controller must not expose POST routes.");
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes<HttpPutAttribute>().Any()), "Controller must not expose PUT routes.");
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes<HttpPatchAttribute>().Any()), "Controller must not expose PATCH routes.");
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes<HttpDeleteAttribute>().Any()), "Controller must not expose DELETE routes.");
    }

    [TestMethod]
    public void ApprovalGateDogfoodCorrelationReportRoutes_DoNotContainForbiddenFragments()
    {
        var routeTexts = typeof(ApprovalGateDogfoodCorrelationReportController)
            .GetCustomAttributes<RouteAttribute>()
            .Select(attribute => attribute.Template ?? string.Empty)
            .Concat(typeof(ApprovalGateDogfoodCorrelationReportController)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetCustomAttributes<HttpGetAttribute>())
                .Select(attribute => attribute.Template ?? string.Empty))
            .ToArray();

        foreach (var fragment in new[]
        {
            "approve/",
            "reject",
            "satisfy",
            "open-gate",
            "invoke",
            "mark-dogfood",
            "release-approval",
            "transition",
            "dispatch",
            "model",
            "prompt",
            "ticket",
            "promote",
            "retrieval",
            "apply",
            "patch"
        })
        {
            Assert.IsFalse(routeTexts.Any(route => route.Contains(fragment, StringComparison.OrdinalIgnoreCase)), $"Forbidden route fragment found: {fragment}");
        }

        CollectionAssert.Contains(routeTexts, "api/v1/governance/correlation-reports");
        CollectionAssert.Contains(routeTexts, "approval-gate-dogfood");
    }

    [TestMethod]
    public void ApprovalGateDogfoodCorrelationReportService_DoesNotDeclareForbiddenMethods()
    {
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ApproveAsync",
            "RejectAsync",
            "SatisfyPolicyAsync",
            "OpenGateAsync",
            "InvokeToolAsync",
            "MarkDogfoodPassedAsync",
            "ApproveReleaseAsync",
            "TransitionWorkflowAsync",
            "DispatchAgentAsync",
            "CallModelAsync",
            "BuildPromptAsync",
            "CreateTicketAsync",
            "PromoteMemoryAsync",
            "ActivateRetrievalAsync",
            "ApplySourceAsync",
            "ApplyPatchAsync",
            "CreateEventAsync",
            "AppendEventAsync",
            "UpdateEventAsync",
            "DeleteEventAsync"
        };

        foreach (var methodName in typeof(IApprovalGateDogfoodCorrelationReportService).GetMethods().Select(method => method.Name))
            Assert.IsFalse(forbidden.Contains(methodName), $"Forbidden correlation report service method declared: {methodName}");
    }

    [TestMethod]
    public void ApprovalGateDogfoodCorrelationReportProductionFiles_DoNotContainForbiddenImplementationMarkers()
    {
        var text = string.Join("\n", ProductionFiles()
            .Where(path => !path.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText));

        foreach (var token in new[]
        {
            "ProcessStartInfo",
            "Process.Start",
            "File.ReadAllText",
            "File.Write",
            "File.Delete",
            "Directory.Enumerate",
            "Directory.GetFiles",
            "HttpClient",
            "OpenAI",
            "ChatCompletion",
            "ILLMService",
            "IWorkflowRunStore",
            "IWorkflowStepStore",
            "IWorkflowCheckpointStore",
            "IApprovalDecisionStore",
            "IPolicyDecisionEventStore",
            "IToolRequestStore",
            "IToolGateDecisionStore",
            "IDogfoodReceiptStore",
            "IAgentHandoffStore",
            "IApplyDryRunStore",
            "AppendGovernanceEvent",
            "CreateGovernanceEvent",
            "UpdateGovernanceEvent",
            "DeleteGovernanceEvent",
            "SqlConnection",
            "ExecuteAsync"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden correlation report implementation marker found: {token}");
        }
    }

    [TestMethod]
    public void ApprovalGateDogfoodCorrelationReportProgram_RegistersReportServiceOnly()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Program.cs"));
        StringAssert.Contains(program, "IApprovalGateDogfoodCorrelationReportService");
        StringAssert.Contains(program, "ApprovalGateDogfoodCorrelationReportService");
    }

    [TestMethod]
    public void ApprovalGateDogfoodCorrelationReportModels_DoNotExposeForbiddenPayloadProperties()
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

        foreach (var property in ReportTypes().SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)))
            Assert.IsFalse(forbidden.Contains(property.Name), $"Forbidden correlation report payload property declared: {property.DeclaringType?.Name}.{property.Name}");
    }

    private static IReadOnlyList<string> ProductionFiles()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "IronDev.Core", "Governance", "ApprovalGateDogfoodCorrelationReportModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IApprovalGateDogfoodCorrelationReportService.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "ApprovalGateDogfoodCorrelationReportService.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "ApprovalGateDogfoodCorrelationReportController.cs"),
            Path.Combine(root, "IronDev.Api", "Program.cs")
        ];
    }

    private static IReadOnlyList<Type> ReportTypes() =>
    [
        typeof(ApprovalGateDogfoodCorrelationReportRequest),
        typeof(ApprovalGateDogfoodCorrelationReportResponse),
        typeof(ApprovalGateDogfoodCorrelationReport),
        typeof(ApprovalCorrelationEvidence),
        typeof(ToolGateCorrelationEvidence),
        typeof(DogfoodCorrelationEvidence),
        typeof(GovernanceCorrelationTraceReference),
        typeof(GovernanceCorrelationMissingEvidence),
        typeof(GovernanceCorrelationConflictSignal),
        typeof(GovernanceCorrelationRecommendation),
        typeof(ApprovalGateDogfoodCorrelationReportIssue)
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
