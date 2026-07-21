using IronDev.Core.Workbench;
using IronDev.Infrastructure.Services;

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
