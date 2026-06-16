using System.Reflection;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("PatchArtifactContract")]
public sealed class PatchArtifactContractTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 6, 16, 13, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void PatchArtifactContract_RequiresDryRunPolicySubjectAndSourceBinding()
    {
        foreach (var property in new[]
        {
            nameof(PatchArtifact.PatchArtifactId),
            nameof(PatchArtifact.ProjectId),
            nameof(PatchArtifact.ControlledDryRunRequestId),
            nameof(PatchArtifact.DryRunExecutionAuditId),
            nameof(PatchArtifact.DryRunAuditHash),
            nameof(PatchArtifact.DryRunReceiptHash),
            nameof(PatchArtifact.PolicySatisfactionId),
            nameof(PatchArtifact.PolicySatisfactionHash),
            nameof(PatchArtifact.SubjectKind),
            nameof(PatchArtifact.SubjectId),
            nameof(PatchArtifact.SubjectHash),
            nameof(PatchArtifact.SourceSnapshotReference),
            nameof(PatchArtifact.SourceBaselineHash),
            nameof(PatchArtifact.WorkspaceBoundaryHash),
            nameof(PatchArtifact.ValidationPlanId),
            nameof(PatchArtifact.ValidationPlanHash)
        })
        {
            AssertHasProperty(typeof(PatchArtifact), property);
        }

        AssertValid(ValidArtifact());
    }

    [TestMethod]
    public void PatchArtifactContract_RequiresPatchHashesAndFileChanges()
    {
        AssertHasProperty(typeof(PatchArtifact), nameof(PatchArtifact.PatchHash));
        AssertHasProperty(typeof(PatchArtifact), nameof(PatchArtifact.ChangeSetHash));
        AssertHasProperty(typeof(PatchArtifact), nameof(PatchArtifact.FileChanges));

        AssertInvalid(ValidArtifact() with { PatchHash = " " }, "PATCH_HASH_REQUIRED");
        AssertInvalid(ValidArtifact() with { ChangeSetHash = " " }, "CHANGE_SET_HASH_REQUIRED");
        AssertInvalid(ValidArtifact() with { FileChanges = [] }, "FILE_CHANGES_REQUIRED");
    }

    [TestMethod]
    public void PatchArtifactContract_RequiresEvidenceBoundaryAndCreatedTimestamp()
    {
        AssertHasProperty(typeof(PatchArtifact), nameof(PatchArtifact.CreatedAtUtc));
        AssertHasProperty(typeof(PatchArtifact), nameof(PatchArtifact.EvidenceReferences));
        AssertHasProperty(typeof(PatchArtifact), nameof(PatchArtifact.BoundaryMaxims));
        AssertHasProperty(typeof(PatchArtifact), nameof(PatchArtifact.Boundary));

        AssertInvalid(ValidArtifact() with { CreatedAtUtc = default }, "CREATED_AT_UTC_REQUIRED");
        AssertInvalid(ValidArtifact() with { EvidenceReferences = [] }, "EVIDENCE_REFERENCES_REQUIRED");
        AssertInvalid(ValidArtifact() with { BoundaryMaxims = [] }, "BOUNDARY_MAXIMS_REQUIRED");
        AssertInvalid(ValidArtifact() with { Boundary = " " }, "BOUNDARY_REQUIRED");
    }

    [TestMethod]
    public void PatchArtifactContract_RejectsMissingPatchArtifactId() =>
        AssertInvalid(ValidArtifact() with { PatchArtifactId = Guid.Empty }, "PATCH_ARTIFACT_ID_REQUIRED");

    [TestMethod]
    public void PatchArtifactContract_RejectsMissingDryRunReceiptHash() =>
        AssertInvalid(ValidArtifact() with { DryRunReceiptHash = " " }, "DRY_RUN_RECEIPT_HASH_REQUIRED");

    [TestMethod]
    public void PatchArtifactContract_RejectsMissingSourceBaselineHash() =>
        AssertInvalid(ValidArtifact() with { SourceBaselineHash = " " }, "SOURCE_BASELINE_HASH_REQUIRED");

    [TestMethod]
    public void PatchArtifactContract_RejectsMissingPatchHash() =>
        AssertInvalid(ValidArtifact() with { PatchHash = " " }, "PATCH_HASH_REQUIRED");

    [TestMethod]
    public void PatchArtifactContract_RejectsMissingChangeSetHash() =>
        AssertInvalid(ValidArtifact() with { ChangeSetHash = " " }, "CHANGE_SET_HASH_REQUIRED");

    [TestMethod]
    public void PatchArtifactContract_RejectsMissingFileChanges() =>
        AssertInvalid(ValidArtifact() with { FileChanges = [] }, "FILE_CHANGES_REQUIRED");

    [TestMethod]
    public void PatchArtifactContract_RejectsUnsafePaths()
    {
        foreach (var path in new[] { "../x.cs", @"C:\repo\x.cs", @"\\server\share\x.cs", ".git/config", "src/.git/config", "/" })
        {
            AssertInvalid(ValidArtifact() with { FileChanges = [ValidChange() with { Path = path }] }, "FILE_CHANGE_PATH_UNSAFE");
        }
    }

    [TestMethod]
    public void PatchArtifactContract_AcceptsSafeRelativePaths()
    {
        foreach (var path in new[] { "src/IronDev.Core/Foo.cs", "tests/IronDev.Tests/FooTests.cs", "Docs/receipts/PR187_PATCH_ARTIFACT_CONTRACT.md" })
        {
            AssertValid(ValidArtifact() with { FileChanges = [ValidChange() with { Path = path }] });
        }
    }

    [TestMethod]
    public void PatchArtifactContract_RejectsInvalidChangeKind() =>
        AssertInvalid(ValidArtifact() with { FileChanges = [ValidChange() with { ChangeKind = "Move" }] }, "FILE_CHANGE_KIND_INVALID");

    [TestMethod]
    public void PatchArtifactContract_ValidatesCreateChangeHashes()
    {
        AssertValid(ValidArtifact() with { FileChanges = [ValidChange() with { ChangeKind = "Create", BeforeContentHash = null, AfterContentHash = "sha256:after" }] });
        AssertInvalid(ValidArtifact() with { FileChanges = [ValidChange() with { ChangeKind = "Create", AfterContentHash = " " }] }, "CREATE_AFTER_CONTENT_HASH_REQUIRED");
        AssertInvalid(ValidArtifact() with { FileChanges = [ValidChange() with { ChangeKind = "Create", BeforeContentHash = "sha256:before" }] }, "CREATE_BEFORE_CONTENT_HASH_FORBIDDEN");
    }

    [TestMethod]
    public void PatchArtifactContract_ValidatesModifyChangeHashes()
    {
        AssertValid(ValidArtifact() with { FileChanges = [ValidChange() with { ChangeKind = "Modify", BeforeContentHash = "sha256:before", AfterContentHash = "sha256:after" }] });
        AssertInvalid(ValidArtifact() with { FileChanges = [ValidChange() with { ChangeKind = "Modify", BeforeContentHash = " " }] }, "MODIFY_BEFORE_CONTENT_HASH_REQUIRED");
        AssertInvalid(ValidArtifact() with { FileChanges = [ValidChange() with { ChangeKind = "Modify", AfterContentHash = " " }] }, "MODIFY_AFTER_CONTENT_HASH_REQUIRED");
    }

    [TestMethod]
    public void PatchArtifactContract_ValidatesDeleteChangeHashes()
    {
        AssertValid(ValidArtifact() with { FileChanges = [ValidChange() with { ChangeKind = "Delete", BeforeContentHash = "sha256:before", AfterContentHash = null }] });
        AssertInvalid(ValidArtifact() with { FileChanges = [ValidChange() with { ChangeKind = "Delete", BeforeContentHash = " " }] }, "DELETE_BEFORE_CONTENT_HASH_REQUIRED");
        AssertInvalid(ValidArtifact() with { FileChanges = [ValidChange() with { ChangeKind = "Delete", AfterContentHash = "sha256:after" }] }, "DELETE_AFTER_CONTENT_HASH_FORBIDDEN");
    }

    [TestMethod]
    public void PatchArtifactContract_ValidatesRenameChangeHashes()
    {
        AssertValid(ValidArtifact() with { FileChanges = [ValidChange() with { ChangeKind = "Rename", PreviousPath = "src/Old.cs", BeforeContentHash = "sha256:before", AfterContentHash = "sha256:after" }] });
        AssertInvalid(ValidArtifact() with { FileChanges = [ValidChange() with { ChangeKind = "Rename", PreviousPath = " " }] }, "RENAME_PREVIOUS_PATH_REQUIRED");
        AssertInvalid(ValidArtifact() with { FileChanges = [ValidChange() with { ChangeKind = "Rename", BeforeContentHash = " " }] }, "RENAME_BEFORE_CONTENT_HASH_REQUIRED");
        AssertInvalid(ValidArtifact() with { FileChanges = [ValidChange() with { ChangeKind = "Rename", AfterContentHash = " " }] }, "RENAME_AFTER_CONTENT_HASH_REQUIRED");
    }

    [TestMethod]
    public void PatchArtifactContract_RejectsPrivateRawMaterial()
    {
        foreach (var marker in new[] { "raw prompt", "raw completion", "raw tool output", "chain-of-thought", "private reasoning", "scratchpad", "secret" })
        {
            AssertInvalid(ValidArtifact() with { FileChanges = [ValidChange() with { NormalizedDiff = $"+ leaked {marker}" }] }, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
            AssertInvalid(ValidArtifact() with { EvidenceReferences = [$"evidence contains {marker}"] }, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
            AssertInvalid(ValidArtifact() with { BoundaryMaxims = [$"boundary contains {marker}"] }, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
            AssertInvalid(ValidArtifact() with { Boundary = $"boundary contains {marker}" }, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
        }
    }

    [TestMethod]
    public void PatchArtifactContract_RejectsAuthorityClaims()
    {
        foreach (var marker in new[] { "source applied", "workflow continued", "release ready", "rollback executed" })
        {
            AssertInvalid(ValidArtifact() with { FileChanges = [ValidChange() with { NormalizedDiff = $"+ claims {marker}" }] }, "AUTHORITY_CLAIM_REJECTED");
            AssertInvalid(ValidArtifact() with { EvidenceReferences = [$"evidence claims {marker}"] }, "AUTHORITY_CLAIM_REJECTED");
            AssertInvalid(ValidArtifact() with { BoundaryMaxims = [$"maxim claims {marker}"] }, "AUTHORITY_CLAIM_REJECTED");
        }
    }

    [TestMethod]
    public void PatchArtifactContract_BoundaryStatesPatchArtifactIsNotSourceApply()
    {
        foreach (var statement in BoundaryStatements())
        {
            StringAssert.Contains(PatchArtifactBoundaryText.Boundary, statement);
        }
    }

    [TestMethod]
    public void PatchArtifactContract_DoesNotExposeSourceApplyAuthority()
    {
        var properties = typeof(PatchArtifact)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        foreach (var forbidden in new[] { "CanApplySource", "SourceApplyApproved", "AppliedAtUtc", "AppliedBy", "ApplyResult", "ReleaseReady", "CanApproveRelease", "CanContinueWorkflow" })
        {
            Assert.IsFalse(properties.Contains(forbidden, StringComparer.Ordinal), $"PatchArtifact must not expose {forbidden}.");
        }
    }

    [TestMethod]
    public void PatchArtifactContract_DoesNotAddPatchCreationOrPersistence()
    {
        foreach (var token in new[] { "CreatePatchArtifactAsync", "PatchArtifactStore", "SqlPatchArtifact", "migrate_patch_artifact" })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void PatchArtifactContract_DoesNotApplySourceOrContinueWorkflow()
    {
        foreach (var token in new[] { "ApplySourceAsync", "SourceApplyService", "ControlledSourceApply", "ContinueWorkflowAsync", "ApproveReleaseAsync" })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void PatchArtifactContract_DoesNotAddSqlApiCliUiRunner()
    {
        foreach (var file in Pr187ChangedFiles())
        {
            var relative = Path.GetRelativePath(RepoRoot(), file);
            foreach (var token in new[] { "Database", "Controller", "Program.cs", "Cli", "Tauri", "UI", "Executor", "Runner" })
            {
                Assert.IsFalse(relative.Contains(token, StringComparison.OrdinalIgnoreCase), $"PR187 must not add {token}: {relative}");
            }
        }
    }

    [TestMethod]
    public void PatchArtifactContract_DoesNotCallModelsAgentsMemoryRetrieval()
    {
        foreach (var token in new[] { "LLM", "model call", "AgentDispatch", "ToolExecution", "PromoteMemory", "ActivateRetrieval" })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void PatchArtifactContract_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(ReceiptPath());

        foreach (var statement in new[]
        {
            "PR187 adds the Patch Artifact contract.",
            "This PR is contract/test/receipt only.",
            "This PR defines the patch artifact shape.",
            "This PR does not create patch artifacts.",
            "This PR does not persist patch artifacts.",
            "This PR does not read patch artifacts.",
            "This PR does not apply source.",
            "This PR does not execute rollback.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "This PR does not add SQL.",
            "This PR does not add API.",
            "This PR does not add CLI.",
            "This PR does not add UI.",
            "Patch artifact binds dry-run receipt hash, dry-run audit hash, policy satisfaction hash, subject hash, source baseline hash, workspace boundary hash, validation plan hash, patch hash, change set hash, evidence references, and boundary maxims.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next Block S target is Patch Artifact Store.",
            "PR188 - Patch Artifact Store",
            "PR187 defines the package. It does not ship or apply it."
        })
        {
            StringAssert.Contains(receipt, statement);
        }

        foreach (var statement in BoundaryStatements())
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private static PatchArtifact ValidArtifact() => new()
    {
        PatchArtifactId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        ProjectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        PatchArtifactKind = "unified-diff-package",
        ControlledDryRunRequestId = Guid.Parse("22222222-3333-4444-5555-666666666666"),
        DryRunExecutionAuditId = Guid.Parse("33333333-4444-5555-6666-777777777777"),
        DryRunAuditHash = "sha256:dry-run-audit",
        DryRunReceiptHash = "sha256:dry-run-receipt",
        PolicySatisfactionId = Guid.Parse("99999999-8888-7777-6666-555555555555"),
        PolicySatisfactionHash = "sha256:policy-satisfaction",
        SubjectKind = "PatchProposal",
        SubjectId = "patch-proposal-123",
        SubjectHash = "sha256:subject",
        SourceSnapshotReference = "source-snapshot:abc123",
        SourceBaselineHash = "sha256:source-baseline",
        WorkspaceBoundaryHash = "sha256:workspace-boundary",
        ValidationPlanId = "validation-plan-123",
        ValidationPlanHash = "sha256:validation-plan",
        PatchHash = "sha256:patch",
        ChangeSetHash = "sha256:change-set",
        FileChanges = [ValidChange()],
        CreatedAtUtc = CreatedAtUtc,
        ExpiresAtUtc = CreatedAtUtc.AddHours(1),
        EvidenceReferences = ["dry-run-receipt:33333333-4444-5555-6666-777777777777"],
        BoundaryMaxims = ["patch artifact is evidence only"],
        Boundary = PatchArtifactBoundaryText.Boundary
    };

    private static PatchArtifactFileChange ValidChange() => new()
    {
        Path = "src/IronDev.Core/Foo.cs",
        PreviousPath = null,
        ChangeKind = "Modify",
        BeforeContentHash = "sha256:before",
        AfterContentHash = "sha256:after",
        DiffHash = "sha256:diff",
        NormalizedDiff = "--- a/src/IronDev.Core/Foo.cs\n+++ b/src/IronDev.Core/Foo.cs\n+safe change",
        IsBinary = false
    };

    private static string[] BoundaryStatements() =>
    [
        "Patch artifact is not source apply.",
        "Patch artifact is not rollback.",
        "Patch artifact is not workflow continuation.",
        "Patch artifact is not release readiness.",
        "Patch artifact does not authorize source mutation by itself.",
        "Patch artifact is a proposed change package only.",
        "Patch artifact must be reviewed before source apply.",
        "Patch artifact must remain bound to its dry-run receipt and source baseline."
    ];

    private static void AssertValid(PatchArtifact artifact)
    {
        var result = PatchArtifactValidation.Validate(artifact);
        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}:{issue.Field}:{issue.Message}")));
    }

    private static void AssertInvalid(PatchArtifact artifact, string expectedCode)
    {
        var result = PatchArtifactValidation.Validate(artifact);
        Assert.IsFalse(result.IsValid, "Expected patch artifact to be invalid.");
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedCode), $"Expected issue code {expectedCode}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertHasProperty(Type type, string propertyName) =>
        Assert.IsNotNull(type.GetProperty(propertyName), $"Expected property {propertyName}.");

    private static void AssertNoProductionToken(string token)
    {
        foreach (var file in Pr187ProductionFiles())
        {
            Assert.IsFalse(File.ReadAllText(file).Contains(token, StringComparison.Ordinal), $"Unexpected production token {token} in {file}.");
        }
    }

    private static string[] Pr187ProductionFiles() =>
    [
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "PatchArtifact.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "PatchArtifactValidation.cs")
    ];

    private static string[] Pr187ChangedFiles() =>
    [
        .. Pr187ProductionFiles(),
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR187_PATCH_ARTIFACT_CONTRACT.md"),
        Path.Combine(RepoRoot(), "IronDev.IntegrationTests", "Governance", "PatchArtifactContractTests.cs")
    ];

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR187_PATCH_ARTIFACT_CONTRACT.md");

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
