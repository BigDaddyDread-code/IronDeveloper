using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBOValidationResultPackageTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public void BlockBO_Builder_RequiresProposalOnlyDisposableWorkspaceValidateEligibility()
    {
        using var fixture = Fixture.Create();
        var result = ValidationResultPackageBuilder.Build(fixture.Request() with { OperationId = string.Empty });

        Assert.IsFalse(result.IsPackageCreated);
        AssertContains(result.Issues, "OperationIdRequired");
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
    }

    [TestMethod]
    public void BlockBO_Builder_RejectsMissingDisposableMarker()
    {
        using var fixture = Fixture.Create(writeMarker: false);
        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "DisposableWorkspaceMarkerRequired");
    }

    [TestMethod]
    public void BlockBO_Builder_RejectsInvalidDisposableMarker()
    {
        using var fixture = Fixture.Create(writeMarker: false);
        Directory.CreateDirectory(Path.Combine(fixture.WorkspacePath, ".irondev"));
        File.WriteAllText(Path.Combine(fixture.WorkspacePath, ".irondev", "disposable-workspace.json"), "{not-json");

        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "DisposableWorkspaceMarkerInvalid");
    }

    [TestMethod]
    public void BlockBO_Builder_RejectsNonDisposableWorkspace()
    {
        using var fixture = Fixture.Create(marker => marker with { Disposable = false });
        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "DisposableWorkspaceMarkerMustBeDisposable");
    }

    [TestMethod]
    public void BlockBO_Builder_RejectsRepoMismatch()
    {
        using var fixture = Fixture.Create(marker => marker with { RepoId = "other/repo" });
        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "DisposableWorkspaceRepoMismatch");
    }

    [TestMethod]
    public void BlockBO_Builder_RejectsBranchMismatch()
    {
        using var fixture = Fixture.Create(marker => marker with { Branch = "feature/wrong" });
        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "DisposableWorkspaceBranchMismatch");
    }

    [TestMethod]
    public void BlockBO_Builder_RejectsWorkspaceNotCreatedForProposalOnly()
    {
        using var fixture = Fixture.Create(marker => marker with { CreatedFor = "full-run" });
        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "DisposableWorkspaceCreatedForProposalOnlyRequired");
    }

    [TestMethod]
    public void BlockBO_Builder_RejectsWorkspacePathEqualToSourceRoot()
    {
        using var fixture = Fixture.Create(workspaceEqualsSource: true);
        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "DisposableWorkspaceCannotEqualSourceRoot");
    }

    [TestMethod]
    public void BlockBO_Builder_RejectsWorkspacePathInsideSourceRoot()
    {
        using var fixture = Fixture.Create(workspaceInsideSource: true);
        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "DisposableWorkspaceCannotBeInsideSourceRoot");
    }

    [TestMethod]
    public void BlockBO_Builder_RejectsOutputPathInsideSourceRoot()
    {
        using var fixture = Fixture.Create(outputInsideSource: true);
        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "ValidationPackageOutputCannotBeInsideSourceRoot");
        Assert.IsFalse(Directory.Exists(fixture.OutputPath));
    }

    [TestMethod]
    public void BlockBO_Builder_RequiresValidationRunId()
    {
        using var fixture = Fixture.Create();
        var result = ValidationResultPackageBuilder.Build(fixture.Request() with { ValidationRunId = string.Empty });

        AssertBlockedWithoutPackage(result, "ValidationRunIdRequired");
    }

    [TestMethod]
    public void BlockBO_Builder_RequiresValidationName()
    {
        using var fixture = Fixture.Create();
        var result = ValidationResultPackageBuilder.Build(fixture.Request() with { ValidationName = string.Empty });

        AssertBlockedWithoutPackage(result, "ValidationNameRequired");
    }

    [TestMethod]
    public void BlockBO_Builder_RequiresProposalId()
    {
        using var fixture = Fixture.Create();
        var result = ValidationResultPackageBuilder.Build(fixture.Request() with { ProposalId = string.Empty });

        AssertBlockedWithoutPackage(result, "ValidationPackageProposalIdRequired");
    }

    [TestMethod]
    public void BlockBO_Builder_RequiresPatchHash()
    {
        using var fixture = Fixture.Create();
        var result = ValidationResultPackageBuilder.Build(fixture.Request() with { PatchHash = string.Empty });

        AssertBlockedWithoutPackage(result, "ValidationPackagePatchHashRequired");
    }

    [TestMethod]
    public void BlockBO_Builder_RequiresAtLeastOneEvidenceFile()
    {
        using var fixture = Fixture.Create();
        var result = ValidationResultPackageBuilder.Build(fixture.Request(evidenceFileNames: []));

        AssertBlockedWithoutPackage(result, "ValidationEvidenceFileRequired");
    }

    [TestMethod]
    public void BlockBO_Builder_RejectsMissingEvidenceFile()
    {
        using var fixture = Fixture.Create();
        var result = ValidationResultPackageBuilder.Build(fixture.Request(evidenceFileNames: ["missing.log"]));

        AssertBlockedWithoutPackage(result, "ValidationEvidenceFileNotFound");
    }

    [TestMethod]
    public void BlockBO_Builder_RejectsEvidenceFileOutsideDisposableWorkspace()
    {
        using var fixture = Fixture.Create();
        File.WriteAllText(Path.Combine(fixture.Root, "outside.log"), "outside");

        var result = ValidationResultPackageBuilder.Build(fixture.Request(evidenceFileNames: ["../outside.log"]));

        AssertBlockedWithoutPackage(result, "ValidationEvidenceFileOutsideWorkspace");
    }

    [TestMethod]
    public void BlockBO_Builder_RejectsEvidenceFileParentTraversalEvenWhenItNormalizesInsideWorkspace()
    {
        using var fixture = Fixture.Create();
        Directory.CreateDirectory(Path.Combine(fixture.WorkspacePath, "logs"));

        var result = ValidationResultPackageBuilder.Build(fixture.Request(evidenceFileNames: ["logs/../validation-output.log"]));

        AssertBlockedWithoutPackage(result, "ValidationEvidenceFileOutsideWorkspace");
    }

    [TestMethod]
    public void BlockBO_Builder_RejectsAbsoluteEvidenceFile()
    {
        using var fixture = Fixture.Create();
        var absolute = Path.Combine(fixture.WorkspacePath, "validation-output.log");
        var result = ValidationResultPackageBuilder.Build(fixture.Request(evidenceFileNames: [absolute]));

        AssertBlockedWithoutPackage(result, "ValidationEvidenceFileOutsideWorkspace");
    }

    [TestMethod]
    public void BlockBO_Builder_RejectsDirectoryEvidenceFile()
    {
        using var fixture = Fixture.Create();
        Directory.CreateDirectory(Path.Combine(fixture.WorkspacePath, "logs"));

        var result = ValidationResultPackageBuilder.Build(fixture.Request(evidenceFileNames: ["logs"]));

        AssertBlockedWithoutPackage(result, "ValidationEvidenceFileMustBeFile");
    }

    [TestMethod]
    public void BlockBO_Builder_CopiesEvidenceFilesIntoPackage()
    {
        using var fixture = Fixture.Create(writeNestedEvidence: true);
        var result = ValidationResultPackageBuilder.Build(fixture.Request(evidenceFileNames: ["validation-output.log", "logs/compiler-output.txt"]));

        AssertPackageCreated(result);
        AssertFileContains(result, Path.Combine("evidence", "validation-output.log"), "validation passed");
        AssertFileContains(result, Path.Combine("evidence", "logs", "compiler-output.txt"), "compiler clean");
    }

    [TestMethod]
    public void BlockBO_Builder_WritesValidationSummary()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertPassedPackage(result);
        AssertFileContains(result, "validation-summary.md", "Validation name: Focused BO");
        AssertFileContains(result, "validation-summary.md", "Validation run id: validation-run-123");
        AssertFileContains(result, "validation-summary.md", "Proposal id: proposal-123");
        AssertFileContains(result, "validation-summary.md", "Patch hash: sha256:patchhash123");
        AssertFileContains(result, "validation-summary.md", "Outcome: Passed");
        AssertFileContains(result, "validation-summary.md", "Validation passed is evidence only.");
        AssertFileContains(result, "validation-summary.md", "Validation passed is not approval.");
        AssertFileContains(result, "validation-summary.md", "Validation passed is not policy satisfaction.");
        AssertFileContains(result, "validation-summary.md", "Validation passed is not source apply authority.");
    }

    [TestMethod]
    public void BlockBO_Builder_WritesValidationEvidence()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertPassedPackage(result);
        AssertFileContains(result, "validation-evidence.md", "Copied evidence files:");
        AssertFileContains(result, "validation-evidence.md", "evidence/validation-output.log");
        AssertFileContains(result, "validation-evidence.md", "Focused validation completed.");
        AssertFileContains(result, "validation-evidence.md", "This file lists supplied evidence. It does not invent validation claims.");
    }

    [TestMethod]
    public void BlockBO_Builder_WritesManifest()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();
        var manifest = ReadManifest(result);

        AssertPassedPackage(result);
        Assert.AreEqual(result.PackageId, manifest.PackageId);
        Assert.AreEqual("validation-run-123", manifest.ValidationRunId);
        Assert.AreEqual("Focused BO", manifest.ValidationName);
        Assert.AreEqual("proposal-123", manifest.ProposalId);
        Assert.AreEqual("sha256:patchhash123", manifest.PatchHash);
        Assert.AreEqual("BigDaddyDread-code/IronDeveloper", manifest.RepoId);
        Assert.AreEqual("main", manifest.Branch);
        Assert.AreEqual("workspace-123", manifest.WorkspaceId);
        Assert.AreEqual(ValidationOutcome.Passed, manifest.Outcome);
        Assert.AreEqual(result.ValidationRef, manifest.ValidationRef);
        AssertContains(manifest.EvidenceFileNames, "validation-output.log");
        AssertContains(manifest.ValidationMessages, "Focused validation completed.");
        AssertContains(manifest.ArtifactRefs, result.ValidationRef);
    }

    [TestMethod]
    public void BlockBO_Builder_WritesOperationStatus()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();
        var status = ReadStatus(result);

        AssertPassedPackage(result);
        Assert.AreEqual("ValidationResultPackage", status.OperationKind);
        Assert.AreEqual(GovernedOperationState.Completed, status.State);
        AssertContains(status.EvidenceRefs, result.ValidationRef);
        AssertContains(status.EvidenceRefs, "validation-outcome:passed");
        AssertContains(status.EvidenceRefs, "proposal:proposal-123");
        AssertContains(status.EvidenceRefs, "patch-hash:sha256:patchhash123");
        AssertContains(status.ReceiptRefs, $"validation-result-package:{result.PackageId}");
    }

    [TestMethod]
    public void BlockBO_PassedValidation_MapsToCompletedValidationResultPackageStatus()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertPassedPackage(result);
        Assert.AreEqual("ValidationResultPackage", result.Status.OperationKind);
        Assert.AreEqual(GovernedOperationState.Completed, result.Status.State);
    }

    [TestMethod]
    public void BlockBO_FailedValidation_MapsToFailedValidationResultPackageStatus()
    {
        using var fixture = Fixture.Create();
        var result = ValidationResultPackageBuilder.Build(fixture.Request(ValidationOutcome.Failed));

        AssertPackageCreated(result);
        Assert.AreEqual(GovernedOperationState.Failed, result.Status.State);
        AssertContains(result.Status.BlockedReasons, "Validation failed.");
        AssertContains(result.Status.EvidenceRefs, "validation-outcome:failed");
        AssertFileContains(result, "validation-summary.md", "Validation failed.");
        AssertFileContains(result, "validation-summary.md", "Patch package must not be treated as ready for source apply.");
    }

    [TestMethod]
    public void BlockBO_InconclusiveValidation_MapsToBlockedValidationResultPackageStatus()
    {
        using var fixture = Fixture.Create();
        var result = ValidationResultPackageBuilder.Build(fixture.Request(ValidationOutcome.Inconclusive));

        AssertPackageCreated(result);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
        AssertContains(result.Status.BlockedReasons, "Validation evidence is inconclusive.");
        AssertContains(result.Status.MissingEvidence, "conclusive-validation-result:validation-run-123");
        AssertContains(result.Status.EvidenceRefs, "validation-outcome:inconclusive");
        AssertFileContains(result, "validation-summary.md", "Validation was inconclusive.");
        AssertFileContains(result, "validation-summary.md", "Additional validation evidence is required before source apply can be requested.");
    }

    [TestMethod]
    public void BlockBO_PassedStatus_IncludesValidationResultRef()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertPassedPackage(result);
        StringAssert.StartsWith(result.ValidationRef, "validation-result:");
        AssertContains(result.Status.EvidenceRefs, result.ValidationRef);
    }

    [TestMethod]
    public void BlockBO_PassedStatus_IncludesValidationOutcomePassed()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertPassedPackage(result);
        AssertContains(result.Status.EvidenceRefs, "validation-outcome:passed");
    }

    [TestMethod]
    public void BlockBO_FailedStatus_IncludesValidationOutcomeFailed()
    {
        using var fixture = Fixture.Create();
        var result = ValidationResultPackageBuilder.Build(fixture.Request(ValidationOutcome.Failed));

        AssertPackageCreated(result);
        AssertContains(result.Status.EvidenceRefs, "validation-outcome:failed");
    }

    [TestMethod]
    public void BlockBO_InconclusiveStatus_IncludesMissingConclusiveValidationEvidence()
    {
        using var fixture = Fixture.Create();
        var result = ValidationResultPackageBuilder.Build(fixture.Request(ValidationOutcome.Inconclusive));

        AssertPackageCreated(result);
        AssertContains(result.Status.MissingEvidence, "conclusive-validation-result:validation-run-123");
        AssertContains(result.Status.NextSafeActions, "collect conclusive validation evidence");
    }

    [TestMethod]
    public void BlockBO_ValidationPass_RemainsEvidenceOnly()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertPassedPackage(result);
        Assert.IsFalse(result.StatusValidation.Boundary.CanApprove);
        Assert.IsFalse(result.StatusValidation.Boundary.CanSatisfyPolicy);
        Assert.IsFalse(result.StatusValidation.Boundary.CanSourceApply);
    }

    [TestMethod]
    public void BlockBO_ValidationFailure_DoesNotAuthorizeRollback()
    {
        using var fixture = Fixture.Create();
        var result = ValidationResultPackageBuilder.Build(fixture.Request(ValidationOutcome.Failed));

        AssertPackageCreated(result);
        Assert.IsFalse(result.StatusValidation.Boundary.CanRollback);
        Assert.IsTrue(result.Status.ForbiddenActions.Any(action => action.Contains("rollback authority", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BlockBO_ValidationResult_DoesNotApproveSatisfyPolicyAuthorizeSourceApplyCommitPushPrMergeReleaseDeployMemoryOrContinuation()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();
        var boundary = result.StatusValidation.Boundary;

        AssertPassedPackage(result);
        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
        Assert.IsFalse(boundary.CanSourceApply);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void BlockBO_Status_ValidatesThroughGovernedOperationStatusValidator()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertPassedPackage(result);
        AssertValid(GovernedOperationStatusValidator.Validate(result.Status));
    }

    [TestMethod]
    public void BlockBO_StaticBoundary_DoesNotTouchExecutorProviderOrMutationSurface()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "ValidationResultPackageModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "ValidationResultPackageBuilder.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "ValidationResultPackageValidator.cs")
        };
        var text = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        var forbidden = new[]
        {
            "RunProcessAsync",
            "ProcessStartInfo",
            "dotnet test",
            "npm test",
            "git apply",
            "git commit",
            "git push",
            "gh pr create",
            "gh api",
            "kubectl",
            "terraform apply",
            "docker push",
            "npm publish",
            "source apply execute",
            "rollback execute",
            "commit execute",
            "push execute",
            "merge execute",
            "release execute",
            "deploy execute",
            "promote memory",
            "continue workflow",
            "create approval",
            "satisfy policy"
        };

        foreach (var value in forbidden)
            Assert.IsFalse(text.Contains(value, StringComparison.OrdinalIgnoreCase), value);
    }

    [TestMethod]
    public void BlockBO_Receipt_RecordsValidationResultPackageBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "BO_VALIDATION_RESULT_PACKAGE.md"));

        StringAssert.Contains(doc, "This slice adds a ProposalOnly-governed validation result package builder.");
        StringAssert.Contains(doc, "It packages existing disposable workspace validation evidence into:");
        StringAssert.Contains(doc, "validation-summary.md");
        StringAssert.Contains(doc, "validation-evidence.md");
        StringAssert.Contains(doc, "validation-result-package-manifest.json");
        StringAssert.Contains(doc, "operation-status.json");
        StringAssert.Contains(doc, "copied validation evidence files");
        StringAssert.Contains(doc, "It does not run validation.");
        StringAssert.Contains(doc, "It does not run tests.");
        StringAssert.Contains(doc, "It does not execute commands.");
        StringAssert.Contains(doc, "It does not generate code changes.");
        StringAssert.Contains(doc, "It does not apply source.");
        StringAssert.Contains(doc, "It does not mutate durable source.");
        StringAssert.Contains(doc, "It does not commit.");
        StringAssert.Contains(doc, "It does not push.");
        StringAssert.Contains(doc, "It does not create PRs.");
        StringAssert.Contains(doc, "It does not mark ready for review.");
        StringAssert.Contains(doc, "It does not merge.");
        StringAssert.Contains(doc, "It does not release.");
        StringAssert.Contains(doc, "It does not deploy.");
        StringAssert.Contains(doc, "It does not execute rollback.");
        StringAssert.Contains(doc, "It does not promote memory.");
        StringAssert.Contains(doc, "It does not continue workflow.");
        StringAssert.Contains(doc, "It does not create approval records.");
        StringAssert.Contains(doc, "It does not satisfy policy.");
        StringAssert.Contains(doc, "Validation result package is evidence only.");
        StringAssert.Contains(doc, "Validation pass is not approval.");
        StringAssert.Contains(doc, "Validation pass is not policy satisfaction.");
        StringAssert.Contains(doc, "Validation pass is not source apply authority.");
        StringAssert.Contains(doc, "Validation failure is not rollback execution authority.");
        StringAssert.Contains(doc, "Validation inconclusive is not workflow continuation authority.");
        StringAssert.Contains(doc, "NextSafeActions are guidance only.");
        StringAssert.Contains(doc, "Validation can say the package survived a check. It cannot say the package may touch source.");
    }

    private static void AssertPassedPackage(ValidationResultPackageResult result)
    {
        AssertPackageCreated(result);
        Assert.AreEqual(ValidationOutcome.Passed, result.Outcome);
        Assert.AreEqual(GovernedOperationState.Completed, result.Status.State, string.Join(", ", result.Issues));
        AssertValid(result.StatusValidation);
    }

    private static void AssertPackageCreated(ValidationResultPackageResult result)
    {
        Assert.IsTrue(result.IsPackageCreated, string.Join(", ", result.Issues));
        Assert.IsTrue(Directory.Exists(result.PackagePath));
        Assert.AreEqual(0, result.RedFlags.Count, string.Join(", ", result.RedFlags));
        AssertValid(result.StatusValidation);
    }

    private static void AssertBlockedWithoutPackage(ValidationResultPackageResult result, string issue)
    {
        Assert.IsFalse(result.IsPackageCreated);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
        AssertContains(result.Issues, issue);
        Assert.IsFalse(Directory.Exists(result.PackagePath));
    }

    private static void AssertValid(GovernedOperationStatusValidationResult result) =>
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues.Concat(result.RedFlags).Concat(result.AmberFlags)));

    private static void AssertContains(IReadOnlyList<string> values, string expected) =>
        Assert.IsTrue(values.Contains(expected, StringComparer.OrdinalIgnoreCase), string.Join(", ", values));

    private static void AssertFileContains(ValidationResultPackageResult result, string fileName, string expected)
    {
        var path = Path.Combine(result.PackagePath, fileName);
        Assert.IsTrue(File.Exists(path), path);
        StringAssert.Contains(File.ReadAllText(path), expected);
    }

    private static ValidationResultPackageManifest ReadManifest(ValidationResultPackageResult result)
    {
        var path = Path.Combine(result.PackagePath, "validation-result-package-manifest.json");
        return JsonSerializer.Deserialize<ValidationResultPackageManifest>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("Manifest could not be read.");
    }

    private static GovernedOperationStatus ReadStatus(ValidationResultPackageResult result)
    {
        var path = Path.Combine(result.PackagePath, "operation-status.json");
        return JsonSerializer.Deserialize<GovernedOperationStatus>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("Status could not be read.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(string root, string sourcePath, string workspacePath, string outputPath)
        {
            Root = root;
            SourcePath = sourcePath;
            WorkspacePath = workspacePath;
            OutputPath = outputPath;
        }

        public string Root { get; }
        public string SourcePath { get; }
        public string WorkspacePath { get; }
        public string OutputPath { get; }

        public static Fixture Create(
            Func<DisposableWorkspaceMarker, DisposableWorkspaceMarker>? mutateMarker = null,
            bool writeMarker = true,
            bool workspaceEqualsSource = false,
            bool workspaceInsideSource = false,
            bool outputInsideSource = false,
            bool writeNestedEvidence = false)
        {
            var root = Path.Combine(Path.GetTempPath(), $"bo-validation-package-{Guid.NewGuid():N}");
            var source = Path.Combine(root, "source");
            var workspace = workspaceEqualsSource
                ? source
                : workspaceInsideSource
                    ? Path.Combine(source, "fake-disposable-workspace")
                    : Path.Combine(root, "workspace");
            var output = outputInsideSource ? Path.Combine(source, "validation-output") : Path.Combine(root, "output");

            Directory.CreateDirectory(source);
            Directory.CreateDirectory(workspace);
            File.WriteAllText(Path.Combine(source, "README.md"), "durable source\n");
            File.WriteAllText(Path.Combine(workspace, "validation-output.log"), "validation passed\n");

            if (writeNestedEvidence)
            {
                Directory.CreateDirectory(Path.Combine(workspace, "logs"));
                File.WriteAllText(Path.Combine(workspace, "logs", "compiler-output.txt"), "compiler clean\n");
            }

            if (writeMarker)
            {
                Directory.CreateDirectory(Path.Combine(workspace, ".irondev"));
                var marker = new DisposableWorkspaceMarker
                {
                    WorkspaceId = "workspace-123",
                    RepoId = "BigDaddyDread-code/IronDeveloper",
                    Branch = "main",
                    SourceRoot = source,
                    CreatedFor = "proposal-only",
                    Disposable = true
                };
                marker = mutateMarker?.Invoke(marker) ?? marker;
                File.WriteAllText(
                    Path.Combine(workspace, ".irondev", "disposable-workspace.json"),
                    JsonSerializer.Serialize(marker, JsonOptions));
            }

            return new Fixture(root, source, workspace, output);
        }

        public ValidationResultPackageRequest Request(
            ValidationOutcome outcome = ValidationOutcome.Passed,
            IReadOnlyList<string>? evidenceFileNames = null) =>
            new()
            {
                OperationId = "validation-package-operation-123",
                RepoId = "BigDaddyDread-code/IronDeveloper",
                Branch = "main",
                WorkspacePath = WorkspacePath,
                OutputPath = OutputPath,
                ProposalId = "proposal-123",
                PatchHash = "sha256:patchhash123",
                ValidationRunId = "validation-run-123",
                ValidationName = "Focused BO",
                Outcome = outcome,
                EvidenceFileNames = evidenceFileNames ?? ["validation-output.log"],
                ValidationMessages = ["Focused validation completed."],
                ObservedAtUtc = DateTimeOffset.Parse("2026-06-21T07:00:00Z")
            };

        public ValidationResultPackageResult Build() =>
            ValidationResultPackageBuilder.Build(Request());

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
