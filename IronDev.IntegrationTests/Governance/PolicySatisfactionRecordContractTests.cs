using System.Reflection;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("PolicySatisfactionRecordContract")]
public sealed class PolicySatisfactionRecordContractTests
{
    private static readonly Guid PolicySatisfactionId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid AcceptedApprovalId = Guid.Parse("99999999-8888-7777-6666-555555555555");
    private static readonly DateTimeOffset ApprovalEvaluatedAtUtc = new(2026, 6, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SatisfiedAtUtc = new(2026, 6, 16, 10, 1, 0, TimeSpan.Zero);

    [TestMethod]
    public void PolicySatisfactionRecordContract_RequiresPolicyBinding()
    {
        foreach (var property in new[]
        {
            nameof(PolicySatisfactionRecord.PolicySatisfactionId),
            nameof(PolicySatisfactionRecord.ProjectId),
            nameof(PolicySatisfactionRecord.PolicyCode),
            nameof(PolicySatisfactionRecord.PolicyVersion),
            nameof(PolicySatisfactionRecord.CapabilityCode)
        })
        {
            AssertHasProperty(typeof(PolicySatisfactionRecord), property);
        }

        AssertValid(ValidRecord());
        AssertInvalid(ValidRecord() with { PolicySatisfactionId = Guid.Empty }, "POLICY_SATISFACTION_ID_REQUIRED");
        AssertInvalid(ValidRecord() with { ProjectId = Guid.Empty }, "PROJECT_ID_REQUIRED");
        AssertInvalid(ValidRecord() with { PolicyCode = " " }, "POLICY_CODE_REQUIRED");
        AssertInvalid(ValidRecord() with { PolicyVersion = " " }, "POLICY_VERSION_REQUIRED");
        AssertInvalid(ValidRecord() with { CapabilityCode = " " }, "CAPABILITY_CODE_REQUIRED");
    }

    [TestMethod]
    public void PolicySatisfactionRecordContract_RequiresSubjectBinding()
    {
        foreach (var property in new[]
        {
            nameof(PolicySatisfactionSubject.ProjectId),
            nameof(PolicySatisfactionSubject.SubjectKind),
            nameof(PolicySatisfactionSubject.SubjectId),
            nameof(PolicySatisfactionSubject.SubjectHash),
            nameof(PolicySatisfactionSubject.CapabilityCode),
            nameof(PolicySatisfactionRecord.SubjectKind),
            nameof(PolicySatisfactionRecord.SubjectId),
            nameof(PolicySatisfactionRecord.SubjectHash)
        })
        {
            Assert.IsTrue(
                typeof(PolicySatisfactionSubject).GetProperty(property) is not null || typeof(PolicySatisfactionRecord).GetProperty(property) is not null,
                $"Expected subject binding property {property}.");
        }

        AssertValid(ValidSubject());
        AssertInvalid(ValidRecord() with { SubjectKind = " " }, "SUBJECT_KIND_REQUIRED");
        AssertInvalid(ValidRecord() with { SubjectId = " " }, "SUBJECT_ID_REQUIRED");
        AssertInvalid(ValidRecord() with { SubjectHash = " " }, "SUBJECT_HASH_REQUIRED");
    }

    [TestMethod]
    public void PolicySatisfactionRecordContract_RequiresAcceptedApprovalInput()
    {
        foreach (var property in new[]
        {
            nameof(PolicySatisfactionRecord.AcceptedApprovalId),
            nameof(PolicySatisfactionRecord.ApprovalRequirementHash),
            nameof(PolicySatisfactionRecord.ApprovalEvaluatedAtUtc)
        })
        {
            AssertHasProperty(typeof(PolicySatisfactionRecord), property);
        }

        AssertInvalid(ValidRecord() with { AcceptedApprovalId = Guid.Empty }, "ACCEPTED_APPROVAL_ID_REQUIRED");
        AssertInvalid(ValidRecord() with { ApprovalRequirementHash = " " }, "APPROVAL_REQUIREMENT_HASH_REQUIRED");
        AssertInvalid(ValidRecord() with { ApprovalEvaluatedAtUtc = default }, "APPROVAL_EVALUATED_AT_UTC_REQUIRED");
    }

    [TestMethod]
    public void PolicySatisfactionRecordContract_RejectsMissingAcceptedApprovalId() =>
        AssertInvalid(ValidRecord() with { AcceptedApprovalId = Guid.Empty }, "ACCEPTED_APPROVAL_ID_REQUIRED");

    [TestMethod]
    public void PolicySatisfactionRecordContract_RejectsMissingSubjectHash() =>
        AssertInvalid(ValidRecord() with { SubjectHash = " " }, "SUBJECT_HASH_REQUIRED");

    [TestMethod]
    public void PolicySatisfactionRecordContract_RejectsMissingPolicyCode() =>
        AssertInvalid(ValidRecord() with { PolicyCode = " " }, "POLICY_CODE_REQUIRED");

    [TestMethod]
    public void PolicySatisfactionRecordContract_RejectsMissingPolicyVersion() =>
        AssertInvalid(ValidRecord() with { PolicyVersion = " " }, "POLICY_VERSION_REQUIRED");

    [TestMethod]
    public void PolicySatisfactionRecordContract_RejectsMissingApprovalRequirementHash() =>
        AssertInvalid(ValidRecord() with { ApprovalRequirementHash = " " }, "APPROVAL_REQUIREMENT_HASH_REQUIRED");

    [TestMethod]
    public void PolicySatisfactionRecordContract_RejectsMissingEvidenceReferences()
    {
        AssertInvalid(ValidRecord() with { EvidenceReferences = [] }, "EVIDENCE_REFERENCES_REQUIRED");
        AssertInvalid(ValidRecord() with { EvidenceReferences = [" "] }, "EVIDENCE_REFERENCES_REQUIRED");
    }

    [TestMethod]
    public void PolicySatisfactionRecordContract_RejectsMissingBoundaryMaxims()
    {
        AssertInvalid(ValidRecord() with { BoundaryMaxims = [] }, "BOUNDARY_MAXIMS_REQUIRED");
        AssertInvalid(ValidRecord() with { BoundaryMaxims = [" "] }, "BOUNDARY_MAXIMS_REQUIRED");
    }

    [TestMethod]
    public void PolicySatisfactionRecordContract_RejectsInvalidExpiry()
    {
        AssertInvalid(ValidRecord() with { ExpiresAtUtc = SatisfiedAtUtc.AddTicks(-1) }, "EXPIRES_AT_UTC_INVALID");
        AssertInvalid(ValidRecord() with { ExpiresAtUtc = SatisfiedAtUtc }, "EXPIRES_AT_UTC_INVALID");
        AssertValid(ValidRecord() with { ExpiresAtUtc = SatisfiedAtUtc.AddDays(1) });
    }

    [TestMethod]
    public void PolicySatisfactionRecordContract_DoesNotAuthorizeExecution()
    {
        var properties = typeof(PolicySatisfactionRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name).ToArray();
        var methods = typeof(PolicySatisfactionValidation).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(method => method.Name).ToArray();

        foreach (var forbidden in new[]
        {
            "CanApplySource",
            "CanContinueWorkflow",
            "CanApproveRelease",
            "RunDryRun",
            "CreatePatchArtifact",
            "ApplySource",
            "ReleaseReady"
        })
        {
            CollectionAssert.DoesNotContain(properties, forbidden);
            CollectionAssert.DoesNotContain(methods, forbidden);
        }
    }

    [TestMethod]
    public void PolicySatisfactionRecordContract_DoesNotCreatePolicySatisfaction() =>
        AssertNoProductionTokens(
            "Create" + "PolicySatisfaction",
            "Save" + "PolicySatisfaction",
            "PolicySatisfaction" + "Store",
            "Sql" + "PolicySatisfaction",
            "Controller",
            "Http" + "Post");

    [TestMethod]
    public void PolicySatisfactionRecordContract_HasNoPersistenceOrApiDependency() =>
        AssertNoProductionTokens(
            "Sql" + "Connection",
            "Db" + "Command",
            "Dapper",
            "Controller",
            "Http" + "Get",
            "Http" + "Post",
            "IAction" + "Result");

    [TestMethod]
    public void PolicySatisfactionRecordContract_ContainsBoundaryLanguage()
    {
        var combined = ReceiptText() + Environment.NewLine + File.ReadAllText(RecordSourcePath());

        foreach (var statement in BoundaryStatements())
        {
            StringAssert.Contains(combined, statement);
        }
    }

    [TestMethod]
    public void PolicySatisfactionRecordContract_ReceiptStatesNonImplementation()
    {
        var receipt = ReceiptText();

        foreach (var statement in new[]
        {
            "PR174 adds the Policy Satisfaction Record contract.",
            "This PR is contract/test/receipt only.",
            "This PR defines the future durable policy satisfaction record shape.",
            "This PR does not create policy satisfaction records.",
            "This PR does not store policy satisfaction records.",
            "This PR does not expose policy satisfaction APIs.",
            "This PR does not evaluate policy satisfaction.",
            "This PR does not satisfy policy.",
            "This PR does not run dry-runs.",
            "This PR does not create patch artifacts.",
            "This PR does not apply source.",
            "This PR does not continue workflow.",
            "This PR does not approve release."
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    [TestMethod]
    public void PolicySatisfactionRecordContract_ContainsAuthorityChain()
    {
        StringAssert.Contains(ReceiptText(), "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate");
    }

    [TestMethod]
    public void PolicySatisfactionRecordContract_StatesNextTarget()
    {
        var receipt = ReceiptText();

        StringAssert.Contains(receipt, "The next Block Q target is Policy Satisfaction SQL Store.");
        StringAssert.Contains(receipt, "PR175 - Policy Satisfaction SQL Store");
    }

    [TestMethod]
    public void PolicySatisfactionRecordContract_ValidationIsShapeOnly()
    {
        var methods = typeof(PolicySatisfactionValidation)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { "Validate", "ValidateSubject" }, methods);
        StringAssert.Contains(ReceiptText(), "Validation is shape-only.");
        StringAssert.Contains(ReceiptText(), "Validation does not create policy satisfaction.");
        StringAssert.Contains(ReceiptText(), "Validation does not authorize execution.");
    }

    private static PolicySatisfactionRecord ValidRecord() =>
        new()
        {
            PolicySatisfactionId = PolicySatisfactionId,
            ProjectId = ProjectId,
            PolicyCode = "source-apply-policy",
            PolicyVersion = "2026-06-16.v1",
            SubjectKind = "patch-artifact",
            SubjectId = "patch-artifact-pr174",
            SubjectHash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            CapabilityCode = "SOURCE_APPLY",
            AcceptedApprovalId = AcceptedApprovalId,
            ApprovalRequirementHash = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            ApprovalEvaluatedAtUtc = ApprovalEvaluatedAtUtc,
            SatisfiedAtUtc = SatisfiedAtUtc,
            ExpiresAtUtc = SatisfiedAtUtc.AddDays(1),
            CorrelationId = "correlation-pr174",
            CausationId = "approval-satisfaction-pr173",
            EvidenceReferences = ["accepted-approval:99999999-8888-7777-6666-555555555555", "approval-satisfaction:evaluation-pr173"],
            BoundaryMaxims = BoundaryStatements()
        };

    private static PolicySatisfactionSubject ValidSubject() =>
        new()
        {
            ProjectId = ProjectId,
            SubjectKind = "patch-artifact",
            SubjectId = "patch-artifact-pr174",
            SubjectHash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            CapabilityCode = "SOURCE_APPLY"
        };

    private static IReadOnlyList<string> BoundaryStatements() =>
    [
        "Accepted approval is an input to policy satisfaction.",
        "Approval satisfaction evaluation is an input to policy satisfaction.",
        "Accepted approval is not policy satisfaction.",
        "Satisfied approval requirement is not policy satisfaction.",
        "Policy satisfaction record is not dry-run execution.",
        "Policy satisfaction record is not patch artifact creation.",
        "Policy satisfaction record is not source apply.",
        "Policy satisfaction record is not rollback.",
        "Policy satisfaction record is not workflow continuation.",
        "Policy satisfaction record is not release readiness.",
        "Policy satisfaction record does not authorize execution by itself."
    ];

    private static void AssertValid(PolicySatisfactionRecord record)
    {
        var result = PolicySatisfactionValidation.Validate(record);

        Assert.IsTrue(result.IsValid, $"Expected valid record, got: {IssueCodes(result)}");
    }

    private static void AssertValid(PolicySatisfactionSubject subject)
    {
        var result = PolicySatisfactionValidation.ValidateSubject(subject);

        Assert.IsTrue(result.IsValid, $"Expected valid subject, got: {IssueCodes(result)}");
    }

    private static void AssertInvalid(PolicySatisfactionRecord record, string expectedIssueCode)
    {
        var result = PolicySatisfactionValidation.Validate(record);

        Assert.IsFalse(result.IsValid, "Expected policy satisfaction validation to fail.");
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedIssueCode), $"Expected issue code '{expectedIssueCode}', got: {IssueCodes(result)}");
    }

    private static string IssueCodes(PolicySatisfactionValidationResult result) =>
        string.Join(", ", result.Issues.Select(issue => issue.Code));

    private static void AssertHasProperty(Type type, string propertyName) =>
        Assert.IsNotNull(type.GetProperty(propertyName), $"Expected {type.Name} property {propertyName}.");

    private static void AssertNoProductionTokens(params string[] tokens)
    {
        foreach (var file in ProductionFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var token in tokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"{Path.GetFileName(file)} must not contain {token}.");
            }
        }
    }

    private static IReadOnlyList<string> ProductionFiles() =>
    [
        SubjectSourcePath(),
        RecordSourcePath(),
        ValidationSourcePath()
    ];

    private static string ReceiptText() => File.ReadAllText(ReceiptPath());

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR174_POLICY_SATISFACTION_RECORD_CONTRACT.md");

    private static string SubjectSourcePath() =>
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "PolicySatisfactionSubject.cs");

    private static string RecordSourcePath() =>
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "PolicySatisfactionRecord.cs");

    private static string ValidationSourcePath() =>
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "PolicySatisfactionValidation.cs");

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
