using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Core.Runs;
using IronDev.Core.Tools;
using IronDev.Data.Models;
using IronDev.Infrastructure.Tools.CodeStandards;
using IronDev.Services;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;
public sealed class DeterministicCodeProposalGenerator : ICodeProposalGenerator
{
    private readonly DiscussionCodeScenarioCatalog _scenarios;
    private readonly ICodeRunProfileCatalog _profiles;

    public DeterministicCodeProposalGenerator(
        DiscussionCodeScenarioCatalog scenarios,
        ICodeRunProfileCatalog profiles)
    {
        _scenarios = scenarios;
        _profiles = profiles;
    }

    public Task<CodeProposal> GenerateAsync(
        TicketReviewResult review,
        string expectedOutput,
        CancellationToken cancellationToken = default)
    {
        var scenario = _scenarios.Get(review.ScenarioId);
        if (scenario is null)
            throw new InvalidOperationException($"No deterministic code proposal scenario is registered for '{review.ScenarioId}'.");
        var profile = _profiles.GetProfile(scenario.Scenario.RuntimeProfileId)
            ?? throw new InvalidOperationException($"No runtime profile is registered for '{scenario.Scenario.RuntimeProfileId}'.");

        var defaultOutput = TryGetFirstExpectedOutput(scenario.Scenario.Verifications);
        if (!string.IsNullOrWhiteSpace(expectedOutput) &&
            !string.Equals(review.ScenarioId, "console.hello-world", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ExpectedOutput overrides are only supported by the Hello World fixture.");
        }

        var output = string.IsNullOrWhiteSpace(expectedOutput) ? defaultOutput : expectedOutput;
        var programText = string.Equals(output, defaultOutput, StringComparison.Ordinal)
            ? scenario.ProgramText
            : BuildSingleWriteLineProgram(output);
        var verifications = string.IsNullOrWhiteSpace(expectedOutput)
            ? scenario.Scenario.Verifications
            :
            [
                new ScenarioVerification
                {
                    Kind = "StdoutContains",
                    Description = "Output contains requested text.",
                    Parameters = new Dictionary<string, string>
                    {
                        ["expected"] = output
                    }
                }
            ];

        return Task.FromResult(new CodeProposal
        {
            ProposalId = $"code-proposal-{review.TicketId}-{Guid.NewGuid():N}",
            ProjectId = review.ProjectId,
            TicketId = review.TicketId,
            ReviewId = review.ReviewId,
            ScenarioId = review.ScenarioId,
            ExpectedOutput = output,
            Files =
            [
                CreateFile($"{scenario.ProjectDirectory}/Program.cs", programText),
                CreateFile($"{scenario.ProjectDirectory}/{scenario.ProjectFileName}", scenario.ProjectFileText)
            ],
            RunProfile = new CodeRunProfile
            {
                RuntimeProfileId = profile.RuntimeProfileId,
                WorkingDirectory = scenario.ProjectDirectory,
                BuildCommand = profile.BuildCommand,
                RunCommand = profile.RunCommand
            },
            Verifications = verifications
        });
    }

    private static string TryGetFirstExpectedOutput(IReadOnlyList<ScenarioVerification> verifications) =>
        verifications
            .Select(item => item.Parameters.TryGetValue("expected", out var expected) ? expected : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? string.Empty;

    private static string BuildSingleWriteLineProgram(string expectedOutput)
    {
        var escapedOutput = expectedOutput.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"Console.WriteLine(\"{escapedOutput}\");{Environment.NewLine}";
    }

    private static GeneratedCodeFile CreateFile(string relativePath, string content) => new()
    {
        RelativePath = relativePath,
        Content = content,
        Sha256 = ComputeSha256(content)
    };

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

