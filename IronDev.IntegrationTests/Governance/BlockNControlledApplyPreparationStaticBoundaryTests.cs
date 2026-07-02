using System.Reflection;
using System.Text.RegularExpressions;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("StaticBoundary")]
[TestClass]
[TestCategory("BlockNControlledApplyPreparation")]
public sealed class BlockNControlledApplyPreparationStaticBoundaryTests
{
    [TestMethod]
    public void BlockNProductionFiles_Exist()
    {
        foreach (var path in BlockNProductionFiles())
            Assert.IsTrue(File.Exists(path), $"Missing Block N production file: {path}");
    }

    [TestMethod]
    public void BlockNReceiptFiles_Exist()
    {
        foreach (var fileName in new[]
        {
            "PR137_SOURCE_APPLY_APPROVAL_REQUIREMENT_CONTRACT.md",
            "PR138_PATCH_PROPOSAL_EVIDENCE_PACKAGE.md",
            "PR139_CONTROLLED_APPLY_PLAN_MODEL.md",
            "PR140_APPLY_DRY_RUN_STORE.md",
            "PR141_APPLY_PREVIEW_API.md",
            "PR142_APPLY_PREVIEW_CLI.md",
            "PR143_HUMAN_APPROVED_APPLY_BOUNDARY_TESTS.md",
            "PR144_BLOCK_N_CONTROLLED_APPLY_PREPARATION_RECEIPT.md"
        })
        {
            Assert.IsTrue(File.Exists(Path.Combine(RepositoryRoot(), "Docs", "receipts", fileName)), $"Missing receipt: {fileName}");
        }
    }

    [TestMethod]
    public void BlockNStatusEnums_DoNotExposeAuthorityOrExecutionStatuses()
    {
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Approved",
            "HumanApproved",
            "ApprovalGranted",
            "ApprovalSatisfied",
            "PolicySatisfied",
            "ReadyToApply",
            "ApplyReady",
            "SourceApplyReady",
            "PatchApplyReady",
            "DryRunExecuted",
            "SourceApplied",
            "PatchApplied",
            "ValidationPassed",
            "RollbackCompleted",
            "WorkflowContinued",
            "ApplyExecuted",
            "ExecutionComplete"
        };

        foreach (var enumType in new[]
        {
            typeof(SourceApplyApprovalRequirementStatus),
            typeof(PatchProposalEvidencePackageStatus),
            typeof(ControlledApplyPlanStatus),
            typeof(ApplyDryRunRecordStatus),
            typeof(ApplyDryRunOutcomeKind),
            typeof(ApplyPreviewStatus)
        })
        {
            foreach (var name in Enum.GetNames(enumType))
                Assert.IsFalse(forbidden.Contains(name), $"{enumType.Name}.{name} must not be an authority/execution status.");
        }
    }

    [TestMethod]
    public void BlockNApiCliClientFiles_DoNotExposeApplyApproveOrContinueRoutes()
    {
        var text = string.Join("\n", ApiCliClientFiles().Select(File.ReadAllText));
        foreach (var fragment in new[]
        {
            "approve-apply",
            "apply-approve",
            "apply-source",
            "patch-apply",
            "execute-apply",
            "run-apply",
            "dry-run-execute",
            "workflow-continue",
            "approval-satisfy",
            "policy-satisfy",
            "transition-workflow"
        })
        {
            Assert.IsFalse(text.Contains(fragment, StringComparison.OrdinalIgnoreCase), $"Forbidden API/CLI/client route fragment found: {fragment}");
        }

        StringAssert.Contains(text, "apply-preview");
    }

    [TestMethod]
    public void BlockNProductionFiles_DoNotDeclareForbiddenExecutionMethods()
    {
        var forbidden = new[]
        {
            "ApproveApply",
            "ApproveSourceApply",
            "GrantApplyApproval",
            "GrantApproval",
            "SatisfyApproval",
            "SatisfyPolicy",
            "ApplySource",
            "ApplyPatch",
            "ExecuteApply",
            "ExecuteDryRun",
            "RunDryRun",
            "RunValidation",
            "RunRollback",
            "ContinueWorkflow",
            "TransitionWorkflow",
            "MutateFiles",
            "ReadSourceFiles",
            "InvokeTool",
            "DispatchAgent",
            "CallModel",
            "BuildPrompt",
            "PromoteMemory",
            "ActivateRetrieval"
        };

        var methodNames = BlockNTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .Select(method => $"{method.DeclaringType?.Name}.{method.Name}");

        foreach (var methodName in methodNames)
        foreach (var token in forbidden)
            Assert.IsFalse(methodName.EndsWith($".{token}", StringComparison.Ordinal), $"Forbidden method declared: {methodName}");
    }

    [TestMethod]
    public void BlockNProductionFiles_DoNotDeclareForbiddenPayloadProperties()
    {
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ApprovalReceipt",
            "ApprovalResult",
            "AcceptedApprovalRecord",
            "ApprovedBy",
            "ApprovalGrantedAt",
            "ApprovalSatisfiedAt",
            "HumanApprovedBy",
            "HumanApprovalToken",
            "ApplyApprovalToken",
            "SourceApplyApprovalToken",
            "PatchPayload",
            "DiffPayload",
            "SourceContent",
            "SourceFileContents",
            "CommandPayload",
            "StdOut",
            "StdErr",
            "RawPrompt",
            "RawCompletion",
            "RawToolOutput",
            "PrivateReasoning",
            "HiddenReasoning",
            "ChainOfThought",
            "PatchText",
            "DiffText",
            "FileContents",
            "ExecutionCommand",
            "RollbackCommand"
        };

        var propertyNames = BlockNTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .Select(property => $"{property.DeclaringType?.Name}.{property.Name}");

        foreach (var propertyName in propertyNames)
            Assert.IsFalse(forbidden.Contains(propertyName.Split('.').Last()), $"Forbidden payload property declared: {propertyName}");
    }

    [TestMethod]
    public void BlockNCoreApiCliClientPreviewPaths_DoNotContainImplementationMarkers()
    {
        var text = string.Join("\n", BlockNProductionFiles().Select(File.ReadAllText));
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
            "PatchWriter",
            "DiffBuilder",
            "SourceWriter",
            "RollbackExecutor",
            "ValidationRunner",
            "TestRunner"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden implementation marker found: {token}");
        }
    }

    [TestMethod]
    public void ApplyPreviewApiController_IsGetOnly()
    {
        var controller = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "ApplyPreviewController.cs"));

        StringAssert.Contains(controller, "HttpGet");
        AssertDoesNotContainAny(controller, "HttpPost", "HttpPut", "HttpPatch", "HttpDelete");
    }

    [TestMethod]
    public void ApplyPreviewCli_ContainsPreviewCommandOnly()
    {
        var cli = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "IronDev.Cli", "CliApplyPreview.cs"));

        StringAssert.Contains(cli, "apply-preview");
        AssertDoesNotContainAny(
            cli,
            "workflow apply-source",
            "workflow patch-apply",
            "workflow approve-apply",
            "workflow continue-apply");
    }

    private static IReadOnlyList<string> BlockNProductionFiles()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "IronDev.Core", "Workflow", "SourceApplyApprovalRequirementContractModels.cs"),
            Path.Combine(root, "IronDev.Core", "Workflow", "PatchProposalEvidencePackageModels.cs"),
            Path.Combine(root, "IronDev.Core", "Workflow", "ControlledApplyPlanModels.cs"),
            Path.Combine(root, "IronDev.Core", "Workflow", "ApplyDryRunStoreModels.cs"),
            Path.Combine(root, "IronDev.Core", "Workflow", "ApplyPreviewModels.cs"),
            Path.Combine(root, "IronDev.Core", "Workflow", "IApplyPreviewService.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Workflow", "ApplyPreviewService.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Workflow", "SqlApplyDryRunStore.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "ApplyPreviewController.cs"),
            Path.Combine(root, "IronDev.Client", "IIronDevApiClient.cs"),
            Path.Combine(root, "IronDev.Client", "IronDevApiClient.cs"),
            Path.Combine(root, "tools", "IronDev.Cli", "CliApplyPreview.cs")
        ];
    }

    private static IReadOnlyList<string> ApiCliClientFiles()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "IronDev.Api", "Controllers", "ApplyPreviewController.cs"),
            Path.Combine(root, "IronDev.Client", "IIronDevApiClient.cs"),
            Path.Combine(root, "IronDev.Client", "IronDevApiClient.cs"),
            Path.Combine(root, "tools", "IronDev.Cli", "CliApplyPreview.cs")
        ];
    }

    private static IReadOnlyList<Type> BlockNTypes() =>
    [
        typeof(SourceApplyApprovalRequirementContract),
        typeof(SourceApplyApprovalRequirementRequest),
        typeof(SourceApplyApprovalRequirementResult),
        typeof(PatchProposalEvidencePackageWorkflow),
        typeof(PatchProposalEvidencePackageRequest),
        typeof(PatchProposalEvidencePackageResult),
        typeof(ControlledApplyPlanWorkflow),
        typeof(ControlledApplyPlanRequest),
        typeof(ControlledApplyPlanResult),
        typeof(ApplyDryRunCreateRequest),
        typeof(ApplyDryRunRecord),
        typeof(ApplyPreviewRequest),
        typeof(ApplyPreviewResponse),
        typeof(ApplyPreviewGate),
        typeof(ApplyPreviewRisk),
        typeof(ApplyPreviewMissingEvidence)
    ];

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker: {marker}");
    }

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
