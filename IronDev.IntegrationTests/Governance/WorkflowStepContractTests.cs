using System.Reflection;
using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowStepContract")]
public sealed class WorkflowStepContractTests
{
    private readonly WorkflowStepContractValidator _validator = new();

    [TestMethod]
    public void WorkflowStepContract_ValidReviewContractPasses()
    {
        var contract = ValidContract();

        var result = _validator.Validate(contract);

        AssertValid(result);
        AssertAllFalse(
            contract.Boundary.AllowsExecution,
            contract.Boundary.AllowsAgentDispatch,
            contract.Boundary.AllowsToolInvocation,
            contract.Boundary.AllowsSourceMutation,
            contract.Boundary.AllowsApprovalMutation,
            contract.Boundary.AllowsMemoryPromotion,
            contract.Boundary.AllowsRetrievalActivation,
            contract.Boundary.AllowsWorkflowContinuation);
    }

    [TestMethod]
    public void WorkflowStepContract_MissingRequiredFieldsFail()
    {
        var contract = ValidContract() with
        {
            StepContractId = " ",
            WorkflowRunId = "",
            AllowedTransitions = [],
            EvidenceRequirements = []
        };

        var result = _validator.Validate(contract);

        AssertInvalid(result, "WORKFLOW_STEP_CONTRACT_STEP_ID_REQUIRED");
        AssertInvalid(result, "WORKFLOW_STEP_CONTRACT_RUN_ID_REQUIRED");
        AssertInvalid(result, "WORKFLOW_STEP_CONTRACT_TRANSITION_REQUIRED");
        AssertInvalid(result, "WORKFLOW_STEP_CONTRACT_EVIDENCE_REQUIRED");
    }

    [TestMethod]
    public void WorkflowStepContract_MissingThoughtLedgerReferenceFails()
    {
        var result = _validator.Validate(ValidContract() with { ThoughtLedgerReference = null });

        AssertInvalid(result, "WORKFLOW_STEP_CONTRACT_THOUGHT_LEDGER_REFERENCE_REQUIRED");
    }

    [TestMethod]
    public void WorkflowStepContract_BlankThoughtLedgerEntryIdFails()
    {
        var result = _validator.Validate(ValidContract() with
        {
            ThoughtLedgerReference = ValidThoughtLedgerReference() with { ThoughtLedgerEntryId = " " }
        });

        AssertInvalid(result, "WORKFLOW_STEP_CONTRACT_THOUGHT_LEDGER_ENTRY_ID_REQUIRED");
    }

    [TestMethod]
    public void WorkflowStepContract_ThoughtLedgerReferenceWithSafeSummaryPasses()
    {
        var result = _validator.Validate(ValidContract() with
        {
            ThoughtLedgerReference = ValidThoughtLedgerReference() with
            {
                SafeSummary = "Safe public trace marker for the workflow step."
            }
        });

        AssertValid(result);
    }

    [TestMethod]
    public void WorkflowStepContract_InvalidEnumsFail()
    {
        var contract = ValidContract() with
        {
            Intent = (WorkflowStepContractIntent)999,
            ExpectedActorKind = WorkflowStepContractActorKind.Unknown,
            InputReference = ValidContract().InputReference with { Kind = (WorkflowStepContractReferenceKind)999 },
            AllowedTransitions = [ValidContract().AllowedTransitions[0] with { Kind = WorkflowStepContractTransitionKind.Unknown }],
            EvidenceRequirements = [ValidContract().EvidenceRequirements[0] with { Kind = (WorkflowStepContractEvidenceRequirementKind)999 }]
        };

        var result = _validator.Validate(contract);

        AssertInvalid(result, "WORKFLOW_STEP_CONTRACT_INTENT_INVALID");
        AssertInvalid(result, "WORKFLOW_STEP_CONTRACT_ACTOR_KIND_INVALID");
        AssertInvalid(result, "WORKFLOW_STEP_CONTRACT_REFERENCE_KIND_INVALID");
        AssertInvalid(result, "WORKFLOW_STEP_CONTRACT_TRANSITION_KIND_INVALID");
        AssertInvalid(result, "WORKFLOW_STEP_CONTRACT_EVIDENCE_KIND_INVALID");
    }

    [TestMethod]
    public void WorkflowStepContract_ExecutionLikeTextIsRejected()
    {
        var contract = ValidContract() with
        {
            SafeSummary = "workflow continued after tool executed",
            AllowedTransitions =
            [
                ValidContract().AllowedTransitions[0] with
                {
                    SafeLabel = "execution succeeded"
                }
            ]
        };

        var result = _validator.Validate(contract);

        AssertInvalid(result, "WORKFLOW_STEP_CONTRACT_TEXT_UNSAFE");
    }

    [TestMethod]
    public void WorkflowStepContract_RawPrivateMarkersAreRejected()
    {
        var contract = ValidContract() with
        {
            InputReference = ValidContract().InputReference with
            {
                SafeSummary = "rawPrompt and chainOfThought are not allowed"
            },
            ThoughtLedgerReference = ValidThoughtLedgerReference() with
            {
                SafeSummary = "rawToolOutput and entirePatch are not allowed"
            },
            EvidenceRequirements =
            [
                ValidContract().EvidenceRequirements[0] with
                {
                    SafeSummary = "rawToolOutput leak"
                }
            ]
        };

        var result = _validator.Validate(contract);

        AssertInvalid(result, "WORKFLOW_STEP_CONTRACT_TEXT_UNSAFE");
    }

    [TestMethod]
    public void WorkflowStepContract_BoundaryAuthorityFlagsFail()
    {
        var contract = ValidContract() with
        {
            Boundary = new WorkflowStepContractBoundary
            {
                AllowsExecution = true,
                AllowsAgentDispatch = true,
                AllowsToolInvocation = true,
                AllowsSourceMutation = true,
                AllowsApprovalMutation = true,
                AllowsMemoryPromotion = true,
                AllowsRetrievalActivation = true,
                AllowsWorkflowContinuation = true
            }
        };

        var result = _validator.Validate(contract);

        AssertInvalid(result, "WORKFLOW_STEP_CONTRACT_BOUNDARY_AUTHORITY_BLOCKED");
    }

    [TestMethod]
    public void WorkflowStepContract_ReferenceAuthorityFlagsFail()
    {
        var contract = ValidContract() with
        {
            ExpectedOutputReference = ValidContract().ExpectedOutputReference with
            {
                HydratesContent = true,
                ActivatesRetrieval = true,
                GrantsApproval = true,
                AllowsExecution = true,
                MutatesSource = true,
                PromotesMemory = true
            }
        };

        var result = _validator.Validate(contract);

        AssertInvalid(result, "WORKFLOW_STEP_CONTRACT_BOUNDARY_AUTHORITY_BLOCKED");
    }

    [TestMethod]
    public void WorkflowStepContract_TransitionAuthorityFlagsFail()
    {
        var contract = ValidContract() with
        {
            AllowedTransitions =
            [
                ValidContract().AllowedTransitions[0] with
                {
                    StartsWorkflow = true,
                    ContinuesWorkflow = true,
                    DispatchesAgent = true,
                    InvokesTool = true,
                    IndicatesExecutionSuccess = true
                }
            ]
        };

        var result = _validator.Validate(contract);

        AssertInvalid(result, "WORKFLOW_STEP_CONTRACT_BOUNDARY_AUTHORITY_BLOCKED");
    }

    [TestMethod]
    public void WorkflowStepContract_EvidenceAuthorityFlagsFail()
    {
        var contract = ValidContract() with
        {
            EvidenceRequirements =
            [
                ValidContract().EvidenceRequirements[0] with
                {
                    IsApproval = true,
                    SatisfiesPolicy = true,
                    AllowsExecution = true,
                    PromotesMemory = true,
                    RequiresHydratedContent = true
                }
            ]
        };

        var result = _validator.Validate(contract);

        AssertInvalid(result, "WORKFLOW_STEP_CONTRACT_BOUNDARY_AUTHORITY_BLOCKED");
    }

    [TestMethod]
    public void WorkflowStepContract_MemoryProposalReferenceIsReviewMaterialOnly()
    {
        var contract = ValidContract() with
        {
            InputReference = new WorkflowStepContractReference
            {
                Kind = WorkflowStepContractReferenceKind.MemoryProposalRecord,
                ReferenceId = "memory-proposal-001",
                SafeSummary = "Memory proposal reference is review material only."
            },
            EvidenceRequirements =
            [
                new WorkflowStepContractEvidenceRequirement
                {
                    Kind = WorkflowStepContractEvidenceRequirementKind.ReviewMaterialReference,
                    RequirementId = "review-material-001",
                    SafeSummary = "Review material is required before any governed memory decision."
                }
            ]
        };

        AssertValid(_validator.Validate(contract));
    }

    [TestMethod]
    public void WorkflowStepContract_ApprovalPolicyReferenceIsRequirementOnly()
    {
        var contract = ValidContract() with
        {
            InputReference = new WorkflowStepContractReference
            {
                Kind = WorkflowStepContractReferenceKind.ApprovalPolicyRecord,
                ReferenceId = "approval-policy-001",
                SafeSummary = "Approval policy reference records the review requirement only."
            },
            EvidenceRequirements =
            [
                new WorkflowStepContractEvidenceRequirement
                {
                    Kind = WorkflowStepContractEvidenceRequirementKind.ApprovalPolicyReference,
                    RequirementId = "approval-policy-ref-001",
                    SafeSummary = "Policy reference is evidence only."
                }
            ]
        };

        AssertValid(_validator.Validate(contract));
    }

    [TestMethod]
    public void WorkflowStepContract_NormalizeTrimsText()
    {
        var normalized = _validator.Normalize(ValidContract() with
        {
            StepContractId = " step-contract-001 ",
            WorkflowRunId = " workflow-run-001 ",
            SafeSummary = " review step contract ",
            InputReference = ValidContract().InputReference with { ReferenceId = " input-001 ", SafeSummary = " input evidence " },
            ThoughtLedgerReference = ValidThoughtLedgerReference() with { ThoughtLedgerEntryId = " thought-ledger-entry-001 " }
        });

        Assert.AreEqual("step-contract-001", normalized.StepContractId);
        Assert.AreEqual("workflow-run-001", normalized.WorkflowRunId);
        Assert.AreEqual("review step contract", normalized.SafeSummary);
        Assert.AreEqual("input-001", normalized.InputReference.ReferenceId);
        Assert.AreEqual("input evidence", normalized.InputReference.SafeSummary);
        Assert.AreEqual("thought-ledger-entry-001", normalized.ThoughtLedgerReference!.ThoughtLedgerEntryId);
    }

    [TestMethod]
    public void WorkflowStepContract_SerializesRoundTrip()
    {
        var contract = ValidContract();

        var json = JsonSerializer.Serialize(contract);
        var roundTrip = JsonSerializer.Deserialize<WorkflowStepContract>(json);

        Assert.IsNotNull(roundTrip);
        Assert.AreEqual(contract.StepContractId, roundTrip!.StepContractId);
        Assert.AreEqual(contract.Intent, roundTrip.Intent);
        Assert.AreEqual(contract.InputReference.ReferenceId, roundTrip.InputReference.ReferenceId);
        Assert.AreEqual(contract.AllowedTransitions[0].Kind, roundTrip.AllowedTransitions[0].Kind);
        Assert.AreEqual(contract.EvidenceRequirements[0].Kind, roundTrip.EvidenceRequirements[0].Kind);
        Assert.AreEqual(contract.ThoughtLedgerReference!.ThoughtLedgerEntryId, roundTrip.ThoughtLedgerReference!.ThoughtLedgerEntryId);
    }

    [TestMethod]
    public void WorkflowStepContractValidator_HasNoRuntimeDependencies()
    {
        var constructors = typeof(WorkflowStepContractValidator).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.IsTrue(constructors.Length == 1, $"Expected one public constructor. Actual: {constructors.Length}");
        Assert.IsFalse(constructors[0].GetParameters().Any(), "Validator constructor should not require runtime services.");
        Assert.IsFalse(typeof(WorkflowStepContractValidator).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Any(), "Validator should not hold runtime service fields.");
    }

    [TestMethod]
    public void WorkflowStepContractModels_DoNotExposeRawPersistenceFields()
    {
        var propertyNames = new[]
        {
            typeof(WorkflowStepContract),
            typeof(WorkflowStepContractReference),
            typeof(WorkflowStepContractEvidenceRequirement),
            typeof(WorkflowStepContractTransitionRule),
            typeof(WorkflowStepContractBoundary),
            typeof(WorkflowStepThoughtLedgerReference)
        }
        .SelectMany(type => type.GetProperties())
        .Select(property => property.Name);

        AssertDoesNotContainAny(propertyNames, "RawPrompt", "RawCompletion", "RawToolOutput", "ChainOfThought", "Scratchpad", "EntirePatch", "PatchPayload");
    }

    [TestMethod]
    public void WorkflowStepContract_ProductionFileDoesNotReferenceRuntimeServices()
    {
        var text = ReadRepoFile("IronDev.Core/Workflow/WorkflowStepContractModels.cs");

        AssertDoesNotContainAny(
            text,
            "IWorkflowRunner",
            "WorkflowRunner",
            "IWorkflowDispatcher",
            "WorkflowDispatcher",
            "IAgentDispatcher",
            "AgentMessageBus",
            "IToolRouter",
            "ProcessStartInfo",
            "BackgroundService",
            "IHostedService",
            "SqlConnection",
            "HttpClient",
            "Weaviate",
            "LangGraph");
    }

    [TestMethod]
    public void WorkflowStepContract_IsNotReferencedByApiOrCli()
    {
        var root = RepositoryRoot();
        var apiText = ReadAllTextIfDirectoryExists(Path.Combine(root, "IronDev.Api"));
        var cliText = ReadAllTextIfDirectoryExists(Path.Combine(root, "tools", "IronDev.Cli")) +
                      ReadAllTextIfDirectoryExists(Path.Combine(root, "IronDev.Cli"));

        AssertDoesNotContainAny(apiText, "WorkflowStepContract");
        AssertDoesNotContainAny(cliText, "WorkflowStepContract");
    }

    [TestMethod]
    public void WorkflowStepContractReceipt_RecordsNonExecutionBoundary()
    {
        var receipt = ReadRepoFile("Docs/receipts/PR117_TYPED_WORKFLOW_STEP_CONTRACT_RECEIPT.md");

        var normalized = receipt.ToLowerInvariant();

        StringAssert.Contains(normalized, "typed workflow step contract");
        StringAssert.Contains(normalized, "not workflow runtime");
        StringAssert.Contains(normalized, "not execution permission");
        StringAssert.Contains(normalized, "memory proposal artifacts remain review material only");
        StringAssert.Contains(normalized, "approval policy references remain requirements only");
    }

    private static WorkflowStepContract ValidContract() =>
        new()
        {
            StepContractId = "step-contract-001",
            WorkflowRunId = "workflow-run-001",
            Intent = WorkflowStepContractIntent.PrepareReviewMaterial,
            InputReference = new WorkflowStepContractReference
            {
                Kind = WorkflowStepContractReferenceKind.WorkflowStepRecord,
                ReferenceId = "workflow-step-001",
                SafeSummary = "Prior workflow step fact is referenced for review context."
            },
            ExpectedOutputReference = new WorkflowStepContractReference
            {
                Kind = WorkflowStepContractReferenceKind.ReviewMaterial,
                ReferenceId = "review-material-001",
                SafeSummary = "Expected output is review material only."
            },
            ExpectedActorKind = WorkflowStepContractActorKind.AgentExpected,
            AllowedTransitions =
            [
                new WorkflowStepContractTransitionRule
                {
                    Kind = WorkflowStepContractTransitionKind.DraftToReadyForReview,
                    SafeLabel = "Draft review material may become ready for human review."
                }
            ],
            EvidenceRequirements =
            [
                new WorkflowStepContractEvidenceRequirement
                {
                    Kind = WorkflowStepContractEvidenceRequirementKind.GovernanceEventReference,
                    RequirementId = "governance-event-001",
                    SafeSummary = "Governance event reference is required as evidence."
                }
            ],
            ThoughtLedgerReference = ValidThoughtLedgerReference(),
            Boundary = new WorkflowStepContractBoundary(),
            SafeSummary = "Typed step contract records intent, references, actor expectation, transitions, and evidence."
        };

    internal static WorkflowStepThoughtLedgerReference ValidThoughtLedgerReference() =>
        new()
        {
            ThoughtLedgerEntryId = "thought-ledger-entry-001",
            TraceId = "trace-001",
            GovernanceEventId = "governance-event-001",
            CorrelationId = "correlation-001",
            SafeSummary = "ThoughtLedger reference is traceability only."
        };

    private static void AssertValid(WorkflowRunValidationResult result) =>
        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues.Select(issue => issue.Code + ":" + issue.Message)));

    private static void AssertInvalid(WorkflowRunValidationResult result, string expectedCode)
    {
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => string.Equals(issue.Code, expectedCode, StringComparison.Ordinal)), $"Expected issue code {expectedCode}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertDoesNotContainAny(IEnumerable<string> values, params string[] forbidden) =>
        AssertDoesNotContainAny(string.Join("\n", values), forbidden);

    private static void AssertDoesNotContainAny(string text, params string[] forbidden)
    {
        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found: {token}");
    }

    private static void AssertAllFalse(params bool[] values)
    {
        foreach (var value in values)
            Assert.IsFalse(value);
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string ReadAllTextIfDirectoryExists(string directory)
    {
        if (!Directory.Exists(directory))
            return string.Empty;

        return string.Join(
            "\n",
            Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
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
