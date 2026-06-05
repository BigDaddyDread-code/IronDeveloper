using System.Text.Json.Serialization;

namespace IronDev.Core.ChatProbe;

// ── Scenarios ──────────────────────────────────────────────────────────────────

public enum ProjectCategory
{
    Game,
    BusinessApp,
    DeveloperTool,
    ConsumerApp
}

public sealed class ProbeScenario
{
    public string ScenarioId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public ProjectCategory Category { get; init; }
    public string ProjectIdea { get; init; } = string.Empty;
    public IReadOnlyList<string> DomainTerms { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DangerZones { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ProbeStep> Steps { get; init; } = Array.Empty<ProbeStep>();
}

public sealed class ProbeStep
{
    public int Order { get; init; }

    /// <summary>Clean version of the message — persona engine transforms this.</summary>
    public string UserMessage { get; init; } = string.Empty;

    public ProbeKind Kind { get; init; }

    /// <summary>If set, hard-evaluator checks that the response mode matches.</summary>
    public string? ExpectedMode { get; init; }

    /// <summary>If set, gate must have CanSaveDiscussion == this value.</summary>
    public bool? ExpectGateSaveDiscussion { get; init; }

    /// <summary>If set, gate must have CanCreateTicket == this value.</summary>
    public bool? ExpectGateCreateTicket { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProbeKind
{
    Seed,
    AskRecommendation,
    ShortConfirm,
    TopicCorrection,
    Formalize,
    AskWhatNext,
    ScopeCreep,
    Contradict,
    AskArchitectureDoc
}

// ── Personas ───────────────────────────────────────────────────────────────────

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PersonaId
{
    MessyRob,
    VagueFounder,
    ShortcutUser,
    ScopeCreeper,
    Contradictor,
    Formalizer
}

public sealed class PersonaProfile
{
    public PersonaId Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>Applied to each step's UserMessage before sending.</summary>
    public Func<string, string> TextTransform { get; init; } = s => s;
}

// ── Run options ────────────────────────────────────────────────────────────────

public sealed class ProbeRunOptions
{
    /// <summary>Maximum total turns (scripted + adaptive).</summary>
    public int MaxTurns { get; init; } = 8;

    /// <summary>Maximum adaptive probes injected by AdaptiveProbeLogic.</summary>
    public int MaxAdaptiveProbes { get; init; } = 3;

    /// <summary>Stop the run if any hard failure is detected.</summary>
    public bool StopOnHardFailure { get; init; } = false;

    /// <summary>Whether to run the soft LLM evaluator (requires live LLM).</summary>
    public bool RunSoftEvaluator { get; init; } = false;
}

// ── Turn result ────────────────────────────────────────────────────────────────

public sealed class ProbeTurnResult
{
    public int TurnNumber { get; init; }
    public string UserMessage { get; init; } = string.Empty;
    public string AssistantResponse { get; init; } = string.Empty;
    public string? Mode { get; init; }
    public double? ModeConfidence { get; init; }
    public string? ModeReason { get; init; }
    public string? ClarificationKind { get; init; }
    public bool ClarificationRequired { get; init; }
    public IReadOnlyList<string> ClarificationQuestions { get; init; } = Array.Empty<string>();
    public bool GateCanSaveDiscussion { get; init; }
    public bool GateCanCreateTicket { get; init; }
    public IReadOnlyList<string> GovernanceActions { get; init; } = Array.Empty<string>();
    public string? DogfoodTraceId { get; init; }
    public bool WasAdaptiveProbe { get; init; }
    public IReadOnlyList<ProbeFailure> Failures { get; init; } = Array.Empty<ProbeFailure>();
    public ProbeSoftScore? SoftScore { get; init; }
}

// ── Failures ───────────────────────────────────────────────────────────────────

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProbeFailureType
{
    WrongMode,
    WrongGate,
    LostTopic,
    BadReferentBinding,
    OverClarification,
    OverbuiltArchitecture,
    GenericTemplateLeak,
    FailedArtifactExtraction,
    UnsafeDomainHandling,
    MushyTone
}

public sealed class ProbeFailure
{
    public ProbeFailureType Type { get; init; }
    public string Message { get; init; } = string.Empty;

    /// <summary>Hard failures always fail the run. Soft failures only affect scoring.</summary>
    public bool IsHard { get; init; }
}

// ── Soft scores ────────────────────────────────────────────────────────────────

public sealed class ProbeSoftScore
{
    public int TopicRelevance { get; init; }      // 0–3
    public int Specificity { get; init; }         // 0–3
    public int BAJudgement { get; init; }         // 0–3
    public int OverbuildControl { get; init; }    // 0–3
    public int ClarificationQuality { get; init; } // 0–3
    public int ArtifactExtraction { get; init; }  // 0–3
    public int Tone { get; init; }                // 0–2
    public int Total => TopicRelevance + Specificity + BAJudgement
        + OverbuildControl + ClarificationQuality + ArtifactExtraction + Tone;
    public int MaxTotal => 20;
    public string Notes { get; init; } = string.Empty;
}

// ── Run result ─────────────────────────────────────────────────────────────────

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProbeRunOutcome { Pass, SoftFail, HardFail }

public sealed class ProbeRunResult
{
    public string RunId { get; init; } = string.Empty;
    public string ScenarioId { get; init; } = string.Empty;
    public PersonaId Persona { get; init; }
    public string ProjectIdea { get; init; } = string.Empty;
    public ProbeRunOutcome Outcome { get; init; }
    public IReadOnlyList<ProbeTurnResult> Turns { get; init; } = Array.Empty<ProbeTurnResult>();
    public IReadOnlyList<ProbeFailure> AllFailures { get; init; } = Array.Empty<ProbeFailure>();
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public TimeSpan Duration => CompletedUtc - StartedUtc;
    public string? ErrorMessage { get; init; }
}

// ── Batch summary ──────────────────────────────────────────────────────────────

public sealed class ProbeBatchSummary
{
    public string BatchId { get; init; } = string.Empty;
    public int TotalRuns { get; init; }
    public int Passed { get; init; }
    public int SoftFailed { get; init; }
    public int HardFailed { get; init; }
    public IReadOnlyDictionary<ProbeFailureType, int> FailureCounts { get; init; }
        = new Dictionary<ProbeFailureType, int>();
    public IReadOnlyList<ProbeRunResult> Runs { get; init; } = Array.Empty<ProbeRunResult>();
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }

    public string FormatTopFailures(int top = 5)
    {
        var lines = FailureCounts
            .OrderByDescending(kv => kv.Value)
            .Take(top)
            .Select(kv => $"- {kv.Key}: {kv.Value}");
        return string.Join(Environment.NewLine, lines);
    }
}
