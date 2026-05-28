using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Infrastructure.Services;

public sealed class CodeRunProfileCatalog : ICodeRunProfileCatalog
{
    private static readonly IReadOnlyList<CodeRunProfileDefinition> Profiles =
    [
        new CodeRunProfileDefinition
        {
            RuntimeProfileId = "dotnet.console",
            DisplayName = ".NET console",
            BuildCommand = "dotnet build --nologo",
            RunCommand = "dotnet run --no-build --nologo",
            TimeoutSeconds = 120,
            MaxFileCount = 12,
            MaxFileBytes = 64_000,
            AllowedVerificationKinds = ["StdoutContains", "CommandExitZero"]
        },
        new CodeRunProfileDefinition
        {
            RuntimeProfileId = "dotnet.aspnet",
            DisplayName = "ASP.NET Core API",
            BuildCommand = "dotnet build --nologo",
            RunCommand = "dotnet run --no-build --no-launch-profile --nologo",
            TimeoutSeconds = 180,
            MaxFileCount = 18,
            MaxFileBytes = 96_000,
            AllowedVerificationKinds = ["HttpGetEquals"]
        }
    ];

    public CodeRunProfileDefinition? GetProfile(string runtimeProfileId) =>
        Profiles.FirstOrDefault(profile => string.Equals(profile.RuntimeProfileId, runtimeProfileId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<CodeRunProfileDefinition> GetProfiles() => Profiles;
}
