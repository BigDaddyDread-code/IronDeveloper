namespace IronDev.UnitTests.Governance;

internal sealed record FileScopeCase(
    string Id,
    string CandidatePath,
    string[] AllowedGlobs,
    string[] ForbiddenGlobs,
    bool ExpectedAllowed,
    bool ExpectedForbidden);

internal sealed record GlobMatchCase(
    string Id,
    string CandidatePath,
    string Glob,
    bool ExpectedMatch);

internal static class FileScopeMatchingPropertyFixtures
{
    internal const string Repository = "repo:g11";
    internal const string Branch = "feature/g11";
    internal const string RunId = "run:g11";
    internal const string PatchHash = "sha256:g11abcdef";

    internal static readonly DateTimeOffset ObservedAtUtc =
        new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);

    internal static IReadOnlyList<string?> SafeRelativePaths { get; } =
    [
        "src/App.cs",
        "src/Feature/Handler.cs",
        "tests/FeatureTests.cs",
        "docs/readme.md",
        "README.md",
        "src/a-b_c/File.Name.cs",
        "src/nested/path/file.test.cs",
        "SRC/Case/Path.cs",
        @"src\windows\path.cs"
    ];

    internal static IReadOnlyList<string?> UnsafePaths { get; } =
    [
        null,
        string.Empty,
        "   ",
        "/path/rooted.cs",
        "//server/share/file.cs",
        "~/profile/file.cs",
        "~",
        "file://repo/file.cs",
        "http://repo/file.cs",
        "https://repo/file.cs",
        "C:/repo/file.cs",
        @"C:\repo\file.cs",
        "../outside.cs",
        "src/../outside.cs",
        "src/feature/../../outside.cs",
        @"..\outside.cs",
        @"src\..\outside.cs"
    ];

    internal static IReadOnlyList<GlobMatchCase> AllowedGlobCases { get; } =
    [
        new("single-star-direct", "src/App.cs", "src/*.cs", true),
        new("single-star-does-not-cross", "src/Feature/App.cs", "src/*.cs", false),
        new("double-star-one-segment", "src/Feature/App.cs", "src/**/*.cs", true),
        new("double-star-multi-segment", "src/Feature/Nested/App.cs", "src/**/*.cs", true),
        new("tests-double-star", "tests/Unit/FooTests.cs", "tests/**/*.cs", true),
        new("docs-single-star", "docs/readme.md", "docs/*.md", true),
        new("readme-literal", "README.md", "README.md", true),
        new("question-mark-one-char", "src/Feature/Handler.c", "src/Feature/Handler.?", true),
        new("question-mark-two-chars", "src/Feature/Handler.cs", "src/Feature/Handler.?", false),
        new("handler-double-star", "src/Feature/Handler.cs", "src/**/Handler.cs", true)
    ];

    internal static IReadOnlyList<GlobMatchCase> ForbiddenGlobCases { get; } =
    [
        new("direct-secret", "src/Secrets/Credential.cs", "src/Secrets/*.cs", true),
        new("nested-secret", "src/Feature/Secrets/Credential.cs", "src/**/Secrets/*.cs", true),
        new("designer", "src/View/App.Designer.cs", "**/*.Designer.cs", true),
        new("generated", "src/View/App.generated.cs", "**/*.generated.cs", true),
        new("bin-output", "src/bin/Debug/App.dll", "**/bin/**", true),
        new("obj-output", "src/obj/Debug/App.g.cs", "**/obj/**", true),
        new("pem-root", "certificate.pem", "*.pem", true),
        new("key-root", "certificate.key", "*.key", true),
        new("env-root", ".env", ".env", true)
    ];

    internal static IReadOnlyList<FileScopeCase> ScopeCases { get; } =
    [
        new("allowed-only", "src/App.cs", ["src/*.cs"], [], true, false),
        new("forbidden-only", "src/Secrets/Credential.cs", ["tests/*.cs"], ["src/Secrets/*.cs"], false, true),
        new("neither", "docs/readme.md", ["src/*.cs"], ["src/Secrets/*.cs"], false, false),
        new("both-forbidden-wins", "src/Feature/Secrets/Credential.cs", ["src/**/*.cs"], ["src/**/Secrets/*.cs"], false, true),
        new("unsafe-candidate", "../outside.cs", ["../*.cs", "src/*.cs"], [], false, true),
        new("unsafe-allowed-glob", "src/App.cs", ["../*.cs", "C:/repo/*.cs"], [], false, false),
        new("unsafe-forbidden-glob-ignored", "src/App.cs", ["src/*.cs"], ["../*.cs", "C:/repo/*.cs"], true, false),
        new("empty-allowed-safe", "src/App.cs", [], [], false, false),
        new("empty-forbidden-unsafe", "../outside.cs", [], [], false, true)
    ];

    internal static IReadOnlyList<FileScopeCase> NormalizationCases { get; } =
    [
        new("candidate-whitespace", "  src/App.cs  ", ["src/*.cs"], [], true, false),
        new("glob-whitespace", "src/App.cs", ["  src/*.cs  "], [], true, false),
        new("backslash-candidate", @"src\Feature\Handler.cs", ["src/**/Handler.cs"], [], true, false),
        new("backslash-glob", "src/Feature/Handler.cs", [@"src\**\Handler.cs"], [], true, false),
        new("case-insensitive-candidate", "SRC/FEATURE/HANDLER.CS", ["src/**/handler.cs"], [], true, false),
        new("case-insensitive-glob", "src/feature/handler.cs", ["SRC/**/HANDLER.CS"], [], true, false)
    ];

    internal static BoundedRunAuthorityGrant Grant(
        string[]? allowedGlobs = null,
        string[]? forbiddenGlobs = null,
        string repository = Repository,
        string branch = Branch,
        string runId = RunId,
        RunAuthorityOperationKind allowedOperation = RunAuthorityOperationKind.PatchPackageWrite) =>
        new()
        {
            GrantId = "grant:g11:file-scope",
            Repository = repository,
            Branch = branch,
            RunId = runId,
            AllowedOperationKinds = [allowedOperation],
            AllowedFileGlobs = allowedGlobs ?? ["src/**/*.cs"],
            ForbiddenFileGlobs = forbiddenGlobs ?? ["src/**/Secrets/*.cs"],
            PatchHash = PatchHash,
            ExpiresAtUtc = ObservedAtUtc.AddHours(1),
            MaxMutations = 1,
            RequiredValidation =
            [
                new BoundedRunAuthorityRequiredValidation
                {
                    ValidationKind = "FocusedG11",
                    MustPass = true,
                    EvidenceRefPrefixes = ["validation-result:"]
                }
            ],
            StopBeforeOperationKinds = RunAuthorityProfileValidator.BoundedRunAuthorityForbiddenOperations,
            GrantedBy = new BoundedRunAuthorityGrantedBy
            {
                PrincipalId = "human:g11",
                PrincipalKind = "Human",
                EvidenceRef = "human-approval:g11"
            },
            HumanReadableIntent = "G11 file-scope matcher fixture."
        };

    internal static BoundedRunAuthorityGrantDecision Match(
        string filePath,
        string[]? allowedGlobs = null,
        string[]? forbiddenGlobs = null) =>
        BoundedRunAuthorityGrantMatcher.Evaluate(
            Grant(allowedGlobs, forbiddenGlobs),
            ObservedAtUtc,
            Repository,
            Branch,
            RunId,
            RunAuthorityOperationKind.PatchPackageWrite,
            filePath);

    internal static string RepoRoot() => GovernanceValidatorTestFixtures.RepoRoot();
}
