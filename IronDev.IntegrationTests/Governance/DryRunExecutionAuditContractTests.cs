using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("DryRunExecutionAuditContract")]
public sealed class DryRunExecutionAuditContractTests
{
    private static readonly Guid DryRunExecutionAuditId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid ControlledDryRunRequestId = Guid.Parse("22222222-3333-4444-5555-666666666666");
    private static readonly Guid PolicySatisfactionId = Guid.Parse("99999999-8888-7777-6666-555555555555");
    private static readonly DateTimeOffset StartedAtUtc = new(2026, 6, 16, 11, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void DryRunExecutionAuditContract_RequiresRequestPolicySubjectWorkspaceAndPlanBinding()
    {
        foreach (var property in new[]
        {
            nameof(ControlledDryRunExecutionAudit.DryRunExecutionAuditId),
            nameof(ControlledDryRunExecutionAudit.ProjectId),
            nameof(ControlledDryRunExecutionAudit.ControlledDryRunRequestId),
            nameof(ControlledDryRunExecutionAudit.PolicySatisfactionId),
            nameof(ControlledDryRunExecutionAudit.PolicySatisfactionHash),
            nameof(ControlledDryRunExecutionAudit.SubjectKind),
            nameof(ControlledDryRunExecutionAudit.SubjectId),
            nameof(ControlledDryRunExecutionAudit.SubjectHash),
            nameof(ControlledDryRunExecutionAudit.WorkspaceId),
            nameof(ControlledDryRunExecutionAudit.WorkspaceKind),
            nameof(ControlledDryRunExecutionAudit.WorkspaceBoundaryHash),
            nameof(ControlledDryRunExecutionAudit.SourceSnapshotReference),
            nameof(ControlledDryRunExecutionAudit.ValidationPlanId),
            nameof(ControlledDryRunExecutionAudit.ValidationPlanHash)
        })
        {
            AssertHasProperty(typeof(ControlledDryRunExecutionAudit), property);
        }

        AssertValid(ValidAudit());
    }

    [TestMethod]
    public void DryRunExecutionAuditContract_RequiresExecutionStatusAndHashes()
    {
        foreach (var property in new[]
        {
            nameof(ControlledDryRunExecutionAudit.StartedAtUtc),
            nameof(ControlledDryRunExecutionAudit.CompletedAtUtc),
            nameof(ControlledDryRunExecutionAudit.DryRunCompleted),
            nameof(ControlledDryRunExecutionAudit.DryRunSucceeded),
            nameof(ControlledDryRunExecutionAudit.ExecutionReportHash),
            nameof(ControlledDryRunExecutionAudit.AuditHash)
        })
        {
            AssertHasProperty(typeof(ControlledDryRunExecutionAudit), property);
        }

        AssertInvalid(ValidAudit() with { ExecutionReportHash = " " }, "EXECUTION_REPORT_HASH_REQUIRED");
        AssertInvalid(ValidAudit() with { AuditHash = " " }, "AUDIT_HASH_REQUIRED");
    }

    [TestMethod]
    public void DryRunExecutionAuditContract_RequiresCommandAuditsEvidenceAndBoundary()
    {
        foreach (var property in new[]
        {
            nameof(ControlledDryRunExecutionAudit.CommandAudits),
            nameof(ControlledDryRunExecutionAudit.EvidenceReferences),
            nameof(ControlledDryRunExecutionAudit.BoundaryMaxims),
            nameof(ControlledDryRunExecutionAudit.Boundary)
        })
        {
            AssertHasProperty(typeof(ControlledDryRunExecutionAudit), property);
        }

        AssertInvalid(ValidAudit() with { CommandAudits = [] }, "COMMAND_AUDITS_REQUIRED");
        AssertInvalid(ValidAudit() with { EvidenceReferences = [] }, "EVIDENCE_REFERENCES_REQUIRED");
        AssertInvalid(ValidAudit() with { BoundaryMaxims = [] }, "BOUNDARY_MAXIMS_REQUIRED");
        AssertInvalid(ValidAudit() with { Boundary = " " }, "BOUNDARY_REQUIRED");
    }

    [TestMethod]
    public void DryRunExecutionAuditContract_RejectsMissingAuditId() =>
        AssertInvalid(ValidAudit() with { DryRunExecutionAuditId = Guid.Empty }, "DRY_RUN_EXECUTION_AUDIT_ID_REQUIRED");

    [TestMethod]
    public void DryRunExecutionAuditContract_RejectsMissingRequestId() =>
        AssertInvalid(ValidAudit() with { ControlledDryRunRequestId = Guid.Empty }, "CONTROLLED_DRY_RUN_REQUEST_ID_REQUIRED");

    [TestMethod]
    public void DryRunExecutionAuditContract_RejectsMissingPolicySatisfactionHash() =>
        AssertInvalid(ValidAudit() with { PolicySatisfactionHash = " " }, "POLICY_SATISFACTION_HASH_REQUIRED");

    [TestMethod]
    public void DryRunExecutionAuditContract_RejectsMissingSubjectHash() =>
        AssertInvalid(ValidAudit() with { SubjectHash = " " }, "SUBJECT_HASH_REQUIRED");

    [TestMethod]
    public void DryRunExecutionAuditContract_RejectsMissingWorkspaceBoundaryHash() =>
        AssertInvalid(ValidAudit() with { WorkspaceBoundaryHash = " " }, "WORKSPACE_BOUNDARY_HASH_REQUIRED");

    [TestMethod]
    public void DryRunExecutionAuditContract_RejectsMissingValidationPlanHash() =>
        AssertInvalid(ValidAudit() with { ValidationPlanHash = " " }, "VALIDATION_PLAN_HASH_REQUIRED");

    [TestMethod]
    public void DryRunExecutionAuditContract_RejectsInvalidCompletedTimestamp() =>
        AssertInvalid(ValidAudit() with { CompletedAtUtc = StartedAtUtc.AddSeconds(-1) }, "COMPLETED_AT_UTC_INVALID");

    [TestMethod]
    public void DryRunExecutionAuditContract_RejectsMissingExecutionReportHash() =>
        AssertInvalid(ValidAudit() with { ExecutionReportHash = " " }, "EXECUTION_REPORT_HASH_REQUIRED");

    [TestMethod]
    public void DryRunExecutionAuditContract_RejectsMissingAuditHash() =>
        AssertInvalid(ValidAudit() with { AuditHash = " " }, "AUDIT_HASH_REQUIRED");

    [TestMethod]
    public void DryRunExecutionAuditContract_RejectsMissingCommandAudits() =>
        AssertInvalid(ValidAudit() with { CommandAudits = [] }, "COMMAND_AUDITS_REQUIRED");

    [TestMethod]
    public void DryRunExecutionAuditContract_RejectsInvalidCommandAuditFields()
    {
        var audit = ValidAudit() with
        {
            CommandAudits =
            [
                ValidCommandAudit() with
                {
                    CommandId = " ",
                    CommandHash = " ",
                    StandardOutputSummaryHash = " ",
                    StandardErrorSummaryHash = " "
                }
            ]
        };

        AssertInvalid(audit, "COMMAND_ID_REQUIRED");
        AssertInvalid(audit, "COMMAND_HASH_REQUIRED");
        AssertInvalid(audit, "STANDARD_OUTPUT_SUMMARY_HASH_REQUIRED");
        AssertInvalid(audit, "STANDARD_ERROR_SUMMARY_HASH_REQUIRED");
    }

    [TestMethod]
    public void DryRunExecutionAuditContract_RejectsRawOrPrivateMaterial()
    {
        foreach (var marker in new[]
        {
            "raw prompt",
            "raw completion",
            "raw tool output",
            "chain-of-thought",
            "private reasoning",
            "scratchpad",
            "secret"
        })
        {
            AssertInvalid(ValidAudit() with { EvidenceReferences = [$"evidence contains {marker}"] }, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
            AssertInvalid(ValidAudit() with { BoundaryMaxims = [$"maxim contains {marker}"] }, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
            AssertInvalid(ValidAudit() with { CommandAudits = [ValidCommandAudit() with { StandardOutputSummary = $"summary contains {marker}" }] }, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
        }
    }

    [TestMethod]
    public void DryRunExecutionAuditContract_RejectsAuthorityClaims()
    {
        foreach (var marker in new[]
        {
            "patch artifact created",
            "source applied",
            "workflow continued",
            "release ready",
            "rollback executed"
        })
        {
            AssertInvalid(ValidAudit() with { EvidenceReferences = [$"evidence says {marker}"] }, "AUTHORITY_CLAIM_REJECTED");
            AssertInvalid(ValidAudit() with { BoundaryMaxims = [$"maxim says {marker}"] }, "AUTHORITY_CLAIM_REJECTED");
            AssertInvalid(ValidAudit() with { CommandAudits = [ValidCommandAudit() with { StandardErrorSummary = $"stderr says {marker}" }] }, "AUTHORITY_CLAIM_REJECTED");
        }
    }

    [TestMethod]
    public void DryRunExecutionAuditContract_BoundaryStatesAuditIsNotExecutionOrAuthority()
    {
        foreach (var statement in BoundaryStatements())
        {
            StringAssert.Contains(ControlledDryRunExecutionAuditBoundaryText.Boundary, statement);
        }
    }

    [TestMethod]
    public void DryRunExecutionAuditContract_DoesNotAddDryRunResultPersistence()
    {
        foreach (var token in new[]
        {
            "DryRunResultStore",
            "SaveDryRunResult",
            "SqlDryRunResult",
            "migrate_dry_run"
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void DryRunExecutionAuditContract_DoesNotAddPatchArtifactOrSourceApply()
    {
        foreach (var token in new[]
        {
            "CreatePatchArtifactAsync",
            "PatchArtifactStore",
            "PatchArtifactId = Guid.NewGuid",
            "ApplySourceAsync",
            "SourceApplyService",
            "ControlledSourceApply"
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void DryRunExecutionAuditContract_HasNoSqlApiCliUiRunnerOrExecutorFiles()
    {
        foreach (var file in Pr183ChangedFiles())
        {
            var relative = Path.GetRelativePath(RepoRoot(), file);
            foreach (var token in new[] { "Database", "Controller", "Program.cs", "Cli", "Tauri", "UI", "Executor", "Runner" })
            {
                Assert.IsFalse(relative.Contains(token, StringComparison.OrdinalIgnoreCase), $"PR183 must not add {token}: {relative}");
            }
        }
    }

    [TestMethod]
    public void DryRunExecutionAuditContract_DoesNotCallModelsAgentsMemoryOrRetrieval()
    {
        foreach (var token in new[]
        {
            "LLM",
            "model call",
            "AgentDispatch",
            "PromoteMemory",
            "ActivateRetrieval",
            "ToolExecution"
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void DryRunExecutionAuditContract_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(ReceiptPath());

        foreach (var statement in new[]
        {
            "PR183 adds the Dry-run Execution Audit contract.",
            "This PR is contract/test/receipt only.",
            "This PR defines the audit evidence shape for controlled dry-run execution.",
            "This PR does not execute dry-runs.",
            "This PR does not persist dry-run results.",
            "This PR does not create patch artifacts.",
            "This PR does not apply source.",
            "This PR does not execute rollback.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "This PR does not add SQL.",
            "This PR does not add API.",
            "This PR does not add CLI.",
            "This PR does not add UI.",
            "The audit binds the dry-run request, policy satisfaction, subject hash, workspace boundary hash, validation plan hash, execution report hash, command audits, evidence references, and boundary maxims.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next Block R target is Controlled Dry-run Result Contract.",
            "PR184 - Controlled Dry-run Result Contract",
            "PR183 records what the cage run proved. It does not store, package, or spend it."
        })
        {
            StringAssert.Contains(receipt, statement);
        }

        foreach (var statement in BoundaryStatements())
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private static ControlledDryRunExecutionAudit ValidAudit() => new()
    {
        DryRunExecutionAuditId = DryRunExecutionAuditId,
        ProjectId = ProjectId,
        ControlledDryRunRequestId = ControlledDryRunRequestId,
        PolicySatisfactionId = PolicySatisfactionId,
        PolicySatisfactionHash = "sha256:policy-satisfaction",
        SubjectKind = "PatchProposal",
        SubjectId = "patch-proposal-123",
        SubjectHash = "sha256:subject",
        WorkspaceId = "workspace-123",
        WorkspaceKind = "disposable workspace",
        WorkspaceBoundaryHash = "sha256:workspace-boundary",
        SourceSnapshotReference = "source-snapshot:abc123",
        ValidationPlanId = "validation-plan-123",
        ValidationPlanHash = "sha256:validation-plan",
        StartedAtUtc = StartedAtUtc,
        CompletedAtUtc = StartedAtUtc.AddMinutes(1),
        DryRunCompleted = true,
        DryRunSucceeded = true,
        ExecutionReportHash = "sha256:execution-report",
        AuditHash = "sha256:audit",
        CommandAudits = [ValidCommandAudit()],
        EvidenceReferences = ["controlled-dry-run-request:22222222-3333-4444-5555-666666666666"],
        BoundaryMaxims = ["audit records evidence only"]
    };

    private static ControlledDryRunCommandAudit ValidCommandAudit() => new()
    {
        CommandId = "command-1",
        WorkingDirectory = "workspace/write-root",
        Executable = "dotnet",
        CommandHash = "sha256:command",
        ExitCode = 0,
        TimedOut = false,
        StandardOutputSummaryHash = "sha256:stdout",
        StandardErrorSummaryHash = "sha256:stderr",
        StandardOutputSummary = "tests passed",
        StandardErrorSummary = "none"
    };

    private static string[] BoundaryStatements() =>
    [
        "Dry-run execution audit is not dry-run execution.",
        "Dry-run execution audit is not dry-run result persistence.",
        "Dry-run execution audit is not patch artifact creation.",
        "Dry-run execution audit is not source apply.",
        "Dry-run execution audit is not rollback.",
        "Dry-run execution audit is not workflow continuation.",
        "Dry-run execution audit is not release readiness.",
        "Dry-run execution audit does not authorize source mutation by itself.",
        "Dry-run execution audit records evidence only."
    ];

    private static void AssertValid(ControlledDryRunExecutionAudit audit)
    {
        var result = ControlledDryRunExecutionAuditValidation.Validate(audit);
        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}:{issue.Field}:{issue.Message}")));
    }

    private static void AssertInvalid(ControlledDryRunExecutionAudit audit, string expectedCode)
    {
        var result = ControlledDryRunExecutionAuditValidation.Validate(audit);
        Assert.IsFalse(result.IsValid, "Expected audit to be invalid.");
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedCode), $"Expected issue code {expectedCode}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertHasProperty(Type type, string propertyName) =>
        Assert.IsNotNull(type.GetProperty(propertyName), $"Expected property {propertyName}.");

    private static void AssertNoProductionToken(string token)
    {
        foreach (var file in Pr183ProductionFiles())
        {
            Assert.IsFalse(File.ReadAllText(file).Contains(token, StringComparison.Ordinal), $"Unexpected production token {token} in {file}.");
        }
    }

    private static string[] Pr183ProductionFiles() =>
    [
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "ControlledDryRunExecutionAudit.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "ControlledDryRunExecutionAuditValidation.cs")
    ];

    private static string[] Pr183ChangedFiles() =>
    [
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "ControlledDryRunExecutionAudit.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "ControlledDryRunExecutionAuditValidation.cs"),
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR183_DRY_RUN_EXECUTION_AUDIT.md"),
        Path.Combine(RepoRoot(), "IronDev.IntegrationTests", "Governance", "DryRunExecutionAuditContractTests.cs")
    ];

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR183_DRY_RUN_EXECUTION_AUDIT.md");

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
