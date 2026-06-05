using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronDev.Core.ChatProbe;

/// <summary>
/// Writes probe run results to disk in JSON and human-readable markdown.
/// Output structure:
///   tools/dogfood/chat-probe-runs/{runId}/
///     result.json
///     summary.md
///     failures.json
/// </summary>
public sealed class ProbeTranscriptWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // ── Single run ────────────────────────────────────────────────────────────

    public async Task WriteRunAsync(ProbeRunResult run, string outputRoot, CancellationToken ct = default)
    {
        var dir = Path.Combine(outputRoot, run.RunId);
        Directory.CreateDirectory(dir);

        await WriteJsonAsync(Path.Combine(dir, "result.json"), run, ct);
        await WriteTextAsync(Path.Combine(dir, "summary.md"), BuildRunMarkdown(run), ct);

        if (run.AllFailures.Count > 0)
            await WriteJsonAsync(Path.Combine(dir, "failures.json"), run.AllFailures, ct);
    }

    // ── Batch ─────────────────────────────────────────────────────────────────

    public async Task WriteBatchAsync(ProbeBatchSummary batch, string outputRoot, CancellationToken ct = default)
    {
        var batchDir = Path.Combine(outputRoot, batch.BatchId);
        var runsDir  = Path.Combine(batchDir, "runs");
        Directory.CreateDirectory(runsDir);

        await WriteJsonAsync(Path.Combine(batchDir, "batch-summary.json"), batch, ct);
        await WriteTextAsync(Path.Combine(batchDir, "batch-summary.md"), BuildBatchMarkdown(batch), ct);

        foreach (var run in batch.Runs)
            await WriteRunAsync(run, runsDir, ct);
    }

    // ── Markdown builders ─────────────────────────────────────────────────────

    private static string BuildRunMarkdown(ProbeRunResult run)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Chat Probe Run Report");
        sb.AppendLine();
        sb.AppendLine($"**Run ID:** `{run.RunId}`");
        sb.AppendLine($"**Scenario:** {run.ScenarioId}");
        sb.AppendLine($"**Persona:** {run.Persona}");
        sb.AppendLine($"**Project Idea:** {run.ProjectIdea}");
        sb.AppendLine($"**Outcome:** {run.Outcome}");
        sb.AppendLine($"**Duration:** {run.Duration.TotalSeconds:F1}s");
        sb.AppendLine($"**Started:** {run.StartedUtc:O}");
        sb.AppendLine();

        if (run.AllFailures.Count > 0)
        {
            sb.AppendLine("## Failures");
            sb.AppendLine();
            foreach (var f in run.AllFailures)
                sb.AppendLine($"- **[{(f.IsHard ? "HARD" : "soft")}] {f.Type}**: {f.Message}");
            sb.AppendLine();
        }

        sb.AppendLine("## Conversation Transcript");
        sb.AppendLine();

        foreach (var turn in run.Turns)
        {
            var label = turn.WasAdaptiveProbe ? "🔄 Adaptive" : $"Turn {turn.TurnNumber}";
            sb.AppendLine($"### {label}");
            sb.AppendLine();
            sb.AppendLine($"**User ({(turn.WasAdaptiveProbe ? "adaptive" : "scripted")}):** {turn.UserMessage}");
            sb.AppendLine();
            sb.AppendLine($"**Mode:** {turn.Mode ?? "—"} | " +
                          $"**SaveDiscussion:** {turn.GateCanSaveDiscussion} | " +
                          $"**CreateTicket:** {turn.GateCanCreateTicket}");
            sb.AppendLine();
            sb.AppendLine("**Assistant:**");
            sb.AppendLine();
            sb.AppendLine(turn.AssistantResponse.Length > 800
                ? turn.AssistantResponse[..800] + "\n…[truncated]"
                : turn.AssistantResponse);
            sb.AppendLine();

            if (turn.Failures.Count > 0)
            {
                sb.AppendLine("**⚠ Failures this turn:**");
                foreach (var f in turn.Failures)
                    sb.AppendLine($"- [{(f.IsHard ? "HARD" : "soft")}] {f.Type}: {f.Message}");
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildBatchMarkdown(ProbeBatchSummary batch)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Chat Probe Batch Report");
        sb.AppendLine();
        sb.AppendLine($"**Batch ID:** `{batch.BatchId}`");
        sb.AppendLine($"**Runs:** {batch.TotalRuns}");
        sb.AppendLine($"**Pass:** {batch.Passed}");
        sb.AppendLine($"**Soft fail:** {batch.SoftFailed}");
        sb.AppendLine($"**Hard fail:** {batch.HardFailed}");
        sb.AppendLine($"**Duration:** {(batch.CompletedUtc - batch.StartedUtc).TotalSeconds:F1}s");
        sb.AppendLine();

        if (batch.FailureCounts.Count > 0)
        {
            sb.AppendLine("## Top Failures");
            sb.AppendLine();
            sb.AppendLine("| Failure Type | Count |");
            sb.AppendLine("|---|---|");
            foreach (var (type, count) in batch.FailureCounts.OrderByDescending(kv => kv.Value))
                sb.AppendLine($"| {type} | {count} |");
            sb.AppendLine();
        }

        sb.AppendLine("## Run Results");
        sb.AppendLine();
        sb.AppendLine("| Run ID | Scenario | Persona | Outcome | Failures |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var run in batch.Runs)
        {
            var emoji = run.Outcome switch
            {
                ProbeRunOutcome.Pass     => "✅",
                ProbeRunOutcome.SoftFail => "⚠",
                ProbeRunOutcome.HardFail => "❌",
                _                       => "?"
            };
            sb.AppendLine($"| `{run.RunId}` | {run.ScenarioId} | {run.Persona} | {emoji} {run.Outcome} | {run.AllFailures.Count} |");
        }

        return sb.ToString();
    }

    // ── I/O helpers ───────────────────────────────────────────────────────────

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken ct)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, ct);
    }

    private static Task WriteTextAsync(string path, string text, CancellationToken ct) =>
        File.WriteAllTextAsync(path, text, ct);
}
