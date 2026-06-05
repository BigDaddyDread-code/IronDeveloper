using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronDev.Core.ChatProbe;

/// <summary>
/// Freezes a failed probe run into a self-contained regression fixture
/// that can be replayed to prove the failure is fixed.
/// </summary>
public sealed class ProbeRegressionExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Export all hard-fail runs in a batch as frozen regression fixtures.
    /// </summary>
    public async Task ExportBatchFailuresAsync(
        ProbeBatchSummary batch,
        string outputDir,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);

        foreach (var run in batch.Runs.Where(r => r.Outcome == ProbeRunOutcome.HardFail))
        {
            await ExportRunAsync(run, outputDir, ct);
        }
    }

    /// <summary>
    /// Export a single run as a frozen regression fixture.
    /// </summary>
    public async Task ExportRunAsync(
        ProbeRunResult run,
        string outputDir,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);

        var fixture = new RegressionFixture
        {
            ScenarioId        = run.ScenarioId,
            Persona           = run.Persona,
            ProjectIdea       = run.ProjectIdea,
            FrozenOutcome     = run.Outcome,
            FrozenAt          = DateTimeOffset.UtcNow,
            OriginalRunId     = run.RunId,
            FrozenFailures    = run.AllFailures,
            ConversationTurns = run.Turns.Select(t => new RegressionTurn
            {
                TurnNumber    = t.TurnNumber,
                UserMessage   = t.UserMessage,
                ExpectedMode  = t.Mode,
                WasAdaptive   = t.WasAdaptiveProbe,
                FailureTypes  = t.Failures.Select(f => f.Type).ToList()
            }).ToList()
        };

        // Filename: {outcome}-{scenario}-{persona}.json
        var fileName = SanitizeFileName($"{run.Outcome.ToString().ToLower()}-{run.ScenarioId}-{run.Persona}.json");
        var path = Path.Combine(outputDir, fileName);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, fixture, JsonOptions, ct);
    }

    // ── Fixture models ────────────────────────────────────────────────────────

    private sealed class RegressionFixture
    {
        public string ScenarioId { get; init; } = string.Empty;
        public PersonaId Persona { get; init; }
        public string ProjectIdea { get; init; } = string.Empty;
        public ProbeRunOutcome FrozenOutcome { get; init; }
        public DateTimeOffset FrozenAt { get; init; }
        public string OriginalRunId { get; init; } = string.Empty;

        /// <summary>These failures MUST be absent in a passing regression replay.</summary>
        public IReadOnlyList<ProbeFailure> FrozenFailures { get; init; } = [];

        public IReadOnlyList<RegressionTurn> ConversationTurns { get; init; } = [];
    }

    private sealed class RegressionTurn
    {
        public int TurnNumber { get; init; }
        public string UserMessage { get; init; } = string.Empty;
        public string? ExpectedMode { get; init; }
        public bool WasAdaptive { get; init; }
        public IReadOnlyList<ProbeFailureType> FailureTypes { get; init; } = [];
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '-');
        return name;
    }
}
