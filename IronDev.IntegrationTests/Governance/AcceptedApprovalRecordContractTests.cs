using System.Reflection;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("AcceptedApprovalRecordContract")]
public sealed class AcceptedApprovalRecordContractTests
{
    private static readonly Guid AcceptedApprovalId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset AcceptedAtUtc = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AcceptedApprovalRecordContract_RequiresStableTargetBinding()
    {
        foreach (var property in new[]
        {
            nameof(AcceptedApprovalTarget.ApprovalTargetKind),
            nameof(AcceptedApprovalTarget.ApprovalTargetId),
            nameof(AcceptedApprovalTarget.ApprovalTargetHash),
            nameof(AcceptedApprovalTarget.ProjectId),
            nameof(AcceptedApprovalTarget.CapabilityCode),
            nameof(AcceptedApprovalRecord.ApprovalTargetKind),
            nameof(AcceptedApprovalRecord.ApprovalTargetId),
            nameof(AcceptedApprovalRecord.ApprovalTargetHash),
            nameof(AcceptedApprovalRecord.ProjectId),
            nameof(AcceptedApprovalRecord.CapabilityCode)
        })
        {
            AssertHasProperty(property);
        }

        AssertValid(ValidRecord());
        AssertInvalid(ValidRecord() with { ApprovalTargetKind = " " }, "APPROVAL_TARGET_KIND_REQUIRED");
        AssertInvalid(ValidRecord() with { ApprovalTargetId = " " }, "APPROVAL_TARGET_ID_REQUIRED");
        AssertInvalid(ValidRecord() with { ProjectId = Guid.Empty }, "PROJECT_ID_REQUIRED");
        AssertInvalid(ValidRecord() with { CapabilityCode = " " }, "CAPABILITY_CODE_REQUIRED");
    }

    [TestMethod]
    public void AcceptedApprovalRecordContract_RejectsMissingActor() =>
        AssertInvalid(ValidRecord() with { ApprovedByActorId = " " }, "APPROVED_BY_ACTOR_ID_REQUIRED");

    [TestMethod]
    public void AcceptedApprovalRecordContract_RejectsMissingTargetHash() =>
        AssertInvalid(ValidRecord() with { ApprovalTargetHash = " " }, "APPROVAL_TARGET_HASH_REQUIRED");

    [TestMethod]
    public void AcceptedApprovalRecordContract_RejectsExpiredOrInvalidExpiry()
    {
        AssertInvalid(ValidRecord() with { ExpiresAtUtc = AcceptedAtUtc.AddMinutes(-1) }, "EXPIRES_AT_UTC_INVALID");
        AssertInvalid(ValidRecord() with { ExpiresAtUtc = AcceptedAtUtc }, "EXPIRES_AT_UTC_INVALID");
        AssertValid(ValidRecord() with { ExpiresAtUtc = AcceptedAtUtc.AddDays(1) });
    }

    [TestMethod]
    public void AcceptedApprovalRecordContract_RequiresEvidenceReferences()
    {
        AssertInvalid(ValidRecord() with { EvidenceReferences = [] }, "EVIDENCE_REFERENCES_REQUIRED");
        AssertInvalid(ValidRecord() with { EvidenceReferences = [" "] }, "EVIDENCE_REFERENCES_REQUIRED");
    }

    [TestMethod]
    public void AcceptedApprovalRecordContract_RequiresBoundaryMaxims()
    {
        AssertInvalid(ValidRecord() with { BoundaryMaxims = [] }, "BOUNDARY_MAXIMS_REQUIRED");
        AssertInvalid(ValidRecord() with { BoundaryMaxims = [" "] }, "BOUNDARY_MAXIMS_REQUIRED");
    }

    [TestMethod]
    public void AcceptedApprovalRecordContract_RequiresCorrelationAndCausation()
    {
        AssertInvalid(ValidRecord() with { CorrelationId = " " }, "CORRELATION_ID_REQUIRED");
        AssertInvalid(ValidRecord() with { CausationId = " " }, "CAUSATION_ID_REQUIRED");
    }

    [TestMethod]
    public void AcceptedApprovalRecordContract_DoesNotSatisfyPolicy()
    {
        var properties = typeof(AcceptedApprovalRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name).ToArray();
        var methods = typeof(AcceptedApprovalValidation).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(method => method.Name).ToArray();

        foreach (var forbidden in new[]
        {
            "SatisfiesPolicy",
            "PolicySatisfied",
            "CanApplySource",
            "CanContinueWorkflow",
            "CanApproveRelease"
        })
        {
            CollectionAssert.DoesNotContain(properties, forbidden);
            CollectionAssert.DoesNotContain(methods, forbidden);
        }
    }

    [TestMethod]
    public void AcceptedApprovalRecordContract_DoesNotCreateApproval()
    {
        var forbiddenTokens = new[]
        {
            "Sql" + "Connection",
            "Db" + "Command",
            "IN" + "SERT",
            "UP" + "DATE",
            "DE" + "LETE",
            "Controller",
            "Http" + "Post",
            "POST",
            "Create" + "AcceptedApproval",
            "Save" + "AcceptedApproval",
            "AcceptedApproval" + "Store",
            "Apply" + "Source",
            "Continue" + "Workflow",
            "Approve" + "Release"
        };

        foreach (var file in Pr168ProductionFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbiddenTokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"{Path.GetFileName(file)} must not reference {token}.");
            }
        }
    }

    [TestMethod]
    public void AcceptedApprovalRecordContract_ContainsBoundaryLanguage()
    {
        var combined = File.ReadAllText(ReceiptPath()) + Environment.NewLine + File.ReadAllText(AcceptedApprovalRecordSourcePath());

        foreach (var statement in BoundaryStatements())
        {
            StringAssert.Contains(combined, statement);
        }
    }

    [TestMethod]
    public void AcceptedApprovalRecordContract_ReceiptStatesNonImplementation()
    {
        var receipt = ReceiptText();

        foreach (var statement in new[]
        {
            "PR168 does not create accepted approval records.",
            "PR168 does not store accepted approval records.",
            "PR168 does not read or write accepted approval records.",
            "PR168 does not expose accepted approval APIs.",
            "PR168 does not satisfy policy.",
            "PR168 does not run dry-runs.",
            "PR168 does not create patch artifacts.",
            "PR168 does not apply source.",
            "PR168 does not continue workflow.",
            "PR168 does not approve release."
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    [TestMethod]
    public void AcceptedApprovalRecordContract_ReceiptContainsAuthorityChainAndReviewLine()
    {
        var receipt = ReceiptText();

        StringAssert.Contains(receipt, "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate");
        StringAssert.Contains(receipt, "PR168 defines the approval brick. It does not approve anything.");
    }

    [TestMethod]
    public void AcceptedApprovalRecordContract_ValidationIsShapeOnly()
    {
        var methods = typeof(AcceptedApprovalValidation)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { "Validate", "ValidateTarget" }, methods);
        StringAssert.Contains(ReceiptText(), "Validation is shape-only.");
        StringAssert.Contains(ReceiptText(), "Validation does not create authority.");
    }

    private static AcceptedApprovalRecord ValidRecord() =>
        new()
        {
            AcceptedApprovalId = AcceptedApprovalId,
            ProjectId = ProjectId,
            ApprovalTargetKind = AcceptedApprovalTargetKinds.PatchArtifact,
            ApprovalTargetId = "patch-artifact-123",
            ApprovalTargetHash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            CapabilityCode = "L4_ACCEPTED_APPROVAL_RECORD",
            ApprovalPurpose = AcceptedApprovalPurposes.PolicySatisfactionInput,
            ApprovedByActorId = "human-operator-1",
            ApprovedByActorDisplayName = "Human Operator",
            AcceptedAtUtc = AcceptedAtUtc,
            ExpiresAtUtc = AcceptedAtUtc.AddDays(7),
            CorrelationId = "correlation-123",
            CausationId = "approval-package-123",
            EvidenceReferences = ["approval-package:approval-package-123"],
            BoundaryMaxims = BoundaryStatements()
        };

    private static IReadOnlyList<string> BoundaryStatements() =>
    [
        "Approval package is not accepted approval.",
        "Requested approval is not accepted approval.",
        "Human-looking approval text is not accepted approval.",
        "UI review is not accepted approval.",
        "Accepted approval must be a backend-owned durable authority record.",
        "Accepted approval is necessary but not sufficient for policy satisfaction.",
        "Accepted approval is necessary but not sufficient for source apply.",
        "Accepted approval is necessary but not sufficient for release readiness."
    ];

    private static void AssertValid(AcceptedApprovalRecord record)
    {
        var result = AcceptedApprovalValidation.Validate(record);

        Assert.IsTrue(result.IsValid, $"Expected valid record, got: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertInvalid(AcceptedApprovalRecord record, string expectedIssueCode)
    {
        var result = AcceptedApprovalValidation.Validate(record);

        Assert.IsFalse(result.IsValid, "Expected accepted approval validation to fail.");
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedIssueCode), $"Expected issue code '{expectedIssueCode}', got: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertHasProperty(string propertyName) =>
        Assert.IsNotNull(typeof(AcceptedApprovalRecord).GetProperty(propertyName) ?? typeof(AcceptedApprovalTarget).GetProperty(propertyName), $"Expected accepted approval property {propertyName}.");

    private static string ReceiptText() => File.ReadAllText(ReceiptPath());

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR168_ACCEPTED_APPROVAL_RECORD_CONTRACT.md");

    private static string AcceptedApprovalRecordSourcePath() =>
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "AcceptedApprovalRecord.cs");

    private static IReadOnlyList<string> Pr168ProductionFiles() => [AcceptedApprovalRecordSourcePath()];

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
