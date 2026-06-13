using System.Reflection;
using IronDev.Core.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
[TestCategory("NoAuthorityTransferValidator")]
[TestCategory("AgentHandoff")]
public sealed class NoAuthorityTransferValidatorTests
{
    private static readonly AgentHandoffAuthorityTransferValidator Validator = new();
    private static readonly DateTimeOffset CreatedUtc = new(2026, 6, 13, 12, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public void NoAuthorityTransferValidator_ExposesPureValidationContract()
    {
        Assert.IsNotNull(typeof(IAgentHandoffAuthorityTransferValidator));
        Assert.IsNotNull(typeof(AgentHandoffAuthorityTransferValidator));
        Assert.IsNotNull(typeof(AgentHandoffAuthorityTransferValidationResult));
        Assert.IsNotNull(typeof(AgentHandoffAuthorityTransferViolation));

        var method = typeof(IAgentHandoffAuthorityTransferValidator).GetMethod(nameof(IAgentHandoffAuthorityTransferValidator.Validate));
        Assert.IsNotNull(method);
        Assert.AreEqual(typeof(AgentHandoffAuthorityTransferValidationResult), method.ReturnType);
        CollectionAssert.AreEqual(new[] { typeof(AgentHandoff) }, method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
    }

    [TestMethod]
    public void NoAuthorityTransferValidator_ReturnsSafeForEvidenceOnlyHandoff()
    {
        var result = Validator.Validate(ValidHandoff());

        AssertSafe(result);
    }

    [TestMethod]
    public void NoAuthorityTransferValidator_ResultAuthorityFlagsAlwaysFalse()
    {
        var result = Validator.Validate(ValidHandoff() with { GrantsExecution = true, TransfersAuthority = true });

        Assert.IsFalse(result.IsSafe);
        AssertAuthorityFlagsFalse(result);
    }

    [TestMethod]
    public void NoAuthorityTransferValidator_DoesNotExposeSendReceiveExecuteApproveMethods()
    {
        var contractTypes = new[]
        {
            typeof(IAgentHandoffAuthorityTransferValidator),
            typeof(AgentHandoffAuthorityTransferValidator),
            typeof(AgentHandoffAuthorityTransferValidationResult),
            typeof(AgentHandoffAuthorityTransferViolation)
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
    public void Validate_RejectsTrueAuthorityFlags()
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
        {
            var result = Validator.Validate(handoff);
            AssertHasViolation(result, AgentHandoffAuthorityTransferValidator.AuthorityFlagSet);
            AssertAuthorityFlagsFalse(result);
        }
    }

    [TestMethod]
    public void Validate_RejectsInvalidEnumValuesAsForbiddenAuthoritySurface()
    {
        var cases = new (AgentHandoff Handoff, string Code)[]
        {
            (ValidHandoff() with { HandoffType = (AgentHandoffType)999 }, AgentHandoffAuthorityTransferValidator.ForbiddenHandoffType),
            (ValidHandoff() with { Status = (AgentHandoffStatus)999 }, AgentHandoffAuthorityTransferValidator.ForbiddenHandoffStatus),
            (ValidHandoff() with { SourceAgent = SourceAgent() with { AgentRole = (AgentHandoffParticipantRole)999 } }, AgentHandoffAuthorityTransferValidator.ForbiddenAgentRole),
            (ValidHandoff() with { TargetAgent = TargetAgent() with { AgentRole = (AgentHandoffParticipantRole)999 } }, AgentHandoffAuthorityTransferValidator.ForbiddenAgentRole),
            (ValidHandoff() with { Subject = Subject() with { SubjectType = (AgentHandoffSubjectType)999 } }, AgentHandoffAuthorityTransferValidator.ForbiddenSubjectMeaning),
            (ValidHandoff() with { EvidenceReferences = [Evidence() with { EvidenceType = (AgentHandoffEvidenceType)999 }] }, AgentHandoffAuthorityTransferValidator.ForbiddenEvidenceType),
            (ValidHandoff() with { EvidenceReferences = [Evidence() with { AllowedUses = [(AgentHandoffEvidenceAllowedUse)999] }] }, AgentHandoffAuthorityTransferValidator.ForbiddenEvidenceAllowedUse),
            (ValidHandoff() with { Constraints = [Constraint() with { ConstraintType = (AgentHandoffConstraintType)999 }] }, AgentHandoffAuthorityTransferValidator.ForbiddenConstraintType)
        };

        foreach (var testCase in cases)
            AssertHasViolation(Validator.Validate(testCase.Handoff), testCase.Code);
    }

    [TestMethod]
    public void Validate_AllowsSafeRolesAllowedUsesAndConstraints()
    {
        var safeRoles = new[]
        {
            AgentHandoffParticipantRole.Planner,
            AgentHandoffParticipantRole.Builder,
            AgentHandoffParticipantRole.Critic,
            AgentHandoffParticipantRole.Tester,
            AgentHandoffParticipantRole.Memory,
            AgentHandoffParticipantRole.Conscience,
            AgentHandoffParticipantRole.Reviewer,
            AgentHandoffParticipantRole.Operator,
            AgentHandoffParticipantRole.ToolGateway
        };

        foreach (var role in safeRoles)
            AssertSafe(Validator.Validate(ValidHandoff() with { SourceAgent = SourceAgent() with { AgentRole = role } }));

        AssertSafe(Validator.Validate(ValidHandoff() with
        {
            EvidenceReferences =
            [
                Evidence(
                    AgentHandoffEvidenceType.ToolGateDecision,
                    AgentHandoffEvidenceAllowedUse.Context,
                    AgentHandoffEvidenceAllowedUse.Review,
                    AgentHandoffEvidenceAllowedUse.Debugging,
                    AgentHandoffEvidenceAllowedUse.Validation,
                    AgentHandoffEvidenceAllowedUse.Traceability,
                    AgentHandoffEvidenceAllowedUse.RequirementEvaluation,
                    AgentHandoffEvidenceAllowedUse.HumanDecisionSupport,
                    AgentHandoffEvidenceAllowedUse.AuditReference,
                    AgentHandoffEvidenceAllowedUse.PolicyInput,
                    AgentHandoffEvidenceAllowedUse.HandoffExplanation)
            ],
            Constraints =
            [
                Constraint(AgentHandoffConstraintType.RequiresHumanReview),
                Constraint(AgentHandoffConstraintType.RequiresApprovalDecision),
                Constraint(AgentHandoffConstraintType.EvidenceOnly),
                Constraint(AgentHandoffConstraintType.DoNotExecute),
                Constraint(AgentHandoffConstraintType.DoNotMutateSource),
                Constraint(AgentHandoffConstraintType.DoNotPromoteMemory),
                Constraint(AgentHandoffConstraintType.DoNotContinueWorkflow)
            ]
        }));
    }

    [TestMethod]
    public void Validate_AllowsCandidateAndContextSubjectsAsNonAuthoritative()
    {
        var cases = new[]
        {
            Subject(AgentHandoffSubjectType.MemoryCandidate, "memory-candidate-1", "Memory candidate context only."),
            Subject(AgentHandoffSubjectType.CodePatchCandidate, "patch-candidate-1", "Source apply context only."),
            Subject(AgentHandoffSubjectType.WorkflowStepCandidate, "workflow-step-candidate-1", "Workflow step candidate only.")
        };

        foreach (var subject in cases)
            AssertSafe(Validator.Validate(ValidHandoff() with { Subject = subject }));
    }

    [TestMethod]
    public void Validate_RejectsAuthorityLanguageAcrossSubjectEvidenceConstraintAndMetadata()
    {
        var cases = new[]
        {
            ValidHandoff() with { Subject = Subject() with { Summary = "execution allowed" } },
            ValidHandoff() with { Subject = Subject() with { ActionName = "continue workflow" } },
            ValidHandoff() with { EvidenceReferences = [Evidence(AgentHandoffEvidenceType.ToolGateDecision, AgentHandoffEvidenceAllowedUse.Review) with { EvidenceLabel = "gate approved" }] },
            ValidHandoff() with { EvidenceReferences = [Evidence(AgentHandoffEvidenceType.ToolGateDecision, AgentHandoffEvidenceAllowedUse.Review) with { EvidenceSummary = "can execute" }] },
            ValidHandoff() with { EvidenceReferences = [Evidence(AgentHandoffEvidenceType.DogfoodReceipt, AgentHandoffEvidenceAllowedUse.Validation) with { EvidenceSummary = "release approved" }] },
            ValidHandoff() with { EvidenceReferences = [Evidence(AgentHandoffEvidenceType.CriticReview, AgentHandoffEvidenceAllowedUse.Review) with { EvidenceSummary = "critic approved" }] },
            ValidHandoff() with { EvidenceReferences = [Evidence(AgentHandoffEvidenceType.CodeStandardsReview, AgentHandoffEvidenceAllowedUse.Review) with { EvidenceSummary = "model approved" }] },
            ValidHandoff() with { EvidenceReferences = [Evidence(AgentHandoffEvidenceType.ApprovalDecision, AgentHandoffEvidenceAllowedUse.HumanDecisionSupport) with { EvidenceSummary = "approval transferred" }] },
            ValidHandoff() with { Constraints = [Constraint() with { ConstraintCode = "APPROVAL_GRANTED" }] },
            ValidHandoff() with { Constraints = [Constraint() with { Description = "source apply permission" }] },
            ValidHandoff() with { MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"canExecute\":true}" },
            ValidHandoff() with { MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"sourceApplyAllowed\":true}" },
            ValidHandoff() with { MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"memoryPromotionAllowed\":true}" },
            ValidHandoff() with { MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"releaseApproved\":true}" },
            ValidHandoff() with { MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"notes\":\"policy satisfied\"}" }
        };

        foreach (var handoff in cases)
        {
            var result = Validator.Validate(handoff);
            Assert.IsFalse(result.IsSafe, $"Expected unsafe handoff for case: {handoff}");
            AssertAuthorityFlagsFalse(result);
        }
    }

    [TestMethod]
    public void Validate_RejectsPrivateReasoningMarkersAcrossMetadataEvidenceAndConstraintText()
    {
        var cases = new[]
        {
            ValidHandoff() with { MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"hiddenReasoning\":\"blocked\"}" },
            ValidHandoff() with { MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"notes\":\"chain-of-thought\"}" },
            ValidHandoff() with { EvidenceReferences = [Evidence() with { EvidenceSummary = "rawPrompt dump" }] },
            ValidHandoff() with { EvidenceReferences = [Evidence() with { EvidenceLabel = "private reasoning" }] },
            ValidHandoff() with { Constraints = [Constraint() with { Description = "scratchpad text" }] }
        };

        foreach (var handoff in cases)
            AssertHasViolation(Validator.Validate(handoff), AgentHandoffAuthorityTransferValidator.PrivateReasoningMarker);
    }

    [TestMethod]
    public void NoAuthorityTransferValidator_DoesNotCreateApprovalPolicyExecutionWorkflowSourceMemoryReleaseOrRuntimeEffects()
    {
        var result = Validator.Validate(ValidHandoff());

        AssertSafe(result);
        AssertAuthorityFlagsFalse(result);
    }

    [TestMethod]
    public void NoAuthorityTransferValidator_StaticBoundary_CoreFileHasNoRuntimePersistenceTransportOrExternalTokens()
    {
        var source = ReadRepositoryFile("IronDev.Core", "Agents", "AgentHandoffAuthorityTransferValidator.cs");
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
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"Forbidden active token found in AgentHandoffAuthorityTransferValidator.cs: {token}");
    }

    [TestMethod]
    public void NoAuthorityTransferValidator_StaticBoundary_IsNotWiredIntoApiCliRuntimeSqlWorkflowOrA2a()
    {
        var root = RepositoryRoot();
        var files = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}IronDev.IntegrationTests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(Path.Combine("IronDev.Core", "Agents", "AgentHandoffAuthorityTransferValidator.cs"), StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(Path.Combine("IronDev.Infrastructure", "Governance", "SqlAgentHandoffStore.cs"), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert.IsFalse(source.Contains("AgentHandoffAuthorityTransferValidator", StringComparison.Ordinal), $"Authority-transfer validator must not be wired into production/runtime file: {file}");
            Assert.IsFalse(source.Contains("IAgentHandoffAuthorityTransferValidator", StringComparison.Ordinal), $"Authority-transfer validator interface must not be wired into production/runtime file: {file}");
        }
    }

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
        Subject(AgentHandoffSubjectType.ToolRequest, "tool-request-1", "Evidence package for target-agent review.");

    private static AgentHandoffSubject Subject(AgentHandoffSubjectType type, string id, string summary) =>
        new()
        {
            SubjectType = type,
            SubjectId = id,
            ActionName = "ReviewEvidence",
            Summary = summary
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
        Constraint(AgentHandoffConstraintType.EvidenceOnly);

    private static AgentHandoffConstraint Constraint(AgentHandoffConstraintType type) =>
        new()
        {
            ConstraintType = type,
            ConstraintCode = type.ToString(),
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

    private static void AssertSafe(AgentHandoffAuthorityTransferValidationResult result)
    {
        Assert.IsTrue(result.IsSafe, FormatViolations(result.Violations));
        Assert.AreEqual(0, result.Violations.Count, FormatViolations(result.Violations));
        AssertAuthorityFlagsFalse(result);
    }

    private static void AssertAuthorityFlagsFalse(AgentHandoffAuthorityTransferValidationResult result)
    {
        Assert.IsFalse(result.GrantsApproval);
        Assert.IsFalse(result.GrantsExecution);
        Assert.IsFalse(result.MutatesSource);
        Assert.IsFalse(result.PromotesMemory);
        Assert.IsFalse(result.StartsWorkflow);
        Assert.IsFalse(result.SatisfiesPolicy);
        Assert.IsFalse(result.TransfersAuthority);
    }

    private static void AssertHasViolation(AgentHandoffAuthorityTransferValidationResult result, string code) =>
        Assert.IsTrue(result.Violations.Any(violation => string.Equals(violation.Code, code, StringComparison.Ordinal)), $"Expected {code}.{Environment.NewLine}{FormatViolations(result.Violations)}");

    private static string FormatViolations(IReadOnlyList<AgentHandoffAuthorityTransferViolation> violations) =>
        string.Join(Environment.NewLine, violations.Select(violation => $"{violation.Code}: {violation.Message} ({violation.Path})"));

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
