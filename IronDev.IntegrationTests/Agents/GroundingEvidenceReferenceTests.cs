using System.Reflection;
using IronDev.Core.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
[TestCategory("GroundingEvidenceReference")]
public sealed class GroundingEvidenceReferenceTests
{
    private static readonly GroundingEvidenceReferenceValidator Validator = new();
    private static readonly DateTimeOffset CreatedUtc = new(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void GroundingEvidenceReference_ExposesExpectedShape()
    {
        Assert.IsNotNull(typeof(GroundingEvidenceReference));
        Assert.IsNotNull(typeof(GroundingEvidenceReferenceCreateRequest));
        Assert.IsNotNull(typeof(GroundingEvidenceReferenceSummary));
        Assert.IsNotNull(typeof(GroundingEvidenceReferenceLocation));
        Assert.IsNotNull(typeof(GroundingEvidenceReferenceValidator));
        Assert.IsNotNull(typeof(GroundingEvidenceReferenceValidationResult));
        Assert.IsNotNull(typeof(GroundingEvidenceReferenceValidationIssue));
        Assert.IsNotNull(typeof(IGroundingEvidenceReferenceFactory));
        Assert.IsNotNull(typeof(GroundingEvidenceReferenceFactory));

        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.GroundingEvidenceReferenceId));
        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.ProjectId));
        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.EvidenceType));
        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.EvidenceId));
        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.ClaimType));
        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.ClaimId));
        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.AllowedUses));
        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.Location));
        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.GovernanceEventId));
        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.AgentHandoffId));
        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.ThoughtLedgerEntryId));
        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.GrantsApproval));
        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.GrantsExecution));
        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.MutatesSource));
        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.PromotesMemory));
        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.StartsWorkflow));
        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.SatisfiesPolicy));
        AssertHasProperty<GroundingEvidenceReference>(nameof(GroundingEvidenceReference.TransfersAuthority));
    }

    [TestMethod]
    public void GroundingEvidenceReferenceLocation_ExposesExpectedShape()
    {
        AssertHasProperty<GroundingEvidenceReferenceLocation>(nameof(GroundingEvidenceReferenceLocation.SourceUri));
        AssertHasProperty<GroundingEvidenceReferenceLocation>(nameof(GroundingEvidenceReferenceLocation.SourcePath));
        AssertHasProperty<GroundingEvidenceReferenceLocation>(nameof(GroundingEvidenceReferenceLocation.StartLine));
        AssertHasProperty<GroundingEvidenceReferenceLocation>(nameof(GroundingEvidenceReferenceLocation.EndLine));
        AssertHasProperty<GroundingEvidenceReferenceLocation>(nameof(GroundingEvidenceReferenceLocation.SectionId));
        AssertHasProperty<GroundingEvidenceReferenceLocation>(nameof(GroundingEvidenceReferenceLocation.AnchorText));
    }

    [TestMethod]
    public void GroundingEvidenceReference_UsesBoundedEvidenceTypes()
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
                "ThoughtLedgerHandoffEntry",
                "AgentHandoff",
                "ValidationOutput",
                "RunReport",
                "HumanNote",
                "CriticReview",
                "CodeStandardsReview",
                "SourceFileRange",
                "DocumentSection",
                "ExternalReference"
            },
            GroundingEvidenceReferenceVocabulary.AllowedEvidenceTypes.ToArray());

        AssertVocabularyExcludes(
            GroundingEvidenceReferenceVocabulary.AllowedEvidenceTypes,
            "Approval",
            "ExecutionPermission",
            "PolicySatisfied",
            "WorkflowContinuation",
            "SourceApplyPermission",
            "MemoryPromotionPermission",
            "ReleaseApproval",
            "AuthorityTransfer",
            "AcceptedMemory",
            "TrustedTruth");
    }

    [TestMethod]
    public void GroundingEvidenceReference_UsesBoundedClaimTypes()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                "HandoffSummary",
                "ThoughtLedgerEntry",
                "ApprovalPackage",
                "PolicyEvaluationInput",
                "ValidationClaim",
                "DebugFinding",
                "ReviewFinding",
                "MemoryCandidateClaim",
                "SourceApplyCandidateClaim",
                "ReleaseEvidenceClaim",
                "HumanDecisionSupportClaim"
            },
            GroundingEvidenceReferenceVocabulary.AllowedClaimTypes.ToArray());

        AssertVocabularyExcludes(
            GroundingEvidenceReferenceVocabulary.AllowedClaimTypes,
            "ApprovedClaim",
            "ExecutionAllowedClaim",
            "PolicySatisfiedClaim",
            "WorkflowContinuedClaim",
            "SourceApplyApprovedClaim",
            "MemoryPromotedClaim",
            "ReleaseApprovedClaim",
            "AuthorityTransferredClaim",
            "AcceptedMemoryClaim");
    }

    [TestMethod]
    public void GroundingEvidenceReference_UsesBoundedAllowedUses()
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
                "HandoffExplanation",
                "ClaimSupport"
            },
            GroundingEvidenceReferenceVocabulary.AllowedUses.ToArray());

        AssertVocabularyExcludes(
            GroundingEvidenceReferenceVocabulary.AllowedUses,
            "Approval",
            "Approve",
            "Approved",
            "ExecutionPermission",
            "CanExecute",
            "ExecutionAllowed",
            "PolicySatisfied",
            "WorkflowContinuation",
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
            "AcceptedMemory",
            "TrustedTruth");
    }

    [TestMethod]
    public void GroundingEvidenceReference_DoesNotExposeApproveExecutePromoteOrContinueMethods()
    {
        var contractTypes = new[]
        {
            typeof(GroundingEvidenceReference),
            typeof(GroundingEvidenceReferenceCreateRequest),
            typeof(GroundingEvidenceReferenceSummary),
            typeof(GroundingEvidenceReferenceLocation),
            typeof(GroundingEvidenceReferenceValidator)
        };
        var forbiddenPrefixes = new[] { "Approve", "Authorize", "Execute", "Promote", "Continue", "Apply", "Ship", "SatisfyPolicy" };

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
    public void GroundingEvidenceReference_AuthorityFlagsAreAlwaysFalse()
    {
        var reference = ValidReference();

        Assert.IsFalse(reference.GrantsApproval);
        Assert.IsFalse(reference.GrantsExecution);
        Assert.IsFalse(reference.MutatesSource);
        Assert.IsFalse(reference.PromotesMemory);
        Assert.IsFalse(reference.StartsWorkflow);
        Assert.IsFalse(reference.SatisfiesPolicy);
        Assert.IsFalse(reference.TransfersAuthority);
        AssertValid(Validator.Validate(reference));
    }

    [DataTestMethod]
    [DataRow("GovernanceEvent")]
    [DataRow("AgentHandoff")]
    [DataRow("ThoughtLedgerHandoffEntry")]
    [DataRow("SourceFileRange")]
    [DataRow("DocumentSection")]
    public void GroundingEvidenceType_AllowsExpectedEvidenceTypes(string evidenceType)
    {
        AssertValid(Validator.Validate(ValidCreateRequest() with { EvidenceType = evidenceType }));
    }

    [DataTestMethod]
    [DataRow("Approval")]
    [DataRow("ExecutionPermission")]
    [DataRow("PolicySatisfied")]
    [DataRow("SourceApplyPermission")]
    [DataRow("MemoryPromotionPermission")]
    [DataRow("ReleaseApproval")]
    [DataRow("AcceptedMemory")]
    [DataRow("TrustedTruth")]
    public void GroundingEvidenceType_RejectsForbiddenEvidenceTypes(string evidenceType)
    {
        AssertHasIssue(
            Validator.Validate(ValidCreateRequest() with { EvidenceType = evidenceType }),
            GroundingEvidenceReferenceValidator.EvidenceTypeForbidden);
    }

    [DataTestMethod]
    [DataRow("HandoffSummary")]
    [DataRow("ThoughtLedgerEntry")]
    [DataRow("DebugFinding")]
    [DataRow("ReviewFinding")]
    [DataRow("MemoryCandidateClaim")]
    [DataRow("SourceApplyCandidateClaim")]
    public void GroundingClaimType_AllowsExpectedClaimTypes(string claimType)
    {
        AssertValid(Validator.Validate(ValidCreateRequest() with { ClaimType = claimType }));
    }

    [DataTestMethod]
    [DataRow("ApprovedClaim")]
    [DataRow("ExecutionAllowedClaim")]
    [DataRow("PolicySatisfiedClaim")]
    [DataRow("MemoryPromotedClaim")]
    [DataRow("ReleaseApprovedClaim")]
    [DataRow("AcceptedMemoryClaim")]
    public void GroundingClaimType_RejectsForbiddenClaimTypes(string claimType)
    {
        AssertHasIssue(
            Validator.Validate(ValidCreateRequest() with { ClaimType = claimType }),
            GroundingEvidenceReferenceValidator.ClaimTypeForbidden);
    }

    [DataTestMethod]
    [DataRow("ClaimSupport")]
    [DataRow("Context")]
    [DataRow("Review")]
    [DataRow("Traceability")]
    [DataRow("AuditReference")]
    [DataRow("HumanDecisionSupport")]
    public void GroundingAllowedUse_AllowsExpectedAllowedUses(string allowedUse)
    {
        AssertValid(Validator.Validate(ValidCreateRequest() with { AllowedUses = [allowedUse] }));
    }

    [DataTestMethod]
    [DataRow("Approval")]
    [DataRow("ExecutionPermission")]
    [DataRow("PolicySatisfied")]
    [DataRow("WorkflowContinuation")]
    [DataRow("SourceApplyPermission")]
    [DataRow("MemoryPromotionPermission")]
    [DataRow("ReleaseApproval")]
    [DataRow("AcceptedMemory")]
    public void GroundingAllowedUse_RejectsForbiddenAllowedUses(string allowedUse)
    {
        AssertHasIssue(
            Validator.Validate(ValidCreateRequest() with { AllowedUses = [allowedUse] }),
            GroundingEvidenceReferenceValidator.AllowedUseForbidden);
    }

    [TestMethod]
    public void Validate_RejectsInvalidShapes()
    {
        var cases = new (GroundingEvidenceReferenceCreateRequest Request, string Code)[]
        {
            (ValidCreateRequest() with { GroundingEvidenceReferenceId = Guid.Empty }, GroundingEvidenceReferenceValidator.ReferenceIdRequired),
            (ValidCreateRequest() with { ProjectId = Guid.Empty }, GroundingEvidenceReferenceValidator.ProjectIdRequired),
            (ValidCreateRequest() with { EvidenceType = string.Empty }, GroundingEvidenceReferenceValidator.EvidenceTypeRequired),
            (ValidCreateRequest() with { EvidenceType = "ModelMemoryBlob" }, GroundingEvidenceReferenceValidator.EvidenceTypeInvalid),
            (ValidCreateRequest() with { EvidenceType = "AuthorityTransfer" }, GroundingEvidenceReferenceValidator.EvidenceTypeForbidden),
            (ValidCreateRequest() with { EvidenceId = string.Empty }, GroundingEvidenceReferenceValidator.EvidenceIdRequired),
            (ValidCreateRequest() with { ClaimType = string.Empty }, GroundingEvidenceReferenceValidator.ClaimTypeRequired),
            (ValidCreateRequest() with { ClaimType = "ModelClaim" }, GroundingEvidenceReferenceValidator.ClaimTypeInvalid),
            (ValidCreateRequest() with { ClaimType = "ApprovedClaim" }, GroundingEvidenceReferenceValidator.ClaimTypeForbidden),
            (ValidCreateRequest() with { ClaimId = string.Empty }, GroundingEvidenceReferenceValidator.ClaimIdRequired),
            (ValidCreateRequest() with { AllowedUses = null! }, GroundingEvidenceReferenceValidator.AllowedUseRequired),
            (ValidCreateRequest() with { AllowedUses = [] }, GroundingEvidenceReferenceValidator.AllowedUseRequired),
            (ValidCreateRequest() with { AllowedUses = ["ModelTruth"] }, GroundingEvidenceReferenceValidator.AllowedUseInvalid),
            (ValidCreateRequest() with { AllowedUses = ["Approval"] }, GroundingEvidenceReferenceValidator.AllowedUseForbidden),
            (ValidCreateRequest() with { AllowedUses = ["Review", "Review"] }, GroundingEvidenceReferenceValidator.AllowedUseDuplicate),
            (ValidCreateRequest() with { CreatedByActorType = string.Empty }, GroundingEvidenceReferenceValidator.ActorRequired),
            (ValidCreateRequest() with { CreatedByActorId = string.Empty }, GroundingEvidenceReferenceValidator.ActorRequired),
            (ValidCreateRequest() with { MetadataVersion = 0 }, GroundingEvidenceReferenceValidator.MetadataVersionInvalid),
            (ValidCreateRequest() with { MetadataJson = string.Empty }, GroundingEvidenceReferenceValidator.MetadataJsonRequired),
            (ValidCreateRequest() with { MetadataJson = "{not-json}" }, GroundingEvidenceReferenceValidator.MetadataJsonInvalid)
        };

        foreach (var testCase in cases)
            AssertHasIssue(Validator.Validate(testCase.Request), testCase.Code);
    }

    [TestMethod]
    public void Validate_RejectsAuthorityLanguageInEvidenceLabelAndSafeSummary()
    {
        AssertHasIssue(
            Validator.Validate(ValidCreateRequest() with { EvidenceLabel = "approval granted" }),
            GroundingEvidenceReferenceValidator.AuthorityTextBlocked);
        AssertHasIssue(
            Validator.Validate(ValidCreateRequest() with { SafeSummary = "policy satisfied and can ship" }),
            GroundingEvidenceReferenceValidator.AuthorityTextBlocked);
    }

    [TestMethod]
    public void Validate_RejectsPrivateReasoningMarkers()
    {
        var cases = new[]
        {
            ValidCreateRequest() with { EvidenceLabel = "hiddenReasoning marker" },
            ValidCreateRequest() with { SafeSummary = "chain-of-thought marker" },
            ValidCreateRequest() with { MetadataJson = "{\"schema\":\"grounding.evidence.reference.metadata.v1\",\"rawPrompt\":\"nope\"}" },
            ValidCreateRequest() with { Location = ValidLocation() with { AnchorText = "scratchpad marker" } },
            ValidCreateRequest() with { Location = ValidLocation() with { SourcePath = "rawCompletion.txt" } }
        };

        foreach (var request in cases)
            AssertHasIssue(Validator.Validate(request), GroundingEvidenceReferenceValidator.PrivateReasoningBlocked);
    }

    [TestMethod]
    public void Validate_RejectsInvalidLocationLineRange()
    {
        AssertHasIssue(
            Validator.Validate(ValidCreateRequest() with { Location = ValidLocation() with { StartLine = 0 } }),
            GroundingEvidenceReferenceValidator.LocationInvalid);
        AssertHasIssue(
            Validator.Validate(ValidCreateRequest() with { Location = ValidLocation() with { StartLine = 20, EndLine = 10 } }),
            GroundingEvidenceReferenceValidator.LocationInvalid);
        AssertHasIssue(
            Validator.Validate(ValidCreateRequest() with { Location = ValidLocation() with { AnchorText = new string('a', 241) } }),
            GroundingEvidenceReferenceValidator.LocationInvalid);
    }

    [TestMethod]
    public void Validate_RejectsAuthorityGrantingMetadata()
    {
        var cases = new[]
        {
            "{\"schema\":\"grounding.evidence.reference.metadata.v1\",\"approved\":true}",
            "{\"schema\":\"grounding.evidence.reference.metadata.v1\",\"canExecute\":true}",
            "{\"schema\":\"grounding.evidence.reference.metadata.v1\",\"policySatisfied\":true}",
            "{\"schema\":\"grounding.evidence.reference.metadata.v1\",\"sourceApplyAllowed\":true}",
            "{\"schema\":\"grounding.evidence.reference.metadata.v1\",\"memoryPromotionAllowed\":true}",
            "{\"schema\":\"grounding.evidence.reference.metadata.v1\",\"releaseApproved\":true}",
            "{\"schema\":\"grounding.evidence.reference.metadata.v1\",\"acceptedMemory\":true}",
            "{\"schema\":\"grounding.evidence.reference.metadata.v1\",\"transfersAuthority\":true}"
        };

        foreach (var metadataJson in cases)
            AssertHasIssue(
                Validator.Validate(ValidCreateRequest() with { MetadataJson = metadataJson }),
                GroundingEvidenceReferenceValidator.AuthorityMetadataBlocked);
    }

    [TestMethod]
    public void Validate_RejectsTrueAuthorityFlags()
    {
        var cases = new[]
        {
            ValidCreateRequest() with { GrantsApproval = true },
            ValidCreateRequest() with { GrantsExecution = true },
            ValidCreateRequest() with { MutatesSource = true },
            ValidCreateRequest() with { PromotesMemory = true },
            ValidCreateRequest() with { StartsWorkflow = true },
            ValidCreateRequest() with { SatisfiesPolicy = true },
            ValidCreateRequest() with { TransfersAuthority = true }
        };

        foreach (var request in cases)
            AssertHasIssue(Validator.Validate(request), GroundingEvidenceReferenceValidator.AuthorityFlagBlocked);
    }

    [TestMethod]
    public void GroundingEvidenceReferenceFactory_CreatesReferenceFromHandoffEvidence()
    {
        var governanceEventId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var evidence = new AgentHandoffEvidenceReference
        {
            EvidenceType = AgentHandoffEvidenceType.ToolGateDecision,
            EvidenceId = "gate-decision-1",
            AllowedUses =
            [
                AgentHandoffEvidenceAllowedUse.Review,
                AgentHandoffEvidenceAllowedUse.Traceability
            ],
            EvidenceLabel = "Gate decision evidence",
            EvidenceSummary = "Gate decision evidence for review.",
            GovernanceEventId = governanceEventId
        };

        var request = new GroundingEvidenceReferenceFactory().CreateFromHandoffEvidence(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "ReviewFinding",
            "finding-1",
            evidence,
            "agent",
            "critic");

        Assert.AreEqual("ToolGateDecision", request.EvidenceType);
        Assert.AreEqual("gate-decision-1", request.EvidenceId);
        Assert.AreEqual("ReviewFinding", request.ClaimType);
        Assert.AreEqual("finding-1", request.ClaimId);
        Assert.AreEqual(governanceEventId, request.GovernanceEventId);
        CollectionAssert.Contains(request.AllowedUses.ToArray(), "Review");
        CollectionAssert.Contains(request.AllowedUses.ToArray(), "Traceability");
        CollectionAssert.Contains(request.AllowedUses.ToArray(), "ClaimSupport");
        AssertAuthorityFlagsFalse(request);
        AssertValid(Validator.Validate(request));
    }

    [TestMethod]
    public void GroundingEvidenceReferenceFactory_DoesNotWriteSqlCreateGovernanceEventThoughtLedgerEntryOrCallRuntime()
    {
        var source = ReadRepositoryFile("IronDev.Core", "Agents", "GroundingEvidenceReferenceModels.cs");
        var forbidden = new[]
        {
            "SqlConnection",
            "INSERT INTO",
            "UPDATE ",
            "DELETE ",
            "MERGE ",
            "CreateGovernanceEvent",
            "RecordGovernanceEvent",
            "CreateThoughtLedger",
            "RecordThoughtLedger",
            "HttpClient",
            "ProcessStartInfo",
            "IHostedService",
            "BackgroundService",
            "WorkflowRunner",
            "LangGraphRuntime",
            "MessageBus",
            "QueueClient",
            "Inbox",
            "Outbox"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"Forbidden token found in grounding contract file: {token}");
    }

    [TestMethod]
    public void GroundingEvidenceReference_IsNotApprovalExecutionPolicyWorkflowSourceMemoryReleaseAcceptedMemoryOrAuthorityTransfer()
    {
        var reference = ValidReference();

        Assert.IsFalse(reference.GrantsApproval);
        Assert.IsFalse(reference.GrantsExecution);
        Assert.IsFalse(reference.SatisfiesPolicy);
        Assert.IsFalse(reference.StartsWorkflow);
        Assert.IsFalse(reference.MutatesSource);
        Assert.IsFalse(reference.PromotesMemory);
        Assert.IsFalse(reference.TransfersAuthority);

        CollectionAssert.Contains(reference.AllowedUses.ToArray(), "ClaimSupport");
    }

    [TestMethod]
    public void GroundingEvidenceReference_DoesNotAddApiEndpointCliCommandSqlMigrationRepositoryOrRuntimeDi()
    {
        AssertNoProductionReference("IronDev.Api", "GroundingEvidenceReference");
        AssertNoProductionReference(Path.Combine("tools", "IronDev.Cli"), "GroundingEvidenceReference");
        AssertNoDatabaseReference("GroundingEvidenceReference", "Database/migrate_workflow_run.sql", "Database/migrate_workflow_step_store.sql", "Database/smoke-workflow-run.ps1", "Database/verify-migrations.ps1", "Database/sql-inventory.json");
        AssertNoProductionToken("IGroundingEvidenceReferenceStore", "SqlGroundingEvidenceReference", "GroundingEvidenceReferenceRepository");
        AssertNoProductionToken("AddScoped<IGroundingEvidenceReferenceFactory", "AddSingleton<IGroundingEvidenceReferenceFactory");
    }

    [TestMethod]
    public void GroundingEvidenceReference_DoesNotReferenceWorkflowRunnerExecutorMemoryPromotionSourceApplyLangGraphA2aRuntimeOrMessageBus()
    {
        var source = ReadRepositoryFile("IronDev.Core", "Agents", "GroundingEvidenceReferenceModels.cs");
        var forbidden = new[]
        {
            "IAgentToolExecutor",
            "ExecuteAsync",
            "RunTool",
            "SqlCollectiveMemoryPromotionService",
            "IDisposableWorkspaceApply",
            "WorkspaceApplyCopy",
            "WorkflowRunner",
            "LangGraphRuntime",
            "IA2aRuntime",
            "A2aRuntime",
            "MessageBus",
            "QueueClient"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"Forbidden runtime token found in grounding contract file: {token}");
    }

    [TestMethod]
    public void BlockIDocumentation_DescribesGroundingEvidenceReferenceBoundary()
    {
        var doc = ReadRepositoryFile("Docs", "BLOCK_I_A2A_HANDOFF_CONTRACT_SPINE.md");

        foreach (var expected in new[]
        {
            "PR95 Grounding Evidence Reference Contract",
            "PR95 adds a shared grounding evidence reference contract.",
            "Grounding references support traceability and claim support.",
            "Grounding references do not approve claims.",
            "Grounding references do not execute tools.",
            "Grounding references do not satisfy policy.",
            "Grounding references do not continue workflow.",
            "Grounding references do not mutate source.",
            "Grounding references do not promote memory.",
            "Grounding references do not approve release.",
            "Grounding references do not create accepted memory.",
            "Grounding references must not contain hidden/private reasoning."
        })
        {
            StringAssert.Contains(doc, expected);
        }
    }

    [TestMethod]
    public void BlockIDocumentation_RemainsAsciiOnly()
    {
        var path = Path.Combine(RepositoryRoot(), "Docs", "BLOCK_I_A2A_HANDOFF_CONTRACT_SPINE.md");
        var bytes = File.ReadAllBytes(path);

        Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "Block I doc must not contain UTF-8 BOM.");

        for (var index = 0; index < bytes.Length; index++)
            Assert.IsTrue(bytes[index] <= 0x7F, $"Block I doc must be ASCII-only. Non-ASCII byte 0x{bytes[index]:X2} at offset {index}.");
    }

    private static GroundingEvidenceReferenceCreateRequest ValidCreateRequest() =>
        new()
        {
            ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            EvidenceType = "AgentHandoff",
            EvidenceId = "handoff-1",
            ClaimType = "HandoffSummary",
            ClaimId = "claim-1",
            EvidenceLabel = "Handoff evidence",
            SafeSummary = "Grounding reference for review.",
            AllowedUses = ["Context", "Traceability", "ClaimSupport"],
            Location = ValidLocation(),
            GovernanceEventId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            AgentHandoffId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ThoughtLedgerEntryId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            CreatedByActorType = "agent",
            CreatedByActorId = "critic",
            MetadataVersion = 1,
            MetadataJson = SafeMetadataJson(),
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false
        };

    private static GroundingEvidenceReference ValidReference() =>
        new()
        {
            GroundingEvidenceReferenceId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            EvidenceType = "AgentHandoff",
            EvidenceId = "handoff-1",
            ClaimType = "HandoffSummary",
            ClaimId = "claim-1",
            EvidenceLabel = "Handoff evidence",
            SafeSummary = "Grounding reference for review.",
            AllowedUses = ["Context", "Traceability", "ClaimSupport"],
            Location = ValidLocation(),
            GovernanceEventId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            AgentHandoffId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ThoughtLedgerEntryId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            CreatedByActorType = "agent",
            CreatedByActorId = "critic",
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

    private static GroundingEvidenceReferenceLocation ValidLocation() =>
        new()
        {
            SourcePath = "Docs/BLOCK_I_A2A_HANDOFF_CONTRACT_SPINE.md",
            StartLine = 10,
            EndLine = 12,
            SectionId = "handoff-boundary",
            AnchorText = "A handoff transfers context and evidence only."
        };

    private static string SafeMetadataJson() =>
        """
        {
          "schema": "grounding.evidence.reference.metadata.v1",
          "notes": "Grounding reference for review claim.",
          "grantsApproval": false,
          "grantsExecution": false,
          "mutatesSource": false,
          "promotesMemory": false,
          "startsWorkflow": false,
          "satisfiesPolicy": false,
          "transfersAuthority": false
        }
        """;

    private static void AssertAuthorityFlagsFalse(GroundingEvidenceReferenceCreateRequest request)
    {
        Assert.IsFalse(request.GrantsApproval);
        Assert.IsFalse(request.GrantsExecution);
        Assert.IsFalse(request.MutatesSource);
        Assert.IsFalse(request.PromotesMemory);
        Assert.IsFalse(request.StartsWorkflow);
        Assert.IsFalse(request.SatisfiesPolicy);
        Assert.IsFalse(request.TransfersAuthority);
    }

    private static void AssertValid(GroundingEvidenceReferenceValidationResult result)
    {
        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        Assert.AreEqual(0, result.Issues.Count);
    }

    private static void AssertHasIssue(GroundingEvidenceReferenceValidationResult result, string code)
    {
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == code), $"Expected issue {code}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertHasProperty<T>(string name) =>
        Assert.IsNotNull(typeof(T).GetProperty(name), $"{typeof(T).Name} missing property {name}.");

    private static void AssertVocabularyExcludes(IReadOnlySet<string> vocabulary, params string[] forbidden)
    {
        foreach (var value in forbidden)
            Assert.IsFalse(vocabulary.Contains(value), $"Vocabulary must not include {value}.");
    }

    private static string ReadRepositoryFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), .. parts]));

    private static void AssertNoProductionReference(string relativeRoot, string token)
    {
        var root = Path.Combine(RepositoryRoot(), relativeRoot);
        if (!Directory.Exists(root))
            return;

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
        {
            var source = File.ReadAllText(file);
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"{token} must not be referenced by {file}.");
        }
    }

    private static void AssertNoDatabaseReference(string token, params string[] allowedRelativePaths)
    {
        var repositoryRoot = RepositoryRoot();
        var root = Path.Combine(repositoryRoot, "Database");
        var allowed = allowedRelativePaths.Select(path => Path.GetFullPath(Path.Combine(repositoryRoot, path))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)
                                    || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                                    || path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)))
        {
            if (allowed.Contains(Path.GetFullPath(file)))
                continue;

            var source = File.ReadAllText(file);
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"{token} must not be referenced by database artifact {file}.");
        }
    }

    private static void AssertNoProductionToken(params string[] tokens)
    {
        var root = RepositoryRoot();
        var files = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}IronDev.IntegrationTests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            foreach (var token in tokens)
                Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"Forbidden token {token} found in {file}.");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        Assert.IsNotNull(directory, "Repository root not found.");
        return directory!.FullName;
    }
}
