using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Models;
using IronDev.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class CodeProposalValidatorTests
{
    [TestMethod]
    public void Validate_ShouldRejectUnsafeHttpGetEqualsPaths()
    {
        var validator = new CodeProposalValidator(new CodeRunProfileCatalog());

        foreach (var path in new[] { "health", "//example.com/health", "http://example.com/health", "/health#fragment", "/health\\evil" })
        {
            var result = validator.Validate(CreateAspNetProposal(path));

            Assert.IsFalse(result.IsValid, $"Path '{path}' should be rejected.");
            Assert.IsTrue(
                result.Errors.Any(error => error.Contains("HttpGetEquals path", StringComparison.OrdinalIgnoreCase)),
                $"Path '{path}' should produce an HttpGetEquals path validation error. Errors: {string.Join(" | ", result.Errors)}");
        }
    }

    [TestMethod]
    public void Validate_ShouldRequireVerificationParametersByKind()
    {
        var validator = new CodeProposalValidator(new CodeRunProfileCatalog());

        var missingHttpExpected = validator.Validate(CreateAspNetProposal("/health", parameters: new Dictionary<string, string>
        {
            ["path"] = "/health"
        }));
        Assert.IsFalse(missingHttpExpected.IsValid);
        Assert.IsTrue(missingHttpExpected.Errors.Contains("Verification 'HttpGetEquals' requires parameter 'expected'."));

        var missingStdoutExpected = validator.Validate(CreateConsoleProposal(new ScenarioVerification
        {
            Kind = "StdoutContains",
            Description = "Output contains expected text.",
            Parameters = new Dictionary<string, string>()
        }));
        Assert.IsFalse(missingStdoutExpected.IsValid);
        Assert.IsTrue(missingStdoutExpected.Errors.Contains("Verification 'StdoutContains' requires parameter 'expected'."));
    }

    [TestMethod]
    public void Validate_ShouldAllowLocalHttpGetEqualsPath()
    {
        var validator = new CodeProposalValidator(new CodeRunProfileCatalog());

        var result = validator.Validate(CreateAspNetProposal("/health"));

        Assert.IsTrue(result.IsValid, string.Join(" | ", result.Errors));
    }

    private static CodeProposal CreateAspNetProposal(string path, IReadOnlyDictionary<string, string>? parameters = null) =>
        CreateProposal(
            "dotnet.aspnet",
            new CodeRunProfile
            {
                RuntimeProfileId = "dotnet.aspnet",
                WorkingDirectory = "HealthApi",
                BuildCommand = "dotnet build --nologo",
                RunCommand = "dotnet run --no-build --no-launch-profile --nologo"
            },
            new ScenarioVerification
            {
                Kind = "HttpGetEquals",
                Description = "GET /health returns healthy.",
                Parameters = parameters ?? new Dictionary<string, string>
                {
                    ["path"] = path,
                    ["expected"] = "healthy"
                }
            });

    private static CodeProposal CreateConsoleProposal(ScenarioVerification verification) =>
        CreateProposal(
            "dotnet.console",
            new CodeRunProfile
            {
                RuntimeProfileId = "dotnet.console",
                WorkingDirectory = "ConsoleApp",
                BuildCommand = "dotnet build --nologo",
                RunCommand = "dotnet run --no-build --nologo"
            },
            verification);

    private static CodeProposal CreateProposal(string scenarioId, CodeRunProfile profile, ScenarioVerification verification)
    {
        const string program = "Console.WriteLine(\"hello\");";
        return new CodeProposal
        {
            ProposalId = "proposal-test",
            ProjectId = 1,
            TicketId = 2,
            ReviewId = "review-test",
            ScenarioId = scenarioId,
            ExpectedOutput = "hello",
            RunProfile = profile,
            Files =
            [
                new GeneratedCodeFile
                {
                    RelativePath = $"{profile.WorkingDirectory}/Program.cs",
                    Content = program,
                    Sha256 = ComputeSha256(program)
                }
            ],
            Verifications = [verification]
        };
    }

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
