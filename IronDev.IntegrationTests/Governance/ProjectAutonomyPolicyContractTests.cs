using IronDev.Core.Policy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ProjectAutonomyPolicy")]
public sealed class ProjectAutonomyPolicyContractTests
{
    private static readonly string[] ForbiddenAutonomyLevels =
    [
        "Free",
        "Unrestricted",
        "Autonomous",
        "FullAuto",
        "NoApproval",
        "GodMode",
        "Unlimited",
        "Unsafe"
    ];

    [TestMethod]
    public void ProjectAutonomyPolicyContracts_ExposeExpectedPolicyShape()
    {
        var policyProperties = typeof(ProjectAutonomyPolicy).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray();
        var requestProperties = typeof(ProjectAutonomyPolicyCreateRequest).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray();
        var summaryProperties = typeof(ProjectAutonomyPolicySummary).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray();

        CollectionAssert.AreEquivalent(new[]
        {
            "AutonomyLevel",
            "CreatedByActorId",
            "CreatedByActorType",
            "CreatedUtc",
            "MetadataJson",
            "MetadataVersion",
            "PolicyName",
            "PolicyVersion",
            "ProjectAutonomyPolicyId",
            "ProjectId",
            "Status",
            "SupersedesPolicyId"
        }, policyProperties);

        CollectionAssert.AreEquivalent(new[]
        {
            "AutonomyLevel",
            "CreatedByActorId",
            "CreatedByActorType",
            "MetadataJson",
            "MetadataVersion",
            "PolicyName",
            "PolicyVersion",
            "ProjectId",
            "Status",
            "SupersedesPolicyId"
        }, requestProperties);

        CollectionAssert.AreEquivalent(new[]
        {
            "AutonomyLevel",
            "CreatedUtc",
            "PolicyName",
            "PolicyVersion",
            "ProjectAutonomyPolicyId",
            "ProjectId",
            "Status"
        }, summaryProperties);
    }

    [TestMethod]
    public void ProjectAutonomyPolicyContracts_ExposeAllowedAutonomyLevelsAndStatuses()
    {
        CollectionAssert.AreEquivalent(
            new[] { "Balanced", "Conservative", "Experimental" },
            Enum.GetNames<ProjectAutonomyLevel>());

        CollectionAssert.AreEquivalent(
            new[] { "Active", "Draft", "Retired", "Superseded" },
            Enum.GetNames<ProjectAutonomyPolicyStatus>());
    }

    [TestMethod]
    public void ProjectAutonomyPolicyContracts_DoNotExposeApprovalExecutionPromotionOrEvaluatorSurface()
    {
        var policyMemberNames = typeof(ProjectAutonomyPolicy).GetMembers().Select(member => member.Name).ToArray();
        var requestMemberNames = typeof(ProjectAutonomyPolicyCreateRequest).GetMembers().Select(member => member.Name).ToArray();
        var summaryMemberNames = typeof(ProjectAutonomyPolicySummary).GetMembers().Select(member => member.Name).ToArray();
        var validatorMethods = typeof(ProjectAutonomyPolicyValidator).GetMethods().Select(method => method.Name).ToArray();

        foreach (var member in policyMemberNames.Concat(requestMemberNames).Concat(summaryMemberNames))
        {
            AssertNoForbiddenMemberName(member);
        }

        foreach (var method in validatorMethods)
        {
            Assert.IsFalse(method.Contains("Evaluate", StringComparison.OrdinalIgnoreCase), method);
            Assert.IsFalse(method.Contains("Approve", StringComparison.OrdinalIgnoreCase), method);
            Assert.IsFalse(method.Contains("Execute", StringComparison.OrdinalIgnoreCase), method);
            Assert.IsFalse(method.Contains("Apply", StringComparison.OrdinalIgnoreCase), method);
            Assert.IsFalse(method.Contains("Promote", StringComparison.OrdinalIgnoreCase), method);
            Assert.IsFalse(method.Contains("Route", StringComparison.OrdinalIgnoreCase), method);
            Assert.IsFalse(method.Contains("StartWorkflow", StringComparison.OrdinalIgnoreCase), method);
        }
    }

    [TestMethod]
    public void AutonomyLevel_AllowsConservativeBalancedExperimental()
    {
        var validator = new ProjectAutonomyPolicyValidator();

        foreach (var level in new[] { "Conservative", "Balanced", "Experimental" })
        {
            var result = validator.ValidateCreate(ValidRequest() with { AutonomyLevel = level });
            Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues.Select(issue => issue.Code)));
        }
    }

    [TestMethod]
    public void AutonomyLevel_RejectsForbiddenNames()
    {
        var validator = new ProjectAutonomyPolicyValidator();

        foreach (var level in ForbiddenAutonomyLevels)
        {
            var result = validator.ValidateCreate(ValidRequest() with { AutonomyLevel = level });
            Assert.IsFalse(result.IsValid, level);
            AssertContainsIssue(result, "AUTONOMY_LEVEL_FORBIDDEN");
        }
    }

    [TestMethod]
    public void AutonomyLevel_DoesNotUseAuthorityLanguage()
    {
        var allowedLevels = Enum.GetNames<ProjectAutonomyLevel>();

        CollectionAssert.DoesNotContain(allowedLevels, "Free");
        CollectionAssert.DoesNotContain(allowedLevels, "Unrestricted");
        CollectionAssert.DoesNotContain(allowedLevels, "Autonomous");
        CollectionAssert.DoesNotContain(allowedLevels, "FullAuto");
        CollectionAssert.DoesNotContain(allowedLevels, "NoApproval");
        CollectionAssert.DoesNotContain(allowedLevels, "GodMode");
        CollectionAssert.DoesNotContain(allowedLevels, "Unlimited");
        CollectionAssert.DoesNotContain(allowedLevels, "Unsafe");
    }

    [TestMethod]
    public void Validate_RejectsInvalidRequiredFields()
    {
        var validator = new ProjectAutonomyPolicyValidator();

        AssertContainsIssue(validator.ValidateCreate(ValidRequest() with { ProjectId = Guid.Empty }), "PROJECT_REQUIRED");
        AssertContainsIssue(validator.ValidateCreate(ValidRequest() with { PolicyName = "" }), "POLICY_NAME_REQUIRED");
        AssertContainsIssue(validator.ValidateCreate(ValidRequest() with { PolicyVersion = 0 }), "POLICY_VERSION_INVALID");
        AssertContainsIssue(validator.ValidateCreate(ValidRequest() with { AutonomyLevel = "Unknown" }), "AUTONOMY_LEVEL_INVALID");
        AssertContainsIssue(validator.ValidateCreate(ValidRequest() with { Status = "Unknown" }), "STATUS_INVALID");
        AssertContainsIssue(validator.ValidateCreate(ValidRequest() with { CreatedByActorType = "" }), "ACTOR_TYPE_REQUIRED");
        AssertContainsIssue(validator.ValidateCreate(ValidRequest() with { CreatedByActorId = "" }), "ACTOR_ID_REQUIRED");
        AssertContainsIssue(validator.ValidateCreate(ValidRequest() with { MetadataVersion = 0 }), "METADATA_VERSION_INVALID");
        AssertContainsIssue(validator.ValidateCreate(ValidRequest() with { MetadataJson = "" }), "METADATA_REQUIRED");
        AssertContainsIssue(validator.ValidateCreate(ValidRequest() with { MetadataJson = "not-json" }), "METADATA_JSON_INVALID");
    }

    [TestMethod]
    public void Validate_RejectsPrivateReasoningMarkers()
    {
        var validator = new ProjectAutonomyPolicyValidator();
        var markers = new[]
        {
            "hiddenReasoning",
            "chainOfThought",
            "chain-of-thought",
            "private reasoning",
            "scratchpad",
            "rawPrompt",
            "rawCompletion",
            "rawToolOutput",
            "entirePatch"
        };

        foreach (var marker in markers)
        {
            var json = $$"""{"schema":"project.autonomy.policy.metadata.v1","{{marker}}":"secret"}""";
            var result = validator.ValidateCreate(ValidRequest() with { MetadataJson = json });
            Assert.IsFalse(result.IsValid, marker);
            AssertContainsIssue(result, "METADATA_UNSAFE");
        }
    }

    [TestMethod]
    public void Validate_RejectsAuthorityGrantingMetadata()
    {
        var validator = new ProjectAutonomyPolicyValidator();
        var properties = new[]
        {
            "grantsApproval",
            "grantsExecution",
            "agentCanApprove",
            "autoApprove",
            "autoExecute",
            "executionAllowed",
            "permissionGranted",
            "mutatesSource",
            "sourceApplyAllowed",
            "promotesMemory",
            "memoryPromotionAllowed",
            "startsWorkflow",
            "satisfiesPolicy",
            "transfersAuthority",
            "releaseApproved"
        };

        foreach (var property in properties)
        {
            var json = $$"""{"schema":"project.autonomy.policy.metadata.v1","{{property}}":true}""";
            var result = validator.ValidateCreate(ValidRequest() with { MetadataJson = json });
            Assert.IsFalse(result.IsValid, property);
        }
    }

    [TestMethod]
    public void ProjectAutonomyPolicy_DoesNotCreateAuthorityOrSideEffectContracts()
    {
        var corePolicyFile = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Core", "Policy", "ProjectAutonomyPolicyModels.cs"));

        AssertNoForbiddenTokens(
            corePolicyFile,
            "IProjectAutonomyPolicyEvaluator",
            "EvaluatePolicy",
            "RecordPolicyDecision",
            "IApprovalDecisionStore",
            "IPolicyDecisionEventStore",
            "IDogfoodReceiptStore",
            "ExecuteTool",
            "StartWorkflow",
            "MutateSource",
            "PromoteMemory",
            "CreateA2aHandoff",
            "ReleaseReady",
            "ControllerBase",
            "CommandType.StoredProcedure",
            "CREATE TABLE",
            "WebApplication");
    }

    [TestMethod]
    public void ProjectAutonomyPolicy_Static_NoApiCliSqlRuntimeOrWorkflowWiring()
    {
        var root = RepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Program.cs"));
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "IronDevCli.cs"));
        var apiControllers = Directory.GetFiles(Path.Combine(root, "IronDev.Api", "Controllers"), "*.cs").Select(File.ReadAllText).ToArray();
        var databaseFiles = Directory.GetFiles(Path.Combine(root, "Database"), "*.sql").Select(Path.GetFileName).ToArray();
        var infrastructureFiles = Directory.GetFiles(Path.Combine(root, "IronDev.Infrastructure"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText).ToArray();

        Assert.IsFalse(program.Contains("ProjectAutonomyPolicy", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("ProjectAutonomyPolicy", StringComparison.Ordinal));
        Assert.IsFalse(apiControllers.Any(text => text.Contains("ProjectAutonomyPolicy", StringComparison.Ordinal)));
        Assert.IsFalse(databaseFiles.Any(name => name!.Contains("project_autonomy_policy", StringComparison.OrdinalIgnoreCase)
            || name.Contains("autonomy_policy", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(infrastructureFiles.Any(text => text.Contains("ProjectAutonomyPolicy", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ProjectAutonomyPolicy_DocumentationDefinesVocabularyOnly()
    {
        var doc = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "BLOCK_H_PROJECT_AUTHORITY_POLICY_MODEL.md"));

        StringAssert.Contains(doc, "Block H begins with policy vocabulary only.");
        StringAssert.Contains(doc, "PR82 defines ProjectAutonomyPolicy contracts.");
        StringAssert.Contains(doc, "PR82 does not evaluate policy.");
        StringAssert.Contains(doc, "PR82 does not approve or execute anything.");
        StringAssert.Contains(doc, "Conservative");
        StringAssert.Contains(doc, "Balanced");
        StringAssert.Contains(doc, "Experimental");
        StringAssert.Contains(doc, "The word \"free\" is intentionally forbidden");
        StringAssert.Contains(doc, "Missing policy must later fail closed.");
        StringAssert.Contains(doc, "Sensitive actions remain human-review gated");
    }

    [TestMethod]
    public void ProjectAutonomyPolicy_Wording_AllowsForbiddenTermsOnlyInNegativeStatements()
    {
        var files = new[]
        {
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
                "auto execute",
                "authorized",
                "ready to run",
                "execution allowed",
                "permission granted",
                "can execute",
                "source apply allowed",
                "memory promotion allowed",
                "release approved",
                "can ship"
            })
            {
                AssertPhraseOnlyAppearsInNegativeStatement(text, phrase);
            }
        }
    }

    private static ProjectAutonomyPolicyCreateRequest ValidRequest() =>
        new()
        {
            ProjectId = Guid.NewGuid(),
            PolicyName = "Backend dogfood autonomy policy",
            PolicyVersion = 1,
            AutonomyLevel = nameof(ProjectAutonomyLevel.Balanced),
            Status = nameof(ProjectAutonomyPolicyStatus.Draft),
            CreatedByActorType = "human",
            CreatedByActorId = "human-reviewer",
            MetadataVersion = 1,
            MetadataJson = """
                {
                  "schema": "project.autonomy.policy.metadata.v1",
                  "notes": "Initial policy contract for backend dogfood.",
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

    private static void AssertContainsIssue(ProjectAutonomyPolicyValidationResult result, string code)
    {
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == code), $"{code}: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertNoForbiddenMemberName(string member)
    {
        foreach (var token in new[] { "Approval", "Execution", "Execute", "Apply", "Promote", "Workflow", "ReleaseReady", "CanRun", "CanShip", "Permission" })
        {
            Assert.IsFalse(member.Contains(token, StringComparison.OrdinalIgnoreCase), member);
        }
    }

    private static void AssertNoForbiddenTokens(string text, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), token);
        }
    }

    private static void AssertPhraseOnlyAppearsInNegativeStatement(string text, string phrase)
    {
        var matchingLines = text
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => line.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var line in matchingLines)
        {
            var lower = line.ToLowerInvariant();
            var isNegative = lower.Contains("forbidden", StringComparison.Ordinal)
                || lower.Contains("must not", StringComparison.Ordinal)
                || lower.Contains("does not", StringComparison.Ordinal)
                || lower.Contains("not ", StringComparison.Ordinal)
                || lower.Contains(" no ", StringComparison.Ordinal)
                || lower.Contains("cannot", StringComparison.Ordinal)
                || lower.Contains("reject", StringComparison.Ordinal);

            Assert.IsTrue(isNegative, $"Forbidden policy phrase must appear only in negative context: {phrase} / {line}");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root containing IronDev.slnx.");
    }
}
