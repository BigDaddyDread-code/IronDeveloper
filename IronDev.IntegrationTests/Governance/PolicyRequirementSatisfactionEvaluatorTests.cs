using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("PolicyRequirementSatisfactionEvaluator")]
public sealed class PolicyRequirementSatisfactionEvaluatorTests
{
    private static readonly Guid AcceptedApprovalId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTimeOffset EvaluatedAtUtc = new(2026, 6, 16, 13, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_SatisfiesWhenApprovalEvaluationSatisfiedAndPolicyRequirementValid()
    {
        var result = Evaluate();

        Assert.IsTrue(result.IsSatisfied, IssueCodes(result));
        Assert.AreEqual(AcceptedApprovalId, result.AcceptedApprovalId);
        Assert.AreEqual(ValidRequirement().ApprovalRequirementHash, result.ApprovalRequirementHash);
        Assert.AreEqual(PolicyRequirementHash.Compute(ValidRequirement()), result.PolicyRequirementHash);
        Assert.AreEqual(0, result.Issues.Count);
        StringAssert.Contains(result.Boundary, "Satisfied policy requirement does not authorize execution.");
    }

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_RejectsNullPolicyRequirement()
    {
        var evaluator = new PolicyRequirementSatisfactionEvaluator();
        var result = evaluator.Evaluate(null, ValidApprovalEvaluation());

        AssertUnsatisfied(result, "POLICY_REQUIREMENT_REQUIRED");
        Assert.IsNull(result.AcceptedApprovalId);
        Assert.IsNull(result.ApprovalRequirementHash);
        Assert.IsNull(result.PolicyRequirementHash);
    }

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_RejectsNullApprovalEvaluation()
    {
        var result = new PolicyRequirementSatisfactionEvaluator().Evaluate(ValidRequirement(), null);

        AssertUnsatisfied(result, "APPROVAL_SATISFACTION_EVALUATION_REQUIRED");
    }

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_RejectsUnsatisfiedApprovalEvaluation()
    {
        var result = Evaluate(approvalEvaluation: ValidApprovalEvaluation() with { IsSatisfied = false });

        AssertUnsatisfied(result, "APPROVAL_REQUIREMENT_NOT_SATISFIED");
    }

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_RejectsApprovalEvaluationWithIssues()
    {
        var result = Evaluate(approvalEvaluation: ValidApprovalEvaluation() with
        {
            Issues = [new ApprovalSatisfactionIssue("APPROVAL_TARGET_HASH_MISMATCH", "ApprovalTargetHash", "Mismatch.")]
        });

        AssertUnsatisfied(result, "APPROVAL_EVALUATION_HAS_ISSUES");
    }

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_RejectsMissingAcceptedApprovalId()
    {
        var result = Evaluate(approvalEvaluation: ValidApprovalEvaluation() with { AcceptedApprovalId = null });

        AssertUnsatisfied(result, "ACCEPTED_APPROVAL_ID_REQUIRED");
    }

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_RejectsMissingPolicyCode() =>
        AssertUnsatisfied(Evaluate(requirement: ValidRequirement() with { PolicyCode = " " }), "POLICY_CODE_REQUIRED");

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_RejectsMissingPolicyVersion() =>
        AssertUnsatisfied(Evaluate(requirement: ValidRequirement() with { PolicyVersion = " " }), "POLICY_VERSION_REQUIRED");

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_RejectsMissingSubjectHash() =>
        AssertUnsatisfied(Evaluate(requirement: ValidRequirement() with { SubjectHash = " " }), "SUBJECT_HASH_REQUIRED");

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_RejectsMissingCapabilityCode() =>
        AssertUnsatisfied(Evaluate(requirement: ValidRequirement() with { CapabilityCode = " " }), "CAPABILITY_CODE_REQUIRED");

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_RejectsMissingApprovalRequirementHash() =>
        AssertUnsatisfied(Evaluate(requirement: ValidRequirement() with { ApprovalRequirementHash = " " }), "APPROVAL_REQUIREMENT_HASH_REQUIRED");

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_RejectsExpiredPolicyRequirement()
    {
        var result = Evaluate(requirement: ValidRequirement() with { ExpiresAtUtc = EvaluatedAtUtc });

        AssertUnsatisfied(result, "POLICY_REQUIREMENT_EXPIRED");
    }

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_AllowsUnexpiredPolicyRequirement()
    {
        var result = Evaluate(requirement: ValidRequirement() with { ExpiresAtUtc = EvaluatedAtUtc.AddTicks(1) });

        Assert.IsTrue(result.IsSatisfied, IssueCodes(result));
    }

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_AllowsNoExpiryPolicyRequirement()
    {
        var result = Evaluate(requirement: ValidRequirement() with { ExpiresAtUtc = null });

        Assert.IsTrue(result.IsSatisfied, IssueCodes(result));
    }

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_RejectsMissingRequiredEvidenceReference()
    {
        var result = Evaluate(approvalEvaluation: ValidApprovalEvaluation() with { EvidenceReferences = ["accepted-approval:" + AcceptedApprovalId] });

        AssertUnsatisfied(result, "REQUIRED_EVIDENCE_REFERENCE_MISSING");
    }

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_RejectsMissingRequiredBoundaryMaxim()
    {
        var result = Evaluate(approvalEvaluation: ValidApprovalEvaluation() with { BoundaryMaxims = ["Accepted approval record is not policy satisfaction."] });

        AssertUnsatisfied(result, "REQUIRED_BOUNDARY_MAXIM_MISSING");
    }

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_UsesDeterministicPolicyRequirementHash()
    {
        var requirement = ValidRequirement();

        Assert.AreEqual(PolicyRequirementHash.Compute(requirement), PolicyRequirementHash.Compute(requirement));
        Assert.AreNotEqual(PolicyRequirementHash.Compute(requirement), PolicyRequirementHash.Compute(requirement with { SubjectHash = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" }));
        Assert.AreNotEqual(PolicyRequirementHash.Compute(requirement), PolicyRequirementHash.Compute(requirement with { PolicyVersion = "2026-06-16.v2" }));
    }

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_DoesNotCreatePolicySatisfactionRecord() =>
        AssertNoProductionTokens(
            "PolicySatisfactionStore",
            "SaveAsync",
            "CreatePolicySatisfaction",
            "SqlPolicySatisfaction");

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_DoesNotAuthorizeExecution() =>
        AssertNoProductionTokens(
            "CanApplySource = true",
            "ApplySourceAsync",
            "RunDryRunAsync",
            "CreatePatchArtifactAsync",
            "ContinueWorkflowAsync",
            "ApproveReleaseAsync",
            "ReleaseReady = true");

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_HasNoPersistenceOrApiDependency() =>
        AssertNoProductionTokens(
            "SqlConnection",
            "DbCommand",
            "Dapper",
            "Controller",
            "HttpGet",
            "HttpPost",
            "IActionResult");

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_DoesNotUseSemanticOrTextInference() =>
        AssertNoProductionTokens(
            "semantic",
            "inferred",
            "contains approval text",
            "human-looking approval",
            "LLM",
            "model",
            "memory",
            "chat");

    [TestMethod]
    public void PolicyRequirementSatisfactionEvaluator_ReceiptStatesBoundary()
    {
        var receipt = ReceiptText();

        foreach (var statement in new[]
        {
            "PR176 adds the Policy Requirement/Satisfaction Evaluator.",
            "This PR evaluates whether a policy requirement is satisfied by an approval satisfaction evaluation.",
            "This PR is pure evaluation only.",
            "This PR adds no SQL.",
            "This PR adds no API.",
            "This PR adds no CLI.",
            "This PR adds no UI.",
            "This PR does not create policy satisfaction records.",
            "This PR does not store policy satisfaction records.",
            "This PR does not satisfy policy.",
            "This PR does not run dry-runs.",
            "This PR does not create patch artifacts.",
            "This PR does not apply source.",
            "This PR does not execute rollback.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "Policy requirement satisfaction evaluation is not a policy satisfaction record.",
            "Policy requirement satisfaction evaluation is not dry-run execution.",
            "Policy requirement satisfaction evaluation is not patch artifact creation.",
            "Policy requirement satisfaction evaluation is not source apply.",
            "Policy requirement satisfaction evaluation is not rollback.",
            "Policy requirement satisfaction evaluation is not workflow continuation.",
            "Policy requirement satisfaction evaluation is not release readiness.",
            "Satisfied policy requirement does not authorize execution.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next Block Q target is Governed Policy Satisfaction Create API.",
            "PR177 - Governed Policy Satisfaction Create API"
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private static PolicyRequirementSatisfactionEvaluation Evaluate(
        PolicyRequirement? requirement = null,
        ApprovalSatisfactionEvaluation? approvalEvaluation = null)
    {
        var evaluator = new PolicyRequirementSatisfactionEvaluator();
        return evaluator.Evaluate(requirement ?? ValidRequirement(), approvalEvaluation ?? ValidApprovalEvaluation());
    }

    private static PolicyRequirement ValidRequirement() =>
        new()
        {
            ProjectId = ProjectId,
            PolicyCode = "source-apply-policy",
            PolicyVersion = "2026-06-16.v1",
            SubjectKind = "patch-artifact",
            SubjectId = "patch-artifact-pr176",
            SubjectHash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            CapabilityCode = "SOURCE_APPLY",
            ApprovalTargetKind = AcceptedApprovalTargetKinds.PatchArtifact,
            ApprovalTargetId = "patch-artifact-pr176",
            ApprovalTargetHash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            ApprovalPurpose = AcceptedApprovalPurposes.PolicySatisfactionInput,
            ApprovalRequirementHash = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            EvaluatedAtUtc = EvaluatedAtUtc,
            ExpiresAtUtc = EvaluatedAtUtc.AddDays(1),
            RequiredEvidenceReferences = EvidenceReferences(),
            RequiredBoundaryMaxims = BoundaryMaxims()
        };

    private static ApprovalSatisfactionEvaluation ValidApprovalEvaluation() =>
        new()
        {
            IsSatisfied = true,
            AcceptedApprovalId = AcceptedApprovalId,
            EvidenceReferences = EvidenceReferences(),
            BoundaryMaxims = BoundaryMaxims(),
            Issues = []
        };

    private static IReadOnlyList<string> EvidenceReferences() =>
    [
        "accepted-approval:" + AcceptedApprovalId,
        "approval-satisfaction:evaluation-pr176"
    ];

    private static IReadOnlyList<string> BoundaryMaxims() =>
    [
        "Accepted approval record is not policy satisfaction.",
        "Satisfied approval requirement is not policy satisfaction.",
        "Satisfied policy requirement does not authorize execution."
    ];

    private static void AssertUnsatisfied(PolicyRequirementSatisfactionEvaluation result, string expectedIssueCode)
    {
        Assert.IsFalse(result.IsSatisfied, "Expected policy requirement satisfaction evaluation to fail.");
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedIssueCode), $"Expected {expectedIssueCode}, got: {IssueCodes(result)}");
    }

    private static string IssueCodes(PolicyRequirementSatisfactionEvaluation result) =>
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
            Path.Combine(root, "IronDev.Core", "Governance", "ApprovalSatisfactionEvaluation.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "PolicyRequirement.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "PolicyRequirementSatisfactionEvaluation.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IPolicyRequirementSatisfactionEvaluator.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "PolicyRequirementSatisfactionEvaluator.cs")
        ];
    }

    private static string ReceiptText() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "PR176_POLICY_REQUIREMENT_SATISFACTION_EVALUATOR.md"));

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
