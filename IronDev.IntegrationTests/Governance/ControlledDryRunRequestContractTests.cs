using System.Reflection;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ControlledDryRunRequestContract")]
public sealed class ControlledDryRunRequestContractTests
{
    private static readonly Guid ControlledDryRunRequestId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid PolicySatisfactionId = Guid.Parse("99999999-8888-7777-6666-555555555555");
    private static readonly DateTimeOffset RequestedAtUtc = new(2026, 6, 16, 11, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ControlledDryRunRequestContract_RequiresPolicySatisfactionBinding()
    {
        foreach (var property in new[]
        {
            nameof(ControlledDryRunRequest.PolicySatisfactionId),
            nameof(ControlledDryRunRequest.PolicySatisfactionHash),
            nameof(ControlledDryRunRequest.ProjectId),
            nameof(ControlledDryRunRequest.CapabilityCode)
        })
        {
            AssertHasProperty(property);
        }

        AssertValid(ValidRequest());
    }

    [TestMethod]
    public void ControlledDryRunRequestContract_RequiresSubjectBinding()
    {
        foreach (var property in new[]
        {
            nameof(ControlledDryRunRequest.SubjectKind),
            nameof(ControlledDryRunRequest.SubjectId),
            nameof(ControlledDryRunRequest.SubjectHash)
        })
        {
            AssertHasProperty(property);
        }

        AssertInvalid(ValidRequest() with { SubjectKind = " " }, "SUBJECT_KIND_REQUIRED");
        AssertInvalid(ValidRequest() with { SubjectId = " " }, "SUBJECT_ID_REQUIRED");
        AssertInvalid(ValidRequest() with { SubjectHash = " " }, "SUBJECT_HASH_REQUIRED");
    }

    [TestMethod]
    public void ControlledDryRunRequestContract_RequiresWorkspaceBoundary()
    {
        foreach (var property in new[]
        {
            nameof(ControlledDryRunRequest.WorkspaceKind),
            nameof(ControlledDryRunRequest.WorkspaceId),
            nameof(ControlledDryRunRequest.WorkspaceBoundaryHash)
        })
        {
            AssertHasProperty(property);
        }

        AssertInvalid(ValidRequest() with { WorkspaceKind = " " }, "WORKSPACE_KIND_REQUIRED");
        AssertInvalid(ValidRequest() with { WorkspaceId = " " }, "WORKSPACE_ID_REQUIRED");
        AssertInvalid(ValidRequest() with { WorkspaceBoundaryHash = " " }, "WORKSPACE_BOUNDARY_HASH_REQUIRED");
    }

    [TestMethod]
    public void ControlledDryRunRequestContract_RequiresRequestedOperationBinding()
    {
        foreach (var property in new[]
        {
            nameof(ControlledDryRunRequest.RequestedOperation),
            nameof(ControlledDryRunRequest.RequestedOperationHash)
        })
        {
            AssertHasProperty(property);
        }

        AssertInvalid(ValidRequest() with { RequestedOperation = " " }, "REQUESTED_OPERATION_REQUIRED");
        AssertInvalid(ValidRequest() with { RequestedOperationHash = " " }, "REQUESTED_OPERATION_HASH_REQUIRED");
    }

    [TestMethod]
    public void ControlledDryRunRequestContract_RequiresValidationPlanBinding()
    {
        foreach (var property in new[]
        {
            nameof(ControlledDryRunRequest.ValidationPlanKind),
            nameof(ControlledDryRunRequest.ValidationPlanId),
            nameof(ControlledDryRunRequest.ValidationPlanHash)
        })
        {
            AssertHasProperty(property);
        }

        AssertInvalid(ValidRequest() with { ValidationPlanKind = " " }, "VALIDATION_PLAN_KIND_REQUIRED");
        AssertInvalid(ValidRequest() with { ValidationPlanId = " " }, "VALIDATION_PLAN_ID_REQUIRED");
        AssertInvalid(ValidRequest() with { ValidationPlanHash = " " }, "VALIDATION_PLAN_HASH_REQUIRED");
    }

    [TestMethod]
    public void ControlledDryRunRequestContract_RequiresEvidenceAndBoundaryMaxims()
    {
        AssertInvalid(ValidRequest() with { EvidenceReferences = [] }, "EVIDENCE_REFERENCES_REQUIRED");
        AssertInvalid(ValidRequest() with { EvidenceReferences = [" "] }, "EVIDENCE_REFERENCES_REQUIRED");
        AssertInvalid(ValidRequest() with { BoundaryMaxims = [] }, "BOUNDARY_MAXIMS_REQUIRED");
        AssertInvalid(ValidRequest() with { BoundaryMaxims = [" "] }, "BOUNDARY_MAXIMS_REQUIRED");
    }

    [TestMethod]
    public void ControlledDryRunRequestContract_RejectsMissingPolicySatisfactionId() =>
        AssertInvalid(ValidRequest() with { PolicySatisfactionId = Guid.Empty }, "POLICY_SATISFACTION_ID_REQUIRED");

    [TestMethod]
    public void ControlledDryRunRequestContract_RejectsMissingPolicySatisfactionHash() =>
        AssertInvalid(ValidRequest() with { PolicySatisfactionHash = " " }, "POLICY_SATISFACTION_HASH_REQUIRED");

    [TestMethod]
    public void ControlledDryRunRequestContract_RejectsMissingSubjectHash() =>
        AssertInvalid(ValidRequest() with { SubjectHash = " " }, "SUBJECT_HASH_REQUIRED");

    [TestMethod]
    public void ControlledDryRunRequestContract_RejectsMissingWorkspaceBoundaryHash() =>
        AssertInvalid(ValidRequest() with { WorkspaceBoundaryHash = " " }, "WORKSPACE_BOUNDARY_HASH_REQUIRED");

    [TestMethod]
    public void ControlledDryRunRequestContract_RejectsMissingRequestedOperationHash() =>
        AssertInvalid(ValidRequest() with { RequestedOperationHash = " " }, "REQUESTED_OPERATION_HASH_REQUIRED");

    [TestMethod]
    public void ControlledDryRunRequestContract_RejectsMissingValidationPlanHash() =>
        AssertInvalid(ValidRequest() with { ValidationPlanHash = " " }, "VALIDATION_PLAN_HASH_REQUIRED");

    [TestMethod]
    public void ControlledDryRunRequestContract_RejectsInvalidExpiry()
    {
        AssertInvalid(ValidRequest() with { ExpiresAtUtc = RequestedAtUtc }, "EXPIRES_AT_UTC_INVALID");
        AssertInvalid(ValidRequest() with { ExpiresAtUtc = RequestedAtUtc.AddSeconds(-1) }, "EXPIRES_AT_UTC_INVALID");
    }

    [TestMethod]
    public void ControlledDryRunRequestContract_BoundaryStatesRequestIsNotExecution()
    {
        foreach (var statement in BoundaryStatements())
        {
            StringAssert.Contains(ControlledDryRunRequestBoundaryText.Boundary, statement);
        }
    }

    [TestMethod]
    public void ControlledDryRunRequestContract_RejectsExecutionAuthorityClaims()
    {
        foreach (var marker in new[]
        {
            "dry-run executed",
            "patch artifact created",
            "source applied",
            "workflow continued",
            "release ready"
        })
        {
            AssertInvalid(ValidRequest() with { EvidenceReferences = [$"evidence says {marker}"] }, "EXECUTION_AUTHORITY_CLAIM_REJECTED");
            AssertInvalid(ValidRequest() with { Boundary = marker }, "EXECUTION_AUTHORITY_CLAIM_REJECTED");
        }
    }

    [TestMethod]
    public void ControlledDryRunRequestContract_RejectsPrivateMaterial()
    {
        foreach (var marker in new[]
        {
            "raw prompt",
            "chain-of-thought",
            "private reasoning",
            "scratchpad",
            "secret"
        })
        {
            AssertInvalid(ValidRequest() with { EvidenceReferences = [$"evidence contains {marker}"] }, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
            AssertInvalid(ValidRequest() with { BoundaryMaxims = [$"maxim contains {marker}"] }, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
        }
    }

    [TestMethod]
    public void ControlledDryRunRequestContract_DoesNotExposeExecutionAuthority()
    {
        foreach (var forbiddenProperty in new[]
        {
            "CanRunDryRun",
            "CanCreatePatchArtifact",
            "CanApplySource",
            "CanContinueWorkflow",
            "CanApproveRelease",
            "ReleaseReady"
        })
        {
            Assert.IsNull(typeof(ControlledDryRunRequest).GetProperty(forbiddenProperty), $"Controlled dry-run request must not expose {forbiddenProperty}.");
        }
    }

    [TestMethod]
    public void ControlledDryRunRequestContract_DoesNotImplementRunner()
    {
        foreach (var token in new[]
        {
            "RunDryRunAsync",
            "DryRunExecutor",
            "ControlledDryRunRunner",
            "DryRunResult",
            "PatchArtifact",
            "ApplySourceAsync",
            "ContinueWorkflowAsync",
            "ApproveReleaseAsync"
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void ControlledDryRunRequestContract_HasNoSqlApiCliUi()
    {
        foreach (var file in Pr180ChangedFiles())
        {
            var relative = Path.GetRelativePath(RepoRoot(), file);
            foreach (var token in new[] { "Database", "Controller", "Program.cs", "Cli", "Tauri", "UI" })
            {
                Assert.IsFalse(relative.Contains(token, StringComparison.OrdinalIgnoreCase), $"PR180 must not add {token}: {relative}");
            }
        }
    }

    [TestMethod]
    public void ControlledDryRunRequestContract_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(ReceiptPath());

        foreach (var statement in new[]
        {
            "PR180 adds the Controlled Dry-run Request contract.",
            "This PR starts Block R.",
            "This PR is contract/test/receipt only.",
            "This PR adds no SQL.",
            "This PR adds no API.",
            "This PR adds no CLI.",
            "This PR adds no UI.",
            "This PR does not execute dry-runs.",
            "This PR does not create dry-run results.",
            "This PR does not create patch artifacts.",
            "This PR does not apply source.",
            "This PR does not execute rollback.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "Policy satisfaction is an input to controlled dry-run request.",
            "Policy satisfaction is not controlled dry-run execution.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next Block R target is Controlled Dry-run SQL Store.",
            "PR181 - Controlled Dry-run SQL Store"
        })
        {
            StringAssert.Contains(receipt, statement);
        }

        foreach (var statement in BoundaryStatements())
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private static ControlledDryRunRequest ValidRequest() => new()
    {
        ControlledDryRunRequestId = ControlledDryRunRequestId,
        ProjectId = ProjectId,
        PolicySatisfactionId = PolicySatisfactionId,
        PolicySatisfactionHash = "sha256:policy-satisfaction",
        SubjectKind = "PatchProposal",
        SubjectId = "patch-proposal-123",
        SubjectHash = "sha256:subject",
        CapabilityCode = "source.apply.preview",
        WorkspaceKind = "disposable workspace",
        WorkspaceId = "workspace-123",
        WorkspaceBoundaryHash = "sha256:workspace-boundary",
        RequestedOperation = "run focused tests",
        RequestedOperationHash = "sha256:requested-operation",
        ValidationPlanKind = "focused-test-band",
        ValidationPlanId = "validation-plan-123",
        ValidationPlanHash = "sha256:validation-plan",
        RequestedAtUtc = RequestedAtUtc,
        ExpiresAtUtc = RequestedAtUtc.AddHours(1),
        CorrelationId = "correlation-123",
        CausationId = "causation-123",
        EvidenceReferences = ["policy-satisfaction:99999999-8888-7777-6666-555555555555"],
        BoundaryMaxims = ["request is not execution"]
    };

    private static string[] BoundaryStatements() =>
    [
        "Controlled dry-run request is not dry-run execution.",
        "Controlled dry-run request is not a dry-run result.",
        "Controlled dry-run request is not patch artifact creation.",
        "Controlled dry-run request is not source apply.",
        "Controlled dry-run request is not rollback.",
        "Controlled dry-run request is not workflow continuation.",
        "Controlled dry-run request is not release readiness.",
        "Controlled dry-run request does not authorize execution by itself."
    ];

    private static void AssertValid(ControlledDryRunRequest request)
    {
        var result = ControlledDryRunRequestValidation.Validate(request);
        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}:{issue.Field}:{issue.Message}")));
    }

    private static void AssertInvalid(ControlledDryRunRequest request, string expectedCode)
    {
        var result = ControlledDryRunRequestValidation.Validate(request);
        Assert.IsFalse(result.IsValid, "Expected request to be invalid.");
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedCode), $"Expected issue code {expectedCode}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertHasProperty(string propertyName) =>
        Assert.IsNotNull(typeof(ControlledDryRunRequest).GetProperty(propertyName), $"Expected property {propertyName}.");

    private static void AssertNoProductionToken(string token)
    {
        foreach (var file in Pr180ProductionFiles())
        {
            Assert.IsFalse(File.ReadAllText(file).Contains(token, StringComparison.Ordinal), $"Unexpected production token {token} in {file}.");
        }
    }

    private static string[] Pr180ProductionFiles() =>
    [
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "ControlledDryRunRequest.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "ControlledDryRunRequestValidation.cs")
    ];

    private static string[] Pr180ChangedFiles() =>
    [
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "ControlledDryRunRequest.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "ControlledDryRunRequestValidation.cs"),
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR180_CONTROLLED_DRY_RUN_REQUEST_CONTRACT.md"),
        Path.Combine(RepoRoot(), "IronDev.IntegrationTests", "Governance", "ControlledDryRunRequestContractTests.cs")
    ];

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR180_CONTROLLED_DRY_RUN_REQUEST_CONTRACT.md");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
