using IronDev.Core.Workbench;
using IronDev.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class RepositorySetupInfrastructureTests
{
    [TestMethod]
    public void PinnedCatalog_IsCompleteImmutablePlanningAuthorityWithRealBundleHash()
    {
        var catalog = new RepositorySetupProfileCatalog();
        var profile = catalog.GetAll().Single();
        Assert.AreEqual(RepositorySetupProfileIds.GreenfieldWinFormsNet10MstestV1,
            profile.ProfileDefinitionId);
        Assert.AreEqual(1, profile.Revision);
        Assert.AreEqual("net10.0-windows", profile.TargetFramework);
        Assert.AreEqual("C#", profile.Language);
        Assert.AreEqual("WinForms", profile.ApplicationKind);
        Assert.AreEqual("MSTest", profile.TestFramework);
        Assert.AreEqual("10.0.302", profile.SdkVersion);
        Assert.AreEqual("10.0.10", profile.RuntimeVersion);
        Assert.AreEqual("dotnet-sdk-10.0.302-runtime-10.0.10-planning-v1",
            profile.ToolchainManifestId);
        Assert.AreEqual("mcr.microsoft.com/dotnet/sdk:10.0-windowsservercore-ltsc2025",
            profile.ExecutionImageReference);
        Assert.AreEqual(RepositoryPlanningReadinessStates.PreviewPlanningOnly,
            profile.PlanningReadiness);
        Assert.AreEqual(RepositoryProfileCertificationStates.NotCertificationReady,
            profile.CertificationState);
        Assert.AreEqual(
            "dotnet restore \"{SolutionPath}\" --configfile C:\\IronDev\\NuGet.Config --locked-mode",
            profile.RestoreCommandTemplate);
        Assert.AreEqual(
            "dotnet build \"{SolutionPath}\" --configuration Release --no-restore",
            profile.BuildCommandTemplate);
        Assert.AreEqual(
            "dotnet test \"{TestProjectPath}\" --configuration Release --no-restore --no-build",
            profile.TestCommandTemplate);
        Assert.AreEqual(64, profile.DescriptorSha256.Length);
        Assert.AreEqual(
            RepositorySetupPinnedProfileHashes.GreenfieldWinFormsNet10MstestV1DescriptorRevision1,
            profile.DescriptorSha256);
        Assert.AreNotEqual(new string('0', 64), profile.DescriptorSha256);
        Assert.AreEqual(
            RepositorySetupTemplateBundleCodec.ComputeHash(profile.TemplateBundle),
            profile.TemplateBundleSha256);
        Assert.AreEqual(
            RepositorySetupPinnedProfileHashes.GreenfieldWinFormsNet10MstestV1TemplateBundleRevision1,
            profile.TemplateBundleSha256);
        Assert.AreEqual(13, profile.TemplateBundle.Files.Count);
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "{{SOLUTION_NAME}}.slnx",
                "global.json",
                ".gitattributes",
                "Directory.Build.props",
                "src/{{APP_PROJECT_NAME}}/packages.lock.json",
                "tests/{{TEST_PROJECT_NAME}}/packages.lock.json"
            },
            profile.TemplateBundle.Files.Select(value => value.RelativePath).ToArray());
        Assert.IsNotNull(catalog.Find(
            profile.ProfileDefinitionId, profile.Revision, profile.DescriptorSha256));
        Assert.IsNull(catalog.Find(profile.ProfileDefinitionId, profile.Revision + 1, profile.DescriptorSha256));
        Assert.IsNotNull(catalog.FindBundle(
            profile.ProfileDefinitionId, profile.Revision, profile.DescriptorSha256));
    }

    [TestMethod]
    public void PathPolicy_AllowsOnlyAbsentDirectChildWithoutWriting()
    {
        const string root = @"C:\IronDevTestWorkspaces\repository-setup";
        var fileSystem = new RecordingFileSystem(root);
        var policy = new RepositorySetupPathPolicy(
            fileSystem,
            new StaticForbiddenRoots([@"C:\Windows", @"C:\Users"]));

        var assessment = policy.Assess(root, "sample-42", inspectEnvironment: true);

        Assert.IsTrue(assessment.IsAvailable);
        Assert.IsFalse(assessment.IsUnsafe);
        Assert.AreEqual(Path.Combine(root, "sample-42"), assessment.TargetPath);
        Assert.IsTrue(fileSystem.DirectoryReadCount > 0);
        Assert.AreEqual(0, fileSystem.WriteCount);
    }

    [TestMethod]
    [DataRow(@"C:\")]
    [DataRow(@"\\server\share\repositories")]
    [DataRow(@"\\?\C:\IronDevTestWorkspaces\repositories")]
    [DataRow(@"C:\IronDevTestWorkspaces\..\Windows")]
    [DataRow(@"C:\Users\builder\repositories")]
    [DataRow(@"C:\Windows\IronDev")]
    public void PathPolicy_RejectsBroadRemoteTraversalAndForbiddenRoots(string root)
    {
        var policy = new RepositorySetupPathPolicy(
            new RecordingFileSystem(root),
            new StaticForbiddenRoots([@"C:\Windows", @"C:\Users"]));
        var assessment = policy.Assess(root, "sample-42", inspectEnvironment: true);
        Assert.IsTrue(assessment.IsUnsafe);
        Assert.AreEqual(RepositorySetupUnsafePathException.ErrorCode, assessment.ReasonCode);
    }

    [TestMethod]
    public void PathPolicy_RejectsExistingTargetAndReparseAncestor()
    {
        const string root = @"C:\IronDevTestWorkspaces\repository-setup";
        var existing = new RecordingFileSystem(root)
        {
            ExistingTarget = Path.Combine(root, "sample-42")
        };
        var policy = new RepositorySetupPathPolicy(existing, new StaticForbiddenRoots([]));
        Assert.IsTrue(policy.Assess(root, "sample-42", true).IsUnsafe);

        var reparse = new RecordingFileSystem(root) { ReparsePath = root };
        policy = new RepositorySetupPathPolicy(reparse, new StaticForbiddenRoots([]));
        Assert.IsTrue(policy.Assess(root, "sample-42", true).IsUnsafe);
    }

    [TestMethod]
    public void SafeNames_UseAsciiOnlyAndNeverStrandTemplateRendering()
    {
        var names = RepositorySetupSafeNames.FromProject("Café 日本語 🚀", 42);
        Assert.IsTrue(names.DirectoryName.All(value => value <= 0x7f));
        Assert.IsTrue(names.SolutionName.All(value => value <= 0x7f));
        Assert.AreEqual("caf-42", names.DirectoryName);
    }

    [TestMethod]
    public void LegacyLocalPathProjection_IsDeterministicDescriptiveAndNeverQualified()
    {
        var first = RepositoryBindingProjection.CreateLegacy(42, @" C:\Legacy\Project ");
        var second = RepositoryBindingProjection.CreateLegacy(42, @"C:\Legacy\Project");

        Assert.IsNotNull(first);
        Assert.AreEqual(first, second);
        Assert.AreEqual(RepositoryKinds.Existing, first.RepositoryKind);
        Assert.AreEqual(RepositoryBindingStates.LegacyUnverified, first.BindingState);
        Assert.AreEqual(@"C:\Legacy\Project", first.CanonicalPath);
        Assert.IsNull(first.DefaultBranch);
        Assert.IsNull(first.BaselineCommit);
        Assert.IsNull(first.ConfirmedAtUtc);
        Assert.IsNull(RepositoryBindingProjection.CreateLegacy(42, "  "));
    }

    [TestMethod]
    public async Task ProvisioningExecutor_PublishesCleanGitRepositoryAndExactReplayRecovers()
    {
        using var workspace = new ProvisioningTestWorkspace();
        var injector = new RecordingProvisioningFailureInjector();
        var executor = CreateProvisioningExecutor(injector);
        var request = CreateProvisioningRequest(workspace.Root);

        var first = await executor.ExecuteOrRecoverAsync(request);
        var replay = await executor.ExecuteOrRecoverAsync(request);

        Assert.IsFalse(first.WasRecovered);
        Assert.IsTrue(replay.WasRecovered);
        Assert.AreEqual(first.BaselineCommit, replay.BaselineCommit);
        Assert.AreEqual(first.GitTreeId, replay.GitTreeId);
        Assert.AreEqual(first.PublishedAtUtc, replay.PublishedAtUtc);
        Assert.AreEqual(40, first.BaselineCommit.Length);
        Assert.AreEqual(40, first.GitTreeId.Length);
        Assert.AreEqual(64, first.ManifestSha256.Length);
        Assert.AreEqual("main", first.BranchName);
        Assert.IsTrue(File.Exists(Path.Combine(first.CanonicalPath, "README.md")));
        Assert.IsTrue(File.Exists(Path.Combine(first.CanonicalPath, ".git", ".irondev-provisioning-attempt.json")));
        Assert.IsTrue(File.Exists(Path.Combine(first.CanonicalPath, ".git", ".irondev-publication-evidence.json")));
        Assert.IsFalse(Directory.Exists(StagingPath(request)));
    }

    [TestMethod]
    public async Task ProvisioningExecutor_PublishesTheCompletePinnedTemplateBundle()
    {
        using var workspace = new ProvisioningTestWorkspace();
        var executor = CreateProvisioningExecutor(new RecordingProvisioningFailureInjector());
        var request = CreatePinnedProvisioningRequest(workspace.Root);

        var evidence = await executor.ExecuteOrRecoverAsync(request);

        Assert.AreEqual(13, RepositorySetupTemplateBundleRenderer.Render(
            request.TemplateBundle, request.ConfirmedPlan).Files.Count);
        Assert.AreEqual("main", evidence.BranchName);
    }

    [TestMethod]
    public async Task ProvisioningExecutor_HostileXdgIgnoreAndAttributesCannotChangePinnedBaseline()
    {
        using var workspace = new ProvisioningTestWorkspace();
        var xdgRoot = Path.Combine(workspace.Root, "hostile-xdg");
        var xdgGit = Path.Combine(xdgRoot, "git");
        Directory.CreateDirectory(xdgGit);
        File.WriteAllText(Path.Combine(xdgGit, "ignore"), "README.md\n");
        File.WriteAllText(Path.Combine(xdgGit, "attributes"), "README.md ident\n");
        var previousXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdgRoot);
            var executor = CreateProvisioningExecutor(new RecordingProvisioningFailureInjector());
            const string readmeTemplate =
                "# {{SOLUTION_NAME}}\n$Id: 0000000000000000000000000000000000000000 $\n";
            var request = CreateProvisioningRequest(workspace.Root, readmeTemplate);

            var evidence = await executor.ExecuteOrRecoverAsync(request);

            Assert.AreEqual("main", evidence.BranchName);
            Assert.AreEqual(40, evidence.BaselineCommit.Length);
            CollectionAssert.AreEqual(
                RepositorySetupTemplateBundleRenderer.Utf8NoBomStrict.GetBytes(
                    "# Sample\n$Id: 0000000000000000000000000000000000000000 $\n"),
                File.ReadAllBytes(Path.Combine(evidence.CanonicalPath, "README.md")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousXdg);
        }
    }

    [TestMethod]
    public async Task ProvisioningExecutor_UnknownTargetAndMismatchedStagingAreNeverAdoptedOrDeleted()
    {
        using var workspace = new ProvisioningTestWorkspace();
        var executor = CreateProvisioningExecutor(new RecordingProvisioningFailureInjector());
        var request = CreateProvisioningRequest(workspace.Root);
        Directory.CreateDirectory(request.ConfirmedPlan.TargetPath);
        var sentinel = Path.Combine(request.ConfirmedPlan.TargetPath, "sentinel.txt");
        File.WriteAllText(sentinel, "unrelated");

        var targetFailure = await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            executor.ExecuteOrRecoverAsync(request));
        Assert.AreEqual(RepositoryProvisioningFailureCodes.TargetAlreadyExists, targetFailure.ReasonCode);
        Assert.IsTrue(File.Exists(sentinel));

        Directory.Delete(request.ConfirmedPlan.TargetPath, recursive: true);
        var staging = StagingPath(request);
        Directory.CreateDirectory(staging);
        var stagingSentinel = Path.Combine(staging, ".irondev-provisioning-attempt.json");
        File.WriteAllText(stagingSentinel, "{\"attemptId\":\"someone-else\"}\n");
        var stagingFailure = await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            executor.ExecuteOrRecoverAsync(request));
        Assert.AreEqual(RepositoryProvisioningFailureCodes.FileSystemFailed, stagingFailure.ReasonCode);
        Assert.IsTrue(File.Exists(stagingSentinel));
    }

    [TestMethod]
    public async Task ProvisioningExecutor_PrePublishFailureCleansOnlyOwnedStaging()
    {
        using var workspace = new ProvisioningTestWorkspace();
        var unrelated = Path.Combine(workspace.Root, "unrelated");
        Directory.CreateDirectory(unrelated);
        var sentinel = Path.Combine(unrelated, "sentinel.txt");
        File.WriteAllText(sentinel, "preserve");
        var injector = new RecordingProvisioningFailureInjector
        {
            FailurePoint = RepositoryProvisioningFailurePoint.GitCommitted
        };
        var executor = CreateProvisioningExecutor(injector);
        var request = CreateProvisioningRequest(workspace.Root);

        var failure = await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            executor.ExecuteOrRecoverAsync(request));

        Assert.AreEqual(RepositoryProvisioningFailureCodes.UnexpectedFailure, failure.ReasonCode);
        Assert.IsFalse(Directory.Exists(request.ConfirmedPlan.TargetPath));
        Assert.IsFalse(Directory.Exists(StagingPath(request)));
        Assert.IsTrue(File.Exists(sentinel));
    }

    [TestMethod]
    public async Task ProvisioningExecutor_AfterPublishFailureRecoversSameCommitWithoutRewriting()
    {
        using var workspace = new ProvisioningTestWorkspace();
        var injector = new RecordingProvisioningFailureInjector
        {
            FailurePoint = RepositoryProvisioningFailurePoint.AfterPublish
        };
        var executor = CreateProvisioningExecutor(injector);
        var request = CreateProvisioningRequest(workspace.Root);

        await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            executor.ExecuteOrRecoverAsync(request));
        Assert.IsTrue(Directory.Exists(request.ConfirmedPlan.TargetPath));
        var publicationMarker = Path.Combine(
            request.ConfirmedPlan.TargetPath, ".git", ".irondev-publication-evidence.json");
        var publishedAtUtc = ReadPublishedAtUtc(publicationMarker);
        await Task.Delay(25);
        injector.FailurePoint = null;

        var recovered = await executor.ExecuteOrRecoverAsync(request);

        Assert.IsTrue(recovered.WasRecovered);
        Assert.AreEqual(publishedAtUtc, recovered.PublishedAtUtc);
        Assert.IsTrue(recovered.PublishedAtUtc < DateTime.UtcNow);
        StringAssert.Contains(
            File.ReadAllText(Path.Combine(request.ConfirmedPlan.TargetPath, ".git", ".irondev-provisioning-attempt.json")),
            request.AttemptId.ToString("D"));
    }

    [TestMethod]
    public async Task ProvisioningExecutor_LockedPublishedMarkerIsVerificationUnavailableNotForeign()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var workspace = new ProvisioningTestWorkspace();
        var injector = new RecordingProvisioningFailureInjector
        {
            FailurePoint = RepositoryProvisioningFailurePoint.AfterPublish
        };
        var executor = CreateProvisioningExecutor(injector);
        var request = CreateProvisioningRequest(workspace.Root);

        await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            executor.ExecuteOrRecoverAsync(request));
        var marker = Path.Combine(
            request.ConfirmedPlan.TargetPath, ".git", ".irondev-provisioning-attempt.json");
        FileStream? exclusive = null;
        injector.FailurePoint = null;
        injector.Callback = point =>
        {
            if (point == RepositoryProvisioningFailurePoint.PublishedMarkerInspected)
                exclusive = new FileStream(
                    marker, FileMode.Open, FileAccess.Read, FileShare.None, 4096, useAsync: true);
        };
        RepositoryProvisioningPublishedInspection inspection;
        try
        {
            inspection = await executor.InspectPublishedRepositoryForAttemptAsync(request);
        }
        finally
        {
            injector.Callback = null;
            if (exclusive is not null)
                await exclusive.DisposeAsync();
        }

        Assert.AreEqual(
            RepositoryProvisioningPublishedInspectionState.VerificationUnavailable,
            inspection.State);
        Assert.IsTrue(Directory.Exists(request.ConfirmedPlan.TargetPath));
    }

    [TestMethod]
    public async Task ProvisioningExecutor_RejectsEmptyChildWithCopiedTrailers()
    {
        using var workspace = new ProvisioningTestWorkspace();
        var injector = new RecordingProvisioningFailureInjector
        {
            FailurePoint = RepositoryProvisioningFailurePoint.AfterPublish
        };
        var executor = CreateProvisioningExecutor(injector);
        var request = CreateProvisioningRequest(workspace.Root);

        await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            executor.ExecuteOrRecoverAsync(request));
        var target = request.ConfirmedPlan.TargetPath;
        var copiedMessage = RunGit(target, "log", "-1", "--format=%B").TrimEnd('\r', '\n');
        _ = RunGit(
            target,
            "-c", "user.name=Foreign Actor",
            "-c", "user.email=foreign@example.invalid",
            "-c", "commit.gpgSign=false",
            "commit", "--allow-empty", "--no-gpg-sign", "--no-verify", "--message", copiedMessage);
        injector.FailurePoint = null;

        var failure = await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            executor.ExecuteOrRecoverAsync(request));

        Assert.AreEqual(RepositoryProvisioningFailureCodes.PublishedRepositoryInvalid, failure.ReasonCode);
    }

    [TestMethod]
    public async Task ProvisioningExecutor_AtomicStagingRacePreservesForeignDirectory()
    {
        using var workspace = new ProvisioningTestWorkspace();
        var creator = new RacingDirectoryCreator();
        var executor = CreateProvisioningExecutor(new RecordingProvisioningFailureInjector(), creator);
        var request = CreateProvisioningRequest(workspace.Root);

        var failure = await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            executor.ExecuteOrRecoverAsync(request));

        Assert.AreEqual(RepositoryProvisioningFailureCodes.FileSystemFailed, failure.ReasonCode);
        Assert.IsTrue(File.Exists(Path.Combine(StagingPath(request), "foreign-sentinel.txt")));
        Assert.IsFalse(Directory.Exists(request.ConfirmedPlan.TargetPath));
    }

    [TestMethod]
    public async Task ProvisioningExecutor_RejectsIndexSuppressionEvenWhenWorkingBytesAreExact()
    {
        using var workspace = new ProvisioningTestWorkspace();
        var injector = new RecordingProvisioningFailureInjector
        {
            FailurePoint = RepositoryProvisioningFailurePoint.AfterPublish
        };
        var executor = CreateProvisioningExecutor(injector);
        var request = CreateProvisioningRequest(workspace.Root);

        await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            executor.ExecuteOrRecoverAsync(request));
        _ = RunGit(request.ConfirmedPlan.TargetPath,
            "update-index", "--assume-unchanged", "README.md");
        injector.FailurePoint = null;

        var failure = await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            executor.ExecuteOrRecoverAsync(request));

        Assert.AreEqual(RepositoryProvisioningFailureCodes.PublishedRepositoryInvalid, failure.ReasonCode);
        Assert.AreEqual("# Sample\n", File.ReadAllText(
            Path.Combine(request.ConfirmedPlan.TargetPath, "README.md")));
    }

    [TestMethod]
    public async Task ProvisioningExecutor_RecoveryRejectsExternalCoreWorktreeAuthority()
    {
        using var workspace = new ProvisioningTestWorkspace();
        var injector = new RecordingProvisioningFailureInjector
        {
            FailurePoint = RepositoryProvisioningFailurePoint.AfterPublish
        };
        var executor = CreateProvisioningExecutor(injector);
        var request = CreateProvisioningRequest(workspace.Root);

        await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            executor.ExecuteOrRecoverAsync(request));
        File.AppendAllText(
            Path.Combine(request.ConfirmedPlan.TargetPath, ".git", "config"),
            "[core]\n\tworktree = C:/outside-irondev\n");
        injector.FailurePoint = null;

        var failure = await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            executor.ExecuteOrRecoverAsync(request));

        Assert.AreEqual(RepositoryProvisioningFailureCodes.PublishedRepositoryInvalid, failure.ReasonCode);
    }

    [TestMethod]
    public async Task ProvisioningExecutor_RecoveryRejectsAnyAdditionalRef()
    {
        using var workspace = new ProvisioningTestWorkspace();
        var injector = new RecordingProvisioningFailureInjector
        {
            FailurePoint = RepositoryProvisioningFailurePoint.AfterPublish
        };
        var executor = CreateProvisioningExecutor(injector);
        var request = CreateProvisioningRequest(workspace.Root);

        await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            executor.ExecuteOrRecoverAsync(request));
        var target = request.ConfirmedPlan.TargetPath;
        var head = RunGit(target, "rev-parse", "--verify", "HEAD").Trim();
        File.WriteAllText(Path.Combine(target, ".git", "refs", "heads", "foreign"), head + "\n");
        injector.FailurePoint = null;

        var failure = await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            executor.ExecuteOrRecoverAsync(request));

        Assert.AreEqual(RepositoryProvisioningFailureCodes.PublishedRepositoryInvalid, failure.ReasonCode);
    }

    [TestMethod]
    [DataRow("irondev-empty-hooks/pre-commit")]
    [DataRow("info/attributes")]
    [DataRow("MERGE_HEAD")]
    public async Task ProvisioningExecutor_RecoveryRejectsGitHookAttributeAndOperationAuthority(
        string gitRelativePath)
    {
        using var workspace = new ProvisioningTestWorkspace();
        var injector = new RecordingProvisioningFailureInjector
        {
            FailurePoint = RepositoryProvisioningFailurePoint.AfterPublish
        };
        var executor = CreateProvisioningExecutor(injector);
        var request = CreateProvisioningRequest(workspace.Root);

        await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            executor.ExecuteOrRecoverAsync(request));
        var authorityPath = Path.Combine(
            request.ConfirmedPlan.TargetPath,
            ".git",
            gitRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(authorityPath)!);
        File.WriteAllText(authorityPath, "foreign authority\n");
        injector.FailurePoint = null;

        var failure = await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            executor.ExecuteOrRecoverAsync(request));

        Assert.AreEqual(RepositoryProvisioningFailureCodes.PublishedRepositoryInvalid, failure.ReasonCode);
    }

    [TestMethod]
    public async Task ProvisioningExecutor_RecoveryRejectsReparsePointAnywhereUnderGitDirectory()
    {
        using var workspace = new ProvisioningTestWorkspace();
        var injector = new RecordingProvisioningFailureInjector
        {
            FailurePoint = RepositoryProvisioningFailurePoint.AfterPublish
        };
        var executor = CreateProvisioningExecutor(injector);
        var request = CreateProvisioningRequest(workspace.Root);
        var external = Path.Combine(
            Path.GetTempPath(), "IronDevRepositoryProvisioningExternal", Guid.NewGuid().ToString("N"));
        var link = Path.Combine(request.ConfirmedPlan.TargetPath, ".git", "objects", "foreign-link");

        await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            executor.ExecuteOrRecoverAsync(request));
        Directory.CreateDirectory(external);
        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, external);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or
                                               PlatformNotSupportedException)
            {
                Assert.Inconclusive($"Symbolic links are unavailable in this test environment: {exception.Message}");
                return;
            }
            injector.FailurePoint = null;

            var failure = await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
                executor.ExecuteOrRecoverAsync(request));

            Assert.AreEqual(RepositoryProvisioningFailureCodes.PublishedRepositoryInvalid, failure.ReasonCode);
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            if (Directory.Exists(external))
                Directory.Delete(external);
        }
    }

    [TestMethod]
    public async Task GitRunner_UsesResolvedAbsoluteGitAndNeverRepositoryLocalExecutable()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var workspace = new ProvisioningTestWorkspace();
        var fakeGit = Path.Combine(workspace.Root, "git.exe");
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe"), fakeGit);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WorkbenchRepositorySetup:ApprovedWorkspaceRoot"] = workspace.Root,
            ["WorkbenchRepositoryProvisioning:GitExecutable"] = "git",
            ["WorkbenchRepositoryProvisioning:GitTimeoutSeconds"] = "30"
        }).Build();
        var runner = new RepositoryProvisioningGitRunner(configuration);

        var result = await runner.RunAsync(
            workspace.Root,
            ["init", "--object-format=sha1", "--initial-branch=main"],
            new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc));

        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        Assert.IsTrue(Directory.Exists(Path.Combine(workspace.Root, ".git")));
    }

    [TestMethod]
    public async Task GitRunner_MissingExecutableDoesNotBreakConstructionAndFailsOnlyWhenInvoked()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WorkbenchRepositoryProvisioning:GitExecutable"] = "irondev-git-intentionally-missing-8d52950f",
            ["WorkbenchRepositoryProvisioning:GitTimeoutSeconds"] = "30"
        }).Build();

        var runner = new RepositoryProvisioningGitRunner(configuration);
        var failure = await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            runner.RunAsync(
                Path.GetTempPath(),
                ["status", "--porcelain=v1"],
                new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc)));

        Assert.AreEqual(RepositoryProvisioningFailureCodes.GitUnavailable, failure.ReasonCode);
    }

    [TestMethod]
    public async Task GitRunner_UnsafeWorkspaceExecutableDoesNotBreakConstructionAndFailsOnlyWhenInvoked()
    {
        using var workspace = new ProvisioningTestWorkspace();
        var unsafeExecutable = Path.Combine(workspace.Root, OperatingSystem.IsWindows() ? "git.exe" : "git");
        File.WriteAllText(unsafeExecutable, "not an executable\n");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WorkbenchRepositorySetup:ApprovedWorkspaceRoot"] = workspace.Root,
            ["WorkbenchRepositoryProvisioning:GitExecutable"] = unsafeExecutable,
            ["WorkbenchRepositoryProvisioning:GitTimeoutSeconds"] = "30"
        }).Build();

        var runner = new RepositoryProvisioningGitRunner(configuration);
        var failure = await Assert.ThrowsExactlyAsync<RepositoryProvisioningExecutionException>(() =>
            runner.RunAsync(
                workspace.Root,
                ["status", "--porcelain=v1"],
                new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc)));

        Assert.AreEqual(RepositoryProvisioningFailureCodes.GitUnavailable, failure.ReasonCode);
    }

    private static RepositoryProvisioningExecutor CreateProvisioningExecutor(
        IRepositoryProvisioningFailureInjector injector,
        IRepositoryProvisioningDirectoryCreator? directoryCreator = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WorkbenchRepositoryProvisioning:GitExecutable"] = "git",
            ["WorkbenchRepositoryProvisioning:GitTimeoutSeconds"] = "30"
        }).Build();
        return new RepositoryProvisioningExecutor(
            new DirectChildTestPathPolicy(),
            new RepositoryProvisioningGitRunner(configuration),
            directoryCreator ?? new RepositoryProvisioningDirectoryCreator(),
            injector);
    }

    private static RepositoryProvisioningExecutionRequest CreateProvisioningRequest(
        string root,
        string readmeTemplate = "# {{SOLUTION_NAME}}\n")
    {
        var bundle = new RepositorySetupTemplateBundle(
            1,
            "test-profile",
            [new RepositorySetupTemplateFileDefinition(1, "README.md", readmeTemplate)]);
        var templateHash = RepositorySetupTemplateBundleCodec.ComputeHash(bundle);
        var profile = new RepositorySetupProfileSummary(
            "test-profile",
            "Test profile",
            RepositoryProfileCompatibilityStates.NoPreference,
            "No preference",
            RepositoryPlanningReadinessStates.PreviewPlanningOnly,
            RepositoryProfileCertificationStates.NotCertificationReady,
            new string('a', 64),
            templateHash);
        var draft = new RepositorySetupPlanPreview(
            1,
            "ProjectUnderstanding",
            42,
            "Sample",
            7001,
            1,
            1,
            new string('b', 64),
            1,
            new string('a', 64),
            RepositorySetupPreviewStates.ReadyForConfirmation,
            RepositorySetupReasonCodes.Ready,
            "Ready",
            profile,
            Path.Combine(root, "sample-42"),
            "Sample",
            "Sample.App",
            "Sample.Tests",
            "Sample.slnx",
            "src/Sample.App/Sample.App.csproj",
            "tests/Sample.Tests/Sample.Tests.csproj",
            templateHash,
            new string('c', 64),
            "net10.0-windows",
            "C#",
            "WinForms",
            "MSTest",
            "10.0.302",
            "10.0.10",
            "restore-not-executed",
            "build-not-executed",
            "test-not-executed",
            "toolchain",
            "image",
            "main",
            true,
            true,
            "sandbox",
            "resource",
            string.Empty);
        var plan = draft with { PlanHash = RepositorySetupPlanCodec.ComputeHash(draft) };
        return new RepositoryProvisioningExecutionRequest(
            Guid.NewGuid(),
            root,
            plan,
            bundle,
            new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc));
    }

    private static RepositoryProvisioningExecutionRequest CreatePinnedProvisioningRequest(string root)
    {
        var descriptor = new RepositorySetupProfileCatalog().GetAll().Single();
        const string solutionName = "FailureInjectionBeforeStagingCreate";
        const string appProjectName = "FailureInjectionBeforeStagingCreate.App";
        const string testProjectName = "FailureInjectionBeforeStagingCreate.Tests";
        var profile = new RepositorySetupProfileSummary(
            descriptor.ProfileDefinitionId,
            descriptor.DisplayName,
            RepositoryProfileCompatibilityStates.NoPreference,
            "No preference",
            descriptor.PlanningReadiness,
            descriptor.CertificationState,
            descriptor.DescriptorSha256,
            descriptor.TemplateBundleSha256);
        var solutionPath = descriptor.SolutionPathTemplate.Replace("{SolutionName}", solutionName);
        var appPath = descriptor.AppProjectPathTemplate.Replace("{AppProjectName}", appProjectName);
        var testPath = descriptor.TestProjectPathTemplate.Replace("{TestProjectName}", testProjectName);
        var draft = new RepositorySetupPlanPreview(
            1,
            "ProjectUnderstanding",
            42,
            solutionName,
            7001,
            1,
            1,
            new string('b', 64),
            descriptor.Revision,
            descriptor.DescriptorSha256,
            RepositorySetupPreviewStates.ReadyForConfirmation,
            RepositorySetupReasonCodes.Ready,
            "Ready",
            profile,
            Path.Combine(root, "failure-injection-beforestagingcreate-1551"),
            solutionName,
            appProjectName,
            testProjectName,
            solutionPath,
            appPath,
            testPath,
            descriptor.TemplateBundleSha256,
            new string('c', 64),
            descriptor.TargetFramework,
            descriptor.Language,
            descriptor.ApplicationKind,
            descriptor.TestFramework,
            descriptor.SdkVersion,
            descriptor.RuntimeVersion,
            descriptor.RestoreCommandTemplate.Replace("{SolutionPath}", solutionPath),
            descriptor.BuildCommandTemplate.Replace("{SolutionPath}", solutionPath),
            descriptor.TestCommandTemplate.Replace("{TestProjectPath}", testPath),
            descriptor.ToolchainManifestId,
            descriptor.ExecutionImageReference,
            "main",
            true,
            true,
            "sandbox",
            "resource",
            string.Empty);
        var plan = draft with { PlanHash = RepositorySetupPlanCodec.ComputeHash(draft) };
        return new RepositoryProvisioningExecutionRequest(
            Guid.NewGuid(),
            root,
            plan,
            descriptor.TemplateBundle,
            new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc));
    }

    private static string StagingPath(RepositoryProvisioningExecutionRequest request)
    {
        var target = request.ConfirmedPlan.TargetPath;
        return Path.Combine(
            Path.GetDirectoryName(target)!,
            $".{Path.GetFileName(target)}.irondev-{request.AttemptId:N}.staging");
    }

    private static DateTime ReadPublishedAtUtc(string markerPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(markerPath));
        return document.RootElement.GetProperty("publishedAtUtc").GetDateTime();
    }

    private sealed class DirectChildTestPathPolicy : IRepositorySetupPathPolicy
    {
        public RepositorySetupPathAssessment Assess(
            string approvedWorkspaceRoot,
            string directChildName,
            bool inspectEnvironment) => new(
                true,
                false,
                RepositorySetupReasonCodes.Ready,
                "Ready",
                Path.GetFullPath(approvedWorkspaceRoot),
                Path.GetFullPath(Path.Combine(approvedWorkspaceRoot, directChildName)));
    }

    private sealed class RecordingProvisioningFailureInjector : IRepositoryProvisioningFailureInjector
    {
        public RepositoryProvisioningFailurePoint? FailurePoint { get; set; }
        public Action<RepositoryProvisioningFailurePoint>? Callback { get; set; }

        public void ThrowIfRequested(RepositoryProvisioningFailurePoint point)
        {
            Callback?.Invoke(point);
            if (FailurePoint == point)
                throw new InvalidOperationException("Injected provisioning failure.");
        }
    }

    private sealed class RacingDirectoryCreator : IRepositoryProvisioningDirectoryCreator
    {
        public void CreateNew(string path)
        {
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "foreign-sentinel.txt"), "foreign\n");
            throw new IOException("Simulated atomic create collision.");
        }
    }

    private static string RunGit(string repository, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = repository,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)
                            ?? throw new InvalidOperationException("Git did not start.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git test setup failed: {error}");
        return output;
    }

    private sealed class ProvisioningTestWorkspace : IDisposable
    {
        public ProvisioningTestWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "IronDevRepositoryProvisioning", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (!Directory.Exists(Root))
                return;
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            foreach (var directory in Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(value => value.Length))
                File.SetAttributes(directory, FileAttributes.Directory);
            File.SetAttributes(Root, FileAttributes.Directory);
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class StaticForbiddenRoots(IReadOnlyList<string> roots)
        : IRepositorySetupForbiddenRootCatalog
    {
        public IReadOnlyList<string> GetForbiddenRoots() => roots;
    }

    private sealed class RecordingFileSystem(string root) : IRepositorySetupFileSystemInspector
    {
        public int DirectoryReadCount { get; private set; }
        public int WriteCount => 0;
        public string? ExistingTarget { get; init; }
        public string? ReparsePath { get; init; }

        public bool DirectoryExists(string path)
        {
            DirectoryReadCount++;
            return string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(path, ExistingTarget, StringComparison.OrdinalIgnoreCase);
        }

        public bool FileExists(string path) => false;

        public FileAttributes GetAttributes(string path) =>
            string.Equals(path, ReparsePath, StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.Directory | FileAttributes.ReparsePoint
                : FileAttributes.Directory;
    }
}
