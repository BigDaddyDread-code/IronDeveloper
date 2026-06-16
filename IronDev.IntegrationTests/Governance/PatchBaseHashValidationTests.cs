using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("PatchBaseHashValidation")]
public sealed class PatchBaseHashValidationTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 6, 16, 16, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void PatchBaseHashValidation_AcceptsValidArtifactAndContext()
    {
        var context = ValidContext();

        var result = PatchBaseHashValidation.Validate(context);

        Assert.IsTrue(result.IsValid, IssueText(result));
        Assert.AreEqual(context.PatchArtifact.ChangeSetHash, result.ComputedChangeSetHash);
        Assert.AreEqual(context.PatchArtifact.PatchHash, result.ComputedPatchHash);
    }

    [TestMethod]
    public void PatchBaseHashValidation_RejectsInvalidPatchArtifactShape()
    {
        var artifact = ValidArtifact() with { PatchArtifactId = Guid.Empty };
        var context = ValidContext(artifact);

        AssertInvalid(context, "PATCH_ARTIFACT_INVALID");
    }

    [TestMethod]
    public void PatchBaseHashValidation_RejectsProjectMismatch()
    {
        var context = ValidContext() with { ProjectId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff") };

        AssertInvalid(context, "PROJECT_ID_MISMATCH");
    }

    [TestMethod]
    public void PatchBaseHashValidation_RejectsDryRunRequestMismatch()
    {
        var context = ValidContext() with { ControlledDryRunRequestId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444") };

        AssertInvalid(context, "CONTROLLED_DRY_RUN_REQUEST_ID_MISMATCH");
    }

    [TestMethod]
    public void PatchBaseHashValidation_RejectsDryRunAuditIdMismatch()
    {
        var context = ValidContext() with { DryRunExecutionAuditId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444") };

        AssertInvalid(context, "DRY_RUN_EXECUTION_AUDIT_ID_MISMATCH");
    }

    [TestMethod]
    public void PatchBaseHashValidation_RejectsDryRunAuditHashMismatch()
    {
        var context = ValidContext() with { DryRunAuditHash = "sha256:different-dry-run-audit" };

        AssertInvalid(context, "DRY_RUN_AUDIT_HASH_MISMATCH");
    }

    [TestMethod]
    public void PatchBaseHashValidation_RejectsDryRunReceiptHashMismatch()
    {
        var context = ValidContext() with { DryRunReceiptHash = "sha256:different-dry-run-receipt" };

        AssertInvalid(context, "DRY_RUN_RECEIPT_HASH_MISMATCH");
    }

    [TestMethod]
    public void PatchBaseHashValidation_RejectsPolicySatisfactionMismatch()
    {
        var context = ValidContext() with
        {
            PolicySatisfactionId = Guid.Parse("cccccccc-1111-2222-3333-444444444444"),
            PolicySatisfactionHash = "sha256:different-policy-satisfaction"
        };

        var result = PatchBaseHashValidation.Validate(context);

        Assert.IsFalse(result.IsValid);
        AssertContainsIssue(result, "POLICY_SATISFACTION_ID_MISMATCH");
        AssertContainsIssue(result, "POLICY_SATISFACTION_HASH_MISMATCH");
    }

    [TestMethod]
    public void PatchBaseHashValidation_RejectsSubjectMismatch()
    {
        var context = ValidContext() with
        {
            SubjectKind = "DifferentSubject",
            SubjectId = "different-subject-id",
            SubjectHash = "sha256:different-subject-hash"
        };

        var result = PatchBaseHashValidation.Validate(context);

        Assert.IsFalse(result.IsValid);
        AssertContainsIssue(result, "SUBJECT_KIND_MISMATCH");
        AssertContainsIssue(result, "SUBJECT_ID_MISMATCH");
        AssertContainsIssue(result, "SUBJECT_HASH_MISMATCH");
    }

    [TestMethod]
    public void PatchBaseHashValidation_RejectsSourceSnapshotMismatch()
    {
        var context = ValidContext() with { SourceSnapshotReference = "source-snapshot:different" };

        AssertInvalid(context, "SOURCE_SNAPSHOT_REFERENCE_MISMATCH");
    }

    [TestMethod]
    public void PatchBaseHashValidation_RejectsSourceBaselineHashMismatch()
    {
        var context = ValidContext() with { SourceBaselineHash = "sha256:different-source-baseline" };

        AssertInvalid(context, "SOURCE_BASELINE_HASH_MISMATCH");
    }

    [TestMethod]
    public void PatchBaseHashValidation_RejectsWorkspaceBoundaryHashMismatch()
    {
        var context = ValidContext() with { WorkspaceBoundaryHash = "sha256:different-workspace-boundary" };

        AssertInvalid(context, "WORKSPACE_BOUNDARY_HASH_MISMATCH");
    }

    [TestMethod]
    public void PatchBaseHashValidation_RejectsValidationPlanMismatch()
    {
        var context = ValidContext() with
        {
            ValidationPlanId = "different-validation-plan",
            ValidationPlanHash = "sha256:different-validation-plan"
        };

        var result = PatchBaseHashValidation.Validate(context);

        Assert.IsFalse(result.IsValid);
        AssertContainsIssue(result, "VALIDATION_PLAN_ID_MISMATCH");
        AssertContainsIssue(result, "VALIDATION_PLAN_HASH_MISMATCH");
    }

    [TestMethod]
    public void PatchBaseHashValidation_ComputesDeterministicChangeSetHash()
    {
        var change = ValidChange("src/IronDev.Core/Foo.cs");
        var hash1 = PatchArtifactHashing.ComputeChangeSetHash(new[] { change });
        var hash2 = PatchArtifactHashing.ComputeChangeSetHash(new[] { change });
        var changedDiff = PatchArtifactHashing.ComputeChangeSetHash(new[] { change with { NormalizedDiff = change.NormalizedDiff + "\n+another safe line" } });
        var changedAfter = PatchArtifactHashing.ComputeChangeSetHash(new[] { change with { AfterContentHash = "sha256:changed-after" } });
        var changedPath = PatchArtifactHashing.ComputeChangeSetHash(new[] { change with { Path = "src/IronDev.Core/Bar.cs" } });

        Assert.AreEqual(hash1, hash2);
        Assert.AreNotEqual(hash1, changedDiff);
        Assert.AreNotEqual(hash1, changedAfter);
        Assert.AreNotEqual(hash1, changedPath);
    }

    [TestMethod]
    public void PatchBaseHashValidation_ChangeSetHashUsesCanonicalOrdering()
    {
        var first = ValidChange("src/IronDev.Core/A.cs");
        var second = ValidChange("src/IronDev.Core/B.cs");

        var hash1 = PatchArtifactHashing.ComputeChangeSetHash(new[] { second, first });
        var hash2 = PatchArtifactHashing.ComputeChangeSetHash(new[] { first, second });

        Assert.AreEqual(hash1, hash2);
    }

    [TestMethod]
    public void PatchBaseHashValidation_ComputesDeterministicPatchHash()
    {
        var artifact = ValidArtifact();
        var sameArtifact = WithComputedHashes(artifact with { PatchHash = "sha256:placeholder", ChangeSetHash = "sha256:placeholder" });
        var sourceChanged = WithComputedHashes(artifact with { SourceBaselineHash = "sha256:changed-source-baseline" });
        var receiptChanged = WithComputedHashes(artifact with { DryRunReceiptHash = "sha256:changed-dry-run-receipt" });
        var boundaryChanged = WithComputedHashes(artifact with { Boundary = PatchArtifactBoundaryText.Boundary + "\nAdditional safe boundary." });

        Assert.AreEqual(artifact.PatchHash, sameArtifact.PatchHash);
        Assert.AreNotEqual(artifact.PatchHash, sourceChanged.PatchHash);
        Assert.AreNotEqual(artifact.PatchHash, receiptChanged.PatchHash);
        Assert.AreNotEqual(artifact.PatchHash, boundaryChanged.PatchHash);
    }

    [TestMethod]
    public void PatchBaseHashValidation_RejectsChangeSetHashMismatch()
    {
        var artifact = ValidArtifact() with { ChangeSetHash = "sha256:wrong-change-set" };
        var context = ValidContext(artifact);

        AssertInvalid(context, "CHANGE_SET_HASH_MISMATCH");
    }

    [TestMethod]
    public void PatchBaseHashValidation_RejectsPatchHashMismatch()
    {
        var artifact = ValidArtifact() with { PatchHash = "sha256:wrong-patch" };
        var context = ValidContext(artifact);

        AssertInvalid(context, "PATCH_HASH_MISMATCH");
    }

    [TestMethod]
    public void PatchBaseHashValidation_RejectsPrivateRawMaterial()
    {
        var context = ValidContext() with { SourceSnapshotReference = "raw prompt leaked" };

        AssertInvalid(context, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
    }

    [TestMethod]
    public void PatchBaseHashValidation_RejectsAuthorityClaims()
    {
        var context = ValidContext() with { ValidationPlanId = "release approved" };

        AssertInvalid(context, "AUTHORITY_CLAIM_REJECTED");
    }

    [TestMethod]
    public void PatchBaseHashValidation_BoundaryStatesValidationIsNotAuthority()
    {
        var boundary = PatchBaseHashValidationBoundaryText.Boundary;

        StringAssert.Contains(boundary, "Patch base/hash validation is not patch artifact creation.");
        StringAssert.Contains(boundary, "Patch base/hash validation is not source apply.");
        StringAssert.Contains(boundary, "Patch base/hash validation is not rollback.");
        StringAssert.Contains(boundary, "Patch base/hash validation is not workflow continuation.");
        StringAssert.Contains(boundary, "Patch base/hash validation is not release readiness.");
        StringAssert.Contains(boundary, "Patch base/hash validation does not authorize source mutation by itself.");
        StringAssert.Contains(boundary, "Patch base/hash validation only verifies artifact binding and hashes.");
    }

    [TestMethod]
    public void PatchBaseHashValidation_DoesNotCreatePatchArtifact()
    {
        var validatorMethods = typeof(PatchBaseHashValidation).GetMethods().Select(method => method.Name).ToArray();
        var hashingMethods = typeof(PatchArtifactHashing).GetMethods().Select(method => method.Name).ToArray();

        CollectionAssert.Contains(validatorMethods, "Validate");
        CollectionAssert.DoesNotContain(validatorMethods, "Create");
        CollectionAssert.DoesNotContain(validatorMethods, "CreateAsync");
        CollectionAssert.DoesNotContain(hashingMethods, "Create");
        CollectionAssert.DoesNotContain(hashingMethods, "CreateAsync");
    }

    [TestMethod]
    public void PatchBaseHashValidation_DoesNotPersistOrReadPatchArtifact()
    {
        AssertNoProductionToken("IPatchArtifactStore");
        AssertNoProductionToken("SqlPatchArtifactStore");
        AssertNoProductionToken("CreateAsync");
        AssertNoProductionToken("SaveAsync");
        AssertNoProductionToken("GetAsync");
        AssertNoProductionToken("ListBy");
    }

    [TestMethod]
    public void PatchBaseHashValidation_DoesNotApplySourceOrContinueWorkflow()
    {
        AssertNoProductionToken("ApplyAsync");
        AssertNoProductionToken("ExecuteRollback");
        AssertNoProductionToken("ContinueAsync");
        AssertNoProductionToken("StartWorkflow");
        AssertNoProductionToken("RunWorkflow");
        AssertNoProductionToken("DispatchAsync");
    }

    [TestMethod]
    public void PatchBaseHashValidation_DoesNotAddSqlApiCliUi()
    {
        var changedFiles = Pr189ChangedFiles();

        Assert.IsFalse(changedFiles.Any(path => path.StartsWith("Database/", StringComparison.OrdinalIgnoreCase)), string.Join(Environment.NewLine, changedFiles));
        Assert.IsFalse(changedFiles.Any(path => path.Contains("Controller", StringComparison.OrdinalIgnoreCase)), string.Join(Environment.NewLine, changedFiles));
        Assert.IsFalse(changedFiles.Any(path => path.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase)), string.Join(Environment.NewLine, changedFiles));
        Assert.IsFalse(changedFiles.Any(path => path.Contains("Cli", StringComparison.OrdinalIgnoreCase)), string.Join(Environment.NewLine, changedFiles));
        Assert.IsFalse(changedFiles.Any(path => path.Contains("Tauri", StringComparison.OrdinalIgnoreCase)), string.Join(Environment.NewLine, changedFiles));
        Assert.IsFalse(changedFiles.Any(path => path.Contains("UI", StringComparison.OrdinalIgnoreCase)), string.Join(Environment.NewLine, changedFiles));
    }

    [TestMethod]
    public void PatchBaseHashValidation_DoesNotCallModelsAgentsMemoryRetrieval()
    {
        AssertNoProductionToken("IAgent");
        AssertNoProductionToken("IModel");
        AssertNoProductionToken("MemoryPromotion");
        AssertNoProductionToken("Retrieval");
        AssertNoProductionToken("Vector");
        AssertNoProductionToken("Embedding");
        AssertNoProductionToken("Weaviate");
    }

    [TestMethod]
    public void PatchBaseHashValidation_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "PR189_PATCH_BASE_HASH_VALIDATION.md"));

        StringAssert.Contains(receipt, "PR189 adds Patch Base/Hash Validation.");
        StringAssert.Contains(receipt, "This PR validates supplied PatchArtifact binding and deterministic hashes.");
        StringAssert.Contains(receipt, "This PR does not create patch artifacts.");
        StringAssert.Contains(receipt, "This PR does not persist patch artifacts.");
        StringAssert.Contains(receipt, "This PR does not read patch artifacts.");
        StringAssert.Contains(receipt, "This PR does not apply source.");
        StringAssert.Contains(receipt, "This PR does not execute rollback.");
        StringAssert.Contains(receipt, "This PR does not continue workflow.");
        StringAssert.Contains(receipt, "This PR does not approve release.");
        StringAssert.Contains(receipt, "This PR does not add SQL.");
        StringAssert.Contains(receipt, "This PR does not add API.");
        StringAssert.Contains(receipt, "This PR does not add CLI.");
        StringAssert.Contains(receipt, "This PR does not add UI.");
        StringAssert.Contains(receipt, "Patch base/hash validation checks dry-run receipt hash, dry-run audit hash, policy satisfaction hash, subject hash, source baseline hash, workspace boundary hash, validation plan hash, change-set hash, and patch hash.");
        StringAssert.Contains(receipt, "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate");
        StringAssert.Contains(receipt, "The next Block S target is Patch Artifact Creator.");
        StringAssert.Contains(receipt, "PR190 - Patch Artifact Creator");
        StringAssert.Contains(receipt, "PR189 checks the package seal. It does not open or apply it.");
    }

    private static PatchArtifact ValidArtifact(string suffix = "main")
    {
        var artifact = new PatchArtifact
        {
            PatchArtifactId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ProjectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            PatchArtifactKind = "unified-diff-package",
            ControlledDryRunRequestId = Guid.Parse("22222222-3333-4444-5555-666666666666"),
            DryRunExecutionAuditId = Guid.Parse("33333333-4444-5555-6666-777777777777"),
            DryRunAuditHash = $"sha256:dry-run-audit-{suffix}",
            DryRunReceiptHash = $"sha256:dry-run-receipt-{suffix}",
            PolicySatisfactionId = Guid.Parse("99999999-8888-7777-6666-555555555555"),
            PolicySatisfactionHash = $"sha256:policy-satisfaction-{suffix}",
            SubjectKind = "PatchProposal",
            SubjectId = $"patch-proposal-{suffix}",
            SubjectHash = $"sha256:subject-{suffix}",
            SourceSnapshotReference = $"source-snapshot:{suffix}",
            SourceBaselineHash = $"sha256:source-baseline-{suffix}",
            WorkspaceBoundaryHash = $"sha256:workspace-boundary-{suffix}",
            ValidationPlanId = $"validation-plan-{suffix}",
            ValidationPlanHash = $"sha256:validation-plan-{suffix}",
            ChangeSetHash = "sha256:placeholder-change-set",
            PatchHash = "sha256:placeholder-patch",
            FileChanges = new List<PatchArtifactFileChange> { ValidChange("src/IronDev.Core/Foo.cs") },
            CreatedAtUtc = CreatedAtUtc,
            ExpiresAtUtc = CreatedAtUtc.AddHours(1),
            EvidenceReferences = new List<string> { $"dry-run-receipt:{suffix}" },
            BoundaryMaxims = new List<string> { "Patch base/hash validation only verifies artifact binding and hashes." },
            Boundary = PatchArtifactBoundaryText.Boundary
        };

        return WithComputedHashes(artifact);
    }

    private static PatchArtifact WithComputedHashes(PatchArtifact artifact)
    {
        var changeSetHash = PatchArtifactHashing.ComputeChangeSetHash(artifact.FileChanges);
        var patchHash = PatchArtifactHashing.ComputePatchHash(artifact with { ChangeSetHash = changeSetHash }, changeSetHash);
        return artifact with { ChangeSetHash = changeSetHash, PatchHash = patchHash };
    }

    private static PatchArtifactFileChange ValidChange(string path)
    {
        return new PatchArtifactFileChange
        {
            Path = path,
            PreviousPath = null,
            ChangeKind = "Modify",
            BeforeContentHash = $"sha256:before-{path}",
            AfterContentHash = $"sha256:after-{path}",
            DiffHash = $"sha256:diff-{path}",
            NormalizedDiff = $"--- a/{path}\n+++ b/{path}\n+safe change",
            IsBinary = false
        };
    }

    private static PatchBaseHashValidationContext ValidContext(PatchArtifact? artifact = null)
    {
        artifact ??= ValidArtifact();

        return new PatchBaseHashValidationContext
        {
            PatchArtifact = artifact,
            ProjectId = artifact.ProjectId,
            ControlledDryRunRequestId = artifact.ControlledDryRunRequestId,
            DryRunExecutionAuditId = artifact.DryRunExecutionAuditId,
            DryRunAuditHash = artifact.DryRunAuditHash,
            DryRunReceiptHash = artifact.DryRunReceiptHash,
            PolicySatisfactionId = artifact.PolicySatisfactionId,
            PolicySatisfactionHash = artifact.PolicySatisfactionHash,
            SubjectKind = artifact.SubjectKind,
            SubjectId = artifact.SubjectId,
            SubjectHash = artifact.SubjectHash,
            SourceSnapshotReference = artifact.SourceSnapshotReference,
            SourceBaselineHash = artifact.SourceBaselineHash,
            WorkspaceBoundaryHash = artifact.WorkspaceBoundaryHash,
            ValidationPlanId = artifact.ValidationPlanId,
            ValidationPlanHash = artifact.ValidationPlanHash,
            EvidenceReferences = artifact.EvidenceReferences,
            BoundaryMaxims = artifact.BoundaryMaxims
        };
    }

    private static void AssertInvalid(PatchBaseHashValidationContext context, string expectedCode)
    {
        var result = PatchBaseHashValidation.Validate(context);

        Assert.IsFalse(result.IsValid, "Expected invalid result.");
        AssertContainsIssue(result, expectedCode);
    }

    private static void AssertContainsIssue(PatchBaseHashValidationResult result, string expectedCode)
    {
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedCode), $"Expected {expectedCode}. Actual: {IssueText(result)}");
    }

    private static string IssueText(PatchBaseHashValidationResult result)
    {
        return string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}:{issue.Field}:{issue.Message}"));
    }

    private static void AssertNoProductionToken(string token)
    {
        foreach (var path in Pr189ProductionFiles())
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), path.Replace('/', Path.DirectorySeparatorChar)));
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Unexpected token {token} in {path}");
        }
    }

    private static string[] Pr189ProductionFiles()
    {
        return new[]
        {
            "IronDev.Core/Governance/PatchArtifactHashing.cs",
            "IronDev.Core/Governance/PatchBaseHashValidation.cs",
            "IronDev.Core/Governance/PatchBaseHashValidationModels.cs"
        };
    }

    private static string[] Pr189ChangedFiles()
    {
        return new[]
        {
            "IronDev.Core/Governance/PatchArtifactHashing.cs",
            "IronDev.Core/Governance/PatchBaseHashValidation.cs",
            "IronDev.Core/Governance/PatchBaseHashValidationModels.cs",
            "Docs/receipts/PR189_PATCH_BASE_HASH_VALIDATION.md",
            "IronDev.IntegrationTests/Governance/PatchBaseHashValidationTests.cs"
        };
    }

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

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}



