using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core;
using IronDev.Core.Models;
using IronDev.Infrastructure.Services;

namespace IronDev.IntegrationTests.Smoke;

[TestClass]
[TestCategory("AlphaSmoke")]
[TestCategory("ReleaseReadiness")]
[TestCategory("RequiresExternalDependency")]
public sealed class AlphaSmokeLiveModelTests
{
    [TestMethod]
    public async Task Rel4_LiveModel_SingleTicketDraft_ProducesBoundedDraftEvidence()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("IRONDEV_ALPHA_SMOKE_LIVE_MODEL"), "1", StringComparison.Ordinal))
        {
            Assert.Inconclusive("REL-4 live model smoke requires IRONDEV_ALPHA_SMOKE_LIVE_MODEL=1. It never falls back to deterministic mode.");
        }

        var provider = RequiredEnv("IRONDEV_ALPHA_SMOKE_LIVE_PROVIDER");
        var model = RequiredEnv("IRONDEV_ALPHA_SMOKE_LIVE_MODEL_NAME");
        var baseUrl = Environment.GetEnvironmentVariable("IRONDEV_ALPHA_SMOKE_LIVE_BASE_URL");
        var apiKey = Environment.GetEnvironmentVariable("IRONDEV_ALPHA_SMOKE_LIVE_API_KEY")
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        var llm = CreateLlm(provider, model, apiKey, baseUrl);
        var prompt =
            """
            You are helping IronDev produce a bounded ticket draft smoke artifact.

            Return JSON only with this exact shape:
            {
              "title": "...",
              "summary": "...",
              "acceptanceCriteria": ["...", "..."],
              "proposedFiles": ["src/BookSeller.Domain/Book.cs"]
            }

            Draft a single ticket for the BookSeller sample:
            validate the Book constructor so empty ISBN, empty title, and negative price are rejected.

            Constraints:
            - include src/BookSeller.Domain/Book.cs exactly in proposedFiles
            - do not claim approval, source apply, commit, push, merge, release, deploy, or workflow continuation
            - do not include secrets, credentials, raw logs, or private reasoning
            """;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var raw = await llm.GetResponseAsync(prompt, timeout.Token);
        var draft = ParseDraft(raw);

        Assert.IsFalse(string.IsNullOrWhiteSpace(draft.Title));
        Assert.IsTrue(draft.Title.Length <= 160, "Live draft title should be bounded.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(draft.Summary));
        Assert.IsTrue(draft.AcceptanceCriteria.Count >= 3, "Live draft should include useful acceptance criteria.");
        Assert.IsTrue(
            draft.ProposedFiles.Any(file => string.Equals(file, "src/BookSeller.Domain/Book.cs", StringComparison.OrdinalIgnoreCase)),
            "Live draft must preserve the exact expected Book.cs file reference.");

        var serialized = JsonSerializer.Serialize(draft);
        foreach (var forbidden in new[]
                 {
                     "approval granted",
                     "safe to apply",
                     "commit approved",
                     "push approved",
                     "merge ready",
                     "release ready",
                     "deployment ready",
                     "workflow continuation authorized"
                 })
        {
            Assert.IsFalse(
                serialized.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"Live draft must not include authority-shaped text: {forbidden}");
        }

        WriteReceipt(new Rel4LiveModelTicketDraftReceipt(
            Ticket: "validate-book",
            Project: "BookSeller",
            ModelMode: "Live",
            RunUntil: "TicketDraft",
            RunId: string.Empty,
            TicketDraftGenerated: true,
            Provider: provider,
            Model: model,
            ResponseSha256: Sha256(raw),
            Title: draft.Title,
            AcceptanceCriteriaCount: draft.AcceptanceCriteria.Count,
            ProposedFiles: draft.ProposedFiles.ToArray(),
            Proves:
            [
                "explicitly configured live model returned bounded ticket-draft JSON",
                "ticket draft includes the expected Book.cs target file",
                "live model smoke wrote a bounded receipt without deterministic fallback"
            ],
            DoesNotProveYet:
            [
                "ticket persistence",
                "SQL/API run persistence",
                "critic review",
                "accepted approval",
                "continuation",
                "source apply",
                "commit, push, release, or deployment"
            ]));
    }

    private static ILLMService CreateLlm(string provider, string model, string? apiKey, string? baseUrl)
    {
        var options = new LlmOptions
        {
            Provider = provider,
            Model = model,
            ApiKey = apiKey,
            BaseUrl = baseUrl,
            TimeoutSeconds = 90
        };

        return provider.Trim().ToLowerInvariant() switch
        {
            "openai" => new OpenAiLlmService(options),
            "localopenai" or "custom" => new LocalOpenAiCompatibleLlmService(options),
            "ollama" => new OllamaLlmService(options),
            _ => throw new AssertFailedException($"Unsupported REL-4 live provider '{provider}'. Use OpenAI, LocalOpenAI, Custom, or Ollama.")
        };
    }

    private static LiveTicketDraft ParseDraft(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        Assert.IsTrue(start >= 0 && end > start, $"Live model response did not contain a JSON object: {raw}");

        var json = raw[start..(end + 1)];
        var draft = JsonSerializer.Deserialize<LiveTicketDraft>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.IsNotNull(draft, $"Live model JSON did not deserialize: {json}");
        return draft!;
    }

    private static string RequiredEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.IsFalse(string.IsNullOrWhiteSpace(value), $"{name} is required for REL-4 live model smoke.");
        return value!;
    }

    private static void WriteReceipt(object receipt)
    {
        var path = Environment.GetEnvironmentVariable("ALPHA_SMOKE_RECEIPT");
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Path.GetTempPath(), "IronDev", "alpha-smoke", "rel4-live-ticket-draft-receipt.json");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"ALPHA_SMOKE_RECEIPT_PATH::{path}");
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record LiveTicketDraft(
        string Title,
        string Summary,
        IReadOnlyList<string> AcceptanceCriteria,
        IReadOnlyList<string> ProposedFiles);

    private sealed record Rel4LiveModelTicketDraftReceipt(
        string Ticket,
        string Project,
        string ModelMode,
        string RunUntil,
        string RunId,
        bool TicketDraftGenerated,
        string Provider,
        string Model,
        string ResponseSha256,
        string Title,
        int AcceptanceCriteriaCount,
        string[] ProposedFiles,
        string[] Proves,
        string[] DoesNotProveYet);
}
