using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ApprovalSatisfactionEvaluator")]
public sealed class ApprovalSatisfactionEvaluatorTests
{
    private static readonly Guid AcceptedApprovalId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTimeOffset EvaluatedAtUtc = new(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_SatisfiesWhenAllBindingsMatch()
    {
        var result = Evaluate();

        Assert.IsTrue(result.IsSatisfied, IssueCodes(result));
        Assert.AreEqual(AcceptedApprovalId, result.AcceptedApprovalId);
        Assert.AreEqual(0, result.Issues.Count);
    }

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_RejectsNullApproval()
    {
        var evaluator = new ApprovalSatisfactionEvaluator();
        var result = evaluator.Evaluate(ValidRequirement(), null);

        AssertUnsatisfied(result, "ACCEPTED_APPROVAL_REQUIRED");
        Assert.IsNull(result.AcceptedApprovalId);
    }

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_RejectsInvalidApprovalShape()
    {
        var result = Evaluate(acceptedApproval: ValidRecord() with { AcceptedApprovalId = Guid.Empty });

        AssertUnsatisfied(result, "ACCEPTED_APPROVAL_ID_REQUIRED");
    }

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_RejectsProjectMismatch()
    {
        var result = Evaluate(acceptedApproval: ValidRecord() with { ProjectId = Guid.NewGuid() });

        AssertUnsatisfied(result, "PROJECT_ID_MISMATCH");
    }

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_RejectsTargetKindMismatch()
    {
        var result = Evaluate(acceptedApproval: ValidRecord() with { ApprovalTargetKind = AcceptedApprovalTargetKinds.SourceApplyRequest });

        AssertUnsatisfied(result, "APPROVAL_TARGET_KIND_MISMATCH");
    }

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_RejectsTargetIdMismatch()
    {
        var result = Evaluate(acceptedApproval: ValidRecord() with { ApprovalTargetId = "patch-artifact-other" });

        AssertUnsatisfied(result, "APPROVAL_TARGET_ID_MISMATCH");
    }

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_RejectsTargetHashMismatch()
    {
        var result = Evaluate(acceptedApproval: ValidRecord() with { ApprovalTargetHash = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" });

        AssertUnsatisfied(result, "APPROVAL_TARGET_HASH_MISMATCH");
    }

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_RejectsCapabilityMismatch()
    {
        var result = Evaluate(acceptedApproval: ValidRecord() with { CapabilityCode = "SOURCE_APPLY" });

        AssertUnsatisfied(result, "CAPABILITY_CODE_MISMATCH");
    }

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_RejectsPurposeMismatch()
    {
        var result = Evaluate(acceptedApproval: ValidRecord() with { ApprovalPurpose = AcceptedApprovalPurposes.SourceApplyInput });

        AssertUnsatisfied(result, "APPROVAL_PURPOSE_MISMATCH");
    }

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_RejectsExpiredApproval()
    {
        var result = Evaluate(acceptedApproval: ValidRecord() with
        {
            AcceptedAtUtc = EvaluatedAtUtc.AddDays(-2),
            ExpiresAtUtc = EvaluatedAtUtc
        });

        AssertUnsatisfied(result, "ACCEPTED_APPROVAL_EXPIRED");
    }

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_AllowsUnexpiredApproval()
    {
        var result = Evaluate(acceptedApproval: ValidRecord() with { ExpiresAtUtc = EvaluatedAtUtc.AddMinutes(1) });

        Assert.IsTrue(result.IsSatisfied, IssueCodes(result));
    }

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_AllowsNoExpiry()
    {
        var result = Evaluate(acceptedApproval: ValidRecord() with { ExpiresAtUtc = null });

        Assert.IsTrue(result.IsSatisfied, IssueCodes(result));
    }

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_RejectsMissingRequiredEvidence()
    {
        var result = Evaluate(requirement: ValidRequirement() with { RequiredEvidenceReferences = ["approval-package:missing"] });

        AssertUnsatisfied(result, "REQUIRED_EVIDENCE_REFERENCE_MISSING");
    }

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_RejectsMissingBoundaryMaxim()
    {
        var result = Evaluate(requirement: ValidRequirement() with { RequiredBoundaryMaxims = ["Missing boundary maxim."] });

        AssertUnsatisfied(result, "REQUIRED_BOUNDARY_MAXIM_MISSING");
    }

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_DoesNotCreatePolicySatisfaction() =>
        AssertNoProductionTokens(
            "PolicySatisfied = true",
            "PolicySatisfactionRecord",
            "PolicySatisfactionStore",
            "CreatePolicySatisfaction",
            "SatisfyPolicy");

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_DoesNotAuthorizeExecution() =>
        AssertNoProductionTokens(
            "CanApplySource = true",
            "ApplySourceAsync",
            "RunDryRunAsync",
            "CreatePatchArtifactAsync",
            "ContinueWorkflowAsync",
            "ApproveReleaseAsync",
            "ReleaseReady = true");

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_HasNoPersistenceOrApiDependency() =>
        AssertNoProductionTokens(
            "SqlConnection",
            "DbCommand",
            "Dapper",
            "Controller",
            "HttpGet",
            "HttpPost",
            "IActionResult");

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_ReceiptStatesBoundary()
    {
        var receipt = ReceiptText();

        foreach (var statement in new[]
        {
            "PR173 adds the Approval Satisfaction Evaluator.",
            "This PR evaluates whether an accepted approval record satisfies an approval requirement.",
            "This PR is pure evaluation only.",
            "Approval satisfaction evaluation is not policy satisfaction.",
            "Satisfied approval requirement is not policy satisfaction.",
            "Satisfied approval requirement is not dry-run execution.",
            "Satisfied approval requirement is not patch artifact creation.",
            "Satisfied approval requirement is not source apply.",
            "Satisfied approval requirement is not workflow continuation.",
            "Satisfied approval requirement is not release readiness.",
            "Satisfied approval requirement does not authorize execution.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next block target is Policy Satisfaction Record Contract.",
            "PR174 - Policy Satisfaction Record Contract"
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_UsesExactHashMatch()
    {
        var result = Evaluate(acceptedApproval: ValidRecord() with { ApprovalTargetHash = ValidRequirement().ApprovalTargetHash.ToUpperInvariant() });

        AssertUnsatisfied(result, "APPROVAL_TARGET_HASH_MISMATCH");
    }

    [TestMethod]
    public void ApprovalSatisfactionEvaluator_DoesNotUseSemanticOrTextInference() =>
        AssertNoProductionTokens(
            "semantic",
            "inferred",
            "contains approval text",
            "human-looking approval",
            "LLM",
            "model",
            "memory",
            "chat");

    private static ApprovalSatisfactionEvaluation Evaluate(
        ApprovalRequirement? requirement = null,
        AcceptedApprovalRecord? acceptedApproval = null)
    {
        var evaluator = new ApprovalSatisfactionEvaluator();
        return evaluator.Evaluate(requirement ?? ValidRequirement(), acceptedApproval ?? ValidRecord());
    }

    private static ApprovalRequirement ValidRequirement() =>
        new()
        {
            ProjectId = ProjectId,
            ApprovalTargetKind = AcceptedApprovalTargetKinds.PatchArtifact,
            ApprovalTargetId = "patch-artifact-pr173",
            ApprovalTargetHash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            CapabilityCode = "L4_ACCEPTED_APPROVAL_RECORD",
            ApprovalPurpose = AcceptedApprovalPurposes.PolicySatisfactionInput,
            EvaluatedAtUtc = EvaluatedAtUtc,
            RequiredEvidenceReferences = ["approval-package:approval-package-pr173"],
            RequiredBoundaryMaxims = BoundaryMaxims()
        };

    private static AcceptedApprovalRecord ValidRecord() =>
        new()
        {
            AcceptedApprovalId = AcceptedApprovalId,
            ProjectId = ProjectId,
            ApprovalTargetKind = AcceptedApprovalTargetKinds.PatchArtifact,
            ApprovalTargetId = "patch-artifact-pr173",
            ApprovalTargetHash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            CapabilityCode = "L4_ACCEPTED_APPROVAL_RECORD",
            ApprovalPurpose = AcceptedApprovalPurposes.PolicySatisfactionInput,
            ApprovedByActorId = "human-operator-pr173",
            ApprovedByActorDisplayName = "Human Operator",
            AcceptedAtUtc = EvaluatedAtUtc.AddDays(-1),
            ExpiresAtUtc = EvaluatedAtUtc.AddDays(7),
            CorrelationId = "correlation-pr173",
            CausationId = "approval-package-pr173",
            EvidenceReferences = ["approval-package:approval-package-pr173"],
            BoundaryMaxims = BoundaryMaxims()
        };

    private static IReadOnlyList<string> BoundaryMaxims() =>
    [
        "Accepted approval record is not policy satisfaction.",
        "Accepted approval record is not dry-run execution.",
        "Accepted approval record is not patch artifact creation.",
        "Accepted approval record is not source apply.",
        "Accepted approval record is not workflow continuation.",
        "Accepted approval record is not release readiness.",
        "Persisting accepted approval does not authorize execution."
    ];

    private static void AssertUnsatisfied(ApprovalSatisfactionEvaluation result, string expectedIssueCode)
    {
        Assert.IsFalse(result.IsSatisfied, "Expected approval satisfaction evaluation to fail.");
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedIssueCode), $"Expected {expectedIssueCode}, got: {IssueCodes(result)}");
    }

    private static string IssueCodes(ApprovalSatisfactionEvaluation result) =>
        string.Join(", ", result.Issues.Select(issue => issue.Code));

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

    private static IReadOnlyList<string> ProductionFiles()
    {
        var root = RepoRoot();
        return
        [
            Path.Combine(root, "IronDev.Core", "Governance", "ApprovalRequirement.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "ApprovalSatisfactionEvaluation.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IApprovalSatisfactionEvaluator.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "ApprovalSatisfactionEvaluator.cs")
        ];
    }

    private static string ReceiptText() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "PR173_APPROVAL_SATISFACTION_EVALUATOR.md"));

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
