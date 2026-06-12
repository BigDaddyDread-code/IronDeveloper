using System.Reflection;
using IronDev.Core.Policy;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class ApprovalPackageTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SourceEvaluationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceRuleId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [TestMethod]
    public void ApprovalPackageContracts_ExposeExpectedPackageShape()
    {
        foreach (var property in new[]
        {
            nameof(ApprovalPackage.ApprovalPackageId),
            nameof(ApprovalPackage.ProjectId),
            nameof(ApprovalPackage.PackageName),
            nameof(ApprovalPackage.PackageVersion),
            nameof(ApprovalPackage.Status),
            nameof(ApprovalPackage.ApprovalScope),
            nameof(ApprovalPackage.SubjectType),
            nameof(ApprovalPackage.SubjectId),
            nameof(ApprovalPackage.ActionName),
            nameof(ApprovalPackage.SourceEvaluationId),
            nameof(ApprovalPackage.SourceEvaluationOutcome),
            nameof(ApprovalPackage.Requirements),
            nameof(ApprovalPackage.EvidenceReferences),
            nameof(ApprovalPackage.GrantsApproval),
            nameof(ApprovalPackage.GrantsExecution),
            nameof(ApprovalPackage.MutatesSource),
            nameof(ApprovalPackage.PromotesMemory),
            nameof(ApprovalPackage.StartsWorkflow),
            nameof(ApprovalPackage.SatisfiesPolicy),
            nameof(ApprovalPackage.TransfersAuthority)
        })
        {
            AssertHasProperty<ApprovalPackage>(property);
        }
    }

    [TestMethod]
    public void ApprovalPackageContracts_ExposeRequirementShape()
    {
        foreach (var property in new[]
        {
            nameof(ApprovalPackageRequirement.ApprovalScope),
            nameof(ApprovalPackageRequirement.ApprovalType),
            nameof(ApprovalPackageRequirement.RiskLevel),
            nameof(ApprovalPackageRequirement.RequiredApproverTypes),
            nameof(ApprovalPackageRequirement.QuorumCount),
            nameof(ApprovalPackageRequirement.RequirementCode),
            nameof(ApprovalPackageRequirement.RequirementReason),
            nameof(ApprovalPackageRequirement.SourceRuleId)
        })
        {
            AssertHasProperty<ApprovalPackageRequirement>(property);
        }
    }

    [TestMethod]
    public void ApprovalPackageContracts_ExposeEvidenceReferenceShape()
    {
        foreach (var property in new[]
        {
            nameof(ApprovalPackageEvidenceReference.EvidenceType),
            nameof(ApprovalPackageEvidenceReference.EvidenceId),
            nameof(ApprovalPackageEvidenceReference.EvidenceLabel),
            nameof(ApprovalPackageEvidenceReference.EvidenceSummary),
            nameof(ApprovalPackageEvidenceReference.GovernanceEventId)
        })
        {
            AssertHasProperty<ApprovalPackageEvidenceReference>(property);
        }
    }

    [TestMethod]
    public void ApprovalPackageContracts_ExposeSummaryShape()
    {
        foreach (var property in new[]
        {
            nameof(ApprovalPackageSummary.ApprovalPackageId),
            nameof(ApprovalPackageSummary.ProjectId),
            nameof(ApprovalPackageSummary.PackageName),
            nameof(ApprovalPackageSummary.PackageVersion),
            nameof(ApprovalPackageSummary.Status),
            nameof(ApprovalPackageSummary.RequirementCount),
            nameof(ApprovalPackageSummary.EvidenceReferenceCount)
        })
        {
            AssertHasProperty<ApprovalPackageSummary>(property);
        }
    }

    [TestMethod]
    public void ApprovalPackageContracts_DoNotExposeApproveExecuteOrContinueMethods()
    {
        var publicMethods = typeof(ApprovalPackageValidator)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.DoesNotContain(publicMethods, "Approve");
        CollectionAssert.DoesNotContain(publicMethods, "Execute");
        CollectionAssert.DoesNotContain(publicMethods, "ContinueWorkflow");
        CollectionAssert.DoesNotContain(publicMethods, "SatisfyPolicy");
        CollectionAssert.DoesNotContain(publicMethods, "CheckApprovalDecision");
        CollectionAssert.DoesNotContain(publicMethods, "CreateApprovalDecision");
    }

    [TestMethod]
    public void ApprovalPackageContracts_DoNotExposeApprovalSatisfactionProperties()
    {
        var propertyNames = typeof(ApprovalPackage).GetProperties().Select(property => property.Name).ToArray();

        CollectionAssert.DoesNotContain(propertyNames, "Approved");
        CollectionAssert.DoesNotContain(propertyNames, "ApprovalSatisfied");
        CollectionAssert.DoesNotContain(propertyNames, "ApprovalDecisionSatisfied");
        CollectionAssert.DoesNotContain(propertyNames, "ExecutionAllowed");
        CollectionAssert.DoesNotContain(propertyNames, "ReadyToRun");
        CollectionAssert.DoesNotContain(propertyNames, "ReleaseReady");
    }

    [TestMethod]
    public void ApprovalPackageStatus_AllowsDraftReadyForReviewSupersededCancelledExpired()
    {
        foreach (var status in ApprovalPackageStatuses.All)
        {
            var result = ApprovalPackageValidator.Validate(ValidPackage(status: status));
            Assert.IsTrue(result.IsValid, $"Expected status '{status}' to be valid.");
        }
    }

    [TestMethod]
    public void ApprovalPackageStatus_RejectsApproved() => AssertInvalidStatus("Approved");

    [TestMethod]
    public void ApprovalPackageStatus_RejectsAuthorized() => AssertInvalidStatus("Authorized");

    [TestMethod]
    public void ApprovalPackageStatus_RejectsExecutionAllowed() => AssertInvalidStatus("ExecutionAllowed");

    [TestMethod]
    public void ApprovalPackageStatus_RejectsPolicySatisfied() => AssertInvalidStatus("PolicySatisfied");

    [TestMethod]
    public void ApprovalPackageStatus_RejectsReleaseReady() => AssertInvalidStatus("ReleaseReady");

    [TestMethod]
    public void ApprovalPackageRequirement_PreservesEvaluatorRequirementShape()
    {
        var evaluatorRequirement = new ApprovalRequirement
        {
            ApprovalScope = ProjectApprovalRuleScopes.SourceApply,
            ApprovalType = ProjectApprovalRuleApprovalTypes.HumanOnly,
            RiskLevel = ProjectApprovalRuleRiskLevels.Critical,
            RequiredApproverTypes = [ProjectApprovalRuleApproverTypes.ProjectLead],
            QuorumCount = null,
            RequirementCode = "APPROVAL_REQUIRED_BY_RULE",
            RequirementReason = "Requirement was derived from evaluator output.",
            SourceRuleId = SourceRuleId
        };

        var packageRequirement = new ApprovalPackageRequirement
        {
            ApprovalScope = evaluatorRequirement.ApprovalScope,
            ApprovalType = evaluatorRequirement.ApprovalType,
            RiskLevel = evaluatorRequirement.RiskLevel,
            RequiredApproverTypes = evaluatorRequirement.RequiredApproverTypes,
            QuorumCount = evaluatorRequirement.QuorumCount,
            RequirementCode = evaluatorRequirement.RequirementCode,
            RequirementReason = evaluatorRequirement.RequirementReason,
            SourceRuleId = evaluatorRequirement.SourceRuleId
        };

        Assert.AreEqual(evaluatorRequirement.ApprovalScope, packageRequirement.ApprovalScope);
        Assert.AreEqual(evaluatorRequirement.ApprovalType, packageRequirement.ApprovalType);
        Assert.AreEqual(evaluatorRequirement.RiskLevel, packageRequirement.RiskLevel);
        CollectionAssert.AreEqual(evaluatorRequirement.RequiredApproverTypes.ToArray(), packageRequirement.RequiredApproverTypes.ToArray());
        Assert.AreEqual(evaluatorRequirement.SourceRuleId, packageRequirement.SourceRuleId);
    }

    [TestMethod]
    public void ApprovalPackageRequirement_PreservesHumanOnlyRequirement()
    {
        var result = ApprovalPackageValidator.Validate(ValidPackage(requirements: [ValidRequirement(approvalType: ProjectApprovalRuleApprovalTypes.HumanOnly, approvers: [ProjectApprovalRuleApproverTypes.ProjectLead])]));

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void ApprovalPackageRequirement_PreservesQuorumRequirement()
    {
        var result = ApprovalPackageValidator.Validate(ValidPackage(requirements:
        [
            ValidRequirement(
                approvalType: ProjectApprovalRuleApprovalTypes.Quorum,
                approvers: [ProjectApprovalRuleApproverTypes.ProjectLead, ProjectApprovalRuleApproverTypes.SecurityOwner],
                quorumCount: 2)
        ]));

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void ApprovalPackageRequirement_RejectsAuthorityGrantingRequirementCode()
    {
        var result = ApprovalPackageValidator.Validate(ValidPackage(requirements: [ValidRequirement(requirementCode: "EXECUTION_ALLOWED")]));

        AssertHasIssue(result, "REQUIREMENT_AUTHORITY_WORDING");
    }

    [TestMethod]
    public void ApprovalPackageRequirement_DoesNotSatisfyRequirement()
    {
        var propertyNames = typeof(ApprovalPackageRequirement).GetProperties().Select(property => property.Name).ToArray();

        CollectionAssert.DoesNotContain(propertyNames, "Satisfied");
        CollectionAssert.DoesNotContain(propertyNames, "Approved");
        CollectionAssert.DoesNotContain(propertyNames, "SatisfiedByApprovalDecisionId");
    }

    [TestMethod]
    public void ApprovalPackageEvidenceReference_AllowsGovernanceEvidenceTypes()
    {
        foreach (var evidenceType in new[]
        {
            ApprovalPackageEvidenceTypes.GovernanceEvent,
            ApprovalPackageEvidenceTypes.ToolRequest,
            ApprovalPackageEvidenceTypes.ToolGateDecision,
            ApprovalPackageEvidenceTypes.PolicyDecisionEvent,
            ApprovalPackageEvidenceTypes.DogfoodReceipt,
            ApprovalPackageEvidenceTypes.ThoughtLedgerReference,
            ApprovalPackageEvidenceTypes.RunReport
        })
        {
            var result = ApprovalPackageValidator.Validate(ValidPackage(evidence: [ValidEvidence(evidenceType)]));
            Assert.IsTrue(result.IsValid, $"Expected evidence type '{evidenceType}' to be valid.");
        }
    }

    [TestMethod]
    public void ApprovalPackageEvidenceReference_AllowsValidationOutputAndHumanNote()
    {
        foreach (var evidenceType in new[] { ApprovalPackageEvidenceTypes.ValidationOutput, ApprovalPackageEvidenceTypes.HumanNote })
        {
            var result = ApprovalPackageValidator.Validate(ValidPackage(evidence: [ValidEvidence(evidenceType)]));
            Assert.IsTrue(result.IsValid, $"Expected evidence type '{evidenceType}' to be valid.");
        }
    }

    [TestMethod]
    public void ApprovalPackageEvidenceReference_RejectsApprovalAsEvidenceType() => AssertInvalidEvidenceType("Approval");

    [TestMethod]
    public void ApprovalPackageEvidenceReference_RejectsExecutionPermissionEvidenceType() => AssertInvalidEvidenceType("ExecutionPermission");

    [TestMethod]
    public void ApprovalPackageEvidenceReference_RejectsPolicySatisfiedEvidenceType() => AssertInvalidEvidenceType("PolicySatisfied");

    [TestMethod]
    public void ApprovalPackageEvidenceReference_DoesNotCreateGovernanceEvent()
    {
        var reference = ValidEvidence(ApprovalPackageEvidenceTypes.GovernanceEvent) with { GovernanceEventId = Guid.NewGuid() };
        var result = ApprovalPackageValidator.Validate(ValidPackage(evidence: [reference]));

        Assert.IsTrue(result.IsValid);
        AssertHasNoProductionToken("IGovernanceEventStore");
        AssertHasNoProductionToken("GovernanceEventRepository");
    }

    [TestMethod]
    public void Validate_RejectsEmptyProjectId() => AssertInvalidPackage(ValidPackage() with { ProjectId = Guid.Empty }, "PROJECT_ID_REQUIRED");

    [TestMethod]
    public void Validate_RejectsBlankPackageName() => AssertInvalidPackage(ValidPackage() with { PackageName = " " }, "PACKAGE_NAME_REQUIRED");

    [TestMethod]
    public void Validate_RejectsNonPositivePackageVersion() => AssertInvalidPackage(ValidPackage() with { PackageVersion = 0 }, "PACKAGE_VERSION_REQUIRED");

    [TestMethod]
    public void Validate_RejectsUnknownStatus() => AssertInvalidPackage(ValidPackage(status: "WaitingForReview"), "STATUS_UNKNOWN");

    [TestMethod]
    public void Validate_RejectsForbiddenStatus() => AssertInvalidPackage(ValidPackage(status: "CanShip"), "STATUS_FORBIDDEN");

    [TestMethod]
    public void Validate_RejectsBlankApprovalScope() => AssertInvalidPackage(ValidPackage(scope: " "), "APPROVAL_SCOPE_REQUIRED");

    [TestMethod]
    public void Validate_RejectsUnknownApprovalScope() => AssertInvalidPackage(ValidPackage(scope: "unbounded_scope"), "APPROVAL_SCOPE_UNKNOWN");

    [TestMethod]
    public void Validate_RejectsBlankSubjectType() => AssertInvalidPackage(ValidPackage() with { SubjectType = " " }, "SUBJECT_TYPE_REQUIRED");

    [TestMethod]
    public void Validate_RejectsBlankSubjectId() => AssertInvalidPackage(ValidPackage() with { SubjectId = " " }, "SUBJECT_ID_REQUIRED");

    [TestMethod]
    public void Validate_RejectsEmptySourceEvaluationId() => AssertInvalidPackage(ValidPackage() with { SourceEvaluationId = Guid.Empty }, "SOURCE_EVALUATION_ID_REQUIRED");

    [TestMethod]
    public void Validate_RejectsAuthorityOutcome() => AssertInvalidPackage(ValidPackage(sourceOutcome: "ExecutionAllowed"), "SOURCE_EVALUATION_OUTCOME_AUTHORITY");

    [TestMethod]
    public void Validate_RejectsApprovalRequiredWithoutRequirements() => AssertInvalidPackage(ValidPackage(requirements: []), "APPROVAL_REQUIRED_WITHOUT_REQUIREMENTS");

    [TestMethod]
    public void Validate_RejectsInvalidRequirement() => AssertInvalidPackage(ValidPackage(requirements: [ValidRequirement(approvalType: "Approved")]), "REQUIREMENT_APPROVAL_TYPE_UNKNOWN");

    [TestMethod]
    public void Validate_RejectsInvalidEvidenceReference() => AssertInvalidPackage(ValidPackage(evidence: [ValidEvidence() with { EvidenceId = " " }]), "EVIDENCE_ID_REQUIRED");

    [TestMethod]
    public void Validate_RejectsBlankActorType() => AssertInvalidPackage(ValidPackage() with { CreatedByActorType = " " }, "ACTOR_TYPE_REQUIRED");

    [TestMethod]
    public void Validate_RejectsBlankActorId() => AssertInvalidPackage(ValidPackage() with { CreatedByActorId = " " }, "ACTOR_ID_REQUIRED");

    [TestMethod]
    public void Validate_RejectsInvalidMetadataJson() => AssertInvalidPackage(ValidPackage(metadataJson: "{"), "METADATA_JSON_INVALID");

    [TestMethod]
    public void Validate_RejectsNonPositiveMetadataVersion() => AssertInvalidPackage(ValidPackage() with { MetadataVersion = 0 }, "METADATA_VERSION_REQUIRED");

    [TestMethod]
    public void Validate_RejectsPrivateReasoningMarkers() => AssertInvalidPackage(ValidPackage(metadataJson: "{\"schema\":\"approval.package.metadata.v1\",\"hiddenReasoning\":\"nope\"}"), "METADATA_PRIVATE_REASONING");

    [TestMethod]
    public void Validate_RejectsAuthorityGrantingMetadata() => AssertInvalidPackage(ValidPackage(metadataJson: "{\"schema\":\"approval.package.metadata.v1\",\"canExecute\":true}"), "METADATA_AUTHORITY_GRANT");

    [TestMethod]
    public void Validate_RejectsTrueAuthorityFlags()
    {
        foreach (var package in new[]
        {
            ValidPackage() with { GrantsApproval = true },
            ValidPackage() with { GrantsExecution = true },
            ValidPackage() with { MutatesSource = true },
            ValidPackage() with { PromotesMemory = true },
            ValidPackage() with { StartsWorkflow = true },
            ValidPackage() with { SatisfiesPolicy = true },
            ValidPackage() with { TransfersAuthority = true }
        })
        {
            AssertInvalidPackage(package, "AUTHORITY_FLAG_TRUE");
        }
    }

    [TestMethod]
    public void ApprovalPackage_DoesNotCreateApprovalDecision() => AssertHasNoProductionToken("CreateApprovalDecision");

    [TestMethod]
    public void ApprovalPackage_DoesNotCheckApprovalDecision() => AssertHasNoProductionToken("CheckApprovalDecision");

    [TestMethod]
    public void ApprovalPackage_DoesNotSatisfyPolicy()
    {
        var package = ValidPackage();
        var result = ApprovalPackageValidator.Validate(package);

        Assert.IsTrue(result.IsValid);
        Assert.IsFalse(package.SatisfiesPolicy);
    }

    [TestMethod]
    public void ApprovalPackage_DoesNotExecuteTool() => AssertHasNoProductionToken("IToolExecutor");

    [TestMethod]
    public void ApprovalPackage_DoesNotStartWorkflow()
    {
        Assert.IsFalse(ValidPackage().StartsWorkflow);
        AssertHasNoProductionToken("WorkflowRunner");
    }

    [TestMethod]
    public void ApprovalPackage_DoesNotMutateSource() => Assert.IsFalse(ValidPackage().MutatesSource);

    [TestMethod]
    public void ApprovalPackage_DoesNotPromoteMemory() => Assert.IsFalse(ValidPackage().PromotesMemory);

    [TestMethod]
    public void ApprovalPackage_DoesNotCreateA2aHandoff() => AssertHasNoProductionToken("A2aHandoff");

    [TestMethod]
    public void ApprovalPackage_DoesNotCreateDogfoodReceipt() => AssertHasNoProductionToken("DogfoodReceiptStore");

    [TestMethod]
    public void ApprovalPackage_DoesNotMarkReleaseReady()
    {
        var propertyNames = typeof(ApprovalPackage).GetProperties().Select(property => property.Name).ToArray();

        CollectionAssert.DoesNotContain(propertyNames, "ReleaseReady");
        AssertHasNoProductionToken("ReleaseReadinessService");
    }

    [TestMethod]
    public void ApprovalPackage_DoesNotTransferAuthority() => Assert.IsFalse(ValidPackage().TransfersAuthority);

    [TestMethod]
    public void ApprovalPackage_DoesNotAddApiEndpoint()
    {
        var apiReferences = Directory.GetFiles(Path.Combine(RepoRoot(), "IronDev.Api"), "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("ApprovalPackage", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.AreEqual(0, apiReferences.Length);
    }

    [TestMethod]
    public void ApprovalPackage_DoesNotAddCliCommand()
    {
        var cliRoot = Path.Combine(RepoRoot(), "tools", "IronDev.Cli");
        var cliReferences = Directory.Exists(cliRoot)
            ? Directory.GetFiles(cliRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).Contains("ApprovalPackage", StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : [];

        Assert.AreEqual(0, cliReferences.Length);
    }

    [TestMethod]
    public void ApprovalPackage_DoesNotAddSqlMigration()
    {
        var databaseRoot = Path.Combine(RepoRoot(), "Database");
        var sqlReferences = Directory.Exists(databaseRoot)
            ? Directory.GetFiles(databaseRoot, "*.sql", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).Contains("ApprovalPackage", StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : [];

        Assert.AreEqual(0, sqlReferences.Length);
    }

    [TestMethod]
    public void ApprovalPackage_DoesNotAddRepository() => AssertHasNoProductionToken("ApprovalPackageRepository");

    [TestMethod]
    public void ApprovalPackage_DoesNotRegisterRuntimeDi()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "IronDev.Api", "Program.cs"));

        Assert.IsFalse(program.Contains("ApprovalPackage", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ApprovalPackage_DoesNotReferenceWorkflowRunner() => AssertHasNoProductionToken("IWorkflowRunner");

    [TestMethod]
    public void ApprovalPackage_DoesNotReferenceExecutor() => AssertHasNoProductionToken("Executor");

    [TestMethod]
    public void ApprovalPackage_DoesNotReferenceMemoryPromotion() => AssertHasNoProductionToken("MemoryPromotionStore");

    [TestMethod]
    public void ApprovalPackage_DoesNotReferenceSourceApply() => AssertHasNoProductionToken("SourceApplyService");

    [TestMethod]
    public void ApprovalPackage_DoesNotReferenceLangGraph() => AssertHasNoProductionToken("LangGraph");

    [TestMethod]
    public void ApprovalPackage_DoesNotReferenceA2a() => AssertHasNoProductionToken("A2a");

    private static ApprovalPackage ValidPackage(
        string status = ApprovalPackageStatuses.ReadyForReview,
        string scope = ProjectApprovalRuleScopes.SourceApply,
        string sourceOutcome = ApprovalRequirementOutcomes.ApprovalRequired,
        IReadOnlyList<ApprovalPackageRequirement>? requirements = null,
        IReadOnlyList<ApprovalPackageEvidenceReference>? evidence = null,
        string? metadataJson = null) =>
        new()
        {
            ApprovalPackageId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            ProjectId = ProjectId,
            PackageName = "Source apply review package",
            PackageVersion = 1,
            Status = status,
            ApprovalScope = scope,
            SubjectType = "ToolRequest",
            SubjectId = "tool-request-123",
            ActionName = "source.apply",
            SourceEvaluationId = SourceEvaluationId,
            SourceEvaluationOutcome = sourceOutcome,
            Requirements = requirements ?? [ValidRequirement(scope: scope)],
            EvidenceReferences = evidence ?? [ValidEvidence()],
            SupersedesPackageId = null,
            CreatedByActorType = "Operator",
            CreatedByActorId = "operator-1",
            MetadataVersion = 1,
            MetadataJson = metadataJson ?? SafeMetadataJson,
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false,
            CreatedUtc = DateTimeOffset.UtcNow
        };

    private static ApprovalPackageRequirement ValidRequirement(
        string scope = ProjectApprovalRuleScopes.SourceApply,
        string approvalType = ProjectApprovalRuleApprovalTypes.HumanOnly,
        string risk = ProjectApprovalRuleRiskLevels.Critical,
        IReadOnlyList<string>? approvers = null,
        int? quorumCount = null,
        string requirementCode = "APPROVAL_REQUIRED_BY_RULE") =>
        new()
        {
            ApprovalScope = scope,
            ApprovalType = approvalType,
            RiskLevel = risk,
            RequiredApproverTypes = approvers ?? [ProjectApprovalRuleApproverTypes.ProjectLead],
            QuorumCount = quorumCount,
            RequirementCode = requirementCode,
            RequirementReason = "Requirement was derived from evaluator output.",
            SourceRuleId = SourceRuleId
        };

    private static ApprovalPackageEvidenceReference ValidEvidence(string evidenceType = ApprovalPackageEvidenceTypes.GovernanceEvent) =>
        new()
        {
            EvidenceType = evidenceType,
            EvidenceId = "evidence-123",
            EvidenceLabel = "Governance evidence",
            EvidenceSummary = "Evidence supports human review.",
            GovernanceEventId = Guid.Parse("55555555-5555-5555-5555-555555555555")
        };

    private const string SafeMetadataJson = "{\"schema\":\"approval.package.metadata.v1\",\"notes\":\"Package prepared for human review.\",\"grantsApproval\":false,\"grantsExecution\":false,\"mutatesSource\":false,\"promotesMemory\":false,\"startsWorkflow\":false,\"satisfiesPolicy\":false,\"transfersAuthority\":false}";

    private static void AssertInvalidStatus(string status)
    {
        var result = ApprovalPackageValidator.Validate(ValidPackage(status: status));

        AssertHasIssue(result, "STATUS_FORBIDDEN");
    }

    private static void AssertInvalidEvidenceType(string evidenceType)
    {
        var result = ApprovalPackageValidator.Validate(ValidPackage(evidence: [ValidEvidence(evidenceType)]));

        AssertHasIssue(result, "EVIDENCE_TYPE_FORBIDDEN");
    }

    private static void AssertInvalidPackage(ApprovalPackage package, string expectedIssueCode)
    {
        var result = ApprovalPackageValidator.Validate(package);

        AssertHasIssue(result, expectedIssueCode);
    }

    private static void AssertHasIssue(ApprovalPackageValidationResult result, string expectedIssueCode)
    {
        Assert.IsFalse(result.IsValid, "Expected approval package validation to fail.");
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedIssueCode), $"Expected issue code '{expectedIssueCode}', got: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertHasProperty<T>(string propertyName) =>
        Assert.IsNotNull(typeof(T).GetProperty(propertyName), $"Expected {typeof(T).Name}.{propertyName}.");

    private static void AssertHasNoProductionToken(string token)
    {
        var source = File.ReadAllText(ApprovalPackageSourcePath());

        Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"Approval package production model should not reference '{token}'.");
    }

    private static string ApprovalPackageSourcePath() =>
        Path.Combine(RepoRoot(), "IronDev.Core", "Policy", "ApprovalPackageModels.cs");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
