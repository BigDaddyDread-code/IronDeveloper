using System.Reflection;
using IronDev.Core.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
[TestCategory("ThoughtLedgerHandoffEntry")]
[TestCategory("AgentHandoff")]
public sealed class ThoughtLedgerHandoffEntryTests
{
    private static readonly DateTimeOffset CreatedUtc = new(2026, 6, 13, 14, 0, 0, TimeSpan.Zero);
    private static readonly ThoughtLedgerHandoffEntryValidator Validator = new();
    private static readonly ThoughtLedgerHandoffEntryFactory Factory = new();

    [TestMethod]
    public void ThoughtLedgerHandoffEntry_ExposesExpectedShape()
    {
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.ThoughtLedgerHandoffEntryId));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.ProjectId));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.AgentHandoffId));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.GovernanceEventId));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.CorrelationId));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.CausationId));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.HandoffType));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.HandoffStatus));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.SourceAgentId));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.SourceAgentRole));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.TargetAgentId));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.TargetAgentRole));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.SubjectType));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.SubjectId));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.ActionName));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.SafeSummary));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.EvidenceSummaries));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.ConstraintSummaries));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.MetadataJson));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.GrantsApproval));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.GrantsExecution));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.MutatesSource));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.PromotesMemory));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.StartsWorkflow));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.SatisfiesPolicy));
        AssertHasProperty<ThoughtLedgerHandoffEntry>(nameof(ThoughtLedgerHandoffEntry.TransfersAuthority));
    }

    [TestMethod]
    public void ThoughtLedgerHandoffEvidenceSummary_ExposesExpectedShape()
    {
        AssertHasProperty<ThoughtLedgerHandoffEvidenceSummary>(nameof(ThoughtLedgerHandoffEvidenceSummary.EvidenceType));
        AssertHasProperty<ThoughtLedgerHandoffEvidenceSummary>(nameof(ThoughtLedgerHandoffEvidenceSummary.EvidenceId));
        AssertHasProperty<ThoughtLedgerHandoffEvidenceSummary>(nameof(ThoughtLedgerHandoffEvidenceSummary.AllowedUses));
        AssertHasProperty<ThoughtLedgerHandoffEvidenceSummary>(nameof(ThoughtLedgerHandoffEvidenceSummary.EvidenceLabel));
        AssertHasProperty<ThoughtLedgerHandoffEvidenceSummary>(nameof(ThoughtLedgerHandoffEvidenceSummary.SafeEvidenceSummary));
        AssertHasProperty<ThoughtLedgerHandoffEvidenceSummary>(nameof(ThoughtLedgerHandoffEvidenceSummary.GovernanceEventId));
    }

    [TestMethod]
    public void ThoughtLedgerHandoffConstraintSummary_ExposesExpectedShape()
    {
        AssertHasProperty<ThoughtLedgerHandoffConstraintSummary>(nameof(ThoughtLedgerHandoffConstraintSummary.ConstraintType));
        AssertHasProperty<ThoughtLedgerHandoffConstraintSummary>(nameof(ThoughtLedgerHandoffConstraintSummary.ConstraintCode));
        AssertHasProperty<ThoughtLedgerHandoffConstraintSummary>(nameof(ThoughtLedgerHandoffConstraintSummary.SafeDescription));
    }

    [TestMethod]
    public void ThoughtLedgerHandoffEntry_DoesNotExposeApproveExecuteOrDispatchMethods()
    {
        var contractTypes = new[]
        {
            typeof(ThoughtLedgerHandoffEntry),
            typeof(ThoughtLedgerHandoffEntryCreateRequest),
            typeof(ThoughtLedgerHandoffEntrySummary),
            typeof(ThoughtLedgerHandoffEvidenceSummary),
            typeof(ThoughtLedgerHandoffConstraintSummary),
            typeof(ThoughtLedgerHandoffEntryFactory),
            typeof(ThoughtLedgerHandoffEntryValidator)
        };
        var forbiddenPrefixes = new[] { "Send", "Receive", "Dispatch", "Approve", "Authorize", "Execute", "ContinueWorkflow", "MutateSource", "PromoteMemory", "Release" };

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
    public void ThoughtLedgerHandoffEntry_AuthorityFlagsAreAlwaysFalse()
    {
        var entry = ValidEntry();

        AssertNoAuthority(entry);
    }

    [TestMethod]
    public void ThoughtLedgerHandoffEntryFactory_CreatesSafeEntryFromValidHandoff()
    {
        var request = Factory.CreateFromHandoff(ValidHandoff(), "agent", "planner-agent");

        Assert.IsTrue(Validator.Validate(request).IsValid, FormatIssues(Validator.Validate(request).Issues));
        StringAssert.Contains(request.SafeSummary, "handed");
        StringAssert.Contains(request.SafeSummary, "context");
    }

    [TestMethod]
    public void ThoughtLedgerHandoffEntryFactory_PreservesProjectId()
    {
        var handoff = ValidHandoff();
        var request = Factory.CreateFromHandoff(handoff, "agent", "planner-agent");

        Assert.AreEqual(handoff.ProjectId, request.ProjectId);
    }

    [TestMethod]
    public void ThoughtLedgerHandoffEntryFactory_PreservesAgentHandoffId()
    {
        var handoff = ValidHandoff();
        var request = Factory.CreateFromHandoff(handoff, "agent", "planner-agent");

        Assert.AreEqual(handoff.AgentHandoffId, request.AgentHandoffId);
    }

    [TestMethod]
    public void ThoughtLedgerHandoffEntryFactory_PreservesCorrelationAndCausation()
    {
        var handoff = ValidHandoff();
        var request = Factory.CreateFromHandoff(handoff, "agent", "planner-agent");

        Assert.AreEqual(handoff.CorrelationId, request.CorrelationId);
        Assert.AreEqual(handoff.CausationId, request.CausationId);
    }

    [TestMethod]
    public void ThoughtLedgerHandoffEntryFactory_PreservesSubject()
    {
        var handoff = ValidHandoff();
        var request = Factory.CreateFromHandoff(handoff, "agent", "planner-agent");

        Assert.AreEqual(handoff.Subject.SubjectType.ToString(), request.SubjectType);
        Assert.AreEqual(handoff.Subject.SubjectId, request.SubjectId);
        Assert.AreEqual(handoff.Subject.ActionName, request.ActionName);
    }

    [TestMethod]
    public void ThoughtLedgerHandoffEntryFactory_PreservesEvidenceAllowedUses()
    {
        var request = Factory.CreateFromHandoff(ValidHandoff(), "agent", "planner-agent");

        CollectionAssert.Contains(request.EvidenceSummaries.Single().AllowedUses.ToArray(), nameof(AgentHandoffEvidenceAllowedUse.Review));
        CollectionAssert.Contains(request.EvidenceSummaries.Single().AllowedUses.ToArray(), nameof(AgentHandoffEvidenceAllowedUse.Traceability));
    }

    [TestMethod]
    public void ThoughtLedgerHandoffEntryFactory_PreservesConstraints()
    {
        var request = Factory.CreateFromHandoff(ValidHandoff(), "agent", "planner-agent");

        Assert.AreEqual(nameof(AgentHandoffConstraintType.EvidenceOnly), request.ConstraintSummaries.Single().ConstraintType);
        Assert.AreEqual("EVIDENCE_ONLY", request.ConstraintSummaries.Single().ConstraintCode);
    }

    [TestMethod]
    public void ThoughtLedgerHandoffEntryFactory_SetsAuthorityFlagsFalse()
    {
        var request = Factory.CreateFromHandoff(ValidHandoff(), "agent", "planner-agent");

        AssertNoAuthority(request);
    }

    [TestMethod] public void ThoughtLedgerHandoffEntryFactory_DoesNotSendReceiveOrDispatchHandoff() => AssertFactoryHasNoForbiddenMethods("Send", "Receive", "Dispatch");
    [TestMethod] public void ThoughtLedgerHandoffEntryFactory_DoesNotCreateApprovalDecision() => AssertFactoryHasNoForbiddenMethods("CreateApproval", "RecordApproval");
    [TestMethod] public void ThoughtLedgerHandoffEntryFactory_DoesNotExecuteTool() => AssertFactoryHasNoForbiddenMethods("Execute", "RunTool");
    [TestMethod] public void ThoughtLedgerHandoffEntryFactory_DoesNotMutateSource() => AssertFactoryHasNoForbiddenMethods("MutateSource", "ApplySource");
    [TestMethod] public void ThoughtLedgerHandoffEntryFactory_DoesNotPromoteMemory() => AssertFactoryHasNoForbiddenMethods("PromoteMemory", "CreateCollectiveMemory");

    [TestMethod] public void Validate_RejectsEmptyProjectId() => AssertHasIssue(ValidRequest() with { ProjectId = Guid.Empty }, ThoughtLedgerHandoffEntryValidator.ProjectIdRequired);
    [TestMethod] public void Validate_RejectsEmptyAgentHandoffId() => AssertHasIssue(ValidRequest() with { AgentHandoffId = Guid.Empty }, ThoughtLedgerHandoffEntryValidator.AgentHandoffIdRequired);
    [TestMethod] public void Validate_RejectsBlankHandoffType() => AssertHasIssue(ValidRequest() with { HandoffType = string.Empty }, ThoughtLedgerHandoffEntryValidator.HandoffTypeRequired);
    [TestMethod] public void Validate_RejectsForbiddenHandoffStatus() => AssertHasIssue(ValidRequest() with { HandoffStatus = "ApprovedForExecution" }, ThoughtLedgerHandoffEntryValidator.HandoffStatusForbidden);
    [TestMethod] public void Validate_RejectsBlankSourceAgent() => AssertHasIssue(ValidRequest() with { SourceAgentId = string.Empty }, ThoughtLedgerHandoffEntryValidator.SourceAgentRequired);
    [TestMethod] public void Validate_RejectsBlankTargetAgent() => AssertHasIssue(ValidRequest() with { TargetAgentId = string.Empty }, ThoughtLedgerHandoffEntryValidator.TargetAgentRequired);
    [TestMethod] public void Validate_RejectsBlankSubject() => AssertHasIssue(ValidRequest() with { SubjectId = string.Empty }, ThoughtLedgerHandoffEntryValidator.SubjectRequired);
    [TestMethod] public void Validate_RejectsBlankSafeSummary() => AssertHasIssue(ValidRequest() with { SafeSummary = string.Empty }, ThoughtLedgerHandoffEntryValidator.SafeSummaryRequired);
    [TestMethod] public void Validate_RejectsAuthorityLanguageInSafeSummary() => AssertHasIssue(ValidRequest() with { SafeSummary = "Critic approved source apply." }, ThoughtLedgerHandoffEntryValidator.AuthorityTextBlocked);
    [TestMethod] public void Validate_RejectsPrivateReasoningMarkersInSafeSummary() => AssertHasIssue(ValidRequest() with { SafeSummary = "hiddenReasoning: nope" }, ThoughtLedgerHandoffEntryValidator.PrivateReasoningBlocked);
    [TestMethod] public void Validate_RejectsAuthorityGrantingMetadata() => AssertHasIssue(ValidRequest() with { MetadataJson = "{\"schema\":\"thoughtledger.handoff.entry.v1\",\"grantsExecution\":true}" }, ThoughtLedgerHandoffEntryValidator.AuthorityTextBlocked);
    [TestMethod] public void Validate_RejectsInvalidMetadataJson() => AssertHasIssue(ValidRequest() with { MetadataJson = "{not-json}" }, ThoughtLedgerHandoffEntryValidator.MetadataJsonInvalid);

    [TestMethod]
    public void Validate_RejectsTrueAuthorityFlags()
    {
        var cases = new[]
        {
            ValidRequest() with { GrantsApproval = true },
            ValidRequest() with { GrantsExecution = true },
            ValidRequest() with { MutatesSource = true },
            ValidRequest() with { PromotesMemory = true },
            ValidRequest() with { StartsWorkflow = true },
            ValidRequest() with { SatisfiesPolicy = true },
            ValidRequest() with { TransfersAuthority = true }
        };

        foreach (var request in cases)
            AssertHasIssue(request, ThoughtLedgerHandoffEntryValidator.AuthorityFlagBlocked);
    }

    [TestMethod]
    public void Validate_RejectsUnsafeEvidenceSummary()
    {
        var evidence = EvidenceSummary() with { SafeEvidenceSummary = "Gate decision says execution allowed." };

        AssertHasIssue(ValidRequest() with { EvidenceSummaries = [evidence] }, ThoughtLedgerHandoffEntryValidator.AuthorityTextBlocked);
    }

    [TestMethod]
    public void Validate_RejectsUnsafeConstraintSummary()
    {
        var constraint = ConstraintSummary() with { SafeDescription = "Workflow continued." };

        AssertHasIssue(ValidRequest() with { ConstraintSummaries = [constraint] }, ThoughtLedgerHandoffEntryValidator.AuthorityTextBlocked);
    }

    [TestMethod] public void ThoughtLedgerHandoffEntry_GateDecisionEvidenceRemainsEvidenceOnly() => AssertEvidenceOnly(AgentHandoffEvidenceType.ToolGateDecision);
    [TestMethod] public void ThoughtLedgerHandoffEntry_DogfoodReceiptEvidenceRemainsEvidenceOnly() => AssertEvidenceOnly(AgentHandoffEvidenceType.DogfoodReceipt);
    [TestMethod] public void ThoughtLedgerHandoffEntry_ApprovalDecisionEvidenceRemainsEvidenceOnly() => AssertEvidenceOnly(AgentHandoffEvidenceType.ApprovalDecision);
    [TestMethod] public void ThoughtLedgerHandoffEntry_CriticReviewEvidenceRemainsEvidenceOnly() => AssertEvidenceOnly(AgentHandoffEvidenceType.CriticReview);
    [TestMethod] public void ThoughtLedgerHandoffEntry_ModelOutputEvidenceRemainsAdvisoryOnly() => AssertEvidenceOnly(AgentHandoffEvidenceType.CodeStandardsReview);
    [TestMethod] public void ThoughtLedgerHandoffEntry_RetrievalEvidenceRemainsAdvisoryOnly() => AssertEvidenceOnly(AgentHandoffEvidenceType.RunReport);

    [TestMethod]
    public void ThoughtLedgerHandoffEntry_AllowedUseDoesNotGrantAuthority()
    {
        var request = ValidRequest() with
        {
            EvidenceSummaries =
            [
                EvidenceSummary() with
                {
                    AllowedUses =
                    [
                        nameof(AgentHandoffEvidenceAllowedUse.PolicyInput),
                        nameof(AgentHandoffEvidenceAllowedUse.HumanDecisionSupport),
                        nameof(AgentHandoffEvidenceAllowedUse.HandoffExplanation)
                    ]
                }
            ]
        };

        AssertValid(request);
        AssertNoAuthority(request);
    }

    [TestMethod] public void ThoughtLedgerHandoffEntry_IsNotApproval() => AssertNoAuthority(ValidRequest());
    [TestMethod] public void ThoughtLedgerHandoffEntry_IsNotExecutionPermission() => AssertNoAuthority(ValidRequest());
    [TestMethod] public void ThoughtLedgerHandoffEntry_IsNotPolicySatisfaction() => AssertNoAuthority(ValidRequest());
    [TestMethod] public void ThoughtLedgerHandoffEntry_IsNotWorkflowContinuation() => AssertNoAuthority(ValidRequest());
    [TestMethod] public void ThoughtLedgerHandoffEntry_IsNotSourceApply() => AssertNoAuthority(ValidRequest());
    [TestMethod] public void ThoughtLedgerHandoffEntry_IsNotMemoryPromotion() => AssertNoAuthority(ValidRequest());
    [TestMethod] public void ThoughtLedgerHandoffEntry_IsNotReleaseApproval() => AssertNoAuthority(ValidRequest());
    [TestMethod] public void ThoughtLedgerHandoffEntry_IsNotA2aDeliveryConfirmation() => AssertNoRuntimeTransport();
    [TestMethod] public void ThoughtLedgerHandoffEntry_IsNotTargetAgentReceipt() => AssertNoRuntimeTransport();
    [TestMethod] public void ThoughtLedgerHandoffEntry_DoesNotTransferMemoryOwnership() => AssertNoAuthority(ValidRequest());

    [TestMethod] public void ThoughtLedgerHandoffEntry_DoesNotAddApiEndpoint() => AssertProductionDoesNotReference("ThoughtLedgerHandoffEntry", "IronDev.Api");
    [TestMethod] public void ThoughtLedgerHandoffEntry_DoesNotAddCliCommand() => AssertProductionDoesNotReference("ThoughtLedgerHandoffEntry", Path.Combine("tools", "IronDev.Cli"));
    [TestMethod] public void ThoughtLedgerHandoffEntry_DoesNotAddSqlMigrationUnlessExplicitlyRequired() => AssertNoDatabaseReference("ThoughtLedgerHandoffEntry");
    [TestMethod] public void ThoughtLedgerHandoffEntry_DoesNotAddRepositoryUnlessExplicitlyRequired() => AssertNoProductionToken("IThoughtLedgerHandoffEntryStore", "SqlThoughtLedgerHandoffEntry", "ThoughtLedgerHandoffEntryRepository");
    [TestMethod] public void ThoughtLedgerHandoffEntry_DoesNotRegisterRuntimeDispatcher() => AssertNoProductionToken("ThoughtLedgerHandoffEntryDispatcher", "RegisterThoughtLedgerHandoffEntry");
    [TestMethod] public void ThoughtLedgerHandoffEntry_DoesNotReferenceWorkflowRunner() => AssertCoreFileDoesNotContain("WorkflowRunner", "IWorkflowRunner");
    [TestMethod] public void ThoughtLedgerHandoffEntry_DoesNotReferenceExecutor() => AssertCoreFileDoesNotContain("IExecutor", "ExecuteAsync", "RunTool");
    [TestMethod] public void ThoughtLedgerHandoffEntry_DoesNotReferenceMemoryPromotion() => AssertCoreFileDoesNotContain("CollectiveMemoryPromotionService", "PromoteMemoryAsync");
    [TestMethod] public void ThoughtLedgerHandoffEntry_DoesNotReferenceSourceApply() => AssertCoreFileDoesNotContain("ApplyCopy", "SourceApplyService", "MutateSourceAsync");
    [TestMethod] public void ThoughtLedgerHandoffEntry_DoesNotReferenceLangGraph() => AssertCoreFileDoesNotContain("LangGraph");
    [TestMethod] public void ThoughtLedgerHandoffEntry_DoesNotReferenceA2aRuntime() => AssertCoreFileDoesNotContain("A2aRuntime", "A2ARuntime", "HandoffRuntime");
    [TestMethod] public void ThoughtLedgerHandoffEntry_DoesNotReferenceMessageBusOrQueue() => AssertCoreFileDoesNotContain("MessageBus", "QueueClient", "Outbox", "Inbox");

    [TestMethod]
    public void BlockIDocumentation_DescribesThoughtLedgerHandoffEntryBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "BLOCK_I_A2A_HANDOFF_CONTRACT_SPINE.md"));

        foreach (var expected in new[]
        {
            "PR94 ThoughtLedger Handoff Entries",
            "PR94 adds safe ThoughtLedger handoff entries.",
            "ThoughtLedger may record that a handoff exists.",
            "ThoughtLedger may summarize handoff context, evidence, allowed uses, and constraints.",
            "ThoughtLedger does not approve handoffs.",
            "ThoughtLedger does not send or receive handoffs.",
            "ThoughtLedger does not execute handoffs.",
            "ThoughtLedger does not continue workflow.",
            "ThoughtLedger does not mutate source.",
            "ThoughtLedger does not promote memory.",
            "ThoughtLedger does not approve release.",
            "ThoughtLedger entries must not contain hidden/private reasoning."
        })
        {
            StringAssert.Contains(doc, expected);
        }
    }

    private static void AssertEvidenceOnly(AgentHandoffEvidenceType evidenceType)
    {
        var request = ValidRequest() with { EvidenceSummaries = [EvidenceSummary(evidenceType)] };

        AssertValid(request);
        AssertNoAuthority(request);
        CollectionAssert.Contains(request.EvidenceSummaries.Single().AllowedUses.ToArray(), nameof(AgentHandoffEvidenceAllowedUse.Review));
    }

    private static void AssertFactoryHasNoForbiddenMethods(params string[] forbiddenPrefixes)
    {
        var methods = typeof(ThoughtLedgerHandoffEntryFactory)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        foreach (var method in methods)
        foreach (var prefix in forbiddenPrefixes)
            Assert.IsFalse(method.StartsWith(prefix, StringComparison.Ordinal), $"Factory exposes forbidden method {method}.");
    }

    private static void AssertNoRuntimeTransport()
    {
        AssertCoreFileDoesNotContain("Dispatch", "ReceiveHandoff", "SendHandoff", "Inbox", "Outbox", "MessageBus", "DeliveryConfirmation");
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
                Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"Production file contains forbidden token {token}: {file}");
        }
    }

    private static void AssertProductionDoesNotReference(string token, string relativeRoot)
    {
        var root = Path.Combine(RepositoryRoot(), relativeRoot);
        if (!Directory.Exists(root))
            return;

        var refs = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(0, refs.Length, string.Join(Environment.NewLine, refs));
    }

    private static void AssertNoDatabaseReference(string token)
    {
        var root = Path.Combine(RepositoryRoot(), "Database");
        var refs = Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(0, refs.Length, string.Join(Environment.NewLine, refs));
    }

    private static void AssertCoreFileDoesNotContain(params string[] tokens)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Core", "Agents", "ThoughtLedgerHandoffEntryModels.cs"));
        foreach (var token in tokens)
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"ThoughtLedgerHandoffEntryModels.cs contains forbidden token {token}.");
    }

    private static void AssertValid(ThoughtLedgerHandoffEntryCreateRequest request) =>
        Assert.IsTrue(Validator.Validate(request).IsValid, FormatIssues(Validator.Validate(request).Issues));

    private static void AssertHasIssue(ThoughtLedgerHandoffEntryCreateRequest request, string code) =>
        Assert.IsTrue(
            Validator.Validate(request).Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)),
            $"Expected {code}.{Environment.NewLine}{FormatIssues(Validator.Validate(request).Issues)}");

    private static void AssertNoAuthority(ThoughtLedgerHandoffEntryCreateRequest request)
    {
        Assert.IsFalse(request.GrantsApproval);
        Assert.IsFalse(request.GrantsExecution);
        Assert.IsFalse(request.MutatesSource);
        Assert.IsFalse(request.PromotesMemory);
        Assert.IsFalse(request.StartsWorkflow);
        Assert.IsFalse(request.SatisfiesPolicy);
        Assert.IsFalse(request.TransfersAuthority);
    }

    private static void AssertNoAuthority(ThoughtLedgerHandoffEntry entry)
    {
        Assert.IsFalse(entry.GrantsApproval);
        Assert.IsFalse(entry.GrantsExecution);
        Assert.IsFalse(entry.MutatesSource);
        Assert.IsFalse(entry.PromotesMemory);
        Assert.IsFalse(entry.StartsWorkflow);
        Assert.IsFalse(entry.SatisfiesPolicy);
        Assert.IsFalse(entry.TransfersAuthority);
    }

    private static ThoughtLedgerHandoffEntryCreateRequest ValidRequest() =>
        Factory.CreateFromHandoff(ValidHandoff(), "agent", "planner-agent");

    private static ThoughtLedgerHandoffEntry ValidEntry()
    {
        var request = ValidRequest();
        return new ThoughtLedgerHandoffEntry
        {
            ThoughtLedgerHandoffEntryId = request.ThoughtLedgerHandoffEntryId!.Value,
            ProjectId = request.ProjectId,
            AgentHandoffId = request.AgentHandoffId,
            GovernanceEventId = request.GovernanceEventId,
            CorrelationId = request.CorrelationId,
            CausationId = request.CausationId,
            HandoffType = request.HandoffType,
            HandoffStatus = request.HandoffStatus,
            SourceAgentId = request.SourceAgentId,
            SourceAgentRole = request.SourceAgentRole,
            TargetAgentId = request.TargetAgentId,
            TargetAgentRole = request.TargetAgentRole,
            SubjectType = request.SubjectType,
            SubjectId = request.SubjectId,
            ActionName = request.ActionName,
            SafeSummary = request.SafeSummary,
            EvidenceSummaries = request.EvidenceSummaries,
            ConstraintSummaries = request.ConstraintSummaries,
            CreatedByActorType = request.CreatedByActorType,
            CreatedByActorId = request.CreatedByActorId,
            MetadataVersion = request.MetadataVersion,
            MetadataJson = request.MetadataJson,
            GrantsApproval = request.GrantsApproval,
            GrantsExecution = request.GrantsExecution,
            MutatesSource = request.MutatesSource,
            PromotesMemory = request.PromotesMemory,
            StartsWorkflow = request.StartsWorkflow,
            SatisfiesPolicy = request.SatisfiesPolicy,
            TransfersAuthority = request.TransfersAuthority,
            CreatedUtc = request.CreatedUtc!.Value
        };
    }

    private static AgentHandoff ValidHandoff() =>
        new()
        {
            AgentHandoffId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            HandoffType = AgentHandoffType.EvidenceTransfer,
            Status = AgentHandoffStatus.ReadyForReview,
            SourceAgent = new AgentHandoffParticipant
            {
                AgentId = "planner-agent",
                AgentRole = AgentHandoffParticipantRole.Planner,
                DisplayName = "Planner Agent"
            },
            TargetAgent = new AgentHandoffParticipant
            {
                AgentId = "builder-agent",
                AgentRole = AgentHandoffParticipantRole.Builder,
                DisplayName = "Builder Agent"
            },
            Subject = new AgentHandoffSubject
            {
                SubjectType = AgentHandoffSubjectType.ToolRequest,
                SubjectId = "tool-request-1",
                ActionName = "ReviewEvidence",
                Summary = "Evidence package for target-agent review."
            },
            EvidenceReferences = [Evidence(AgentHandoffEvidenceType.ApprovalDecision)],
            Constraints = [new AgentHandoffConstraint
            {
                ConstraintType = AgentHandoffConstraintType.EvidenceOnly,
                ConstraintCode = "EVIDENCE_ONLY",
                Description = "This handoff transfers context and evidence only."
            }],
            CorrelationId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            CausationId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            CreatedByActorType = "agent",
            CreatedByActorId = "planner-agent",
            MetadataVersion = 1,
            MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"grantsApproval\":false,\"grantsExecution\":false,\"mutatesSource\":false,\"promotesMemory\":false,\"startsWorkflow\":false,\"satisfiesPolicy\":false,\"transfersAuthority\":false}",
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false,
            CreatedUtc = CreatedUtc
        };

    private static AgentHandoffEvidenceReference Evidence(AgentHandoffEvidenceType evidenceType) =>
        new()
        {
            EvidenceType = evidenceType,
            EvidenceId = $"{evidenceType}-1",
            AllowedUses =
            [
                AgentHandoffEvidenceAllowedUse.Review,
                AgentHandoffEvidenceAllowedUse.Traceability,
                AgentHandoffEvidenceAllowedUse.HumanDecisionSupport
            ],
            EvidenceLabel = $"{evidenceType} evidence",
            EvidenceSummary = $"{evidenceType} is cited only as evidence.",
            GovernanceEventId = Guid.Parse("44444444-4444-4444-4444-444444444444")
        };

    private static ThoughtLedgerHandoffEvidenceSummary EvidenceSummary(AgentHandoffEvidenceType evidenceType = AgentHandoffEvidenceType.ApprovalDecision) =>
        new()
        {
            EvidenceType = evidenceType.ToString(),
            EvidenceId = $"{evidenceType}-1",
            AllowedUses = [nameof(AgentHandoffEvidenceAllowedUse.Review), nameof(AgentHandoffEvidenceAllowedUse.Traceability)],
            EvidenceLabel = $"{evidenceType} evidence",
            SafeEvidenceSummary = $"{evidenceType} is cited only as evidence.",
            GovernanceEventId = Guid.Parse("44444444-4444-4444-4444-444444444444")
        };

    private static ThoughtLedgerHandoffConstraintSummary ConstraintSummary() =>
        new()
        {
            ConstraintType = nameof(AgentHandoffConstraintType.EvidenceOnly),
            ConstraintCode = "EVIDENCE_ONLY",
            SafeDescription = "This handoff transfers context and evidence only."
        };

    private static void AssertHasProperty<T>(string name) =>
        Assert.IsNotNull(typeof(T).GetProperty(name), $"{typeof(T).Name} missing property {name}.");

    private static string FormatIssues(IReadOnlyList<ThoughtLedgerHandoffEntryValidationIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message} ({issue.Field})"));

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
