using IronDev.Core.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
[TestCategory("A2aContractValidation")]
[TestCategory("A2aContractComposition")]
[TestCategory("A2aAuthoritySeparation")]
[TestCategory("A2aEvidenceOnlySemantics")]
public sealed class A2aContractValidationTestPackTests
{
    private static readonly Guid ProjectId = Guid.Parse("61111111-1111-4111-8111-111111111111");
    private static readonly Guid HandoffId = Guid.Parse("62222222-2222-4222-8222-222222222222");
    private static readonly Guid ThoughtLedgerEntryId = Guid.Parse("63333333-3333-4333-8333-333333333333");
    private static readonly Guid GroundingReferenceId = Guid.Parse("64444444-4444-4444-8444-444444444444");
    private static readonly Guid CorrelationId = Guid.Parse("65555555-5555-4555-8555-555555555555");
    private static readonly Guid CausationId = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly DateTimeOffset CreatedUtc = new(2026, 6, 13, 0, 0, 0, TimeSpan.Zero);

    private static readonly AgentHandoffValidator HandoffValidator = new();
    private static readonly AgentHandoffAuthorityTransferValidator AuthorityTransferValidator = new();
    private static readonly ThoughtLedgerHandoffEntryFactory ThoughtLedgerFactory = new();
    private static readonly ThoughtLedgerHandoffEntryValidator ThoughtLedgerValidator = new();
    private static readonly GroundingEvidenceReferenceValidator GroundingValidator = new();

    [TestMethod]
    public void A2aContractValidation_ComposesHandoffStoreThoughtLedgerAndGroundingEvidence()
    {
        var composed = Compose();

        AssertValid(HandoffValidator.Validate(composed.Handoff));
        AssertSafe(AuthorityTransferValidator.Validate(composed.Handoff));
        AssertValid(ThoughtLedgerValidator.Validate(composed.ThoughtLedgerEntry));
        AssertValid(GroundingValidator.Validate(composed.GroundingReference));

        Assert.AreEqual(composed.Handoff.ProjectId, composed.ThoughtLedgerEntry.ProjectId);
        Assert.AreEqual(composed.Handoff.ProjectId, composed.GroundingReference.ProjectId);
        Assert.AreEqual(composed.Handoff.AgentHandoffId, composed.ThoughtLedgerEntry.AgentHandoffId);
        Assert.AreEqual(composed.Handoff.AgentHandoffId, composed.GroundingReference.AgentHandoffId);
        Assert.AreEqual(composed.ThoughtLedgerEntry.ThoughtLedgerHandoffEntryId, composed.GroundingReference.ThoughtLedgerEntryId);
        Assert.AreEqual("ThoughtLedgerHandoffEntry", composed.GroundingReference.EvidenceType);
        Assert.AreEqual(composed.ThoughtLedgerEntry.ThoughtLedgerHandoffEntryId.ToString(), composed.GroundingReference.EvidenceId);
        AssertAllAuthorityFlagsFalse(composed);
    }

    [TestMethod]
    public void A2aContractValidation_PreservesCorrelationCausationAgentsSubjectEvidenceAllowedUsesAndConstraints()
    {
        var composed = Compose();

        Assert.AreEqual(CorrelationId, composed.Handoff.CorrelationId);
        Assert.AreEqual(CorrelationId, composed.ThoughtLedgerEntry.CorrelationId);
        Assert.AreEqual(CorrelationId, composed.GroundingReference.CorrelationId);
        Assert.AreEqual(CausationId, composed.Handoff.CausationId);
        Assert.AreEqual(CausationId, composed.ThoughtLedgerEntry.CausationId);
        Assert.AreEqual(CausationId, composed.GroundingReference.CausationId);
        Assert.AreEqual("planner-agent", composed.ThoughtLedgerEntry.SourceAgentId);
        Assert.AreEqual("critic-agent", composed.ThoughtLedgerEntry.TargetAgentId);
        Assert.AreEqual("run-report-1", composed.ThoughtLedgerEntry.SubjectId);
        CollectionAssert.AreEquivalent(
            new[] { "Review", "Traceability", "AuditReference" },
            composed.ThoughtLedgerEntry.EvidenceSummaries.Single().AllowedUses.ToArray());
        CollectionAssert.Contains(composed.GroundingReference.AllowedUses.ToArray(), "ClaimSupport");
        Assert.AreEqual(3, composed.ThoughtLedgerEntry.ConstraintSummaries.Count);
    }

    [DataTestMethod]
    [DataRow(nameof(AgentHandoff.GrantsApproval))]
    [DataRow(nameof(AgentHandoff.GrantsExecution))]
    [DataRow(nameof(AgentHandoff.MutatesSource))]
    [DataRow(nameof(AgentHandoff.PromotesMemory))]
    [DataRow(nameof(AgentHandoff.StartsWorkflow))]
    [DataRow(nameof(AgentHandoff.SatisfiesPolicy))]
    [DataRow(nameof(AgentHandoff.TransfersAuthority))]
    public void A2aContractValidation_ComposedContractsNeverSetAuthorityFlagsTrue(string flagName)
    {
        var composed = Compose();

        AssertBooleanPropertyFalse(composed.Handoff, flagName);
        AssertBooleanPropertyFalse(composed.ThoughtLedgerEntry, flagName);
        AssertBooleanPropertyFalse(composed.GroundingReference, flagName);
    }

    [DataTestMethod]
    [DataRow("Approval", "ForbiddenApprovalEvidenceType")]
    [DataRow("ExecutionPermission", "ForbiddenExecutionEvidenceType")]
    [DataRow("PolicySatisfied", "ForbiddenPolicyEvidenceType")]
    [DataRow("SourceApplyPermission", "ForbiddenSourceApplyEvidenceType")]
    [DataRow("MemoryPromotionPermission", "ForbiddenMemoryEvidenceType")]
    [DataRow("ReleaseApproval", "ForbiddenReleaseEvidenceType")]
    [DataRow("AuthorityTransfer", "ForbiddenAuthorityEvidenceType")]
    [DataRow("AcceptedMemory", "ForbiddenAcceptedMemoryEvidenceType")]
    public void A2aContractValidation_GroundingEvidenceDoesNotLaunderAuthority(string evidenceType, string scenario)
    {
        var grounding = GroundingReference(evidenceType: evidenceType);

        var result = GroundingValidator.Validate(grounding);

        Assert.IsFalse(result.IsValid, scenario);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == GroundingEvidenceReferenceValidator.EvidenceTypeForbidden), scenario);
    }

    [DataTestMethod]
    [DataRow("Approval")]
    [DataRow("ExecutionPermission")]
    [DataRow("PolicySatisfied")]
    [DataRow("WorkflowContinuation")]
    [DataRow("SourceApplyPermission")]
    [DataRow("MemoryPromotionPermission")]
    [DataRow("ReleaseApproval")]
    [DataRow("AuthorityTransfer")]
    [DataRow("AcceptedMemory")]
    public void A2aContractValidation_AllowedUseCannotBecomeCapability(string allowedUse)
    {
        var grounding = GroundingReference(allowedUses: ["Review", allowedUse]);

        var result = GroundingValidator.Validate(grounding);

        Assert.IsFalse(result.IsValid, allowedUse);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == GroundingEvidenceReferenceValidator.AllowedUseForbidden), allowedUse);
    }

    [DataTestMethod]
    [DataRow("hiddenReasoning")]
    [DataRow("chainOfThought")]
    [DataRow("private reasoning")]
    [DataRow("scratchpad")]
    [DataRow("rawPrompt")]
    [DataRow("rawCompletion")]
    [DataRow("rawToolOutput")]
    [DataRow("entirePatch")]
    public void A2aContractValidation_ComposedContractsRejectHiddenPrivateReasoningAndRawDumps(string marker)
    {
        AssertHasIssue(
            HandoffValidator.Validate(ValidHandoff() with { MetadataJson = $"{{\"schema\":\"agent.handoff.metadata.v1\",\"notes\":\"{marker}\"}}" }),
            AgentHandoffValidator.PrivateReasoningBlocked);
        Assert.IsFalse(AuthorityTransferValidator.Validate(ValidHandoff() with { MetadataJson = $"{{\"schema\":\"agent.handoff.metadata.v1\",\"notes\":\"{marker}\"}}" }).IsSafe);
        AssertHasIssue(
            ThoughtLedgerValidator.Validate(Compose().ThoughtLedgerEntry with { SafeSummary = marker }),
            ThoughtLedgerHandoffEntryValidator.PrivateReasoningBlocked);
        AssertHasIssue(
            GroundingValidator.Validate(GroundingReference(safeSummary: marker)),
            GroundingEvidenceReferenceValidator.PrivateReasoningBlocked);
    }

    [DataTestMethod]
    [DataRow("approved")]
    [DataRow("authorized")]
    [DataRow("execution allowed")]
    [DataRow("policy satisfied")]
    [DataRow("workflow continued")]
    [DataRow("source apply allowed")]
    [DataRow("memory promotion allowed")]
    [DataRow("release approved")]
    [DataRow("authority transferred")]
    public void A2aContractValidation_ComposedContractsRejectAuthorityLanguage(string marker)
    {
        Assert.IsFalse(AuthorityTransferValidator.Validate(ValidHandoff() with { Subject = Subject(summary: marker) }).IsSafe);
        AssertHasIssue(
            ThoughtLedgerValidator.Validate(Compose().ThoughtLedgerEntry with { SafeSummary = marker }),
            ThoughtLedgerHandoffEntryValidator.AuthorityTextBlocked);
    }

    [DataTestMethod]
    [DataRow("approved")]
    [DataRow("execution permission")]
    [DataRow("policy satisfied")]
    [DataRow("workflow continuation")]
    [DataRow("source apply permission")]
    [DataRow("memory promotion permission")]
    [DataRow("release approval")]
    [DataRow("authority transfer")]
    [DataRow("accepted memory")]
    public void A2aContractValidation_GroundingReferenceRejectsAuthorityLanguage(string marker)
    {
        AssertHasIssue(
            GroundingValidator.Validate(GroundingReference(safeSummary: marker)),
            GroundingEvidenceReferenceValidator.AuthorityTextBlocked);
    }

    [TestMethod]
    public void A2aContractValidation_DurableHandoffStoreContractHasNoDeliveryOrExecutionSurface()
    {
        var methodNames = typeof(IAgentHandoffStore).GetMethods().Select(method => method.Name).ToArray();

        AssertNoTokens(string.Join("\n", methodNames), "IAgentHandoffStore", "Send", "Receive", "Accept", "Dispatch", "Execute", "ContinueWorkflow", "TransferAuthority", "Approve");
    }

    [TestMethod]
    public void A2aContractValidation_UnsafeHandoffCannotBecomeThoughtLedgerHandoffEntry()
    {
        var unsafeHandoff = ValidHandoff() with { GrantsExecution = true };

        AssertHasIssue(HandoffValidator.Validate(unsafeHandoff), AgentHandoffValidator.AuthorityFlagBlocked);
        Assert.IsFalse(AuthorityTransferValidator.Validate(unsafeHandoff).IsSafe);
    }

    [TestMethod]
    public void A2aContractValidation_ProjectScopeStaysStableAcrossComposedContracts()
    {
        var first = Compose(projectId: ProjectId);
        var second = Compose(projectId: Guid.Parse("67777777-7777-4777-8777-777777777777"), handoffId: Guid.Parse("68888888-8888-4888-8888-888888888888"));

        Assert.AreEqual(first.Handoff.ProjectId, first.ThoughtLedgerEntry.ProjectId);
        Assert.AreEqual(first.Handoff.ProjectId, first.GroundingReference.ProjectId);
        Assert.AreEqual(second.Handoff.ProjectId, second.ThoughtLedgerEntry.ProjectId);
        Assert.AreEqual(second.Handoff.ProjectId, second.GroundingReference.ProjectId);
        Assert.AreNotEqual(first.Handoff.ProjectId, second.Handoff.ProjectId);
        Assert.AreNotEqual(first.ThoughtLedgerEntry.AgentHandoffId, second.ThoughtLedgerEntry.AgentHandoffId);
        Assert.AreNotEqual(first.GroundingReference.AgentHandoffId, second.GroundingReference.AgentHandoffId);
    }

    [TestMethod]
    public void A2aContractValidation_StaticBoundary_DoesNotAddApiCliSqlRuntimeWorkflowModelOrMessagingSurface()
    {
        AssertNoProductionReference("IronDev.Api", "A2aContractValidation");
        AssertNoProductionReference(Path.Combine("tools", "IronDev.Cli"), "A2aContractValidation");
        AssertNoProductionReference("IronDev.Api", "A2aContractValidationTestPack");
        AssertNoProductionReference(Path.Combine("tools", "IronDev.Cli"), "A2aContractValidationTestPack");
        AssertNoDatabaseReference("A2aContractValidation");
        AssertNoDatabaseReference("A2aContractValidationTestPack");
        AssertNoDatabaseReference("BLOCK_I_A2A_CONTRACT_VALIDATION_TEST_PACK");
    }

    [TestMethod]
    public void A2aContractValidation_DocumentationDefinesEvidenceOnlyCompositionAndNonGoals()
    {
        var doc = ReadRepositoryFile("Docs", "BLOCK_I_A2A_CONTRACT_VALIDATION_TEST_PACK.md");

        foreach (var required in new[]
                 {
                     "Purpose",
                     "Composition Chain",
                     "Evidence-Only Semantics",
                     "Authority Boundary",
                     "Hidden Reasoning Boundary",
                     "Static No-Runtime Boundary",
                     "What This Test Pack Proves",
                     "What This Test Pack Does Not Prove"
                 })
        {
            StringAssert.Contains(doc, required);
        }

        StringAssert.Contains(doc, "The pack proves that PR90 through PR95 contracts compose without turning evidence into authority.");
        StringAssert.Contains(doc, "This pack does not prove runtime delivery, inbox processing, target-agent acceptance, workflow continuation, LangGraph orchestration, API or CLI exposure, source apply, memory promotion, release approval, policy satisfaction, or execution.");
    }

    [TestMethod]
    public void A2aContractValidation_BlockISpineMentionsPr96WithoutAddingRuntimeAuthority()
    {
        var spine = ReadRepositoryFile("Docs", "BLOCK_I_A2A_HANDOFF_CONTRACT_SPINE.md");

        StringAssert.Contains(spine, "PR96 A2A Contract Validation Test Pack");
        StringAssert.Contains(spine, "PR96 adds a contract validation test pack for the Block I A2A spine.");
        StringAssert.Contains(spine, "The pack proves that handoff, allowed-use evidence, no-authority-transfer validation, durable handoff storage, ThoughtLedger handoff entries, and grounding evidence references compose without creating approval, execution permission, workflow continuation, source mutation, memory promotion, accepted memory, release approval, policy satisfaction, or authority transfer.");
        StringAssert.Contains(spine, "The pack adds no A2A runtime, transport, API, CLI, workflow runner, LangGraph, source apply, memory promotion, accepted memory, release approval, approval satisfaction, or execution path.");
    }

    [TestMethod]
    public void A2aContractValidation_DocumentationIsAsciiAndHasNoHiddenUnicode()
    {
        foreach (var relativePath in new[]
                 {
                     Path.Combine("Docs", "BLOCK_I_A2A_CONTRACT_VALIDATION_TEST_PACK.md"),
                     Path.Combine("Docs", "BLOCK_I_A2A_HANDOFF_CONTRACT_SPINE.md")
                 })
        {
            var bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot(), relativePath));
            Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, relativePath);
            Assert.IsTrue(bytes.All(b => b is 9 or 10 or 13 || (b >= 32 && b <= 126)), relativePath);
        }
    }

    private static A2aComposedContracts Compose(Guid? projectId = null, Guid? handoffId = null)
    {
        var handoff = ValidHandoff(projectId, handoffId);
        var thoughtLedger = ThoughtLedgerFactory.CreateFromHandoff(handoff, "agent", "critic-agent") with
        {
            ThoughtLedgerHandoffEntryId = ThoughtLedgerEntryId,
            MetadataJson = "{\"schema\":\"thoughtledger.handoff.entry.v1\",\"recordsHandoffOnly\":true}"
        };
        var grounding = GroundingReference(
            projectId: thoughtLedger.ProjectId,
            evidenceType: "ThoughtLedgerHandoffEntry",
            evidenceId: thoughtLedger.ThoughtLedgerHandoffEntryId!.Value.ToString(),
            claimType: "HandoffSummary",
            claimId: handoff.AgentHandoffId.ToString(),
            agentHandoffId: handoff.AgentHandoffId,
            thoughtLedgerEntryId: thoughtLedger.ThoughtLedgerHandoffEntryId,
            correlationId: handoff.CorrelationId,
            causationId: handoff.CausationId);

        return new A2aComposedContracts(handoff, thoughtLedger, grounding);
    }

    private static AgentHandoff ValidHandoff(Guid? projectId = null, Guid? handoffId = null) =>
        new()
        {
            AgentHandoffId = handoffId ?? HandoffId,
            ProjectId = projectId ?? ProjectId,
            HandoffType = AgentHandoffType.ReviewRequest,
            Status = AgentHandoffStatus.ReadyForReview,
            SourceAgent = Participant("planner-agent", AgentHandoffParticipantRole.Planner),
            TargetAgent = Participant("critic-agent", AgentHandoffParticipantRole.Critic),
            Subject = Subject(),
            EvidenceReferences =
            [
                new AgentHandoffEvidenceReference
                {
                    EvidenceType = AgentHandoffEvidenceType.RunReport,
                    EvidenceId = "run-report-1",
                    AllowedUses = [AgentHandoffEvidenceAllowedUse.Review, AgentHandoffEvidenceAllowedUse.Traceability, AgentHandoffEvidenceAllowedUse.AuditReference],
                    EvidenceLabel = "Run report",
                    EvidenceSummary = "Run report excerpt for reviewer context."
                }
            ],
            Constraints =
            [
                Constraint(AgentHandoffConstraintType.RequiresHumanReview, "human-review-required", "Human review remains required."),
                Constraint(AgentHandoffConstraintType.EvidenceOnly, "evidence-only", "Keep as evidence only."),
                Constraint(AgentHandoffConstraintType.DoNotContinueWorkflow, "workflow-stays-paused", "Workflow stays paused.")
            ],
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            CreatedByActorType = "agent",
            CreatedByActorId = "planner-agent",
            MetadataVersion = 1,
            MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"recordsOnly\":true}",
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false,
            CreatedUtc = CreatedUtc
        };

    private static AgentHandoffParticipant Participant(string agentId, AgentHandoffParticipantRole role) =>
        new()
        {
            AgentId = agentId,
            AgentRole = role,
            DisplayName = agentId
        };

    private static AgentHandoffSubject Subject(string summary = "Review the run report and return findings only.") =>
        new()
        {
            SubjectType = AgentHandoffSubjectType.RunReport,
            SubjectId = "run-report-1",
            ActionName = "review",
            Summary = summary
        };

    private static AgentHandoffConstraint Constraint(AgentHandoffConstraintType type, string code, string description) =>
        new()
        {
            ConstraintType = type,
            ConstraintCode = code,
            Description = description
        };

    private static GroundingEvidenceReferenceCreateRequest GroundingReference(
        Guid? projectId = null,
        string evidenceType = "RunReport",
        string evidenceId = "run-report-1",
        string claimType = "HandoffSummary",
        string claimId = "run-report-1",
        string safeSummary = "Evidence supports a review-only handoff summary.",
        IReadOnlyList<string>? allowedUses = null,
        Guid? agentHandoffId = null,
        Guid? thoughtLedgerEntryId = null,
        Guid? correlationId = null,
        Guid? causationId = null) =>
        new()
        {
            GroundingEvidenceReferenceId = GroundingReferenceId,
            ProjectId = projectId ?? ProjectId,
            EvidenceType = evidenceType,
            EvidenceId = evidenceId,
            ClaimType = claimType,
            ClaimId = claimId,
            EvidenceLabel = "Grounding evidence",
            SafeSummary = safeSummary,
            AllowedUses = allowedUses ?? ["Review", "Traceability", "ClaimSupport"],
            AgentHandoffId = agentHandoffId ?? HandoffId,
            ThoughtLedgerEntryId = thoughtLedgerEntryId,
            CorrelationId = correlationId ?? CorrelationId,
            CausationId = causationId ?? CausationId,
            CreatedByActorType = "agent",
            CreatedByActorId = "critic-agent",
            MetadataVersion = 1,
            MetadataJson = "{\"schema\":\"grounding.evidence.reference.metadata.v1\",\"recordsEvidenceOnly\":true}",
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false,
            CreatedUtc = CreatedUtc
        };

    private static void AssertAllAuthorityFlagsFalse(A2aComposedContracts composed)
    {
        foreach (var flagName in new[]
                 {
                     nameof(AgentHandoff.GrantsApproval),
                     nameof(AgentHandoff.GrantsExecution),
                     nameof(AgentHandoff.MutatesSource),
                     nameof(AgentHandoff.PromotesMemory),
                     nameof(AgentHandoff.StartsWorkflow),
                     nameof(AgentHandoff.SatisfiesPolicy),
                     nameof(AgentHandoff.TransfersAuthority)
                 })
        {
            AssertBooleanPropertyFalse(composed.Handoff, flagName);
            AssertBooleanPropertyFalse(composed.ThoughtLedgerEntry, flagName);
            AssertBooleanPropertyFalse(composed.GroundingReference, flagName);
        }
    }

    private static void AssertBooleanPropertyFalse(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName);
        Assert.IsNotNull(property, $"{instance.GetType().Name} missing {propertyName}.");
        Assert.AreEqual(false, property!.GetValue(instance), $"{instance.GetType().Name}.{propertyName} must remain false.");
    }

    private static void AssertValid(AgentHandoffValidationResult result)
    {
        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
    }

    private static void AssertValid(ThoughtLedgerHandoffEntryValidationResult result)
    {
        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
    }

    private static void AssertValid(GroundingEvidenceReferenceValidationResult result)
    {
        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
    }

    private static void AssertSafe(AgentHandoffAuthorityTransferValidationResult result)
    {
        Assert.IsTrue(result.IsSafe, string.Join(Environment.NewLine, result.Violations.Select(violation => $"{violation.Code}: {violation.Message}")));
        Assert.IsFalse(result.GrantsApproval);
        Assert.IsFalse(result.GrantsExecution);
        Assert.IsFalse(result.MutatesSource);
        Assert.IsFalse(result.PromotesMemory);
        Assert.IsFalse(result.StartsWorkflow);
        Assert.IsFalse(result.SatisfiesPolicy);
        Assert.IsFalse(result.TransfersAuthority);
    }

    private static void AssertHasIssue(AgentHandoffValidationResult result, string code)
    {
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == code), $"Expected {code}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertHasIssue(ThoughtLedgerHandoffEntryValidationResult result, string code)
    {
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == code), $"Expected {code}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertHasIssue(GroundingEvidenceReferenceValidationResult result, string code)
    {
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == code), $"Expected {code}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertNoTokens(string source, string context, params string[] forbiddenTokens)
    {
        foreach (var token in forbiddenTokens)
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"{context} must not contain {token}.");
    }

    private static string ReadRepositoryFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), .. parts]));

    private static void AssertNoProductionReference(string relativeRoot, string token)
    {
        var root = Path.Combine(RepositoryRoot(), relativeRoot);
        if (!Directory.Exists(root))
            return;

        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"{file} must not reference {token}.");
        }
    }

    private static void AssertNoDatabaseReference(string token)
    {
        var root = Path.Combine(RepositoryRoot(), "Database");
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"{file} must not reference {token}.");
        }
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

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed record A2aComposedContracts(
        AgentHandoff Handoff,
        ThoughtLedgerHandoffEntryCreateRequest ThoughtLedgerEntry,
        GroundingEvidenceReferenceCreateRequest GroundingReference);
}
