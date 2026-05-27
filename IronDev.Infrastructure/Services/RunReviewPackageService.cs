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
public sealed class RunReviewPackageService : IRunReviewPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRunStore _runs;
    private readonly IRunEventStore _events;
    private readonly IRunEvidenceService _evidence;

    public RunReviewPackageService(
        IRunStore runs,
        IRunEventStore events,
        IRunEvidenceService evidence)
    {
        _runs = runs;
        _events = events;
        _evidence = evidence;
    }

    public async Task<RunReviewPackage?> GetReviewPackageAsync(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        var run = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run is null || run.ProjectId != projectId || run.TicketId != ticketId)
            return null;

        var events = await _events.GetEventsAsync(runId, cancellationToken).ConfigureAwait(false);
        var evidence = await _evidence.GetEvidenceAsync(runId, cancellationToken).ConfigureAwait(false);
        var generatedFiles = await LoadGeneratedFilesAsync(runId, evidence, cancellationToken).ConfigureAwait(false);
        var commandEvidence = events
            .Where(item => item.EventType == "CommandCompleted")
            .Select(item => new CommandEvidence
            {
                Command = TryPayload(item, "command") ?? "unknown",
                ExitCode = TryPayload(item, "exitCode"),
                StdoutPath = TryPayload(item, "stdoutPath"),
                StderrPath = TryPayload(item, "stderrPath"),
                DurationMs = TryPayload(item, "durationMs")
            })
            .ToArray();

        var outputVerifications = await LoadOutputVerificationsAsync(runId, evidence, cancellationToken).ConfigureAwait(false);
        var outputVerification = outputVerifications.FirstOrDefault()
                                 ?? new OutputVerificationEvidence { Expected = string.Empty, Verified = false };
        var codeStandards = await LoadCodeStandardsAsync(runId, evidence, cancellationToken).ConfigureAwait(false);

        return new RunReviewPackage
        {
            RunId = run.RunId,
            ProjectId = projectId,
            TicketId = ticketId,
            State = run.State.ToString(),
            GeneratedFiles = generatedFiles,
            CommandEvidence = commandEvidence,
            OutputVerification = outputVerification,
            OutputVerifications = outputVerifications,
            CodeStandards = codeStandards,
            FileSetHash = ComputeFileSetHash(generatedFiles),
            Risks = BuildRisks(run, codeStandards, outputVerifications),
            HumanReviewChecklist =
            [
                "Inspect generated files and file hashes.",
                "Confirm dotnet build and dotnet run command evidence.",
                "Confirm output verification matches the ticket acceptance criteria.",
                "Confirm no generated code was applied to the real repository.",
                "Approve or reject the exact package before any future apply step."
            ],
            Events = events.Select(item => new RunEventSummary
            {
                EventType = item.EventType,
                Message = item.Message,
                TimestampUtc = item.TimestampUtc
            }).ToArray()
        };
    }

    private async Task<IReadOnlyList<GeneratedCodeFile>> LoadGeneratedFilesAsync(
        string runId,
        IReadOnlyList<RunEvidenceItem> evidence,
        CancellationToken cancellationToken)
    {
        var proposalItem = evidence.FirstOrDefault(item => item.Path.EndsWith("code-proposal.json", StringComparison.OrdinalIgnoreCase));
        if (proposalItem is not null)
        {
            var proposalText = await _evidence.ReadEvidenceTextAsync(runId, proposalItem.Path, cancellationToken).ConfigureAwait(false)
                               ?? await File.ReadAllTextAsync(proposalItem.Path, cancellationToken).ConfigureAwait(false);
            var proposal = JsonSerializer.Deserialize<CodeProposal>(proposalText, JsonOptions);
            if (proposal is not null)
                return proposal.Files;
        }

        var generated = new List<GeneratedCodeFile>();
        foreach (var item in evidence.Where(item =>
                     item.Path.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase) ||
                     item.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            var content = await File.ReadAllTextAsync(item.Path, cancellationToken).ConfigureAwait(false);
            generated.Add(new GeneratedCodeFile
            {
                RelativePath = Path.GetFileName(item.Path),
                Content = content,
                Sha256 = ComputeSha256(content)
            });
        }

        return generated;
    }

    private async Task<IReadOnlyList<OutputVerificationEvidence>> LoadOutputVerificationsAsync(
        string runId,
        IReadOnlyList<RunEvidenceItem> evidence,
        CancellationToken cancellationToken)
    {
        var item = evidence.FirstOrDefault(item => item.Path.EndsWith("output-verification.json", StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return [];

        var text = await _evidence.ReadEvidenceTextAsync(runId, item.Path, cancellationToken).ConfigureAwait(false)
                   ?? await File.ReadAllTextAsync(item.Path, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().Select(element => MapOutputVerification(element, item.Path)).ToArray();
        }

        return [MapOutputVerification(root, item.Path)];
    }

    private static OutputVerificationEvidence MapOutputVerification(JsonElement root, string evidencePath) => new()
    {
        Expected = root.TryGetProperty("expected", out var expected) ? expected.GetString() ?? string.Empty : string.Empty,
        Actual = root.TryGetProperty("actual", out var actual) ? actual.GetString() ?? string.Empty : string.Empty,
        Verified = root.TryGetProperty("verified", out var verified) && verified.GetBoolean(),
        EvidencePath = evidencePath
    };

    private async Task<CodeStandardsEvidence> LoadCodeStandardsAsync(
        string runId,
        IReadOnlyList<RunEvidenceItem> evidence,
        CancellationToken cancellationToken)
    {
        var item = evidence.FirstOrDefault(item => item.Path.EndsWith("code-standards.json", StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return new CodeStandardsEvidence();

        var text = await _evidence.ReadEvidenceTextAsync(runId, item.Path, cancellationToken).ConfigureAwait(false)
                   ?? await File.ReadAllTextAsync(item.Path, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        return new CodeStandardsEvidence
        {
            Status = root.TryGetProperty("status", out var status) ? status.ToString() : "Unknown",
            Summary = root.TryGetProperty("summary", out var summary) ? summary.GetString() ?? string.Empty : "Code standards result was captured.",
            EvidencePath = item.Path
        };
    }

    private static IReadOnlyList<string> BuildRisks(
        RunRecord run,
        CodeStandardsEvidence codeStandards,
        IReadOnlyList<OutputVerificationEvidence> outputVerifications)
    {
        var risks = new List<string>
        {
            "This is a deterministic scenario fixture running through the reusable code proposal pipeline.",
            "Generated code remains in a disposable workspace until a separate human-approved apply path exists."
        };
        if (run.State == RunLifecycleState.Failed)
            risks.Add(run.FailureReason ?? "The disposable run failed.");
        if (outputVerifications.Count == 0 || outputVerifications.Any(item => !item.Verified))
            risks.Add("Expected output was not verified.");
        if (!string.Equals(codeStandards.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(codeStandards.Status, "Passed", StringComparison.OrdinalIgnoreCase))
        {
            risks.Add("Code standards did not report a clean success state.");
        }

        return risks;
    }

    private static string ComputeFileSetHash(IReadOnlyList<GeneratedCodeFile> files)
    {
        var value = string.Join('\n', files.OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .Select(file => $"{file.RelativePath}:{file.Sha256}"));
        return ComputeSha256(value);
    }

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? TryPayload(RunEventDto runEvent, string key) =>
        runEvent.Payload.TryGetValue(key, out var value) ? value : null;
}

