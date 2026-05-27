using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Models;
using IronDev.Infrastructure.Services;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CodeProposalValidationTests
{
    [TestMethod]
    public void Validator_AcceptsSmallConsoleProposal()
    {
        var validator = CreateValidator();
        var proposal = CreateValidProposal();

        var result = validator.Validate(proposal);

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Errors));
        Assert.AreEqual("dotnet.console", result.RuntimeProfile?.RuntimeProfileId);
    }

    [TestMethod]
    public void Validator_RejectsPathsCommandsProfilesAndVerificationEscapes()
    {
        var validator = CreateValidator();
        var proposal = CreateValidProposal() with
        {
            Files =
            [
                new GeneratedCodeFile
                {
                    RelativePath = "..\\RealRepo\\Program.cs",
                    Content = "Console.WriteLine(\"bad\");",
                    Sha256 = ComputeSha256("Console.WriteLine(\"bad\");")
                },
                new GeneratedCodeFile
                {
                    RelativePath = "C:\\RealRepo\\Other.cs",
                    Content = "Console.WriteLine(\"bad\");",
                    Sha256 = "not-a-real-hash"
                }
            ],
            RunProfile = new CodeRunProfile
            {
                RuntimeProfileId = "shell.anything",
                WorkingDirectory = "..\\RealRepo",
                BuildCommand = "powershell Remove-Item -Recurse",
                RunCommand = "cmd /c whoami"
            },
            Verifications =
            [
                new ScenarioVerification
                {
                    Kind = "RunShell",
                    Description = "Try arbitrary command execution.",
                    Parameters = new Dictionary<string, string> { ["command"] = "whoami" }
                }
            ]
        };

        var result = validator.Validate(proposal);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(string.Join(Environment.NewLine, result.Errors), "not allow-listed");
        StringAssert.Contains(string.Join(Environment.NewLine, result.Errors), "must not contain '..'");
        StringAssert.Contains(string.Join(Environment.NewLine, result.Errors), "must be relative");
        StringAssert.Contains(string.Join(Environment.NewLine, result.Errors), "invalid SHA-256");
    }

    [TestMethod]
    public void Validator_RejectsModelCommandMutationForAllowedProfile()
    {
        var validator = CreateValidator();
        var proposal = CreateValidProposal() with
        {
            RunProfile = new CodeRunProfile
            {
                RuntimeProfileId = "dotnet.console",
                WorkingDirectory = "GeneratedApp",
                BuildCommand = "dotnet publish",
                RunCommand = "dotnet test"
            }
        };

        var result = validator.Validate(proposal);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(string.Join(Environment.NewLine, result.Errors), "BuildCommand must match");
        StringAssert.Contains(string.Join(Environment.NewLine, result.Errors), "RunCommand must match");
    }

    private static CodeProposalValidator CreateValidator() => new(new CodeRunProfileCatalog());

    private static CodeProposal CreateValidProposal()
    {
        const string program = "Console.WriteLine(\"Hello\");";
        return new CodeProposal
        {
            ProposalId = "proposal-1",
            ProjectId = 7,
            TicketId = 42,
            ReviewId = "review-1",
            ScenarioId = "model.assisted",
            ExpectedOutput = "Hello",
            Files =
            [
                new GeneratedCodeFile
                {
                    RelativePath = "GeneratedApp/Program.cs",
                    Content = program,
                    Sha256 = ComputeSha256(program)
                }
            ],
            RunProfile = new CodeRunProfile
            {
                RuntimeProfileId = "dotnet.console",
                WorkingDirectory = "GeneratedApp",
                BuildCommand = "dotnet build --nologo",
                RunCommand = "dotnet run --no-build --nologo"
            },
            Verifications =
            [
                new ScenarioVerification
                {
                    Kind = "StdoutContains",
                    Description = "Output includes greeting.",
                    Parameters = new Dictionary<string, string> { ["expected"] = "Hello" }
                }
            ]
        };
    }

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
