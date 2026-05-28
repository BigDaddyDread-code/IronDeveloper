using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public sealed class ModelAssistedCodeProposalGenerator : ICodeProposalGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILLMService _llm;
    private readonly ITicketService _tickets;
    private readonly IProjectDocumentService _documents;
    private readonly ICodeRunProfileCatalog _profiles;

    public ModelAssistedCodeProposalGenerator(
        ILLMService llm,
        ITicketService tickets,
        IProjectDocumentService documents,
        ICodeRunProfileCatalog profiles)
    {
        _llm = llm;
        _tickets = tickets;
        _documents = documents;
        _profiles = profiles;
    }

    public async Task<CodeProposal> GenerateAsync(
        TicketReviewResult review,
        string expectedOutput,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(review.TicketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || ticket.ProjectId != review.ProjectId)
            throw new InvalidOperationException("Ticket could not be resolved for model-assisted code proposal generation.");

        var discussion = await ResolveDiscussionAsync(ticket.SourceDocumentVersionId, cancellationToken).ConfigureAwait(false);
        var response = await _llm.GetResponseAsync(BuildPrompt(review, ticket, discussion, expectedOutput), cancellationToken).ConfigureAwait(false);
        var dto = DeserializeResponse(response);
        var runtimeProfileId = string.IsNullOrWhiteSpace(dto.RuntimeProfileId)
            ? InferRuntimeProfile(ticket, discussion)
            : dto.RuntimeProfileId.Trim();
        var profile = _profiles.GetProfile(runtimeProfileId)
            ?? throw new InvalidOperationException($"Model requested unsupported runtime profile '{runtimeProfileId}'.");

        var workingDirectory = SanitizeDirectory(dto.WorkingDirectory, ticket.Id);
        var files = dto.Files.Select(file => CreateFile(file.RelativePath, file.Content)).ToArray();
        if (files.Length == 0)
            throw new InvalidOperationException("Model-assisted proposal did not include any generated files.");

        return new CodeProposal
        {
            ProposalId = $"model-code-proposal-{review.TicketId}-{Guid.NewGuid():N}",
            ProjectId = review.ProjectId,
            TicketId = review.TicketId,
            ReviewId = review.ReviewId,
            ScenarioId = string.IsNullOrWhiteSpace(review.ScenarioId) ? "model.assisted" : review.ScenarioId,
            ExpectedOutput = expectedOutput.Trim(),
            Files = files,
            RunProfile = new CodeRunProfile
            {
                RuntimeProfileId = profile.RuntimeProfileId,
                WorkingDirectory = workingDirectory,
                BuildCommand = profile.BuildCommand,
                RunCommand = profile.RunCommand
            },
            Verifications = dto.Verifications.Select(item => new ScenarioVerification
            {
                Kind = item.Kind,
                Description = item.Description,
                Parameters = item.Parameters
            }).ToArray()
        };
    }

    private async Task<string> ResolveDiscussionAsync(long? documentVersionId, CancellationToken cancellationToken)
    {
        if (documentVersionId is null)
            return string.Empty;

        var version = await _documents.GetVersionAsync(documentVersionId.Value, cancellationToken).ConfigureAwait(false);
        return version?.ContentMarkdown ?? string.Empty;
    }

    private string BuildPrompt(TicketReviewResult review, ProjectTicket ticket, string discussion, string expectedOutput)
    {
        var profileText = string.Join(Environment.NewLine, _profiles.GetProfiles().Select(profile =>
            $"- {profile.RuntimeProfileId}: {profile.DisplayName}; verifications={string.Join(", ", profile.AllowedVerificationKinds)}"));

        return $$"""
You are generating a small review-only code proposal for IronDev.

Return JSON only. Do not wrap it in Markdown.

Hard rules:
- Do not write to the real repository.
- Do not include shell commands.
- Do not include absolute paths or .. paths.
- Generate a tiny project that can run in a disposable workspace.
- Use only one runtimeProfileId from the allow-list.
- For dotnet.console, include StdoutContains or CommandExitZero verifications.
- For dotnet.aspnet, include HttpGetEquals verification with path and expected parameters.

Allowed runtime profiles:
{{profileText}}

Required JSON shape:
{
  "runtimeProfileId": "dotnet.console",
  "workingDirectory": "GeneratedApp",
  "expectedOutput": "optional human-readable expected output",
  "files": [
    { "relativePath": "GeneratedApp/GeneratedApp.csproj", "content": "<Project Sdk=\"Microsoft.NET.Sdk\">...</Project>" },
    { "relativePath": "GeneratedApp/Program.cs", "content": "Console.WriteLine(\"hello\");" }
  ],
  "verifications": [
    { "kind": "StdoutContains", "description": "Runtime output contains expected text.", "parameters": { "expected": "hello" } }
  ]
}

Ticket:
Title: {{ticket.Title}}
Summary: {{ticket.Summary}}
Problem: {{ticket.Problem}}
Acceptance Criteria:
{{ticket.AcceptanceCriteria}}

Ticket review:
ReviewId: {{review.ReviewId}}
RecommendedNextStep: {{review.Decision.RecommendedNextStep}}
Guardrails:
{{string.Join(Environment.NewLine, review.Decision.Guardrails.Select(item => "- " + item))}}

Linked discussion:
{{discussion}}

Requested expected output, if any:
{{expectedOutput}}
""";
    }

    private static ModelProposalDto DeserializeResponse(string response)
    {
        var json = ExtractJson(response);
        try
        {
            return JsonSerializer.Deserialize<ModelProposalDto>(json, JsonOptions)
                ?? throw new InvalidOperationException("Model-assisted proposal response was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Model-assisted proposal was not valid JSON: {ex.Message}", ex);
        }
    }

    private static string ExtractJson(string response)
    {
        var trimmed = response.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
                trimmed = trimmed[(firstNewLine + 1)..lastFence].Trim();
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace)
            throw new InvalidOperationException("Model-assisted proposal response did not contain a JSON object.");

        return trimmed[firstBrace..(lastBrace + 1)];
    }

    private static string InferRuntimeProfile(ProjectTicket ticket, string discussion)
    {
        var text = $"{ticket.Title} {ticket.Summary} {ticket.AcceptanceCriteria} {discussion}";
        return text.Contains("ASP.NET", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("/health", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("endpoint", StringComparison.OrdinalIgnoreCase)
            ? "dotnet.aspnet"
            : "dotnet.console";
    }

    private static string SanitizeDirectory(string? value, long ticketId)
    {
        if (string.IsNullOrWhiteSpace(value))
            return $"GeneratedTicket{ticketId}";

        return value.Trim().Replace('\\', '/');
    }

    private static GeneratedCodeFile CreateFile(string relativePath, string content) => new()
    {
        RelativePath = relativePath.Trim().Replace('\\', '/'),
        Content = content,
        Sha256 = ComputeSha256(content)
    };

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record ModelProposalDto
    {
        public string RuntimeProfileId { get; init; } = string.Empty;
        public string WorkingDirectory { get; init; } = string.Empty;
        public string ExpectedOutput { get; init; } = string.Empty;
        public IReadOnlyList<ModelFileDto> Files { get; init; } = [];
        public IReadOnlyList<ModelVerificationDto> Verifications { get; init; } = [];
    }

    private sealed record ModelFileDto
    {
        public string RelativePath { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
    }

    private sealed record ModelVerificationDto
    {
        public string Kind { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
    }
}
