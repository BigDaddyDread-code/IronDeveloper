using System.Reflection;
using IronDev.Api.Controllers;
using IronDev.Core.Workflow;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("StaticBoundary")]
[TestClass]
[TestCategory("FailedWorkflowDiagnosisReport")]
public sealed class FailedWorkflowDiagnosisReportStaticBoundaryTests
{
    [TestMethod]
    public void FailedWorkflowDiagnosisReportProductionFiles_Exist()
    {
        foreach (var path in ProductionFiles())
            Assert.IsTrue(File.Exists(path), $"Missing failed workflow diagnosis report production file: {path}");
    }

    [TestMethod]
    public void FailedWorkflowDiagnosisReportReceipt_ExistsAndPreservesBoundaryLanguage()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR146_FAILED_WORKFLOW_DIAGNOSIS_REPORT.md"));
        StringAssert.Contains(receipt, "PR146 adds a read-only failed workflow diagnosis report surface.");
        StringAssert.Contains(receipt, "The Failed Workflow Diagnosis Report is read-only.");
        StringAssert.Contains(receipt, "does not restart the engine");
        StringAssert.Contains(receipt, "does not invoke tools");
        StringAssert.Contains(receipt, "does not apply source");
        StringAssert.Contains(receipt, "does not promote memory");
    }

    [TestMethod]
    public void FailedWorkflowDiagnosisReportController_IsGetOnly()
    {
        var methods = typeof(FailedWorkflowDiagnosisReportController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToArray();

        Assert.IsTrue(methods.Any(method => method.GetCustomAttributes<HttpGetAttribute>().Any()), "Controller should expose GET routes.");
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes<HttpPostAttribute>().Any()), "Controller must not expose POST routes.");
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes<HttpPutAttribute>().Any()), "Controller must not expose PUT routes.");
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes<HttpPatchAttribute>().Any()), "Controller must not expose PATCH routes.");
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes<HttpDeleteAttribute>().Any()), "Controller must not expose DELETE routes.");
    }

    [TestMethod]
    public void FailedWorkflowDiagnosisReportRoutes_DoNotContainForbiddenFragments()
    {
        var routeTexts = typeof(FailedWorkflowDiagnosisReportController)
            .GetCustomAttributes<RouteAttribute>()
            .Select(attribute => attribute.Template ?? string.Empty)
            .Concat(typeof(FailedWorkflowDiagnosisReportController)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetCustomAttributes<HttpGetAttribute>())
                .Select(attribute => attribute.Template ?? string.Empty))
            .ToArray();

        foreach (var fragment in new[]
        {
            "repair",
            "fix",
            "retry",
            "rerun",
            "resume",
            "continue",
            "transition",
            "approve",
            "reject",
            "satisfy",
            "invoke",
            "dispatch",
            "execute",
            "run-tool",
            "create-ticket",
            "promote-memory",
            "apply",
            "patch"
        })
        {
            Assert.IsFalse(routeTexts.Any(route => route.Contains(fragment, StringComparison.OrdinalIgnoreCase)), $"Forbidden route fragment found: {fragment}");
        }

        CollectionAssert.Contains(routeTexts, "api/v1/workflow/failures");
        CollectionAssert.Contains(routeTexts, "{workflowRunId}/diagnosis-report");
    }

    [TestMethod]
    public void FailedWorkflowDiagnosisReportService_DoesNotDeclareForbiddenMethods()
    {
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RepairAsync",
            "FixAsync",
            "RetryAsync",
            "RerunAsync",
            "ResumeAsync",
            "ContinueAsync",
            "TransitionAsync",
            "CreateTicketAsync",
            "ApproveAsync",
            "RejectAsync",
            "SatisfyPolicyAsync",
            "SatisfyApprovalAsync",
            "InvokeToolAsync",
            "DispatchAgentAsync",
            "CallModelAsync",
            "PromoteMemoryAsync",
            "ActivateRetrievalAsync",
            "ApplySourceAsync",
            "ApplyPatchAsync",
            "CreateEventAsync",
            "AppendEventAsync",
            "UpdateEventAsync",
            "DeleteEventAsync"
        };

        foreach (var methodName in typeof(IFailedWorkflowDiagnosisReportService).GetMethods().Select(method => method.Name))
            Assert.IsFalse(forbidden.Contains(methodName), $"Forbidden diagnosis service method declared: {methodName}");
    }

    [TestMethod]
    public void FailedWorkflowDiagnosisReportProductionFiles_DoNotContainForbiddenImplementationMarkers()
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
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden diagnosis implementation marker found: {token}");
        }
    }

    [TestMethod]
    public void FailedWorkflowDiagnosisReportProgram_RegistersReportServiceOnly()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Program.cs"));
        StringAssert.Contains(program, "IFailedWorkflowDiagnosisReportService");
        StringAssert.Contains(program, "FailedWorkflowDiagnosisReportService");
    }

    [TestMethod]
    public void FailedWorkflowDiagnosisReportModels_DoNotExposeForbiddenPayloadProperties()
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

        foreach (var property in DiagnosisTypes().SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)))
            Assert.IsFalse(forbidden.Contains(property.Name), $"Forbidden diagnosis payload property declared: {property.DeclaringType?.Name}.{property.Name}");
    }

    private static IReadOnlyList<string> ProductionFiles()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "IronDev.Core", "Workflow", "FailedWorkflowDiagnosisReportModels.cs"),
            Path.Combine(root, "IronDev.Core", "Workflow", "IFailedWorkflowDiagnosisReportService.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Workflow", "FailedWorkflowDiagnosisReportService.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "FailedWorkflowDiagnosisReportController.cs"),
            Path.Combine(root, "IronDev.Api", "Program.cs")
        ];
    }

    private static IReadOnlyList<Type> DiagnosisTypes() =>
    [
        typeof(FailedWorkflowDiagnosisReportRequest),
        typeof(FailedWorkflowDiagnosisReportResponse),
        typeof(FailedWorkflowDiagnosisReport),
        typeof(FailedWorkflowDiagnosisSignal),
        typeof(FailedWorkflowDiagnosisHypothesis),
        typeof(FailedWorkflowDiagnosisMissingEvidence),
        typeof(FailedWorkflowDiagnosisTraceItem),
        typeof(FailedWorkflowDiagnosisRecommendation),
        typeof(FailedWorkflowDiagnosisReportIssue)
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
