using System.Reflection;
using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("PatchArtifactRegression")]
public sealed class PatchArtifactRegressionTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 6, 16, 18, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void PatchArtifactRegression_ReceiptPinsRegressionOnlyBoundary()
    {
        var receipt = ReadRepoText("Docs/receipts/PR192_PATCH_ARTIFACT_REGRESSION_TESTS.md");

        foreach (var statement in new[]
        {
            "PR192 adds Patch Artifact Regression Tests.",
            "This PR is test/receipt only.",
            "This PR does not add production code.",
            "This PR does not add SQL.",
            "This PR does not add API.",
            "This PR does not add CLI.",
            "This PR does not add UI.",
            "This PR does not create patch artifacts.",
            "This PR does not persist patch artifacts.",
            "This PR does not apply source.",
            "This PR does not execute rollback.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "PR192 locks the package cage. It does not add the launch button."
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    [TestMethod]
    public void PatchArtifactRegression_ReceiptChainPreservesPr187ThroughPr191Boundaries()
    {
        AssertReceiptContains("Docs/receipts/PR187_PATCH_ARTIFACT_CONTRACT.md", [
            "PR187 adds the Patch Artifact contract.",
            "This PR does not create patch artifacts.",
            "This PR does not persist patch artifacts.",
            "This PR does not apply source.",
            "Patch artifact is a proposed change package only.",
            "PR187 defines the package. It does not ship or apply it."
        ]);

        AssertReceiptContains("Docs/receipts/PR188_PATCH_ARTIFACT_STORE.md", [
            "PR188 adds the Patch Artifact Store.",
            "This PR persists supplied PatchArtifact records.",
            "This PR does not create patch artifacts.",
            "Persisted patch artifact is not source apply.",
            "Patch artifact storage does not spend dry-run receipts.",
            "PR188 puts the package in the vault. It does not ship or apply it."
        ]);

        AssertReceiptContains("Docs/receipts/PR189_PATCH_BASE_HASH_VALIDATION.md", [
            "PR189 adds Patch Base/Hash Validation.",
            "This PR validates supplied PatchArtifact binding and deterministic hashes.",
            "This PR does not create patch artifacts.",
            "This PR does not persist patch artifacts.",
            "Patch base/hash validation only verifies artifact binding and hashes.",
            "PR189 checks the package seal. It does not open or apply it."
        ]);

        AssertReceiptContains("Docs/receipts/PR190_PATCH_ARTIFACT_READ_API.md", [
            "PR190 adds the Patch Artifact Read API.",
            "This PR exposes project-scoped read-only endpoints for persisted PatchArtifact records.",
            "Patch artifact read API is read-only.",
            "Reading a patch artifact does not authorize source mutation.",
            "PR190 opens the package window. It does not ship or apply it."
        ]);

        AssertReceiptContains("Docs/receipts/PR191_PATCH_ARTIFACT_CREATION_INTEGRATION.md", [
            "PR191 adds Patch Artifact Creation Integration.",
            "This PR creates PatchArtifact records from existing dry-run evidence and supplied file-change data.",
            "This PR does not apply source.",
            "Failed dry-run receipts may be evidence, but failed dry-run receipts must not create patch artifacts.",
            "Patch artifact creation validates with PatchArtifactValidation and PatchBaseHashValidation before storage.",
            "PR191 builds the package. It does not ship or apply it."
        ]);
    }

    [TestMethod]
    public void PatchArtifactRegression_ContractStillRejectsPrivateRawMaterialAndAuthorityClaims()
    {
        foreach (var marker in new[] { "raw prompt", "raw completion", "raw tool output", "chain-of-thought", "private reasoning", "scratchpad", "secret" })
        {
            AssertPatchArtifactInvalid(
                ValidArtifact() with { FileChanges = [ValidChange("src/raw.cs") with { NormalizedDiff = $"+ leaked {marker}" }] },
                "PRIVATE_OR_RAW_MATERIAL_REJECTED");
        }

        foreach (var marker in new[] { "source applied", "workflow continued", "release ready", "rollback executed" })
        {
            AssertPatchArtifactInvalid(
                ValidArtifact() with { EvidenceReferences = [$"evidence claims {marker}"] },
                "AUTHORITY_CLAIM_REJECTED");
        }
    }

    [TestMethod]
    public void PatchArtifactRegression_BaseHashValidationStillRejectsPatchAndChangeSetMismatch()
    {
        AssertBaseHashInvalid(ValidContext(ValidArtifact() with { PatchHash = "sha256:wrong-patch" }), "PATCH_HASH_MISMATCH");
        AssertBaseHashInvalid(ValidContext(ValidArtifact() with { ChangeSetHash = "sha256:wrong-change-set" }), "CHANGE_SET_HASH_MISMATCH");
    }

    [TestMethod]
    public void PatchArtifactRegression_BaseHashValidationStillRejectsRawMaterialAndAuthorityClaims()
    {
        AssertBaseHashInvalid(ValidContext() with { SourceSnapshotReference = "raw prompt leaked" }, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
        AssertBaseHashInvalid(ValidContext() with { ValidationPlanId = "release approved" }, "AUTHORITY_CLAIM_REJECTED");
    }

    [TestMethod]
    public void PatchArtifactRegression_StoreRegressionTestsPinAppendOnlyAndDirectSqlBypassCoverage()
    {
        var tests = ReadRepoText("IronDev.IntegrationTests/Governance/PatchArtifactStoreTests.cs");

        foreach (var testName in new[]
        {
            "PatchArtifactStore_RejectsDuplicatePatchArtifactId",
            "PatchArtifactStore_RejectsDuplicatePatchHashWithinProject",
            "PatchArtifactStore_DoesNotExposeUpdateOrDelete",
            "PatchArtifactStore_BlocksUnsafeDirectSqlMaterialAndMutation",
            "PatchArtifactStore_MigrationAndInventoryAreRegistered"
        })
        {
            StringAssert.Contains(tests, testName);
        }

        StringAssert.Contains(tests, "TR_PatchArtifact_BlockUpdateDelete");
        StringAssert.Contains(tests, "TR_PatchArtifact_ValidateInsert");
        StringAssert.Contains(tests, "raw prompt leaked");
        StringAssert.Contains(tests, "source applied");
    }

    [TestMethod]
    public void PatchArtifactRegression_CreationIntegrationStillRequiresSuccessfulDryRunAndComputesHashes()
    {
        var tests = ReadRepoText("IronDev.IntegrationTests/Governance/PatchArtifactCreationIntegrationTests.cs");
        var creator = ReadRepoText("IronDev.Infrastructure/Governance/PatchArtifactCreator.cs");

        foreach (var token in new[]
        {
            "PatchArtifactCreation_RejectsFailedDryRun",
            "PatchArtifactCreation_DoesNotAcceptCallerSuppliedPatchHash",
            "PatchArtifactCreation_UsesPatchBaseHashValidationBeforeSave",
            "PatchArtifactCreation_DoesNotApplySourceRollbackWorkflowRelease",
            "PatchArtifactCreation_DoesNotRunDryRunOrCreateWorkspace"
        })
        {
            StringAssert.Contains(tests, token);
        }

        StringAssert.Contains(creator, "PatchArtifactHashing.ComputeChangeSetHash");
        StringAssert.Contains(creator, "PatchArtifactHashing.ComputePatchHash");
        StringAssert.Contains(creator, "PatchArtifactValidation.Validate");
        StringAssert.Contains(creator, "PatchBaseHashValidation.Validate");
    }

    [TestMethod]
    public void PatchArtifactRegression_ReadApiRemainsGetOnlyAndReadOnly()
    {
        var methods = typeof(PatchArtifactsV1Controller).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var httpMethods = methods
            .SelectMany(method => method.GetCustomAttributes(inherit: false).OfType<HttpMethodAttribute>())
            .ToArray();

        Assert.AreEqual(7, httpMethods.Count(attribute => attribute.HttpMethods.Contains("GET")));
        Assert.IsFalse(httpMethods.Any(attribute => attribute.HttpMethods.Any(method => method is "POST" or "PUT" or "PATCH" or "DELETE")));

        var controller = ReadRepoText("IronDev.Api/Controllers/PatchArtifactsV1Controller.cs");
        StringAssert.Contains(controller, "MutationOccurred = false");
        StringAssert.Contains(controller, "HumanApprovalRequired = true");
        StringAssert.Contains(controller, "PatchArtifactReadBoundaryText.Warnings");
    }

    [TestMethod]
    public void PatchArtifactRegression_StaticScanFindsNoLaunchButtonTokensInPatchArtifactSurface()
    {
        foreach (var path in PatchArtifactSurfaceFiles())
        {
            var text = ReadRepoText(path);
            foreach (var token in new[]
            {
                "ApplySourceAsync(",
                "SourceApplyService",
                "ControlledSourceApply",
                "ExecuteRollback",
                "ContinueWorkflowAsync",
                "ApproveReleaseAsync",
                "CanApplySource = true",
                "SourceApplyApproved = true",
                "ReleaseReady = true",
                "DispatchAgent",
                "InvokeToolAsync",
                "RunToolAsync",
                "PromoteMemoryAsync",
                "ActivateRetrievalAsync",
                "CallModelAsync"
            })
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Unexpected launch-button token {token} in {path}.");
            }
        }
    }

    [TestMethod]
    public void PatchArtifactRegression_Pr192ChangedFilesStayTestAndReceiptOnly()
    {
        var changedFiles = Pr192ChangedFiles();

        CollectionAssert.AreEquivalent(new[]
        {
            "Docs/receipts/PR192_PATCH_ARTIFACT_REGRESSION_TESTS.md",
            "IronDev.IntegrationTests/Governance/PatchArtifactRegressionTests.cs",
            "IronDev.IntegrationTests.Api/PatchArtifactApiRegressionTests.cs"
        }, changedFiles);

        foreach (var file in changedFiles)
        {
            Assert.IsFalse(file.StartsWith("Database/", StringComparison.OrdinalIgnoreCase), file);
            Assert.IsFalse(file.Contains("Controller", StringComparison.OrdinalIgnoreCase), file);
            Assert.IsFalse(file.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase), file);
            Assert.IsFalse(file.Contains("Cli", StringComparison.OrdinalIgnoreCase), file);
            Assert.IsFalse(file.Contains("Tauri", StringComparison.OrdinalIgnoreCase), file);
            Assert.IsFalse(file.Contains("UI", StringComparison.OrdinalIgnoreCase), file);
        }
    }

    private static PatchArtifact ValidArtifact(string suffix = "main")
    {
        var artifact = new PatchArtifact
        {
            PatchArtifactId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ProjectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            PatchArtifactKind = "UnifiedDiffPackage",
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
            FileChanges = [ValidChange($"src/{suffix}.cs")],
            CreatedAtUtc = CreatedAtUtc,
            ExpiresAtUtc = CreatedAtUtc.AddHours(1),
            EvidenceReferences = [$"controlled-dry-run-receipt:{suffix}"],
            BoundaryMaxims = ["Patch artifact is a proposed change package only."],
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

    private static PatchArtifactFileChange ValidChange(string path) => new()
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

    private static void AssertPatchArtifactInvalid(PatchArtifact artifact, string expectedCode)
    {
        var result = PatchArtifactValidation.Validate(artifact);
        Assert.IsFalse(result.IsValid, "Expected patch artifact validation to fail.");
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedCode), $"Expected {expectedCode}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertBaseHashInvalid(PatchBaseHashValidationContext context, string expectedCode)
    {
        var result = PatchBaseHashValidation.Validate(context);
        Assert.IsFalse(result.IsValid, "Expected base/hash validation to fail.");
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedCode), $"Expected {expectedCode}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertReceiptContains(string relativePath, IReadOnlyList<string> statements)
    {
        var receipt = ReadRepoText(relativePath);
        foreach (var statement in statements)
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private static IReadOnlyList<string> PatchArtifactSurfaceFiles() =>
    [
        "IronDev.Core/Governance/PatchArtifact.cs",
        "IronDev.Core/Governance/PatchArtifactValidation.cs",
        "IronDev.Core/Governance/PatchArtifactHashing.cs",
        "IronDev.Core/Governance/PatchBaseHashValidation.cs",
        "IronDev.Infrastructure/Governance/SqlPatchArtifactStore.cs",
        "IronDev.Infrastructure/Governance/PatchArtifactCreator.cs",
        "IronDev.Infrastructure/Governance/PatchArtifactQueryService.cs",
        "IronDev.Api/Controllers/PatchArtifactsV1Controller.cs"
    ];

    private static string[] Pr192ChangedFiles() =>
    [
        "Docs/receipts/PR192_PATCH_ARTIFACT_REGRESSION_TESTS.md",
        "IronDev.IntegrationTests/Governance/PatchArtifactRegressionTests.cs",
        "IronDev.IntegrationTests.Api/PatchArtifactApiRegressionTests.cs"
    ];

    private static string ReadRepoText(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

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
