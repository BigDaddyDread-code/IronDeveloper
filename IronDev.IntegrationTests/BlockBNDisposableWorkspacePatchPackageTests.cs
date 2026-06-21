using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBNDisposableWorkspacePatchPackageTests
{
    private const string PatchText = "diff --git a/README.md b/README.md\n--- a/README.md\n+++ b/README.md\n@@ -1 +1 @@\n-old\n+new\n";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public void BlockBN_Builder_RequiresProposalOnlyPatchPackageWriteEligibility()
    {
        using var fixture = Fixture.Create();
        var result = DisposableWorkspacePatchPackageBuilder.Build(fixture.Request() with { OperationId = string.Empty });

        Assert.IsFalse(result.IsPackageCreated);
        AssertContains(result.Issues, "OperationIdRequired");
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
    }

    [TestMethod]
    public void BlockBN_Builder_RejectsNonDisposableWorkspace()
    {
        using var fixture = Fixture.Create(marker => marker with { Disposable = false });
        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "DisposableWorkspaceMarkerMustBeDisposable");
    }

    [TestMethod]
    public void BlockBN_Builder_RejectsMissingDisposableMarker()
    {
        using var fixture = Fixture.Create(writeMarker: false);
        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "DisposableWorkspaceMarkerRequired");
    }

    [TestMethod]
    public void BlockBN_Builder_RejectsMalformedDisposableMarker()
    {
        using var fixture = Fixture.Create(writeMarker: false);
        Directory.CreateDirectory(Path.Combine(fixture.WorkspacePath, ".irondev"));
        File.WriteAllText(Path.Combine(fixture.WorkspacePath, ".irondev", "disposable-workspace.json"), "{not-json");

        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "DisposableWorkspaceMarkerInvalid");
    }

    [TestMethod]
    public void BlockBN_Builder_RejectsRepoMismatch()
    {
        using var fixture = Fixture.Create(marker => marker with { RepoId = "other/repo" });
        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "DisposableWorkspaceRepoMismatch");
    }

    [TestMethod]
    public void BlockBN_Builder_RejectsBranchMismatch()
    {
        using var fixture = Fixture.Create(marker => marker with { Branch = "other-branch" });
        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "DisposableWorkspaceBranchMismatch");
    }

    [TestMethod]
    public void BlockBN_Builder_RejectsWorkspaceNotCreatedForProposalOnly()
    {
        using var fixture = Fixture.Create(marker => marker with { CreatedFor = "full-run" });
        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "DisposableWorkspaceCreatedForProposalOnlyRequired");
    }

    [TestMethod]
    public void BlockBN_Builder_RejectsWorkspacePathEqualToSourceRoot()
    {
        using var fixture = Fixture.Create(workspaceEqualsSource: true);
        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "DisposableWorkspaceCannotEqualSourceRoot");
    }

    [TestMethod]
    public void BlockBN_Builder_RejectsOutputPathInsideDurableSourceRoot()
    {
        using var fixture = Fixture.Create(outputInsideSource: true);
        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "PatchPackageOutputCannotBeInsideSourceRoot");
        Assert.IsFalse(Directory.Exists(fixture.OutputPath), "Builder must not create package output under source root.");
    }

    [TestMethod]
    public void BlockBN_Builder_RequiresPatchDiff()
    {
        using var fixture = Fixture.Create(writePatch: false);
        var result = fixture.Build();

        AssertBlockedWithoutPackage(result, "PatchDiffRequired");
    }

    [TestMethod]
    public void BlockBN_Builder_ComputesStablePatchHash()
    {
        using var first = Fixture.Create();
        using var second = Fixture.Create();

        var firstResult = first.Build();
        var secondResult = second.Build();

        AssertCompleted(firstResult);
        AssertCompleted(secondResult);
        Assert.AreEqual(firstResult.PatchHash, secondResult.PatchHash);
        StringAssert.StartsWith(firstResult.PatchHash, "sha256:");
    }

    [TestMethod]
    public void BlockBN_Builder_WritesPatchDiff()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertCompleted(result);
        AssertFileContains(result, "patch.diff", "+new");
    }

    [TestMethod]
    public void BlockBN_Builder_WritesReviewSummary()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertCompleted(result);
        AssertFileContains(result, "review-summary.md", "Task: Add proposal-only package evidence.");
        AssertFileContains(result, "review-summary.md", $"Patch hash: {result.PatchHash}");
        AssertFileContains(result, "review-summary.md", "request controlled source apply for patch hash");
    }

    [TestMethod]
    public void BlockBN_Builder_WritesKnownRisks()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertCompleted(result);
        AssertFileContains(result, "known-risks.md", "source apply not performed");
        AssertFileContains(result, "known-risks.md", "commit not performed");
        AssertFileContains(result, "known-risks.md", "push not performed");
        AssertFileContains(result, "known-risks.md", "PR not created");
        AssertFileContains(result, "known-risks.md", "manual review required");
        AssertFileContains(result, "known-risks.md", "ProposalOnly evidence is not approval");
    }

    [TestMethod]
    public void BlockBN_Builder_WritesValidationSummary()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertCompleted(result);
        AssertFileContains(result, "validation-summary.md", "validation-result:focused-pass");
        AssertFileContains(result, "validation-summary.md", "Validation refs are evidence only.");
    }

    [TestMethod]
    public void BlockBN_Builder_WritesValidationMissingSummaryWhenValidationRefsAreAbsent()
    {
        using var fixture = Fixture.Create();
        var result = DisposableWorkspacePatchPackageBuilder.Build(fixture.Request(validationRefs: []));

        Assert.IsTrue(result.IsPackageCreated);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
        AssertFileContains(result, "validation-summary.md", "Validation not supplied for this package.");
        AssertFileContains(result, "validation-summary.md", "Controlled source apply must not infer validation from package existence.");
    }

    [TestMethod]
    public void BlockBN_Builder_WritesPatchPackageManifest()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();
        var manifest = ReadManifest(result);

        AssertCompleted(result);
        Assert.AreEqual(result.PackageId, manifest.PackageId);
        Assert.AreEqual("proposal-123", manifest.ProposalId);
        Assert.AreEqual("BigDaddyDread-code/IronDeveloper", manifest.RepoId);
        Assert.AreEqual("main", manifest.Branch);
        Assert.AreEqual("workspace-123", manifest.WorkspaceId);
        Assert.AreEqual(result.PatchHash, manifest.PatchHash);
        AssertContains(manifest.ArtifactRefs, $"patch-package:{result.PackageId}");
        AssertContains(manifest.ValidationRefs, "validation-result:focused-pass");
        Assert.IsTrue(manifest.ForbiddenActions.Count > 0);
    }

    [TestMethod]
    public void BlockBN_Builder_WritesOperationStatusJson()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();
        var status = ReadStatus(result);

        AssertCompleted(result);
        Assert.AreEqual("PatchProposal", status.OperationKind);
        Assert.AreEqual(GovernedOperationState.Completed, status.State);
        AssertContains(status.EvidenceRefs, $"patch-package:{result.PackageId}");
        AssertContains(status.EvidenceRefs, $"patch-hash:{result.PatchHash}");
        AssertContains(status.ReceiptRefs, "patch-proposal-status-artifact:proposal-123");
    }

    [TestMethod]
    public void BlockBN_CompletedPackage_MapsToCompletedPatchProposalStatusWhenValidationRefsExist()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertCompleted(result);
        Assert.AreEqual("PatchProposal", result.Status.OperationKind);
        Assert.AreEqual(GovernedOperationState.Completed, result.Status.State);
        AssertContains(result.Status.NextSafeActions, $"request controlled source apply for patch hash {result.PatchHash}");
    }

    [TestMethod]
    public void BlockBN_MissingValidationRefs_MapToBlockedPatchProposalStatus()
    {
        using var fixture = Fixture.Create();
        var result = DisposableWorkspacePatchPackageBuilder.Build(fixture.Request(validationRefs: []));

        Assert.IsTrue(result.IsPackageCreated);
        Assert.AreEqual("PatchProposal", result.Status.OperationKind);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
        AssertContains(result.Status.BlockedReasons, "Validation evidence is missing.");
        AssertContains(result.Status.MissingEvidence, "validation-result:proposal-only");
        AssertValid(result.StatusValidation);
    }

    [TestMethod]
    public void BlockBN_PackageOutput_ForbidsDirectSourceApply()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertCompleted(result);
        AssertContains(result.Status.ForbiddenActions, "do not apply patch proposal directly to source");
        AssertContains(result.Status.ForbiddenActions, "do not treat patch proposal completion as source apply authority");
    }

    [TestMethod]
    public void BlockBN_PackageOutput_ForbidsCommitPushPrMergeReleaseDeployMemoryAndContinuation()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();
        var forbidden = string.Join("\n", result.Status.ForbiddenActions);

        AssertCompleted(result);
        StringAssert.Contains(forbidden, "commit");
        StringAssert.Contains(forbidden, "push");
        StringAssert.Contains(forbidden, "create PRs");
        StringAssert.Contains(forbidden, "merge");
        StringAssert.Contains(forbidden, "release");
        StringAssert.Contains(forbidden, "deploy");
        StringAssert.Contains(forbidden, "promote memory");
        StringAssert.Contains(forbidden, "continue workflow");
    }

    [TestMethod]
    public void BlockBN_PatchHash_RemainsEvidenceOnly()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertCompleted(result);
        AssertContains(result.Status.EvidenceRefs, $"patch-hash:{result.PatchHash}");
        Assert.IsFalse(result.StatusValidation.Boundary.CanSourceApply);
    }

    [TestMethod]
    public void BlockBN_ValidationRefs_RemainEvidenceOnly()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertCompleted(result);
        AssertContains(result.Status.EvidenceRefs, "validation-result:focused-pass");
        Assert.IsFalse(result.StatusValidation.Boundary.CanApprove);
    }

    [TestMethod]
    public void BlockBN_ReviewSummary_RemainsEvidenceOnly()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertCompleted(result);
        AssertContains(result.Status.EvidenceRefs, $"review-summary:{result.PackageId}");
        Assert.IsFalse(result.StatusValidation.Boundary.CanApprove);
    }

    [TestMethod]
    public void BlockBN_KnownRisks_RemainEvidenceOnly()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertCompleted(result);
        AssertContains(result.Status.EvidenceRefs, $"known-risks:{result.PackageId}");
        Assert.IsFalse(result.StatusValidation.Boundary.CanSatisfyPolicy);
    }

    [TestMethod]
    public void BlockBN_Manifest_RemainsEvidenceOnly()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertCompleted(result);
        AssertContains(result.Status.EvidenceRefs, $"patch-package-manifest:{result.PackageId}");
        Assert.IsFalse(result.StatusValidation.Boundary.CanExecute);
    }

    [TestMethod]
    public void BlockBN_PackageStatus_ValidatesThroughGovernedOperationStatusValidator()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();

        AssertCompleted(result);
        AssertValid(GovernedOperationStatusValidator.Validate(result.Status));
    }

    [TestMethod]
    public void BlockBN_Result_DoesNotGrantApprovalPolicyExecutionMutationMemoryOrContinuation()
    {
        using var fixture = Fixture.Create();
        var result = fixture.Build();
        var boundary = result.StatusValidation.Boundary;

        AssertCompleted(result);
        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
        Assert.IsFalse(boundary.CanExecute);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void BlockBN_Builder_DoesNotMutateDurableSource()
    {
        using var fixture = Fixture.Create();
        var before = Directory.GetFiles(fixture.SourcePath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(fixture.SourcePath, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = fixture.Build();

        AssertCompleted(result);
        var after = Directory.GetFiles(fixture.SourcePath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(fixture.SourcePath, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        CollectionAssert.AreEqual(before, after);
        Assert.AreEqual("durable source\n", File.ReadAllText(Path.Combine(fixture.SourcePath, "README.md")));
    }

    [TestMethod]
    public void BlockBN_StaticBoundary_DoesNotTouchExecutorProviderOrMutationSurface()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "DisposableWorkspacePatchPackageModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "DisposableWorkspacePatchPackageBuilder.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "DisposableWorkspacePatchPackageValidator.cs")
        };
        var text = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        var forbidden = new[]
        {
            "RunProcessAsync",
            "ProcessStartInfo",
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
    public void BlockBN_Receipt_RecordsDisposableWorkspacePatchPackageBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "BN_DISPOSABLE_WORKSPACE_PATCH_PACKAGE.md"));

        StringAssert.Contains(doc, "This slice adds a ProposalOnly-governed disposable workspace patch package builder.");
        StringAssert.Contains(doc, "It packages existing disposable workspace proposal artifacts into:");
        StringAssert.Contains(doc, "patch.diff");
        StringAssert.Contains(doc, "review-summary.md");
        StringAssert.Contains(doc, "known-risks.md");
        StringAssert.Contains(doc, "validation-summary.md");
        StringAssert.Contains(doc, "patch-package-manifest.json");
        StringAssert.Contains(doc, "operation-status.json");
        StringAssert.Contains(doc, "It does not create or modify the disposable workspace.");
        StringAssert.Contains(doc, "It does not generate code changes.");
        StringAssert.Contains(doc, "It does not run tests.");
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
        StringAssert.Contains(doc, "The patch package is evidence only.");
        StringAssert.Contains(doc, "Patch hash is evidence only.");
        StringAssert.Contains(doc, "Validation refs are evidence only.");
        StringAssert.Contains(doc, "Review summary is evidence only.");
        StringAssert.Contains(doc, "Known risks are evidence only.");
        StringAssert.Contains(doc, "Operation status is explanation only.");
        StringAssert.Contains(doc, "NextSafeActions are guidance only.");
        StringAssert.Contains(doc, "A patch package can hand the reviewer the file. It cannot put the file into source.");
    }

    private static void AssertCompleted(DisposableWorkspacePatchPackageResult result)
    {
        Assert.IsTrue(result.IsPackageCreated, string.Join(", ", result.Issues));
        Assert.AreEqual(GovernedOperationState.Completed, result.Status.State, string.Join(", ", result.Issues));
        AssertValid(result.StatusValidation);
        Assert.AreEqual(0, result.RedFlags.Count, string.Join(", ", result.RedFlags));
    }

    private static void AssertBlockedWithoutPackage(DisposableWorkspacePatchPackageResult result, string issue)
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

    private static void AssertFileContains(DisposableWorkspacePatchPackageResult result, string fileName, string expected)
    {
        var path = Path.Combine(result.PackagePath, fileName);
        Assert.IsTrue(File.Exists(path), path);
        StringAssert.Contains(File.ReadAllText(path), expected);
    }

    private static DisposableWorkspacePatchPackageManifest ReadManifest(DisposableWorkspacePatchPackageResult result)
    {
        var path = Path.Combine(result.PackagePath, "patch-package-manifest.json");
        return JsonSerializer.Deserialize<DisposableWorkspacePatchPackageManifest>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("Manifest could not be read.");
    }

    private static GovernedOperationStatus ReadStatus(DisposableWorkspacePatchPackageResult result)
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
            bool writePatch = true,
            bool workspaceEqualsSource = false,
            bool outputInsideSource = false)
        {
            var root = Path.Combine(Path.GetTempPath(), $"bn-patch-package-{Guid.NewGuid():N}");
            var source = Path.Combine(root, "source");
            var workspace = workspaceEqualsSource ? source : Path.Combine(root, "workspace");
            var output = outputInsideSource ? Path.Combine(source, "package-output") : Path.Combine(root, "output");

            Directory.CreateDirectory(source);
            Directory.CreateDirectory(workspace);
            File.WriteAllText(Path.Combine(source, "README.md"), "durable source\n");

            if (writePatch)
                File.WriteAllText(Path.Combine(workspace, "patch.diff"), PatchText);

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

        public DisposableWorkspacePatchPackageRequest Request(IReadOnlyList<string>? validationRefs = null) =>
            new()
            {
                OperationId = "patch-package-operation-123",
                RepoId = "BigDaddyDread-code/IronDeveloper",
                Branch = "main",
                WorkspacePath = WorkspacePath,
                OutputPath = OutputPath,
                ProposalId = "proposal-123",
                TaskSummary = "Add proposal-only package evidence.",
                ValidationRefs = validationRefs ?? ["validation-result:focused-pass"],
                ObservedAtUtc = DateTimeOffset.Parse("2026-06-21T06:00:00Z")
            };

        public DisposableWorkspacePatchPackageResult Build() =>
            DisposableWorkspacePatchPackageBuilder.Build(Request());

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
