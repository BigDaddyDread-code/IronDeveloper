using System.Reflection;
using IronDev.Core.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
[TestCategory("AgentHandoff")]
public sealed class AgentHandoffContractTests
{
    private static readonly AgentHandoffValidator Validator = new();
    private static readonly DateTimeOffset CreatedUtc = new(2026, 6, 13, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AgentHandoffContracts_ExposeExpectedHandoffShape()
    {
        Assert.IsNotNull(typeof(AgentHandoff));
        Assert.IsNotNull(typeof(AgentHandoffCreateRequest));
        Assert.IsNotNull(typeof(AgentHandoffSummary));
        Assert.IsNotNull(typeof(AgentHandoffStatus));
        Assert.IsNotNull(typeof(AgentHandoffType));
        Assert.IsNotNull(typeof(AgentHandoffEvidenceAllowedUse));
        Assert.IsNotNull(typeof(AgentHandoffValidator));
        Assert.IsNotNull(typeof(AgentHandoffValidationResult));
        Assert.IsNotNull(typeof(AgentHandoffValidationIssue));

        AssertHasProperty<AgentHandoff>(nameof(AgentHandoff.AgentHandoffId));
        AssertHasProperty<AgentHandoff>(nameof(AgentHandoff.ProjectId));
        AssertHasProperty<AgentHandoff>(nameof(AgentHandoff.HandoffType));
        AssertHasProperty<AgentHandoff>(nameof(AgentHandoff.Status));
        AssertHasProperty<AgentHandoff>(nameof(AgentHandoff.SourceAgent));
        AssertHasProperty<AgentHandoff>(nameof(AgentHandoff.TargetAgent));
        AssertHasProperty<AgentHandoff>(nameof(AgentHandoff.Subject));
        AssertHasProperty<AgentHandoff>(nameof(AgentHandoff.EvidenceReferences));
        AssertHasProperty<AgentHandoff>(nameof(AgentHandoff.Constraints));
        AssertHasProperty<AgentHandoff>(nameof(AgentHandoff.MetadataJson));
        AssertHasProperty<AgentHandoff>(nameof(AgentHandoff.GrantsApproval));
        AssertHasProperty<AgentHandoff>(nameof(AgentHandoff.GrantsExecution));
        AssertHasProperty<AgentHandoff>(nameof(AgentHandoff.MutatesSource));
        AssertHasProperty<AgentHandoff>(nameof(AgentHandoff.PromotesMemory));
        AssertHasProperty<AgentHandoff>(nameof(AgentHandoff.StartsWorkflow));
        AssertHasProperty<AgentHandoff>(nameof(AgentHandoff.SatisfiesPolicy));
        AssertHasProperty<AgentHandoff>(nameof(AgentHandoff.TransfersAuthority));
    }

    [TestMethod]
    public void AgentHandoffContracts_ExposeParticipantSubjectEvidenceConstraintAndSummaryShape()
    {
        AssertHasProperty<AgentHandoffParticipant>(nameof(AgentHandoffParticipant.AgentId));
        AssertHasProperty<AgentHandoffParticipant>(nameof(AgentHandoffParticipant.AgentRole));
        AssertHasProperty<AgentHandoffParticipant>(nameof(AgentHandoffParticipant.DisplayName));
        AssertHasProperty<AgentHandoffSubject>(nameof(AgentHandoffSubject.SubjectType));
        AssertHasProperty<AgentHandoffSubject>(nameof(AgentHandoffSubject.SubjectId));
        AssertHasProperty<AgentHandoffSubject>(nameof(AgentHandoffSubject.ActionName));
        AssertHasProperty<AgentHandoffSubject>(nameof(AgentHandoffSubject.Summary));
        AssertHasProperty<AgentHandoffEvidenceReference>(nameof(AgentHandoffEvidenceReference.EvidenceType));
        AssertHasProperty<AgentHandoffEvidenceReference>(nameof(AgentHandoffEvidenceReference.EvidenceId));
        AssertHasProperty<AgentHandoffEvidenceReference>(nameof(AgentHandoffEvidenceReference.AllowedUses));
        AssertHasProperty<AgentHandoffEvidenceReference>(nameof(AgentHandoffEvidenceReference.GovernanceEventId));
        AssertHasProperty<AgentHandoffConstraint>(nameof(AgentHandoffConstraint.ConstraintType));
        AssertHasProperty<AgentHandoffConstraint>(nameof(AgentHandoffConstraint.ConstraintCode));
        AssertHasProperty<AgentHandoffConstraint>(nameof(AgentHandoffConstraint.Description));
        AssertHasProperty<AgentHandoffSummary>(nameof(AgentHandoffSummary.AgentHandoffId));
        AssertHasProperty<AgentHandoffSummary>(nameof(AgentHandoffSummary.SourceAgentId));
        AssertHasProperty<AgentHandoffSummary>(nameof(AgentHandoffSummary.TargetAgentId));
        AssertHasProperty<AgentHandoffSummary>(nameof(AgentHandoffSummary.EvidenceReferenceCount));
        AssertHasProperty<AgentHandoffSummary>(nameof(AgentHandoffSummary.ConstraintCount));
    }

    [TestMethod]
    public void AgentHandoffContracts_DoNotExposeSendReceiveExecuteOrApproveMethods()
    {
        var contractTypes = new[]
        {
            typeof(AgentHandoff),
            typeof(AgentHandoffCreateRequest),
            typeof(AgentHandoffSummary),
            typeof(AgentHandoffParticipant),
            typeof(AgentHandoffSubject),
            typeof(AgentHandoffEvidenceReference),
            typeof(AgentHandoffConstraint),
            typeof(AgentHandoffValidator)
        };
        var forbiddenPrefixes = new[] { "Send", "Receive", "Execute", "Approve", "Authorize", "Dispatch", "Route", "Store", "Persist", "Promote", "Apply" };

        foreach (var type in contractTypes)
        {
            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                foreach (var prefix in forbiddenPrefixes)
                    Assert.IsFalse(method.Name.StartsWith(prefix, StringComparison.Ordinal), $"{type.Name} exposes forbidden method {method.Name}.");
            }
        }
    }

    [TestMethod]
    public void AgentHandoffStatus_AllowsOnlyBoundedNonAuthoritativeStatuses()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                "Draft",
                "ReadyForReview",
                "Offered",
                "Received",
                "Rejected",
                "Cancelled",
                "Expired",
                "Superseded"
            },
            Enum.GetNames<AgentHandoffStatus>());

        AssertEnumExcludes<AgentHandoffStatus>("Approved", "Authorized", "AcceptedAsApproval", "ExecutionAllowed", "PolicySatisfied", "WorkflowContinued", "SourceApplyAllowed", "MemoryPromotionAllowed", "ReleaseApproved", "CanExecute", "CanShip");
    }

    [TestMethod]
    public void AgentHandoffType_AllowsOnlyContextAndEvidenceTransferTypes()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                "TaskContext",
                "ReviewRequest",
                "EvidenceTransfer",
                "RequirementTransfer",
                "DebugContext",
                "ImplementationContext",
                "ValidationContext",
                "MemoryCandidateContext",
                "SourceApplyContext",
                "ReleaseEvidenceContext"
            },
            Enum.GetNames<AgentHandoffType>());

        AssertEnumExcludes<AgentHandoffType>("ApprovalTransfer", "ExecutionTransfer", "AuthorityTransfer", "MemoryOwnershipTransfer", "SourceApplyPermission", "ReleaseApproval", "WorkflowContinuation", "PolicySatisfied");
    }

    [TestMethod]
    public void AgentHandoffEvidence_AllowsEvidenceOnlyReferenceTypes()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                "GovernanceEvent",
                "ToolRequest",
                "ToolGateDecision",
                "ApprovalRequirementEvaluation",
                "ApprovalPackage",
                "ApprovalDecision",
                "PolicyDecisionEvent",
                "DogfoodReceipt",
                "ThoughtLedgerReference",
                "ValidationOutput",
                "RunReport",
                "HumanNote",
                "CriticReview",
                "CodeStandardsReview"
            },
            Enum.GetNames<AgentHandoffEvidenceType>());

        AssertEnumExcludes<AgentHandoffEvidenceType>("ExecutionPermission", "TransferredApproval", "PolicySatisfied", "SourceApplyAllowed", "MemoryPromotionAllowed", "ReleaseApproved", "WorkflowContinuationAllowed");
    }

    [TestMethod]
    public void AgentHandoffEvidenceAllowedUse_AllowsKnownNonAuthoritativeUseVocabulary()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                "Context",
                "Review",
                "Debugging",
                "Validation",
                "Traceability",
                "RequirementEvaluation",
                "HumanDecisionSupport",
                "AuditReference",
                "PolicyInput",
                "HandoffExplanation"
            },
            Enum.GetNames<AgentHandoffEvidenceAllowedUse>());
    }

    [TestMethod]
    public void AgentHandoffEvidenceAllowedUse_DoesNotExposeAuthorityUseVocabulary()
    {
        AssertEnumExcludes<AgentHandoffEvidenceAllowedUse>(
            "Approval",
            "Approve",
            "Approved",
            "ExecutionPermission",
            "CanExecute",
            "Execute",
            "ExecutionAllowed",
            "PolicySatisfied",
            "SatisfyPolicy",
            "WorkflowContinuation",
            "ContinueWorkflow",
            "SourceApplyPermission",
            "SourceApplyAllowed",
            "MemoryPromotionPermission",
            "MemoryPromotionAllowed",
            "ReleaseApproval",
            "ReleaseApproved",
            "CanShip",
            "AuthorityTransfer",
            "PermissionTransfer",
            "ApprovalTransfer",
            "HumanApprovalTransfer",
            "GateApproval",
            "DogfoodApproval",
            "CriticApproval",
            "ModelApproval",
            "RetrievalApproval");
    }

    [TestMethod]
    public void AgentHandoffConstraint_AllowsRequirementsAndNegativeBoundariesOnly()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                "RequiresHumanReview",
                "RequiresApprovalDecision",
                "RequiresPolicyEvaluation",
                "RequiresValidation",
                "RequiresDogfoodReceipt",
                "RequiresSourceApplyApproval",
                "RequiresMemoryPromotionApproval",
                "EvidenceOnly",
                "DoNotExecute",
                "DoNotMutateSource",
                "DoNotPromoteMemory",
                "DoNotContinueWorkflow"
            },
            Enum.GetNames<AgentHandoffConstraintType>());

        AssertEnumExcludes<AgentHandoffConstraintType>("ApprovalGranted", "ExecutionGranted", "SourceApplyGranted", "MemoryPromotionGranted", "WorkflowContinuationGranted", "ReleaseApproved");
    }

    [TestMethod]
    public void AgentHandoffParticipantRole_ExcludesAuthorityRoles()
    {
        AssertEnumExcludes<AgentHandoffParticipantRole>("Approver", "ExecutorWithPermission", "ReleaseApprover", "MemoryOwnerAuthority", "SourceApplyAuthority", "PolicySatisfier");
    }

    [TestMethod]
    public void AgentHandoffCreateRequest_ValidContextHandoff_Validates()
    {
        AssertValid(Validator.Validate(ValidCreateRequest()));
    }

    [TestMethod]
    public void AgentHandoff_ValidMaterializedHandoff_ValidatesWithAllAuthorityFlagsFalse()
    {
        AssertValid(Validator.Validate(ValidHandoff()));
    }

    [TestMethod]
    public void AgentHandoffCreateRequest_InvalidShapes_AreRejected()
    {
        var cases = new (AgentHandoffCreateRequest Request, string Code)[]
        {
            (ValidCreateRequest() with { ProjectId = Guid.Empty }, AgentHandoffValidator.ProjectIdRequired),
            (ValidCreateRequest() with { HandoffType = (AgentHandoffType)999 }, AgentHandoffValidator.HandoffTypeInvalid),
            (ValidCreateRequest() with { Status = (AgentHandoffStatus)999 }, AgentHandoffValidator.HandoffStatusInvalid),
            (ValidCreateRequest() with { SourceAgent = null! }, AgentHandoffValidator.ParticipantRequired),
            (ValidCreateRequest() with { TargetAgent = null! }, AgentHandoffValidator.ParticipantRequired),
            (ValidCreateRequest() with { SourceAgent = SourceAgent() with { AgentId = string.Empty } }, AgentHandoffValidator.ParticipantInvalid),
            (ValidCreateRequest() with { TargetAgent = TargetAgent() with { AgentId = string.Empty } }, AgentHandoffValidator.ParticipantInvalid),
            (ValidCreateRequest() with { SourceAgent = SourceAgent() with { AgentRole = (AgentHandoffParticipantRole)999 } }, AgentHandoffValidator.ParticipantRoleInvalid),
            (ValidCreateRequest() with { TargetAgent = SourceAgent() }, AgentHandoffValidator.SameParticipantBlocked),
            (ValidCreateRequest() with { Subject = null! }, AgentHandoffValidator.SubjectRequired),
            (ValidCreateRequest() with { Subject = Subject() with { SubjectType = (AgentHandoffSubjectType)999 } }, AgentHandoffValidator.SubjectInvalid),
            (ValidCreateRequest() with { Subject = Subject() with { SubjectId = string.Empty } }, AgentHandoffValidator.SubjectInvalid),
            (ValidCreateRequest() with { EvidenceReferences = [] }, AgentHandoffValidator.EvidenceRequired),
            (ValidCreateRequest() with { EvidenceReferences = [Evidence() with { EvidenceType = (AgentHandoffEvidenceType)999 }] }, AgentHandoffValidator.EvidenceTypeInvalid),
            (ValidCreateRequest() with { EvidenceReferences = [Evidence() with { EvidenceId = string.Empty }] }, AgentHandoffValidator.EvidenceInvalid),
            (ValidCreateRequest() with { EvidenceReferences = [Evidence() with { AllowedUses = null! }] }, AgentHandoffValidator.EvidenceAllowedUseRequired),
            (ValidCreateRequest() with { EvidenceReferences = [Evidence() with { AllowedUses = [] }] }, AgentHandoffValidator.EvidenceAllowedUseRequired),
            (ValidCreateRequest() with { EvidenceReferences = [Evidence() with { AllowedUses = [(AgentHandoffEvidenceAllowedUse)999] }] }, AgentHandoffValidator.EvidenceAllowedUseInvalid),
            (ValidCreateRequest() with { EvidenceReferences = [Evidence() with { AllowedUses = [AgentHandoffEvidenceAllowedUse.Review, AgentHandoffEvidenceAllowedUse.Review] }] }, AgentHandoffValidator.EvidenceAllowedUseDuplicate),
            (ValidCreateRequest() with { Constraints = [] }, AgentHandoffValidator.ConstraintRequired),
            (ValidCreateRequest() with { Constraints = [Constraint() with { ConstraintType = (AgentHandoffConstraintType)999 }] }, AgentHandoffValidator.ConstraintTypeInvalid),
            (ValidCreateRequest() with { Constraints = [Constraint() with { ConstraintCode = string.Empty }] }, AgentHandoffValidator.ConstraintInvalid),
            (ValidCreateRequest() with { Constraints = [Constraint() with { Description = string.Empty }] }, AgentHandoffValidator.ConstraintInvalid),
            (ValidCreateRequest() with { CreatedByActorType = string.Empty }, AgentHandoffValidator.ActorRequired),
            (ValidCreateRequest() with { CreatedByActorId = string.Empty }, AgentHandoffValidator.ActorRequired),
            (ValidCreateRequest() with { MetadataVersion = 0 }, AgentHandoffValidator.MetadataVersionInvalid),
            (ValidCreateRequest() with { MetadataJson = string.Empty }, AgentHandoffValidator.MetadataJsonRequired),
            (ValidCreateRequest() with { MetadataJson = "{not-json}" }, AgentHandoffValidator.MetadataJsonInvalid)
        };

        foreach (var testCase in cases)
            AssertHasIssue(Validator.Validate(testCase.Request), testCase.Code);
    }

    [TestMethod]
    public void AgentHandoffCreateRequest_RejectsPrivateReasoningMarkers()
    {
        var cases = new[]
        {
            ValidCreateRequest() with { MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"hiddenReasoning\":\"nope\"}" },
            ValidCreateRequest() with { MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"notes\":\"contains chain-of-thought\"}" },
            ValidCreateRequest() with { Subject = Subject() with { Summary = "private reasoning marker" } },
            ValidCreateRequest() with { EvidenceReferences = [Evidence() with { EvidenceSummary = "rawPrompt should not travel" }] },
            ValidCreateRequest() with { Constraints = [Constraint() with { Description = "scratchpad should not travel" }] }
        };

        foreach (var request in cases)
            AssertHasIssue(Validator.Validate(request), AgentHandoffValidator.PrivateReasoningBlocked);
    }

    [TestMethod]
    public void AgentHandoffCreateRequest_RejectsAuthorityGrantingMetadata()
    {
        var cases = new[]
        {
            ValidCreateRequest() with { MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"approvalTransferred\":true}" },
            ValidCreateRequest() with { MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"canExecute\":true}" },
            ValidCreateRequest() with { MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"sourceApplyAllowed\":true}" },
            ValidCreateRequest() with { MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"memoryPromotionAllowed\":true}" },
            ValidCreateRequest() with { MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"workflowContinues\":true}" },
            ValidCreateRequest() with { MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"releaseApproved\":true}" },
            ValidCreateRequest() with { Subject = Subject() with { ActionName = "canExecute" } },
            ValidCreateRequest() with { EvidenceReferences = [Evidence() with { EvidenceLabel = "approval granted" }] },
            ValidCreateRequest() with { EvidenceReferences = [Evidence() with { EvidenceLabel = "approval transfer" }] },
            ValidCreateRequest() with { EvidenceReferences = [Evidence() with { EvidenceSummary = "approved to execute" }] },
            ValidCreateRequest() with { EvidenceReferences = [Evidence(AgentHandoffEvidenceType.ToolGateDecision, AgentHandoffEvidenceAllowedUse.Review) with { EvidenceSummary = "execution permission" }] },
            ValidCreateRequest() with { EvidenceReferences = [Evidence(AgentHandoffEvidenceType.DogfoodReceipt, AgentHandoffEvidenceAllowedUse.Validation) with { EvidenceSummary = "release approval" }] },
            ValidCreateRequest() with { EvidenceReferences = [Evidence(AgentHandoffEvidenceType.CriticReview, AgentHandoffEvidenceAllowedUse.Review) with { EvidenceSummary = "critic approval" }] },
            ValidCreateRequest() with { EvidenceReferences = [Evidence(AgentHandoffEvidenceType.CodeStandardsReview, AgentHandoffEvidenceAllowedUse.Review) with { EvidenceSummary = "model approval" }] },
            ValidCreateRequest() with { EvidenceReferences = [Evidence(AgentHandoffEvidenceType.ApprovalPackage, AgentHandoffEvidenceAllowedUse.Review) with { EvidenceSummary = "policy satisfied" }] },
            ValidCreateRequest() with { EvidenceReferences = [Evidence(AgentHandoffEvidenceType.ApprovalDecision, AgentHandoffEvidenceAllowedUse.HumanDecisionSupport) with { EvidenceSummary = "human approval transfer" }] },
            ValidCreateRequest() with { EvidenceReferences = [Evidence(AgentHandoffEvidenceType.ValidationOutput, AgentHandoffEvidenceAllowedUse.Validation) with { EvidenceSummary = "can ship" }] },
            ValidCreateRequest() with { EvidenceReferences = [Evidence(AgentHandoffEvidenceType.RunReport, AgentHandoffEvidenceAllowedUse.Debugging) with { EvidenceSummary = "release approved" }] },
            ValidCreateRequest() with { EvidenceReferences = [Evidence(AgentHandoffEvidenceType.ThoughtLedgerReference, AgentHandoffEvidenceAllowedUse.Traceability) with { EvidenceSummary = "authority transfer" }] },
            ValidCreateRequest() with { Constraints = [Constraint() with { Description = "execution granted" }] }
        };

        foreach (var request in cases)
            AssertHasIssue(Validator.Validate(request), AgentHandoffValidator.AuthorityMetadataBlocked);
    }

    [TestMethod]
    public void AgentHandoffCreateRequest_AllowsSafeEvidenceUseMappings()
    {
        var safeEvidence = new[]
        {
            Evidence(AgentHandoffEvidenceType.ToolGateDecision, AgentHandoffEvidenceAllowedUse.Context, AgentHandoffEvidenceAllowedUse.Review, AgentHandoffEvidenceAllowedUse.Traceability, AgentHandoffEvidenceAllowedUse.HumanDecisionSupport),
            Evidence(AgentHandoffEvidenceType.DogfoodReceipt, AgentHandoffEvidenceAllowedUse.Validation, AgentHandoffEvidenceAllowedUse.Review, AgentHandoffEvidenceAllowedUse.Traceability),
            Evidence(AgentHandoffEvidenceType.ApprovalRequirementEvaluation, AgentHandoffEvidenceAllowedUse.RequirementEvaluation, AgentHandoffEvidenceAllowedUse.Review, AgentHandoffEvidenceAllowedUse.Traceability),
            Evidence(AgentHandoffEvidenceType.ApprovalPackage, AgentHandoffEvidenceAllowedUse.Review, AgentHandoffEvidenceAllowedUse.HumanDecisionSupport, AgentHandoffEvidenceAllowedUse.Traceability),
            Evidence(AgentHandoffEvidenceType.CriticReview, AgentHandoffEvidenceAllowedUse.Review, AgentHandoffEvidenceAllowedUse.HumanDecisionSupport, AgentHandoffEvidenceAllowedUse.Traceability),
            Evidence(AgentHandoffEvidenceType.ValidationOutput, AgentHandoffEvidenceAllowedUse.Validation, AgentHandoffEvidenceAllowedUse.Traceability),
            Evidence(AgentHandoffEvidenceType.RunReport, AgentHandoffEvidenceAllowedUse.Debugging, AgentHandoffEvidenceAllowedUse.Validation, AgentHandoffEvidenceAllowedUse.Traceability),
            Evidence(AgentHandoffEvidenceType.PolicyDecisionEvent, AgentHandoffEvidenceAllowedUse.PolicyInput, AgentHandoffEvidenceAllowedUse.Review, AgentHandoffEvidenceAllowedUse.Traceability),
            Evidence(AgentHandoffEvidenceType.HumanNote, AgentHandoffEvidenceAllowedUse.Context, AgentHandoffEvidenceAllowedUse.Review, AgentHandoffEvidenceAllowedUse.HumanDecisionSupport),
            Evidence(AgentHandoffEvidenceType.ThoughtLedgerReference, AgentHandoffEvidenceAllowedUse.Context, AgentHandoffEvidenceAllowedUse.Traceability, AgentHandoffEvidenceAllowedUse.HandoffExplanation)
        };

        var result = Validator.Validate(ValidCreateRequest() with { EvidenceReferences = safeEvidence });

        AssertValid(result);
    }

    [TestMethod]
    public void AgentHandoff_RejectsTrueAuthorityFlags()
    {
        var cases = new[]
        {
            ValidHandoff() with { GrantsApproval = true },
            ValidHandoff() with { GrantsExecution = true },
            ValidHandoff() with { MutatesSource = true },
            ValidHandoff() with { PromotesMemory = true },
            ValidHandoff() with { StartsWorkflow = true },
            ValidHandoff() with { SatisfiesPolicy = true },
            ValidHandoff() with { TransfersAuthority = true }
        };

        foreach (var handoff in cases)
            AssertHasIssue(Validator.Validate(handoff), AgentHandoffValidator.AuthorityFlagBlocked);
    }

    [TestMethod]
    public void AgentHandoff_DoesNotCreateApprovalPolicyExecutionWorkflowSourceMemoryOrReleaseEffects()
    {
        var handoff = ValidHandoff();

        Assert.IsFalse(handoff.GrantsApproval);
        Assert.IsFalse(handoff.GrantsExecution);
        Assert.IsFalse(handoff.MutatesSource);
        Assert.IsFalse(handoff.PromotesMemory);
        Assert.IsFalse(handoff.StartsWorkflow);
        Assert.IsFalse(handoff.SatisfiesPolicy);
        Assert.IsFalse(handoff.TransfersAuthority);

        CollectionAssert.Contains(handoff.EvidenceReferences.Single().AllowedUses.ToArray(), AgentHandoffEvidenceAllowedUse.Review);
    }

    [TestMethod]
    public void AgentHandoff_StaticBoundary_CoreFileHasNoRuntimePersistenceTransportOrExternalTokens()
    {
        var source = ReadRepositoryFile("IronDev.Core", "Agents", "AgentHandoffModels.cs");
        var forbidden = new[]
        {
            "SqlConnection",
            "DbConnection",
            "INSERT INTO",
            "UPDATE ",
            "DELETE ",
            "MERGE ",
            "HttpClient",
            "ProcessStartInfo",
            "System.Diagnostics.Process",
            "File.WriteAllText",
            "File.Delete",
            "File.Copy",
            "Directory.Delete",
            "IHostedService",
            "BackgroundService",
            "AddHostedService",
            "IAgentRunAuditEnvelopeStore",
            "IAgentToolExecutor",
            "AgentToolRouter",
            "WorkflowRunner",
            "LangGraphRuntime",
            "MessageBus",
            "QueueClient",
            "Inbox",
            "Outbox",
            "WeaviateClient",
            "OpenAiLlmService",
            "ChatCompletion",
            "ResponsesApi",
            "SubmitReview",
            "CreatePullRequest",
            "SqlCollectiveMemoryPromotionService"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"Forbidden active token found in AgentHandoffModels.cs: {token}");
    }

    [TestMethod]
    public void AgentHandoff_StaticBoundary_IsNotWiredIntoApiCliRuntimeSqlWorkflowOrA2a()
    {
        var root = RepositoryRoot();
        var files = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}IronDev.IntegrationTests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(Path.Combine("IronDev.Core", "Agents", "AgentHandoffModels.cs"), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert.IsFalse(source.Contains("AgentHandoff", StringComparison.Ordinal), $"AgentHandoff must not be wired into production/runtime file: {file}");
        }
    }

    [TestMethod]
    public void BlockIDocumentation_StatesHandoffTransfersContextAndEvidenceOnly()
    {
        var doc = ReadRepositoryFile("Docs", "BLOCK_I_A2A_HANDOFF_CONTRACT_SPINE.md");

        foreach (var expected in new[]
        {
            "Block I begins with the Agent Handoff contract.",
            "A handoff transfers context and evidence only.",
            "A handoff does not transfer approval, execution permission, memory ownership, workflow authority, source apply authority, memory promotion authority, or release authority.",
            "A handoff may cite approval decisions only as evidence.",
            "A handoff may cite gate decisions, dogfood receipts, critic output, validation output, ThoughtLedger references, and governance events only as evidence.",
            "PR91 adds bounded allowed-use semantics to each evidence reference.",
            "Evidence references must declare allowed uses.",
            "Allowed use is not authority.",
            "Allowed use cannot be approval, execution permission, policy satisfaction, workflow continuation, source apply permission, memory promotion permission, release approval, or authority transfer.",
            "Approval decisions may be cited only as evidence.",
            "Gate decisions may be cited only as evidence.",
            "Dogfood receipts may be cited only as evidence.",
            "Critic/model/retrieval output may be cited only as advisory evidence.",
            "A handoff does not send itself.",
            "A handoff does not create A2A runtime messages.",
            "A handoff does not add API/CLI/SQL/runtime wiring.",
            "PR90 defines the envelope.",
            "It does not send it, receive it, store it, route it, execute it, approve it, or make it powerful."
        })
        {
            StringAssert.Contains(doc, expected);
        }
    }

    [TestMethod]
    public void BlockIDocumentation_DoesNotContainHiddenOrBidirectionalUnicode()
    {
        var path = Path.Combine(RepositoryRoot(), "Docs", "BLOCK_I_A2A_HANDOFF_CONTRACT_SPINE.md");
        var bytes = File.ReadAllBytes(path);

        Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "Block I doc must not contain UTF-8 BOM.");

        for (var index = 0; index < bytes.Length; index++)
            Assert.IsTrue(bytes[index] <= 0x7F, $"Block I doc must be ASCII-only. Non-ASCII byte 0x{bytes[index]:X2} at offset {index}.");
    }

    private static AgentHandoffCreateRequest ValidCreateRequest() =>
        new()
        {
            ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            HandoffType = AgentHandoffType.EvidenceTransfer,
            Status = AgentHandoffStatus.ReadyForReview,
            SourceAgent = SourceAgent(),
            TargetAgent = TargetAgent(),
            Subject = Subject(),
            EvidenceReferences = [Evidence()],
            Constraints = [Constraint()],
            CorrelationId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            CausationId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            CreatedByActorType = "agent",
            CreatedByActorId = "planner-agent",
            MetadataVersion = 1,
            MetadataJson = SafeMetadataJson()
        };

    private static AgentHandoff ValidHandoff() =>
        new()
        {
            AgentHandoffId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            HandoffType = AgentHandoffType.EvidenceTransfer,
            Status = AgentHandoffStatus.ReadyForReview,
            SourceAgent = SourceAgent(),
            TargetAgent = TargetAgent(),
            Subject = Subject(),
            EvidenceReferences = [Evidence()],
            Constraints = [Constraint()],
            CorrelationId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            CausationId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            CreatedByActorType = "agent",
            CreatedByActorId = "planner-agent",
            MetadataVersion = 1,
            MetadataJson = SafeMetadataJson(),
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false,
            CreatedUtc = CreatedUtc
        };

    private static AgentHandoffParticipant SourceAgent() =>
        new()
        {
            AgentId = "planner-agent",
            AgentRole = AgentHandoffParticipantRole.Planner,
            DisplayName = "Planner Agent"
        };

    private static AgentHandoffParticipant TargetAgent() =>
        new()
        {
            AgentId = "builder-agent",
            AgentRole = AgentHandoffParticipantRole.Builder,
            DisplayName = "Builder Agent"
        };

    private static AgentHandoffSubject Subject() =>
        new()
        {
            SubjectType = AgentHandoffSubjectType.ToolRequest,
            SubjectId = "tool-request-1",
            ActionName = "ReviewEvidence",
            Summary = "Evidence package for target-agent review."
        };

    private static AgentHandoffEvidenceReference Evidence() =>
        Evidence(AgentHandoffEvidenceType.ApprovalDecision, AgentHandoffEvidenceAllowedUse.Review, AgentHandoffEvidenceAllowedUse.Traceability, AgentHandoffEvidenceAllowedUse.HumanDecisionSupport);

    private static AgentHandoffEvidenceReference Evidence(AgentHandoffEvidenceType evidenceType, params AgentHandoffEvidenceAllowedUse[] allowedUses) =>
        new()
        {
            EvidenceType = evidenceType,
            EvidenceId = $"{evidenceType}-1",
            AllowedUses = allowedUses,
            EvidenceLabel = $"{evidenceType} evidence",
            EvidenceSummary = $"{evidenceType} is cited only as evidence.",
            GovernanceEventId = Guid.Parse("44444444-4444-4444-4444-444444444444")
        };

    private static AgentHandoffConstraint Constraint() =>
        new()
        {
            ConstraintType = AgentHandoffConstraintType.EvidenceOnly,
            ConstraintCode = "EVIDENCE_ONLY",
            Description = "This handoff transfers context and evidence only."
        };

    private static string SafeMetadataJson() =>
        """
        {
          "schema": "agent.handoff.metadata.v1",
          "notes": "Evidence package for tester review.",
          "grantsApproval": false,
          "grantsExecution": false,
          "mutatesSource": false,
          "promotesMemory": false,
          "startsWorkflow": false,
          "satisfiesPolicy": false,
          "transfersAuthority": false
        }
        """;

    private static void AssertValid(AgentHandoffValidationResult result) =>
        Assert.IsTrue(result.IsValid, FormatIssues(result.Issues));

    private static void AssertHasIssue(AgentHandoffValidationResult result, string code) =>
        Assert.IsTrue(result.Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)), $"Expected {code}.{Environment.NewLine}{FormatIssues(result.Issues)}");

    private static void AssertHasProperty<T>(string name) =>
        Assert.IsNotNull(typeof(T).GetProperty(name), $"{typeof(T).Name} missing property {name}.");

    private static void AssertEnumExcludes<TEnum>(params string[] forbidden)
        where TEnum : struct, Enum
    {
        var names = Enum.GetNames<TEnum>();
        foreach (var name in forbidden)
            Assert.IsFalse(names.Contains(name, StringComparer.Ordinal), $"{typeof(TEnum).Name} exposes forbidden value {name}.");
    }

    private static string FormatIssues(IReadOnlyList<AgentHandoffValidationIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private static string ReadRepositoryFile(params string[] pathParts) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new InvalidOperationException("Could not locate repository root.");

        return directory.FullName;
    }
}
