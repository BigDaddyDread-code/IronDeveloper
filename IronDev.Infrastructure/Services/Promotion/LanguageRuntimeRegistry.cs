using IronDev.Core.Promotion;

namespace IronDev.Infrastructure.Services.Promotion;

public sealed class LanguageRuntimeRegistry : ILanguageRuntimeRegistry
{
    private readonly IReadOnlyList<LanguageRuntimeProfile> _profiles;

    public LanguageRuntimeRegistry()
    {
        _profiles =
        [
            new LanguageRuntimeProfile
            {
                RuntimeProfileId = "csharp-dotnet",
                TargetLanguage = "CSharp",
                TargetStack = ".NET",
                Availability = LanguageRuntimeAvailability.Executable,
                BuildTool = "dotnet build",
                TestTool = "dotnet test",
                SourceFileExtensions = [".cs", ".xaml", ".csproj", ".sln"],
                ProjectFileNames = ["*.csproj", "*.sln"],
                DependencyFileNames = ["*.csproj", "Directory.Packages.props", "packages.lock.json"],
                TestFilePatterns = ["*Tests.cs", "*.Tests.csproj"],
                ForbiddenPathSegments = ["bin/", "obj/", ".vs/", ".git/", "TestResults/"],
                EvidenceRequirements = ["dotnet build success", "dotnet test success", "real repo mutation count zero"],
                KnownRisks = ["Generated UI can compile while remaining shallow; product review still required."]
            },
            UnavailableRuntime("java-maven", "Java", "Maven", "mvn test", "mvn test", [".java", ".xml", ".properties"], ["pom.xml"]),
            UnavailableRuntime("typescript-node", "TypeScript", "Node", "npm test", "npm test", [".ts", ".tsx", ".js", ".json"], ["package.json", "tsconfig.json"]),
            UnavailableRuntime("python-pytest", "Python", "Pytest", "python -m pytest", "python -m pytest", [".py", ".toml", ".txt"], ["pyproject.toml", "requirements.txt"])
        ];
    }

    public IReadOnlyList<LanguageRuntimeProfile> ListProfiles() => _profiles;

    public LanguageRuntimeProfile GetRequired(string runtimeProfileId) =>
        _profiles.FirstOrDefault(profile => string.Equals(profile.RuntimeProfileId, runtimeProfileId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Unknown language runtime profile: {runtimeProfileId}");

    public LanguageRuntimeProfile DetectForWorkspace(string workspacePath)
    {
        if (!Directory.Exists(workspacePath))
        {
            throw new DirectoryNotFoundException($"Cannot detect language runtime because workspace does not exist: {workspacePath}");
        }

        foreach (var profile in _profiles)
        {
            if (ContainsAnyProjectFile(workspacePath, profile.ProjectFileNames))
            {
                return profile;
            }
        }

        throw new InvalidOperationException($"No supported language runtime profile could be detected for workspace: {workspacePath}");
    }

    private static bool ContainsAnyProjectFile(string workspacePath, IReadOnlyList<string> filePatterns) =>
        filePatterns.Any(pattern => Directory.EnumerateFiles(workspacePath, pattern, SearchOption.AllDirectories).Any());

    private static LanguageRuntimeProfile UnavailableRuntime(
        string id,
        string language,
        string stack,
        string buildTool,
        string testTool,
        IReadOnlyList<string> extensions,
        IReadOnlyList<string> projectFiles) =>
        new()
        {
            RuntimeProfileId = id,
            TargetLanguage = language,
            TargetStack = stack,
            Availability = LanguageRuntimeAvailability.NotExecutableYet,
            BuildTool = buildTool,
            TestTool = testTool,
            SourceFileExtensions = extensions,
            ProjectFileNames = projectFiles,
            DependencyFileNames = projectFiles,
            TestFilePatterns = [],
            ForbiddenPathSegments = ["bin/", "build/", "dist/", "target/", ".git/"],
            EvidenceRequirements = ["runtime executor implementation required before build/test execution"],
            KnownRisks = ["Profile contract exists, but execution is intentionally unavailable until a reviewed adapter is added."],
            Boundary = "Profile contract only. This runtime is not executable yet and must not claim build/test support."
        };
}
