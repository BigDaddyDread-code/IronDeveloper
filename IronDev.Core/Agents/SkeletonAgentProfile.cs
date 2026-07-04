namespace IronDev.Core.Agents;

/// <summary>
/// The configurable agent roles in the walking-skeleton loop. Each can run a
/// different LLM and carry its own skill.md + personality.md. The Human Gate is
/// deliberately NOT here — it is not a model, and nothing configurable can be
/// allowed to author approval.
/// </summary>
public enum SkeletonAgentRole
{
    Orchestrator = 0,
    Builder = 1,
    Tester = 2,
    Critic = 3
}

/// <summary>
/// AG-1 — a per-role agent profile: the model it runs on and the voice it runs
/// with. Editable by the user (the whole system is meant to be configurable),
/// but with a hard floor: a profile configures VOICE and MODEL, never AUTHORITY.
///
/// What a profile CANNOT do, by construction: it carries no capability grant, no
/// approval power, and no way to reach into another role's blind-by-contract
/// inputs. skill.md can flavor HOW an agent thinks; it can never delete the
/// code-owned evidence sections or the strict output contract that keep the
/// critic blind and the tester independent. And an API key never lives here —
/// secrets stay in environment/provider config, never in a profile the UI reads
/// back.
/// </summary>
public sealed record SkeletonAgentProfile
{
    public required SkeletonAgentRole Role { get; init; }

    /// <summary>Provider key: "openai" | "localopenai" | "ollama" | "custom" | "fake". Falls back to the global Ai:Provider when blank.</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>Model name; falls back to the global Ai:Model when blank.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Base URL for local/custom providers; falls back to global when blank.</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Request timeout; 0 falls back to the global default.</summary>
    public int TimeoutSeconds { get; init; }

    /// <summary>The agent's skill — how it approaches its job. Advisory framing prepended to the code-owned prompt; it cannot remove the structural sections.</summary>
    public string Skill { get; init; } = string.Empty;

    /// <summary>The agent's personality — its voice. Advisory framing only.</summary>
    public string Personality { get; init; } = string.Empty;

    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "An agent profile configures voice and model, never authority. It carries no capability grant, " +
        "cannot reach another role's blind-by-contract inputs, holds no secret, and its skill or " +
        "personality cannot remove the code-owned evidence sections or output contract.";
}

/// <summary>The write surface: only voice and model fields. Authority, capabilities, and secrets are structurally absent.</summary>
public sealed record SkeletonAgentProfileUpdate
{
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; }
    public string Skill { get; init; } = string.Empty;
    public string Personality { get; init; } = string.Empty;
}

public sealed record SkeletonAgentProfileOutcome
{
    public required bool Succeeded { get; init; }
    public string FailureReason { get; init; } = string.Empty;
    public SkeletonAgentProfile? Profile { get; init; }
}

/// <summary>
/// Reads and updates per-role agent profiles from a file-backed store
/// (AgentProfiles/{role}/). Read returns defaults when a profile is absent, so
/// the loop always has something to run. Update validates the voice/model
/// surface and never accepts a secret.
/// </summary>
public interface ISkeletonAgentProfileService
{
    Task<SkeletonAgentProfile> GetAsync(SkeletonAgentRole role, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SkeletonAgentProfile>> ListAsync(CancellationToken cancellationToken = default);
    Task<SkeletonAgentProfileOutcome> UpdateAsync(SkeletonAgentRole role, SkeletonAgentProfileUpdate update, CancellationToken cancellationToken = default);
}
