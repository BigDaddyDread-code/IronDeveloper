using System.Reflection;
using IronDev.Core.Policy;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ProjectPolicyProfile")]
public sealed class ProjectPolicyProfileTests
{
    private static readonly string[] SensitiveScopes =
    [
        ProjectApprovalRuleScopes.SourceApply,
        ProjectApprovalRuleScopes.MemoryPromotion,
        ProjectApprovalRuleScopes.ReleaseReadiness,
        ProjectApprovalRuleScopes.ExternalSideEffect,
        ProjectApprovalRuleScopes.DestructiveOperation
    ];

    [TestMethod]
    public void ProjectPolicyProfileCatalog_ExposesOnlyConservativeBalancedExperimental()
    {
        CollectionAssert.AreEquivalent(
            new[] { ProjectPolicyProfileNames.Conservative, ProjectPolicyProfileNames.Balanced, ProjectPolicyProfileNames.Experimental },
            ProjectPolicyProfileCatalog.All.Select(profile => profile.ProfileName).ToArray());

        Assert.AreEqual(3, ProjectPolicyProfileCatalog.Summaries.Count);
    }

    [TestMethod]
    public void ProjectPolicyProfileNames_RejectForbiddenProfileNames()
    {
        foreach (var profileName in ProjectPolicyProfileNames.Forbidden)
        {
            Assert.IsTrue(ProjectPolicyProfileNames.IsForbidden(profileName));
            Assert.IsFalse(ProjectPolicyProfileNames.IsAllowed(profileName));
            Assert.IsNull(ProjectPolicyProfileCatalog.Find(profileName));
        }
    }

    [TestMethod]
    public void ProjectPolicyProfiles_ValidateAndGrantNoAuthority()
    {
        foreach (var profile in ProjectPolicyProfileCatalog.All)
        {
            var result = ProjectPolicyProfileValidator.Validate(profile);

            Assert.IsTrue(result.IsValid, $"{profile.ProfileName}: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
            AssertNoProfileAuthority(profile);
        }
    }

    [TestMethod]
    public void ProjectPolicyProfiles_UseKnownAutonomyLevels()
    {
        Assert.AreEqual(nameof(ProjectAutonomyLevel.Conservative), ProjectPolicyProfileCatalog.Get(ProjectPolicyProfileNames.Conservative).AutonomyLevel);
        Assert.AreEqual(nameof(ProjectAutonomyLevel.Balanced), ProjectPolicyProfileCatalog.Get(ProjectPolicyProfileNames.Balanced).AutonomyLevel);
        Assert.AreEqual(nameof(ProjectAutonomyLevel.Experimental), ProjectPolicyProfileCatalog.Get(ProjectPolicyProfileNames.Experimental).AutonomyLevel);
    }

    [TestMethod]
    public void ConservativeProfile_RequiresApprovalForEveryTemplate()
    {
        var profile = ProjectPolicyProfileCatalog.Get(ProjectPolicyProfileNames.Conservative);

        Assert.IsFalse(profile.RuleTemplates.Any(rule => string.Equals(rule.ApprovalType, ProjectApprovalRuleApprovalTypes.None, StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BalancedProfile_RequiresApprovalForEveryTemplate()
    {
        var profile = ProjectPolicyProfileCatalog.Get(ProjectPolicyProfileNames.Balanced);

        Assert.IsFalse(profile.RuleTemplates.Any(rule => string.Equals(rule.ApprovalType, ProjectApprovalRuleApprovalTypes.None, StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ExperimentalProfile_RelaxesOnlyNonSensitiveScopes()
    {
        var relaxed = ProjectPolicyProfileCatalog.Get(ProjectPolicyProfileNames.Experimental).RuleTemplates
            .Where(rule => string.Equals(rule.ApprovalType, ProjectApprovalRuleApprovalTypes.None, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.IsTrue(relaxed.Length > 0);
        Assert.IsTrue(relaxed.All(rule => !ProjectApprovalRuleScopes.IsSensitive(rule.ApprovalScope)));
    }

    [TestMethod]
    public void EveryProfile_SensitiveScopesRequireHumanApproval()
    {
        foreach (var profile in ProjectPolicyProfileCatalog.All)
        {
            foreach (var scope in SensitiveScopes)
            {
                var rule = profile.RuleTemplates.Single(template => template.ApprovalScope == scope);

                Assert.AreNotEqual(ProjectApprovalRuleApprovalTypes.None, rule.ApprovalType, $"{profile.ProfileName}/{scope}");
                Assert.AreEqual(ProjectApprovalRuleRiskLevels.Critical, rule.RiskLevel, $"{profile.ProfileName}/{scope}");
                Assert.IsTrue(rule.ApproverTypes.Any(ProjectApprovalRuleApproverTypes.IsHumanClass), $"{profile.ProfileName}/{scope}");
                Assert.IsFalse(rule.ApproverTypes.Any(ProjectApprovalRuleApproverTypes.IsAutomated), $"{profile.ProfileName}/{scope}");
            }
        }
    }

    [TestMethod]
    public void Factory_CreatesDraftPoliciesThatPassExistingValidator()
    {
        var factory = new ProjectPolicyProfileFactory();
        foreach (var profileName in ProjectPolicyProfileNames.All)
        {
            var request = factory.CreateDraftPolicy(Guid.NewGuid(), profileName, "human", "human-reviewer");
            var result = new ProjectAutonomyPolicyValidator().ValidateCreate(request);

            Assert.IsTrue(result.IsValid, $"{profileName}: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
            Assert.AreEqual(nameof(ProjectAutonomyPolicyStatus.Draft), request.Status);
            Assert.AreEqual(ProjectPolicyProfileNames.Normalize(profileName), request.AutonomyLevel);
        }
    }

    [TestMethod]
    public void Factory_CreatesDraftApprovalRulesThatPassExistingValidator()
    {
        var factory = new ProjectPolicyProfileFactory();
        foreach (var profileName in ProjectPolicyProfileNames.All)
        {
            var rules = factory.CreateDraftApprovalRules(Guid.NewGuid(), Guid.NewGuid(), profileName, "human", "human-reviewer");

            Assert.AreEqual(ProjectPolicyProfileCatalog.Get(profileName).RuleTemplates.Count, rules.Count);
            foreach (var rule in rules)
            {
                var result = ProjectApprovalRuleValidator.ValidateCreate(rule);
                Assert.IsTrue(result.IsValid, $"{profileName}/{rule.RuleName}: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
                Assert.AreEqual(ProjectApprovalRuleStatuses.Draft, rule.Status);
            }
        }
    }

    [TestMethod]
    public void GeneratedExperimentalRules_DoNotBypassSensitiveEvaluatorRequirements()
    {
        var factory = new ProjectPolicyProfileFactory();
        var projectId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var policy = ActivePolicy(factory.CreateDraftPolicy(projectId, ProjectPolicyProfileNames.Experimental, "human", "human-reviewer"), policyId);
        var rules = factory.CreateDraftApprovalRules(projectId, policyId, ProjectPolicyProfileNames.Experimental, "human", "human-reviewer")
            .Select((rule, index) => ActiveRule(rule, Guid.NewGuid()))
            .ToArray();

        foreach (var scope in SensitiveScopes)
        {
            var rule = rules.Single(candidate => candidate.ApprovalScope == scope);
            var result = new ApprovalRequirementEvaluator().Evaluate(new ApprovalRequirementEvaluationRequest
            {
                ProjectId = projectId,
                ProjectPolicy = policy,
                ApprovalRules = rules,
                ApprovalScope = scope,
                SubjectType = rule.SubjectTypePattern ?? "subject",
                SubjectId = "subject-1",
                ActionName = rule.ActionNamePattern,
                RequestedByActorType = "human",
                RequestedByActorId = "human-reviewer",
                ContextVersion = 1,
                ContextJson = """
                    {
                      "schema": "approval.requirement.context.v1",
                      "notes": "Safe profile evaluation context."
                    }
                    """
            });

            Assert.AreEqual(ApprovalRequirementOutcomes.ApprovalRequired, result.Outcome, scope);
            Assert.IsFalse(result.GrantsApproval);
            Assert.IsFalse(result.GrantsExecution);
            Assert.IsFalse(result.MutatesSource);
            Assert.IsFalse(result.PromotesMemory);
            Assert.IsTrue(result.Requirements.Single().RequiredApproverTypes.Any(ProjectApprovalRuleApproverTypes.IsHumanClass), scope);
        }
    }

    [TestMethod]
    public void GeneratedRequirements_CanBeCarriedByApprovalPackageWithoutGrantingAuthority()
    {
        var factory = new ProjectPolicyProfileFactory();
        var projectId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var policy = ActivePolicy(factory.CreateDraftPolicy(projectId, ProjectPolicyProfileNames.Balanced, "human", "human-reviewer"), policyId);
        var rules = factory.CreateDraftApprovalRules(projectId, policyId, ProjectPolicyProfileNames.Balanced, "human", "human-reviewer")
            .Select(rule => ActiveRule(rule, Guid.NewGuid()))
            .ToArray();
        var sourceApply = rules.Single(rule => rule.ApprovalScope == ProjectApprovalRuleScopes.SourceApply);
        var evaluation = new ApprovalRequirementEvaluator().Evaluate(new ApprovalRequirementEvaluationRequest
        {
            ProjectId = projectId,
            ProjectPolicy = policy,
            ApprovalRules = rules,
            ApprovalScope = ProjectApprovalRuleScopes.SourceApply,
            SubjectType = sourceApply.SubjectTypePattern ?? "source_apply_package",
            SubjectId = "source-apply-1",
            ActionName = sourceApply.ActionNamePattern,
            RequestedByActorType = "human",
            RequestedByActorId = "human-reviewer",
            ContextVersion = 1,
            ContextJson = """
                {
                  "schema": "approval.requirement.context.v1",
                  "notes": "Safe package context."
                }
                """
        });

        var package = new ApprovalPackage
        {
            ApprovalPackageId = Guid.NewGuid(),
            ProjectId = projectId,
            PackageName = "Source apply review package",
            PackageVersion = 1,
            Status = ApprovalPackageStatuses.Draft,
            ApprovalScope = ProjectApprovalRuleScopes.SourceApply,
            SubjectType = "SourceApplyPackage",
            SubjectId = "source-apply-1",
            ActionName = sourceApply.ActionNamePattern,
            SourceEvaluationId = Guid.NewGuid(),
            SourceEvaluationOutcome = evaluation.Outcome,
            Requirements = evaluation.Requirements.Select(requirement => new ApprovalPackageRequirement
            {
                ApprovalScope = requirement.ApprovalScope,
                ApprovalType = requirement.ApprovalType,
                RiskLevel = requirement.RiskLevel,
                RequiredApproverTypes = requirement.RequiredApproverTypes,
                QuorumCount = requirement.QuorumCount,
                RequirementCode = requirement.RequirementCode,
                RequirementReason = requirement.RequirementReason,
                SourceRuleId = requirement.SourceRuleId
            }).ToArray(),
            EvidenceReferences =
            [
                new ApprovalPackageEvidenceReference
                {
                    EvidenceType = ApprovalPackageEvidenceTypes.GovernanceEvent,
                    EvidenceId = "governance-event-1",
                    EvidenceLabel = "Governance event",
                    EvidenceSummary = "Evaluation evidence reference."
                }
            ],
            CreatedByActorType = "human",
            CreatedByActorId = "human-reviewer",
            MetadataVersion = 1,
            MetadataJson = """
                {
                  "schema": "approval.package.metadata.v1",
                  "notes": "Safe package metadata."
                }
                """,
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        var result = ApprovalPackageValidator.Validate(package);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues.Select(issue => issue.Code)));
        Assert.IsFalse(package.GrantsApproval);
        Assert.IsFalse(package.GrantsExecution);
        Assert.IsFalse(package.MutatesSource);
        Assert.IsFalse(package.PromotesMemory);
    }

    [TestMethod]
    public void ProjectPolicyProfileFactory_DoesNotExposeActivateEvaluateApproveOrExecuteMethods()
    {
        var methods = typeof(IProjectPolicyProfileFactory).GetMethods().Select(method => method.Name).ToArray();

        CollectionAssert.Contains(methods, nameof(IProjectPolicyProfileFactory.CreateDraftPolicy));
        CollectionAssert.Contains(methods, nameof(IProjectPolicyProfileFactory.CreateDraftApprovalRules));
        Assert.AreEqual(2, methods.Length);

        foreach (var method in methods)
        {
            foreach (var token in new[] { "Activate", "Evaluate", "Approve", "Execute", "Run", "Start", "Continue", "Apply", "Promote", "Record" })
            {
                Assert.IsFalse(method.Contains(token, StringComparison.OrdinalIgnoreCase), method);
            }
        }
    }

    [TestMethod]
    public void ProjectPolicyProfileModels_DoNotAddRuntimeApiCliSqlOrPersistenceWiring()
    {
        var source = File.ReadAllText(ProjectPolicyProfileSourcePath());
        foreach (var token in new[]
        {
            "SqlConnection",
            "DbCommand",
            "ControllerBase",
            "WebApplication",
            "IHostedService",
            "WorkflowRunner",
            "IToolExecutor",
            "ApprovalDecisionStore",
            "PolicyDecisionEventStore",
            "MemoryPromotionStore",
            "SourceApplyService",
            "CollectiveMemoryPromotion",
            "DogfoodReceiptStore"
        })
        {
            Assert.IsFalse(source.Contains(token, StringComparison.OrdinalIgnoreCase), token);
        }

        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Program.cs"));
        Assert.IsFalse(program.Contains("ProjectPolicyProfile", StringComparison.OrdinalIgnoreCase));
        AssertDirectoryDoesNotContain(Path.Combine("tools", "IronDev.Cli"), "ProjectPolicyProfile", "policy-profile");
        AssertDirectoryDoesNotContain("Database", "ProjectPolicyProfile", "policy_profile");
    }

    [TestMethod]
    public void ProjectPolicyProfileDocumentation_RecordsTemplateOnlyBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "BLOCK_H_PROJECT_AUTHORITY_POLICY_MODEL.md"));

        StringAssert.Contains(doc, "PR86 Conservative/Balanced/Experimental Policy Profiles");
        StringAssert.Contains(doc, "Profiles produce draft policy and rule shapes only.");
        StringAssert.Contains(doc, "Profiles do not activate policy.");
        StringAssert.Contains(doc, "Profiles do not evaluate policy.");
        StringAssert.Contains(doc, "Profiles do not approve anything.");
        StringAssert.Contains(doc, "Profiles do not execute anything.");
        StringAssert.Contains(doc, "Experimental does not bypass human approval for sensitive scopes.");
    }

    private static void AssertNoProfileAuthority(ProjectPolicyProfile profile)
    {
        Assert.IsFalse(profile.GrantsApproval);
        Assert.IsFalse(profile.GrantsExecution);
        Assert.IsFalse(profile.MutatesSource);
        Assert.IsFalse(profile.PromotesMemory);
        Assert.IsFalse(profile.StartsWorkflow);
        Assert.IsFalse(profile.SatisfiesPolicy);
        Assert.IsFalse(profile.TransfersAuthority);
    }

    private static ProjectAutonomyPolicy ActivePolicy(ProjectAutonomyPolicyCreateRequest request, Guid policyId) =>
        new()
        {
            ProjectAutonomyPolicyId = policyId,
            ProjectId = request.ProjectId,
            PolicyName = request.PolicyName,
            PolicyVersion = request.PolicyVersion,
            AutonomyLevel = request.AutonomyLevel,
            Status = nameof(ProjectAutonomyPolicyStatus.Active),
            SupersedesPolicyId = request.SupersedesPolicyId,
            CreatedByActorType = request.CreatedByActorType,
            CreatedByActorId = request.CreatedByActorId,
            MetadataVersion = request.MetadataVersion,
            MetadataJson = request.MetadataJson,
            CreatedUtc = DateTimeOffset.UtcNow
        };

    private static ProjectApprovalRule ActiveRule(ProjectApprovalRuleCreateRequest request, Guid ruleId) =>
        new()
        {
            ProjectApprovalRuleId = ruleId,
            ProjectId = request.ProjectId,
            ProjectAutonomyPolicyId = request.ProjectAutonomyPolicyId,
            RuleName = request.RuleName,
            RuleVersion = request.RuleVersion,
            Status = ProjectApprovalRuleStatuses.Active,
            ApprovalScope = request.ApprovalScope,
            SubjectTypePattern = request.SubjectTypePattern,
            ActionNamePattern = request.ActionNamePattern,
            RiskLevel = request.RiskLevel,
            ApprovalType = request.ApprovalType,
            ApproverTypes = request.ApproverTypes,
            QuorumCount = request.QuorumCount,
            SupersedesRuleId = request.SupersedesRuleId,
            CreatedByActorType = request.CreatedByActorType,
            CreatedByActorId = request.CreatedByActorId,
            MetadataVersion = request.MetadataVersion,
            MetadataJson = request.MetadataJson,
            CreatedUtc = DateTimeOffset.UtcNow
        };

    private static void AssertDirectoryDoesNotContain(string relativeDirectory, params string[] tokens)
    {
        var directory = Path.Combine(RepositoryRoot(), relativeDirectory);
        if (!Directory.Exists(directory))
        {
            return;
        }

        var text = string.Join(Environment.NewLine, Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText));

        foreach (var token in tokens)
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), token);
        }
    }

    private static string ProjectPolicyProfileSourcePath() =>
        Path.Combine(RepositoryRoot(), "IronDev.Core", "Policy", "ProjectPolicyProfileModels.cs");

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
