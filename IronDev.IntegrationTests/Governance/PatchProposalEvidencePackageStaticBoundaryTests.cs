using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class PatchProposalEvidencePackageStaticBoundaryTests
{
    private static readonly string ProductionPath = Path.Combine(
        RepositoryRoot(),
        "IronDev.Core",
        "Workflow",
        "PatchProposalEvidencePackageModels.cs");

    [DataTestMethod]
    [DataRow("ProcessStartInfo")]
    [DataRow("HttpClient")]
    [DataRow("SqlConnection")]
    [DataRow("DbConnection")]
    [DataRow("File.ReadAllText")]
    [DataRow("File.Write")]
    [DataRow("File.Delete")]
    [DataRow("Directory.Enumerate")]
    [DataRow("Directory.GetFiles")]
    [DataRow("Directory.CreateDirectory")]
    [DataRow("IHostedService")]
    [DataRow("BackgroundService")]
    [DataRow("ControllerBase")]
    [DataRow("WebApplication")]
    [DataRow("OpenAI")]
    [DataRow("ChatCompletion")]
    [DataRow("ToolInvoker")]
    [DataRow("AgentDispatcher")]
    [DataRow("A2aSender")]
    [DataRow("WorkflowTransitionWriter")]
    [DataRow("ApprovalMutation")]
    [DataRow("ApprovalRepository")]
    [DataRow("PolicySatisfaction")]
    [DataRow("SourceMutation")]
    [DataRow("PatchApply")]
    [DataRow("PatchWriter")]
    [DataRow("DiffBuilder")]
    [DataRow("SourceWriter")]
    [DataRow("MemoryPromotion")]
    [DataRow("RetrievalActivation")]
    [DataRow("VectorStore")]
    [DataRow("Embedding")]
    [DataRow("GitHub")]
    [DataRow("CI")]
    public void ProductionFile_DoesNotContainRuntimePatchOrSourceMutationSurface(string marker)
    {
        var text = File.ReadAllText(ProductionPath);

        Assert.IsFalse(text.Contains(marker, StringComparison.Ordinal), $"Unexpected production marker: {marker}");
    }

    [TestMethod]
    public void Interface_ExposesOnlyPrepare()
    {
        var methods = typeof(IPatchProposalEvidencePackageWorkflow)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Prepare" }, methods);
    }

    [DataTestMethod]
    [DataRow("GeneratePatch")]
    [DataRow("BuildPatch")]
    [DataRow("CreatePatch")]
    [DataRow("ApplyPatch")]
    [DataRow("ApplySource")]
    [DataRow("MutateSource")]
    [DataRow("WriteFiles")]
    [DataRow("ReadSourceFiles")]
    [DataRow("Approve")]
    [DataRow("Reject")]
    [DataRow("GrantApproval")]
    [DataRow("SatisfyApproval")]
    [DataRow("SatisfyPolicy")]
    [DataRow("ContinueWorkflow")]
    [DataRow("TransitionWorkflow")]
    [DataRow("Run")]
    [DataRow("RunAsync")]
    [DataRow("Execute")]
    [DataRow("ExecuteAsync")]
    [DataRow("InvokeTool")]
    [DataRow("DispatchAgent")]
    [DataRow("CallModel")]
    [DataRow("BuildPrompt")]
    [DataRow("CreateTicket")]
    [DataRow("PromoteMemory")]
    [DataRow("ActivateRetrieval")]
    public void WorkflowClass_DoesNotExposeForbiddenMethods(string forbidden)
    {
        var methodNames = typeof(PatchProposalEvidencePackageWorkflow)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        Assert.IsFalse(methodNames.Contains(forbidden, StringComparer.Ordinal), $"Unexpected method: {forbidden}");
    }

    [DataTestMethod]
    [DataRow("PatchPayload")]
    [DataRow("DiffPayload")]
    [DataRow("SourceContent")]
    [DataRow("SourceFileContents")]
    [DataRow("CommandPayload")]
    [DataRow("RawPrompt")]
    [DataRow("RawCompletion")]
    [DataRow("RawToolOutput")]
    [DataRow("PrivateReasoning")]
    [DataRow("HiddenReasoning")]
    [DataRow("ChainOfThought")]
    [DataRow("ApprovalReceipt")]
    [DataRow("ApprovalResult")]
    [DataRow("AcceptedApprovalRecord")]
    [DataRow("ApprovedBy")]
    [DataRow("ApprovalGrantedAt")]
    [DataRow("PatchText")]
    [DataRow("DiffText")]
    [DataRow("FileContents")]
    public void RequestAndResultModels_DoNotExposeForbiddenPayloadProperties(string forbidden)
    {
        var propertyNames = new[]
            {
                typeof(PatchProposalEvidencePackageRequest),
                typeof(PatchProposalEvidencePackageResult)
            }
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(property => property.Name)
            .ToArray();

        Assert.IsFalse(propertyNames.Contains(forbidden, StringComparer.Ordinal), $"Unexpected property: {forbidden}");
    }

    [DataTestMethod]
    [DataRow("PatchGenerated")]
    [DataRow("PatchReady")]
    [DataRow("DiffReady")]
    [DataRow("SourceApplyReady")]
    [DataRow("SourceApplied")]
    [DataRow("PatchApplied")]
    [DataRow("ImplementationReady")]
    [DataRow("Approved")]
    [DataRow("ApprovalSatisfied")]
    [DataRow("PolicySatisfied")]
    [DataRow("WorkflowContinued")]
    public void StatusEnum_DoesNotContainForbiddenAuthorityStates(string forbidden)
    {
        var names = Enum.GetNames<PatchProposalEvidencePackageStatus>();

        Assert.IsFalse(names.Contains(forbidden, StringComparer.Ordinal), $"Unexpected status: {forbidden}");
    }

    [TestMethod]
    public void Receipt_ContainsRequiredBoundaryLanguage()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR138_PATCH_PROPOSAL_EVIDENCE_PACKAGE.md"));

        StringAssert.Contains(receipt, "PR138 adds a Patch Proposal Evidence Package.");
        StringAssert.Contains(receipt, "Patch proposal evidence package is not a patch.");
        StringAssert.Contains(receipt, "Patch proposal evidence package is not source apply.");
        StringAssert.Contains(receipt, "Implementation proposal is not implementation.");
        StringAssert.Contains(receipt, "Source apply approval requirement is not approval satisfaction.");
        StringAssert.Contains(receipt, "Human approval package is not approval.");
        StringAssert.Contains(receipt, "Affected file reference is not source access.");
        StringAssert.Contains(receipt, "Expected validation reference is not validation proof.");
        StringAssert.Contains(receipt, "Patch generation remains unimplemented.");
        StringAssert.Contains(receipt, "Source apply remains unimplemented.");
        StringAssert.Contains(receipt, "PR138 gathers the patch evidence folder. It does not write the patch.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        Assert.IsNotNull(directory, "Could not locate repository root.");
        return directory.FullName;
    }
}
