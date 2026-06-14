using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowA2aHandoff")]
public sealed class WorkflowA2aHandoffValidatorTests
{
    private readonly WorkflowA2aHandoffValidator _validator = new();

    [TestMethod]
    public void WorkflowA2aHandoff_MissingRequestReturnsInvalidRequest()
    {
        var result = _validator.Validate(null);

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.InvalidRequest, result.Status);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_MissingStepContractReturnsInvalidRequest()
    {
        var result = _validator.Validate(ValidRequest() with { StepContract = null });

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.InvalidStepContract);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_InvalidStepContractReturnsInvalidStepContract()
    {
        var result = _validator.Validate(ValidRequest() with { StepContract = ValidStep() with { ThoughtLedgerReference = null } });

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.InvalidStepContract, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.MissingThoughtLedgerReference);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_MissingHandoffReferenceReturnsInvalidHandoffReference()
    {
        var result = _validator.Validate(ValidRequest() with { HandoffReference = null });

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.InvalidHandoffReference, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.MissingHandoffReference);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_MissingReferenceIdFails()
    {
        var result = _validator.Validate(ValidRequest() with { HandoffReference = ValidHandoff() with { HandoffReferenceId = " " } });

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.InvalidHandoffReference, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.MissingHandoffReferenceId);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_UnsafeHandoffReferenceIdFailsClosedWithoutSerializingUnsafeMarker()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            HandoffReference = ValidHandoff() with { HandoffReferenceId = "raw prompt handoff reference" }
        });
        var serialized = JsonSerializer.Serialize(result);

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.InvalidHandoffReference, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.InvalidThoughtLedgerReference);
        AssertDoesNotContainAny(serialized, "raw prompt", "rawPrompt");
    }

    [TestMethod]
    public void WorkflowA2aHandoff_WorkflowRunMismatchFails()
    {
        var result = _validator.Validate(ValidRequest() with { HandoffReference = ValidHandoff() with { WorkflowRunId = "other-run" } });

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.InvalidHandoffReference, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.WorkflowRunMismatch);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_WorkflowStepMismatchFails()
    {
        var result = _validator.Validate(ValidRequest() with { HandoffReference = ValidHandoff() with { WorkflowStepId = "other-step" } });

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.InvalidHandoffReference, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.WorkflowStepMismatch);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_MissingSenderFails()
    {
        var result = _validator.Validate(ValidRequest() with { HandoffReference = ValidHandoff() with { Sender = null } });

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.InvalidHandoffReference, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.MissingSender);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_MissingReceiverFails()
    {
        var result = _validator.Validate(ValidRequest() with { HandoffReference = ValidHandoff() with { Receiver = null } });

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.InvalidHandoffReference, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.MissingReceiver);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_UnknownSenderKindFails()
    {
        var result = _validator.Validate(ValidRequest() with { HandoffReference = ValidHandoff() with { Sender = Participant(WorkflowA2aParticipantKind.Unknown, "sender") } });

        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.UnknownParticipantKind);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_UnknownReceiverKindFails()
    {
        var result = _validator.Validate(ValidRequest() with { HandoffReference = ValidHandoff() with { Receiver = Participant(WorkflowA2aParticipantKind.Unknown, "receiver") } });

        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.UnknownParticipantKind);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_MissingSenderReferenceIdFails()
    {
        var result = _validator.Validate(ValidRequest() with { HandoffReference = ValidHandoff() with { Sender = Participant(WorkflowA2aParticipantKind.Agent, " ") } });

        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.MissingParticipantReference);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_MissingReceiverReferenceIdFails()
    {
        var result = _validator.Validate(ValidRequest() with { HandoffReference = ValidHandoff() with { Receiver = Participant(WorkflowA2aParticipantKind.Agent, " ") } });

        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.MissingParticipantReference);
    }

    [DataTestMethod]
    [DataRow("private reasoning")]
    [DataRow("hidden reasoning")]
    [DataRow("raw prompt")]
    [DataRow("raw completion")]
    [DataRow("raw tool output")]
    [DataRow("whole patch")]
    [DataRow("approval granted")]
    [DataRow("policy satisfied")]
    [DataRow("receiver may act")]
    public void WorkflowA2aHandoff_UnsafeTextFails(string marker)
    {
        var result = _validator.Validate(ValidRequest() with
        {
            HandoffReference = ValidHandoff() with
            {
                SafeSummary = $"Unsafe {marker} payload."
            }
        });

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.InvalidHandoffReference, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.InvalidThoughtLedgerReference);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_MissingHandoffThoughtLedgerReferenceFails()
    {
        var result = _validator.Validate(ValidRequest() with { HandoffReference = ValidHandoff() with { ThoughtLedgerReference = null } });

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.InvalidHandoffReference, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.MissingThoughtLedgerReference);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_ThoughtLedgerMismatchFails()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            HandoffReference = ValidHandoff() with
            {
                ThoughtLedgerReference = ValidThoughtLedgerReference() with { ThoughtLedgerEntryId = "other-ledger" }
            }
        });

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.InvalidHandoffReference, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.InvalidThoughtLedgerReference);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_MissingGovernanceEvidenceBlocksValidation()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            AvailableEvidence = ValidEvidence().Where(evidence => evidence.Kind != WorkflowA2aHandoffEvidenceKind.GovernanceEventReference).ToArray()
        });

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.MissingGovernanceEvidence);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_MissingGovernanceEventIdDoesNotSynthesizePlaceholderEvidence()
    {
        var step = ValidStep() with
        {
            ThoughtLedgerReference = ValidThoughtLedgerReference() with { GovernanceEventId = null }
        };
        var handoff = ValidHandoff() with
        {
            ThoughtLedgerReference = ValidThoughtLedgerReference() with { GovernanceEventId = null }
        };

        var result = _validator.Validate(ValidRequest() with
        {
            StepContract = step,
            HandoffReference = handoff,
            AvailableEvidence =
            [
                Evidence(WorkflowA2aHandoffEvidenceKind.GovernanceEventReference, "governance-event-required"),
                Evidence(WorkflowA2aHandoffEvidenceKind.HandoffContractReference, "handoff-reference-001"),
                Evidence(WorkflowA2aHandoffEvidenceKind.HandoffValidationReference, "handoff-reference-001"),
                Evidence(WorkflowA2aHandoffEvidenceKind.PolicyPreflightReference, "policy-preflight-001")
            ]
        });
        var serialized = JsonSerializer.Serialize(result);

        Assert.AreNotEqual(WorkflowA2aHandoffValidationStatus.ValidForFutureHandoff, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.MissingGovernanceEvidence);
        AssertDoesNotContainAny(serialized, "governance-event-required");
    }

    [TestMethod]
    public void WorkflowA2aHandoff_MissingHandoffContractEvidenceBlocksValidation()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            AvailableEvidence = ValidEvidence().Where(evidence => evidence.Kind != WorkflowA2aHandoffEvidenceKind.HandoffContractReference).ToArray()
        });

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.MissingHandoffContractEvidence);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_MissingPolicyPreflightEvidenceBlocksSensitiveValidation()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            AvailableEvidence = ValidEvidence().Where(evidence => evidence.Kind != WorkflowA2aHandoffEvidenceKind.PolicyPreflightReference).ToArray()
        });

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.MissingPolicyPreflightEvidence);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_ValidHandoffValidationReturnsValidForFutureHandoff()
    {
        var result = _validator.Validate(ValidRequest());

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.ValidForFutureHandoff, result.Status);
        Assert.AreEqual(0, result.BlockReasons.Count);
        Assert.AreEqual(0, result.MissingEvidence.Count);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_ValidHandoffValidationDoesNotApproveDispatchExecuteTransitionOrSatisfyPolicy()
    {
        var result = _validator.Validate(ValidRequest());
        var serialized = JsonSerializer.Serialize(result);

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.ValidForFutureHandoff, result.Status);
        AssertDoesNotContainAny(serialized, "Approved", "Authorized", "Dispatched", "Executed", "Transitioned", "PolicySatisfied", "AuthorityGranted");
    }

    [TestMethod]
    public void WorkflowA2aHandoff_SameRequestProducesSameResult()
    {
        var first = JsonSerializer.Serialize(_validator.Validate(ValidRequest()));
        var second = JsonSerializer.Serialize(_validator.Validate(ValidRequest()));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_SerializedResultContainsNoRawPrivateOrWholePatchPayloads()
    {
        var serialized = JsonSerializer.Serialize(_validator.Validate(ValidRequest()));

        AssertDoesNotContainAny(serialized, "raw prompt", "raw completion", "raw tool output", "private reasoning", "hidden reasoning", "whole patch", "entire patch");
    }

    internal static WorkflowA2aHandoffValidationRequest ValidRequest() =>
        new()
        {
            StepContract = ValidStep(),
            HandoffReference = ValidHandoff(),
            AvailableEvidence = ValidEvidence()
        };

    internal static WorkflowStepContract ValidStep(string stepId = "workflow-step-001") =>
        new()
        {
            StepContractId = stepId,
            WorkflowRunId = "workflow-run-001",
            Intent = WorkflowStepContractIntent.RecordHandoffContext,
            InputReference = Reference(WorkflowStepContractReferenceKind.WorkflowStepRecord, "workflow-step-input"),
            ExpectedOutputReference = Reference(WorkflowStepContractReferenceKind.HandoffRecord, "handoff-reference-001"),
            ExpectedActorKind = WorkflowStepContractActorKind.AgentExpected,
            AllowedTransitions =
            [
                new()
                {
                    Kind = WorkflowStepContractTransitionKind.ReadyForReviewToReceiptRecorded,
                    SafeLabel = "Record handoff review receipt."
                }
            ],
            EvidenceRequirements =
            [
                new()
                {
                    Kind = WorkflowStepContractEvidenceRequirementKind.GovernanceEventReference,
                    RequirementId = "governance-event-001",
                    SafeSummary = "Governance event reference."
                },
                new()
                {
                    Kind = WorkflowStepContractEvidenceRequirementKind.HandoffRecordReference,
                    RequirementId = "handoff-reference-001",
                    SafeSummary = "Handoff contract reference."
                }
            ],
            ThoughtLedgerReference = ValidThoughtLedgerReference(),
            Boundary = new WorkflowStepContractBoundary(),
            SafeSummary = "Validate handoff envelope before future eligibility."
        };

    internal static WorkflowA2aHandoffReference ValidHandoff() =>
        new()
        {
            HandoffReferenceId = "handoff-reference-001",
            WorkflowRunId = "workflow-run-001",
            WorkflowStepId = "workflow-step-001",
            Sender = Participant(WorkflowA2aParticipantKind.Agent, "planner-agent"),
            Receiver = Participant(WorkflowA2aParticipantKind.Agent, "builder-agent"),
            ThoughtLedgerReference = ValidThoughtLedgerReference(),
            CorrelationId = "policy-preflight-001",
            SafeSummary = "Reference-only handoff validation material."
        };

    internal static WorkflowStepThoughtLedgerReference ValidThoughtLedgerReference() =>
        new()
        {
            ThoughtLedgerEntryId = "thought-ledger-entry-001",
            TraceId = "trace-001",
            GovernanceEventId = "governance-event-001",
            CorrelationId = "policy-preflight-001",
            SafeSummary = "Traceability only."
        };

    internal static WorkflowA2aHandoffEvidenceReference[] ValidEvidence() =>
    [
        Evidence(WorkflowA2aHandoffEvidenceKind.GovernanceEventReference, "governance-event-001"),
        Evidence(WorkflowA2aHandoffEvidenceKind.HandoffContractReference, "handoff-reference-001"),
        Evidence(WorkflowA2aHandoffEvidenceKind.HandoffValidationReference, "handoff-reference-001"),
        Evidence(WorkflowA2aHandoffEvidenceKind.PolicyPreflightReference, "policy-preflight-001")
    ];

    internal static WorkflowA2aParticipantReference Participant(WorkflowA2aParticipantKind kind, string referenceId) =>
        new()
        {
            Kind = kind,
            ReferenceId = referenceId,
            SafeLabel = $"{kind} reference"
        };

    private static WorkflowStepContractReference Reference(WorkflowStepContractReferenceKind kind, string id) =>
        new()
        {
            Kind = kind,
            ReferenceId = id,
            SafeSummary = "Reference only."
        };

    private static WorkflowA2aHandoffEvidenceReference Evidence(WorkflowA2aHandoffEvidenceKind kind, string referenceId) =>
        new()
        {
            Kind = kind,
            ReferenceId = referenceId,
            CorrelationId = "policy-preflight-001"
        };

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }
}
