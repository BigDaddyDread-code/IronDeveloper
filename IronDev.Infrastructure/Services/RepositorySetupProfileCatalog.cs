using IronDev.Core.Workbench;
using System.Collections.Immutable;

namespace IronDev.Infrastructure.Services;

public sealed class RepositorySetupProfileCatalog : IRepositorySetupProfileCatalog, IRepositorySetupTemplateBundleCatalog
{
    private readonly IReadOnlyList<RepositorySetupProfileDescriptor> _profiles;

    public RepositorySetupProfileCatalog()
    {
        const string profileId = RepositorySetupProfileIds.GreenfieldWinFormsNet10MstestV1;
        const int revision = 1;
        const string displayName = ".NET 10 Windows Forms with MSTest (planning preview)";
        const string targetFramework = "net10.0-windows";
        const string language = "C#";
        const string applicationKind = "WinForms";
        const string testFramework = "MSTest";
        const string sdkVersion = "10.0.302";
        const string runtimeVersion = "10.0.10";
        const string toolchain = "dotnet-sdk-10.0.302-runtime-10.0.10-planning-v1";
        const string executionImage = "mcr.microsoft.com/dotnet/sdk:10.0-windowsservercore-ltsc2025";
        const string solutionPath = "{SolutionName}.slnx";
        const string appPath = "src/{AppProjectName}/{AppProjectName}.csproj";
        const string testPath = "tests/{TestProjectName}/{TestProjectName}.csproj";
        const string restore = "dotnet restore \"{SolutionPath}\" --configfile C:\\IronDev\\NuGet.Config --locked-mode";
        const string build = "dotnet build \"{SolutionPath}\" --configuration Release --no-restore";
        const string test = "dotnet test \"{TestProjectPath}\" --configuration Release --no-restore --no-build";
        var templateBundle = new RepositorySetupTemplateBundle(
            1,
            profileId,
            ImmutableArray.Create(
                new RepositorySetupTemplateFileDefinition(
                    1,
                    "{{SOLUTION_NAME}}.slnx",
                    RepositorySetupPinnedTemplateContent.Solution),
                new RepositorySetupTemplateFileDefinition(
                    2,
                    "global.json",
                    "{\n  \"sdk\": {\n    \"version\": \"10.0.302\",\n    \"rollForward\": \"disable\",\n    \"allowPrerelease\": false\n  }\n}\n"),
                new RepositorySetupTemplateFileDefinition(3, ".gitattributes", "* text=auto eol=lf\n"),
                new RepositorySetupTemplateFileDefinition(4, ".gitignore", "bin/\nobj/\n.vs/\nTestResults/\n"),
                new RepositorySetupTemplateFileDefinition(
                    5,
                    "Directory.Build.props",
                    "<Project>\n  <PropertyGroup>\n    <LangVersion>latest</LangVersion>\n    <Nullable>enable</Nullable>\n    <ImplicitUsings>enable</ImplicitUsings>\n    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>\n  </PropertyGroup>\n</Project>\n"),
                new RepositorySetupTemplateFileDefinition(
                    6,
                    "src/{{APP_PROJECT_NAME}}/{{APP_PROJECT_NAME}}.csproj",
                    "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>WinExe</OutputType>\n    <TargetFramework>net10.0-windows</TargetFramework>\n    <UseWindowsForms>true</UseWindowsForms>\n  </PropertyGroup>\n</Project>\n"),
                new RepositorySetupTemplateFileDefinition(
                    7,
                    "src/{{APP_PROJECT_NAME}}/Program.cs",
                    "namespace {{APP_PROJECT_NAME}};\n\ninternal static class Program\n{\n    [STAThread]\n    private static void Main()\n    {\n        ApplicationConfiguration.Initialize();\n        Application.Run(new MainForm());\n    }\n}\n"),
                new RepositorySetupTemplateFileDefinition(
                    8,
                    "src/{{APP_PROJECT_NAME}}/MainForm.cs",
                    "namespace {{APP_PROJECT_NAME}};\n\npublic sealed class MainForm : Form\n{\n    public MainForm()\n    {\n        Text = \"{{SOLUTION_NAME}}\";\n    }\n}\n"),
                new RepositorySetupTemplateFileDefinition(
                    9,
                    "src/{{APP_PROJECT_NAME}}/packages.lock.json",
                    RepositorySetupPinnedTemplateContent.ApplicationPackagesLock),
                new RepositorySetupTemplateFileDefinition(
                    10,
                    "tests/{{TEST_PROJECT_NAME}}/{{TEST_PROJECT_NAME}}.csproj",
                    "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0-windows</TargetFramework>\n    <IsPackable>false</IsPackable>\n    <IsTestProject>true</IsTestProject>\n  </PropertyGroup>\n  <ItemGroup>\n    <PackageReference Include=\"MSTest\" Version=\"4.0.2\" />\n    <ProjectReference Include=\"..\\..\\src\\{{APP_PROJECT_NAME}}\\{{APP_PROJECT_NAME}}.csproj\" />\n  </ItemGroup>\n  <ItemGroup>\n    <Using Include=\"Microsoft.VisualStudio.TestTools.UnitTesting\" />\n  </ItemGroup>\n</Project>\n"),
                new RepositorySetupTemplateFileDefinition(
                    11,
                    "tests/{{TEST_PROJECT_NAME}}/TestAssembly.cs",
                    "using Microsoft.VisualStudio.TestTools.UnitTesting;\n\n[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]\n"),
                new RepositorySetupTemplateFileDefinition(
                    12,
                    "tests/{{TEST_PROJECT_NAME}}/SmokeTests.cs",
                    "namespace {{TEST_PROJECT_NAME}};\n\n[TestClass]\npublic sealed class SmokeTests\n{\n    [TestMethod]\n    public void Application_project_is_loadable() =>\n        Assert.AreEqual(\"{{APP_PROJECT_NAME}}\", typeof({{APP_PROJECT_NAME}}.MainForm).Assembly.GetName().Name);\n}\n"),
                new RepositorySetupTemplateFileDefinition(
                    13,
                    "tests/{{TEST_PROJECT_NAME}}/packages.lock.json",
                    RepositorySetupPinnedTemplateContent.TestPackagesLock)
            ));
        var templateBundleHash = RepositorySetupTemplateBundleCodec.ComputeHash(templateBundle);
        if (!string.Equals(
                templateBundleHash,
                RepositorySetupPinnedProfileHashes.GreenfieldWinFormsNet10MstestV1TemplateBundleRevision1,
                StringComparison.Ordinal))
            throw new RepositorySetupIntegrityException(
                "The pinned template bundle changed without a profile revision and reviewed hash update.");

        var canonicalDescriptor = RepositorySetupCanonicalJson.Serialize(new
        {
            schemaVersion = 1,
            profileDefinitionId = profileId,
            revision,
            displayName,
            targetFramework,
            language,
            applicationKind,
            testFramework,
            sdkVersion,
            runtimeVersion,
            toolchainManifestId = toolchain,
            executionImageReference = executionImage,
            solutionPathTemplate = solutionPath,
            appProjectPathTemplate = appPath,
            testProjectPathTemplate = testPath,
            restoreCommandTemplate = restore,
            buildCommandTemplate = build,
            testCommandTemplate = test,
            planningReadiness = RepositoryPlanningReadinessStates.PreviewPlanningOnly,
            certificationState = RepositoryProfileCertificationStates.NotCertificationReady,
            templateBundleSha256 = templateBundleHash
        });
        var descriptorHash = RepositorySetupCanonicalJson.Sha256(canonicalDescriptor);
        if (!string.Equals(
                descriptorHash,
                RepositorySetupPinnedProfileHashes.GreenfieldWinFormsNet10MstestV1DescriptorRevision1,
                StringComparison.Ordinal))
            throw new RepositorySetupIntegrityException(
                "The pinned planning descriptor changed without a profile revision and reviewed hash update.");

        _profiles = ImmutableArray.Create(
            new RepositorySetupProfileDescriptor(
                profileId,
                revision,
                displayName,
                targetFramework,
                language,
                applicationKind,
                testFramework,
                sdkVersion,
                runtimeVersion,
                toolchain,
                executionImage,
                solutionPath,
                appPath,
                testPath,
                restore,
                build,
                test,
                RepositoryPlanningReadinessStates.PreviewPlanningOnly,
                RepositoryProfileCertificationStates.NotCertificationReady,
                templateBundle,
                descriptorHash,
                templateBundleHash));
    }

    public IReadOnlyList<RepositorySetupProfileDescriptor> GetAll() => _profiles;

    public RepositorySetupProfileDescriptor? Find(
        string profileDefinitionId,
        int? revision = null,
        string? descriptorSha256 = null) =>
        _profiles.SingleOrDefault(value =>
            string.Equals(value.ProfileDefinitionId, profileDefinitionId, StringComparison.Ordinal) &&
            (revision is null || value.Revision == revision) &&
            (descriptorSha256 is null ||
             string.Equals(value.DescriptorSha256, descriptorSha256, StringComparison.Ordinal)));

    public RepositorySetupTemplateBundle? FindBundle(
        string profileDefinitionId,
        int revision,
        string descriptorSha256) =>
        Find(profileDefinitionId, revision, descriptorSha256)?.TemplateBundle;
}
