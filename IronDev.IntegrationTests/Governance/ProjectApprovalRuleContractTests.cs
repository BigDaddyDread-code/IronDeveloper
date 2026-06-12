using System.Reflection;
using IronDev.Core.Policy;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ProjectApprovalRule")]
public sealed class ProjectApprovalRuleContractTests
{
    [TestMethod]
    public void ProjectApprovalRuleContracts_ExposeExpectedRuleShape()
    {
        AssertProperties<ProjectApprovalRule>(
            nameof(ProjectApprovalRule.ProjectApprovalRuleId),
            nameof(ProjectApprovalRule.ProjectId),
            nameof(ProjectApprovalRule.ProjectAutonomyPolicyId),
            nameof(ProjectApprovalRule.RuleName),
            nameof(ProjectApprovalRule.RuleVersion),
            nameof(ProjectApprovalRule.Status),
            nameof(ProjectApprovalRule.ApprovalScope),
            nameof(ProjectApprovalRule.SubjectTypePattern),
            nameof(ProjectApprovalRule.ActionNamePattern),
            nameof(ProjectApprovalRule.RiskLevel),
            nameof(ProjectApprovalRule.ApprovalType),
            nameof(ProjectApprovalRule.ApproverTypes),
            nameof(ProjectApprovalRule.QuorumCount),
            nameof(ProjectApprovalRule.SupersedesRuleId),
            nameof(ProjectApprovalRule.CreatedByActorType),
            nameof(ProjectApprovalRule.CreatedByActorId),
            nameof(ProjectApprovalRule.MetadataVersion),
            nameof(ProjectApprovalRule.MetadataJson),
            nameof(ProjectApprovalRule.CreatedUtc));

        AssertProperties<ProjectApprovalRuleCreateRequest>(
            nameof(ProjectApprovalRuleCreateRequest.ProjectId),
            nameof(ProjectApprovalRuleCreateRequest.ProjectAutonomyPolicyId),
            nameof(ProjectApprovalRuleCreateRequest.RuleName),
            nameof(ProjectApprovalRuleCreateRequest.RuleVersion),
            nameof(ProjectApprovalRuleCreateRequest.Status),
            nameof(ProjectApprovalRuleCreateRequest.ApprovalScope),
            nameof(ProjectApprovalRuleCreateRequest.SubjectTypePattern),
            nameof(ProjectApprovalRuleCreateRequest.ActionNamePattern),
            nameof(ProjectApprovalRuleCreateRequest.RiskLevel),
            nameof(ProjectApprovalRuleCreateRequest.ApprovalType),
            nameof(ProjectApprovalRuleCreateRequest.ApproverTypes),
            nameof(ProjectApprovalRuleCreateRequest.QuorumCount),
            nameof(ProjectApprovalRuleCreateRequest.SupersedesRuleId),
            nameof(ProjectApprovalRuleCreateRequest.CreatedByActorType),
            nameof(ProjectApprovalRuleCreateRequest.CreatedByActorId),
            nameof(ProjectApprovalRuleCreateRequest.MetadataVersion),
            nameof(ProjectApprovalRuleCreateRequest.MetadataJson));

        AssertProperties<ProjectApprovalRuleSummary>(
            nameof(ProjectApprovalRuleSummary.ProjectApprovalRuleId),
            nameof(ProjectApprovalRuleSummary.ProjectId),
            nameof(ProjectApprovalRuleSummary.ProjectAutonomyPolicyId),
            nameof(ProjectApprovalRuleSummary.RuleName),
            nameof(ProjectApprovalRuleSummary.RuleVersion),
            nameof(ProjectApprovalRuleSummary.Status),
            nameof(ProjectApprovalRuleSummary.ApprovalScope),
            nameof(ProjectApprovalRuleSummary.RiskLevel),
            nameof(ProjectApprovalRuleSummary.ApprovalType),
            nameof(ProjectApprovalRuleSummary.CreatedUtc));
    }

    [TestMethod]
    public void ProjectApprovalRuleContracts_ExposeApprovalScopes()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                ProjectApprovalRuleScopes.ToolExecution,
                ProjectApprovalRuleScopes.SourceApply,
                ProjectApprovalRuleScopes.MemoryPromotion,
                ProjectApprovalRuleScopes.ProposalAcceptance,
                ProjectApprovalRuleScopes.ReleaseReadiness,
                ProjectApprovalRuleScopes.ExternalSideEffect,
                ProjectApprovalRuleScopes.DestructiveOperation,
                ProjectApprovalRuleScopes.DogfoodReceiptClassification,
                ProjectApprovalRuleScopes.WorkflowStepRouting,
                ProjectApprovalRuleScopes.A2aHandoffValidation
            },
            ProjectApprovalRuleScopes.All.ToArray());
    }

    [TestMethod]
    public void ProjectApprovalRuleContracts_ExposeApprovalTypes()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                ProjectApprovalRuleApprovalTypes.None,
                ProjectApprovalRuleApprovalTypes.Single,
                ProjectApprovalRuleApprovalTypes.AnyOf,
                ProjectApprovalRuleApprovalTypes.AllOf,
                ProjectApprovalRuleApprovalTypes.Quorum,
                ProjectApprovalRuleApprovalTypes.HumanOnly
            },
            ProjectApprovalRuleApprovalTypes.All.ToArray());
    }

    [TestMethod]
    public void ProjectApprovalRuleContracts_ExposeApproverTypes()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                ProjectApprovalRuleApproverTypes.Human,
                ProjectApprovalRuleApproverTypes.ProjectLead,
                ProjectApprovalRuleApproverTypes.MemoryOwner,
                ProjectApprovalRuleApproverTypes.SecurityOwner,
                ProjectApprovalRuleApproverTypes.ReleaseOwner,
                ProjectApprovalRuleApproverTypes.Operator,
                ProjectApprovalRuleApproverTypes.System,
                ProjectApprovalRuleApproverTypes.Agent
            },
            ProjectApprovalRuleApproverTypes.All.ToArray());
    }

    [TestMethod]
    public void ProjectApprovalRuleContracts_ExposeRiskLevels()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                ProjectApprovalRuleRiskLevels.Low,
                ProjectApprovalRuleRiskLevels.Medium,
                ProjectApprovalRuleRiskLevels.High,
                ProjectApprovalRuleRiskLevels.Critical
            },
            ProjectApprovalRuleRiskLevels.All.ToArray());
    }

    [TestMethod]
    public void ProjectApprovalRuleContracts_DoNotExposeEvaluatorMethods()
    {
        var forbidden = new[] { "Evaluate", "Satisfy", "Record", "Execute", "Route", "Run", "Start" };
        var members = typeof(ProjectApprovalRuleValidator)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name)
            .ToArray();

        foreach (var member in members)
        {
            foreach (var token in forbidden)
            {
                Assert.IsFalse(member.Contains(token, StringComparison.OrdinalIgnoreCase), member);
            }
        }
    }

    [TestMethod]
    public void ProjectApprovalRuleContracts_DoNotExposeExecutionOrPromotionProperties()
    {
        var forbidden = new[] { "Executed", "CanExecute", "ExecutionPermission", "SourceApplied", "MemoryPromoted", "ReleaseApproved", "CanShip" };
        var memberNames = typeof(ProjectApprovalRule).GetProperties().Select(property => property.Name)
            .Concat(typeof(ProjectApprovalRuleCreateRequest).GetProperties().Select(property => property.Name))
            .Concat(typeof(ProjectApprovalRuleSummary).GetProperties().Select(property => property.Name));

        foreach (var memberName in memberNames)
        {
            foreach (var token in forbidden)
            {
                Assert.IsFalse(memberName.Contains(token, StringComparison.OrdinalIgnoreCase), memberName);
            }
        }
    }

    [TestMethod] public void ApprovalScope_AllowsToolExecution() => AssertValidScope(ProjectApprovalRuleScopes.ToolExecution);

    [TestMethod] public void ApprovalScope_AllowsSourceApply() => AssertValidSensitiveScope(ProjectApprovalRuleScopes.SourceApply);

    [TestMethod] public void ApprovalScope_AllowsMemoryPromotion() => AssertValidSensitiveScope(ProjectApprovalRuleScopes.MemoryPromotion);

    [TestMethod] public void ApprovalScope_AllowsReleaseReadiness() => AssertValidSensitiveScope(ProjectApprovalRuleScopes.ReleaseReadiness);

    [TestMethod] public void ApprovalScope_AllowsExternalSideEffect() => AssertValidSensitiveScope(ProjectApprovalRuleScopes.ExternalSideEffect);

    [TestMethod] public void ApprovalScope_AllowsDestructiveOperation() => AssertValidSensitiveScope(ProjectApprovalRuleScopes.DestructiveOperation);

    [TestMethod]
    public void ApprovalScope_RejectsUnknownScope()
    {
        var result = ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with { ApprovalScope = "unknown_scope" });

        AssertContainsIssue(result, "APPROVAL_SCOPE_UNKNOWN");
    }

    [TestMethod]
    public void SensitiveScope_RejectsApprovalTypeNone()
    {
        var result = ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with
        {
            ApprovalScope = ProjectApprovalRuleScopes.SourceApply,
            RiskLevel = ProjectApprovalRuleRiskLevels.Critical,
            ApprovalType = ProjectApprovalRuleApprovalTypes.None,
            ApproverTypes = []
        });

        AssertContainsIssue(result, "SENSITIVE_SCOPE_REQUIRES_APPROVAL");
    }

    [TestMethod]
    public void SensitiveScope_RequiresHumanApprover()
    {
        var result = ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with
        {
            ApprovalScope = ProjectApprovalRuleScopes.ExternalSideEffect,
            RiskLevel = ProjectApprovalRuleRiskLevels.Critical,
            ApprovalType = ProjectApprovalRuleApprovalTypes.Single,
            ApproverTypes = [ProjectApprovalRuleApproverTypes.System]
        });

        AssertContainsIssue(result, "SENSITIVE_SCOPE_REQUIRES_HUMAN_APPROVER");
    }

    [TestMethod] public void SourceApply_RequiresHumanApproval() => AssertSensitiveScopeRequiresHuman(ProjectApprovalRuleScopes.SourceApply);

    [TestMethod] public void MemoryPromotion_RequiresHumanApproval() => AssertSensitiveScopeRequiresHuman(ProjectApprovalRuleScopes.MemoryPromotion);

    [TestMethod] public void ReleaseReadiness_RequiresHumanApproval() => AssertSensitiveScopeRequiresHuman(ProjectApprovalRuleScopes.ReleaseReadiness);

    [TestMethod] public void ExternalSideEffect_RequiresHumanApproval() => AssertSensitiveScopeRequiresHuman(ProjectApprovalRuleScopes.ExternalSideEffect);

    [TestMethod] public void DestructiveOperation_RequiresHumanApproval() => AssertSensitiveScopeRequiresHuman(ProjectApprovalRuleScopes.DestructiveOperation);

    [TestMethod]
    public void ApprovalType_AllowsNoneForNonSensitiveScope()
    {
        var result = ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with
        {
            ApprovalScope = ProjectApprovalRuleScopes.ToolExecution,
            RiskLevel = ProjectApprovalRuleRiskLevels.Low,
            ApprovalType = ProjectApprovalRuleApprovalTypes.None,
            ApproverTypes = []
        });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues.Select(issue => issue.Code)));
    }

    [TestMethod] public void ApprovalType_AllowsSingle() => AssertValidApprovalType(ProjectApprovalRuleApprovalTypes.Single, [ProjectApprovalRuleApproverTypes.Human]);

    [TestMethod] public void ApprovalType_AllowsAnyOf() => AssertValidApprovalType(ProjectApprovalRuleApprovalTypes.AnyOf, [ProjectApprovalRuleApproverTypes.Human, ProjectApprovalRuleApproverTypes.ProjectLead]);

    [TestMethod] public void ApprovalType_AllowsAllOf() => AssertValidApprovalType(ProjectApprovalRuleApprovalTypes.AllOf, [ProjectApprovalRuleApproverTypes.Human, ProjectApprovalRuleApproverTypes.SecurityOwner]);

    [TestMethod] public void ApprovalType_AllowsQuorum() => AssertValidApprovalType(ProjectApprovalRuleApprovalTypes.Quorum, [ProjectApprovalRuleApproverTypes.Human, ProjectApprovalRuleApproverTypes.ProjectLead], 2);

    [TestMethod] public void ApprovalType_AllowsHumanOnly() => AssertValidApprovalType(ProjectApprovalRuleApprovalTypes.HumanOnly, [ProjectApprovalRuleApproverTypes.Human]);

    [TestMethod]
    public void ApprovalType_RejectsUnknownValue()
    {
        var result = ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with { ApprovalType = "AutoApprove" });

        AssertContainsIssue(result, "APPROVAL_TYPE_UNKNOWN");
    }

    [TestMethod]
    public void Quorum_RequiresPositiveCount()
    {
        var result = ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with
        {
            ApprovalType = ProjectApprovalRuleApprovalTypes.Quorum,
            ApproverTypes = [ProjectApprovalRuleApproverTypes.Human, ProjectApprovalRuleApproverTypes.ProjectLead],
            QuorumCount = 0
        });

        AssertContainsIssue(result, "QUORUM_COUNT_REQUIRED");
    }

    [TestMethod]
    public void Quorum_RejectsCountGreaterThanApproverCount()
    {
        var result = ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with
        {
            ApprovalType = ProjectApprovalRuleApprovalTypes.Quorum,
            ApproverTypes = [ProjectApprovalRuleApproverTypes.Human],
            QuorumCount = 2
        });

        AssertContainsIssue(result, "QUORUM_COUNT_EXCEEDS_APPROVERS");
    }

    [TestMethod]
    public void NonQuorum_RejectsQuorumCount()
    {
        var result = ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with
        {
            ApprovalType = ProjectApprovalRuleApprovalTypes.Single,
            ApproverTypes = [ProjectApprovalRuleApproverTypes.Human],
            QuorumCount = 1
        });

        AssertContainsIssue(result, "QUORUM_COUNT_NOT_ALLOWED");
    }

    [TestMethod]
    public void ApproverType_AllowsHumanProjectLeadMemoryOwnerSecurityOwnerReleaseOwnerOperator()
    {
        foreach (var approverType in new[]
        {
            ProjectApprovalRuleApproverTypes.Human,
            ProjectApprovalRuleApproverTypes.ProjectLead,
            ProjectApprovalRuleApproverTypes.MemoryOwner,
            ProjectApprovalRuleApproverTypes.SecurityOwner,
            ProjectApprovalRuleApproverTypes.ReleaseOwner,
            ProjectApprovalRuleApproverTypes.Operator
        })
        {
            var result = ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with
            {
                ApprovalType = ProjectApprovalRuleApprovalTypes.Single,
                ApproverTypes = [approverType]
            });

            Assert.IsTrue(result.IsValid, $"{approverType}: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
        }
    }

    [TestMethod]
    public void ApproverType_AllowsSystemOrAgentOnlyForNonSensitiveScope()
    {
        foreach (var approverType in new[] { ProjectApprovalRuleApproverTypes.System, ProjectApprovalRuleApproverTypes.Agent })
        {
            var result = ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with
            {
                ApprovalScope = ProjectApprovalRuleScopes.ToolExecution,
                RiskLevel = ProjectApprovalRuleRiskLevels.Low,
                ApprovalType = ProjectApprovalRuleApprovalTypes.Single,
                ApproverTypes = [approverType]
            });

            Assert.IsTrue(result.IsValid, $"{approverType}: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
        }
    }

    [TestMethod]
    public void ApproverType_RejectsModelLlmCriticRetrieverVectorStoreWorkflowLangGraphA2aDogfoodReceiptGateDecisionPolicyDecision()
    {
        foreach (var approverType in ProjectApprovalRuleApproverTypes.Forbidden)
        {
            var result = ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with
            {
                ApprovalType = ProjectApprovalRuleApprovalTypes.Single,
                ApproverTypes = [approverType]
            });

            AssertContainsIssue(result, "APPROVER_TYPE_FORBIDDEN");
        }
    }

    [TestMethod] public void SensitiveScope_RejectsAgentOnlyApprover() => AssertSensitiveRejectsAutomatedApprover(ProjectApprovalRuleApproverTypes.Agent);

    [TestMethod] public void SensitiveScope_RejectsSystemOnlyApprover() => AssertSensitiveRejectsAutomatedApprover(ProjectApprovalRuleApproverTypes.System);

    [TestMethod]
    public void Validate_RejectsEmptyProjectId()
    {
        AssertContainsIssue(ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with { ProjectId = Guid.Empty }), "PROJECT_ID_REQUIRED");
    }

    [TestMethod]
    public void Validate_RejectsEmptyProjectAutonomyPolicyId()
    {
        AssertContainsIssue(ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with { ProjectAutonomyPolicyId = Guid.Empty }), "PROJECT_AUTONOMY_POLICY_ID_REQUIRED");
    }

    [TestMethod]
    public void Validate_RejectsBlankRuleName()
    {
        AssertContainsIssue(ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with { RuleName = " " }), "RULE_NAME_REQUIRED");
    }

    [TestMethod]
    public void Validate_RejectsNonPositiveRuleVersion()
    {
        AssertContainsIssue(ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with { RuleVersion = 0 }), "RULE_VERSION_REQUIRED");
    }

    [TestMethod]
    public void Validate_RejectsUnknownStatus()
    {
        AssertContainsIssue(ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with { Status = "Enabled" }), "STATUS_UNKNOWN");
    }

    [TestMethod]
    public void Validate_RejectsBlankApprovalScope()
    {
        AssertContainsIssue(ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with { ApprovalScope = " " }), "APPROVAL_SCOPE_REQUIRED");
    }

    [TestMethod]
    public void Validate_RejectsUnknownApprovalScope()
    {
        AssertContainsIssue(ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with { ApprovalScope = "ship_it" }), "APPROVAL_SCOPE_UNKNOWN");
    }

    [TestMethod]
    public void Validate_RejectsUnknownRiskLevel()
    {
        AssertContainsIssue(ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with { RiskLevel = "Severe" }), "RISK_LEVEL_UNKNOWN");
    }

    [TestMethod]
    public void Validate_RejectsUnknownApprovalType()
    {
        AssertContainsIssue(ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with { ApprovalType = "NoApproval" }), "APPROVAL_TYPE_UNKNOWN");
    }

    [TestMethod]
    public void Validate_RejectsMissingApproversWhenRequired()
    {
        AssertContainsIssue(ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with { ApprovalType = ProjectApprovalRuleApprovalTypes.Single, ApproverTypes = [] }), "APPROVERS_REQUIRED");
    }

    [TestMethod]
    public void Validate_RejectsBlankActorType()
    {
        AssertContainsIssue(ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with { CreatedByActorType = " " }), "ACTOR_TYPE_REQUIRED");
    }

    [TestMethod]
    public void Validate_RejectsBlankActorId()
    {
        AssertContainsIssue(ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with { CreatedByActorId = " " }), "ACTOR_ID_REQUIRED");
    }

    [TestMethod]
    public void Validate_RejectsInvalidMetadataJson()
    {
        AssertContainsIssue(ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with { MetadataJson = "{not-json" }), "METADATA_JSON_INVALID");
    }

    [TestMethod]
    public void Validate_RejectsNonPositiveMetadataVersion()
    {
        AssertContainsIssue(ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with { MetadataVersion = 0 }), "METADATA_VERSION_REQUIRED");
    }

    [TestMethod]
    public void Validate_RejectsPrivateReasoningMarkers()
    {
        AssertContainsIssue(ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with
        {
            MetadataJson = """
                {
                  "schema": "project.approval.rule.metadata.v1",
                  "hiddenReasoning": "secret chain of thought"
                }
                """
        }), "METADATA_PRIVATE_REASONING");
    }

    [TestMethod]
    public void Validate_RejectsAuthorityGrantingMetadata()
    {
        AssertContainsIssue(ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with
        {
            MetadataJson = """
                {
                  "schema": "project.approval.rule.metadata.v1",
                  "grantsApproval": true
                }
                """
        }), "METADATA_AUTHORITY_GRANT");
    }

    [TestMethod]
    public void ProjectApprovalRule_DoesNotCreateApprovalDecision() => AssertSourceDoesNotContain("IApprovalDecisionStore", "ApprovalDecisionStore", "approval.decision.recorded");

    [TestMethod]
    public void ProjectApprovalRule_DoesNotCreatePolicyDecisionEvent() => AssertSourceDoesNotContain("IPolicyDecisionEventStore", "PolicyDecisionEventStore", "policy.decision.recorded");

    [TestMethod]
    public void ProjectApprovalRule_DoesNotExecuteTool() => AssertSourceDoesNotContain("IToolExecutor", "ToolExecutionAuditRecordFactory", "Process.Start", "RunTool");

    [TestMethod]
    public void ProjectApprovalRule_DoesNotStartWorkflow() => AssertSourceDoesNotContain("IWorkflowRunner", "StartWorkflow", "ContinueWorkflow");

    [TestMethod]
    public void ProjectApprovalRule_DoesNotMutateSource() => AssertSourceDoesNotContain("IWorkspaceApply", "File.Copy", "File.Delete", "sourceRepoMutated");

    [TestMethod]
    public void ProjectApprovalRule_DoesNotPromoteMemory() => AssertSourceDoesNotContain("CollectiveMemoryPromotion", "PromoteCollectiveMemory", "AcceptedMemory");

    [TestMethod]
    public void ProjectApprovalRule_DoesNotCreateA2aHandoff() => AssertSourceDoesNotContain("CreateA2aHandoff", "IA2a", "A2aMessageBus");

    [TestMethod]
    public void ProjectApprovalRule_DoesNotCreateDogfoodReceipt() => AssertSourceDoesNotContain("IDogfoodReceiptStore", "DogfoodReceiptStore", "dogfood.receipt.recorded");

    [TestMethod]
    public void ProjectApprovalRule_DoesNotMarkReleaseReady() => AssertSourceDoesNotContain("ReleaseReady", "MarkReleaseReady", "ReadyToShip");

    [TestMethod]
    public void ProjectApprovalRule_IsNotRegisteredAsRuntimeEvaluator()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Program.cs"));

        Assert.IsFalse(program.Contains("ProjectApprovalRule", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ProjectApprovalRule_DoesNotAddApiEndpoint()
    {
        AssertDirectoryDoesNotContain("IronDev.Api", "ProjectApprovalRule", "approval-rules", "approval rules");
    }

    [TestMethod]
    public void ProjectApprovalRule_DoesNotAddCliCommand()
    {
        AssertDirectoryDoesNotContain(Path.Combine("tools", "IronDev.Cli"), "ProjectApprovalRule", "approval-rule", "approval rules");
    }

    [TestMethod]
    public void ProjectApprovalRule_DoesNotAddSqlMigration()
    {
        var databaseRoot = Path.Combine(RepositoryRoot(), "Database");
        if (!Directory.Exists(databaseRoot))
        {
            return;
        }

        var sqlFiles = Directory.EnumerateFiles(databaseRoot, "*.sql", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .ToArray();

        Assert.IsFalse(sqlFiles.Any(name => name!.Contains("approval_rule", StringComparison.OrdinalIgnoreCase)), string.Join(", ", sqlFiles));
    }

    [TestMethod]
    public void ProjectApprovalRule_DoesNotReferenceWorkflowRunner() => AssertSourceDoesNotContain("WorkflowRunner", "IWorkflowRunner", "ContinueWorkflow");

    [TestMethod]
    public void ProjectApprovalRule_DoesNotReferenceExecutor() => AssertSourceDoesNotContain("ExecutorService", "IToolExecutor", "IAgentToolExecutor");

    [TestMethod]
    public void ProjectApprovalRule_DoesNotReferenceMemoryPromotion() => AssertSourceDoesNotContain("MemoryPromotionService", "PromoteMemory", "CollectiveMemoryPromotion");

    [TestMethod]
    public void ProjectApprovalRule_DoesNotReferenceSourceApply() => AssertSourceDoesNotContain("ApplyCopy", "SourceApplyService", "IWorkspaceApplyCopy");

    [TestMethod]
    public void ProjectApprovalRule_Documentation_DefinesVocabularyOnly()
    {
        var doc = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "BLOCK_H_PROJECT_AUTHORITY_POLICY_MODEL.md"));

        StringAssert.Contains(doc, "PR83 defines project approval rule vocabulary only.");
        StringAssert.Contains(doc, "PR83 does not evaluate rules.");
        StringAssert.Contains(doc, "PR83 does not create approval decisions.");
        StringAssert.Contains(doc, "PR83 does not execute anything.");
        StringAssert.Contains(doc, "PR83 does not add SQL, API, CLI, or runtime wiring.");
        StringAssert.Contains(doc, "ApprovalType=None is forbidden");
        StringAssert.Contains(doc, "free remains forbidden");
    }

    [TestMethod]
    public void ProjectApprovalRule_Wording_AllowsForbiddenTermsOnlyInNegativeStatements()
    {
        var files = new[]
        {
            File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Core", "Policy", "ProjectApprovalRuleModels.cs")),
            File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "BLOCK_H_PROJECT_AUTHORITY_POLICY_MODEL.md"))
        };

        foreach (var text in files)
        {
            foreach (var phrase in new[]
            {
                "free",
                "unrestricted",
                "fully autonomous",
                "no approval",
                "auto approve",
                "auto-approved",
                "auto execute",
                "authorized",
                "ready to run",
                "execution allowed",
                "permission granted",
                "can execute",
                "source apply allowed",
                "memory promotion allowed",
                "release approved",
                "can ship",
                "policy satisfied"
            })
            {
                AssertPhraseOnlyAppearsInNegativeOrGuardContext(text, phrase);
            }
        }
    }

    private static void AssertValidScope(string scope)
    {
        var result = ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with { ApprovalScope = scope });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues.Select(issue => issue.Code)));
    }

    private static void AssertValidSensitiveScope(string scope)
    {
        var result = ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with
        {
            ApprovalScope = scope,
            RiskLevel = ProjectApprovalRuleRiskLevels.Critical,
            ApprovalType = ProjectApprovalRuleApprovalTypes.HumanOnly,
            ApproverTypes = [ProjectApprovalRuleApproverTypes.Human]
        });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues.Select(issue => issue.Code)));
    }

    private static void AssertSensitiveScopeRequiresHuman(string scope)
    {
        var result = ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with
        {
            ApprovalScope = scope,
            RiskLevel = ProjectApprovalRuleRiskLevels.Critical,
            ApprovalType = ProjectApprovalRuleApprovalTypes.Single,
            ApproverTypes = [ProjectApprovalRuleApproverTypes.Agent]
        });

        AssertContainsIssue(result, "SENSITIVE_SCOPE_REQUIRES_HUMAN_APPROVER");
    }

    private static void AssertValidApprovalType(string approvalType, IReadOnlyList<string> approverTypes, int? quorumCount = null)
    {
        var result = ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with
        {
            ApprovalType = approvalType,
            ApproverTypes = approverTypes,
            QuorumCount = quorumCount
        });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues.Select(issue => issue.Code)));
    }

    private static void AssertSensitiveRejectsAutomatedApprover(string approverType)
    {
        var result = ProjectApprovalRuleValidator.ValidateCreate(ValidRequest() with
        {
            ApprovalScope = ProjectApprovalRuleScopes.SourceApply,
            RiskLevel = ProjectApprovalRuleRiskLevels.Critical,
            ApprovalType = ProjectApprovalRuleApprovalTypes.Single,
            ApproverTypes = [approverType]
        });

        AssertContainsIssue(result, "SENSITIVE_SCOPE_REJECTS_AUTOMATED_APPROVER");
    }

    private static ProjectApprovalRuleCreateRequest ValidRequest() =>
        new()
        {
            ProjectId = Guid.NewGuid(),
            ProjectAutonomyPolicyId = Guid.NewGuid(),
            RuleName = "Source governance rule",
            RuleVersion = 1,
            Status = ProjectApprovalRuleStatuses.Draft,
            ApprovalScope = ProjectApprovalRuleScopes.ToolExecution,
            SubjectTypePattern = "tool_request",
            ActionNamePattern = "workspace.diff",
            RiskLevel = ProjectApprovalRuleRiskLevels.Low,
            ApprovalType = ProjectApprovalRuleApprovalTypes.None,
            ApproverTypes = [],
            CreatedByActorType = "human",
            CreatedByActorId = "human-reviewer",
            MetadataVersion = 1,
            MetadataJson = """
                {
                  "schema": "project.approval.rule.metadata.v1",
                  "notes": "Rule contract metadata for governed project actions.",
                  "grantsApproval": false,
                  "grantsExecution": false,
                  "mutatesSource": false,
                  "promotesMemory": false,
                  "startsWorkflow": false,
                  "satisfiesPolicy": false,
                  "transfersAuthority": false
                }
                """
        };

    private static void AssertProperties<T>(params string[] propertyNames)
    {
        var actual = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        foreach (var propertyName in propertyNames)
        {
            CollectionAssert.Contains(actual, propertyName);
        }
    }

    private static void AssertContainsIssue(ProjectApprovalRuleValidationResult result, string code)
    {
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == code), $"{code}: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertSourceDoesNotContain(params string[] tokens)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Core", "Policy", "ProjectApprovalRuleModels.cs"));
        foreach (var token in tokens)
        {
            Assert.IsFalse(source.Contains(token, StringComparison.OrdinalIgnoreCase), token);
        }
    }

    private static void AssertDirectoryDoesNotContain(string relativeDirectory, params string[] tokens)
    {
        var directory = Path.Combine(RepositoryRoot(), relativeDirectory);
        if (!Directory.Exists(directory))
        {
            return;
        }

        var text = string.Join(Environment.NewLine, Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));

        foreach (var token in tokens)
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), token);
        }
    }

    private static void AssertPhraseOnlyAppearsInNegativeOrGuardContext(string text, string phrase)
    {
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (!line.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var contextStart = Math.Max(0, index - 80);
            var context = string.Join(" ", lines.Skip(contextStart).Take(index - contextStart + 1)).ToLowerInvariant();
            var isAllowed = context.Contains("forbidden", StringComparison.Ordinal)
                || context.Contains("unsafe", StringComparison.Ordinal)
                || context.Contains("must not", StringComparison.Ordinal)
                || context.Contains("does not", StringComparison.Ordinal)
                || context.Contains("not ", StringComparison.Ordinal)
                || context.Contains(" no ", StringComparison.Ordinal)
                || context.Contains("cannot", StringComparison.Ordinal)
                || context.Contains("reject", StringComparison.Ordinal);

            Assert.IsTrue(isAllowed, $"Forbidden policy phrase must appear only in negative or guard context: {phrase} / {line}");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
