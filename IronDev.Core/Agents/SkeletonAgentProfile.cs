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
    Critic = 3,
    Analyst = 4
}

public static class SkeletonAgentRoles
{
    public static readonly IReadOnlyList<SkeletonAgentRole> ProfileOrder =
    [
        SkeletonAgentRole.Analyst,
        SkeletonAgentRole.Builder,
        SkeletonAgentRole.Tester,
        SkeletonAgentRole.Critic,
        SkeletonAgentRole.Orchestrator
    ];

    public static string DisplayName(SkeletonAgentRole role) => role switch
    {
        SkeletonAgentRole.Analyst => "Workshop guide",
        _ => role.ToString()
    };

    public static string CodeOwnedBoundary(SkeletonAgentRole role) => $"{SkeletonAgentProfile.BoundaryText} {RoleBoundary(role)}";

    private static string RoleBoundary(SkeletonAgentRole role) => role switch
    {
        SkeletonAgentRole.Analyst =>
            "The Analyst is the Workshop guide. It inspects project context, asks useful questions, " +
            "separates facts from assumptions, preserves provenance, and prepares Work Item proposals. " +
            "It cannot approve, start a governed build without a separate action, continue workflow, " +
            "disposition findings, or apply source.",
        SkeletonAgentRole.Builder =>
            "The Builder proposes bounded implementation changes from the confirmed contract. It cannot approve its own work, " +
            "alter the Work Item contract silently, claim tests executed when they did not, or apply source without the governed apply chain.",
        SkeletonAgentRole.Tester =>
            "The Tester independently derives and reports test evidence. It cannot approve, suppress failed evidence, " +
            "treat compilation as test proof, or inherit Builder conclusions as fact.",
        SkeletonAgentRole.Critic =>
            "The Critic independently reviews the sealed package. It cannot share agent memory, receive hidden Builder reasoning, " +
            "approve, modify the package, or disposition its own findings.",
        SkeletonAgentRole.Orchestrator =>
            "The Orchestrator deterministically coordinates workflow stages and gates. It runs no model, has no editable skill, " +
            "has no provider connection, and has no approval authority.",
        _ => SkeletonAgentProfile.BoundaryText
    };
}

public sealed record SkeletonAgentBuiltInDefault
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Skill { get; init; }
    public required string Personality { get; init; }
}

public static class SkeletonAgentBuiltInDefaults
{
    public const string Name = "IronDev Agent Defaults";
    public const string Version = "IronDev Agent Defaults 2.5.0";

    private static readonly SkeletonAgentBuiltInDefault Empty = new()
    {
        Name = string.Empty,
        Version = string.Empty,
        Skill = string.Empty,
        Personality = string.Empty
    };

    public static SkeletonAgentBuiltInDefault For(SkeletonAgentRole role) => role switch
    {
        SkeletonAgentRole.Analyst => new SkeletonAgentBuiltInDefault
        {
            Name = Name,
            Version = Version,
            Skill =
                "Inspect available project context before making claims. Ask only questions that materially improve " +
                "the work contract. Separate confirmed facts, assumptions, constraints, dependencies, and unresolved " +
                "questions. Prefer precise acceptance criteria that can be tested. Preserve exact provenance for " +
                "documents, source files, messages, and decisions. Do not imply that a draft is approved work.",
            Personality =
                "Curious, structured, practical, and plain-speaking. Avoid generic consultancy language. Ask one " +
                "useful question rather than five weak ones."
        },
        SkeletonAgentRole.Builder => new SkeletonAgentBuiltInDefault
        {
            Name = Name,
            Version = Version,
            Skill =
                "Read the confirmed contract, linked files, architecture context, and current project structure before " +
                "proposing changes. Prefer the smallest coherent implementation that satisfies the acceptance criteria. " +
                "Do not invent file paths, APIs, types, dependencies, or test outcomes. Identify scope expansion " +
                "explicitly. Preserve existing conventions unless the contract requires change.",
            Personality =
                "Calm, precise, pragmatic, and economical. State uncertainty instead of bluffing. Explain decisions " +
                "in terms of the contract and evidence."
        },
        SkeletonAgentRole.Tester => new SkeletonAgentBuiltInDefault
        {
            Name = Name,
            Version = Version,
            Skill =
                "Derive tests independently from the acceptance criteria and actual implementation. Cover normal " +
                "behavior, failure paths, boundaries, regressions, and relevant security conditions. Distinguish tests " +
                "that were authored, discovered, compiled, and executed. Never present a green build as proof that " +
                "tests ran.",
            Personality =
                "Methodical, skeptical, patient, and evidence-oriented. Prefer reproducible results over optimistic " +
                "interpretation."
        },
        SkeletonAgentRole.Critic => new SkeletonAgentBuiltInDefault
        {
            Name = Name,
            Version = Version,
            Skill =
                "Review the exact current sealed package independently. Verify claims against source, build evidence, " +
                "executed tests, requirement coverage, and scope. Attack the weakest material claim first. Separate " +
                "defects, risks, unsupported claims, missing evidence, and subjective preferences. Do not manufacture " +
                "objections when the evidence is sufficient.",
            Personality =
                "Blunt, concise, independent, and unimpressed by confident wording. Comfortable saying either blocked " +
                "or no material objection."
        },
        _ => Empty
    };
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

    public string DisplayName { get; init; } = string.Empty;

    public string BuiltInDefaultName { get; init; } = string.Empty;

    public string BuiltInDefaultVersion { get; init; } = string.Empty;

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

/// <summary>
/// The write surface: only voice and model fields. Authority, capabilities, and
/// secrets are structurally absent — and so is BaseUrl: the outbound endpoint is
/// a deployment concern (global Ai:BaseUrl), never user-editable, so a profile
/// edit can never redirect an agent's calls to an attacker-chosen host.
/// </summary>
public sealed record SkeletonAgentProfileUpdate
{
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; }
    public string Skill { get; init; } = string.Empty;
    public string Personality { get; init; } = string.Empty;
}

/// <summary>The provider keys a user may set. "fake" is deliberately absent — it is test/local only, gated by config.</summary>
public static class SkeletonAgentProviders
{
    public static readonly IReadOnlyList<string> UserSelectable = ["openai", "localopenai", "ollama", "custom"];

    public const string Fake = "fake";

    public static bool IsUserSelectable(string provider) =>
        UserSelectable.Contains(provider?.Trim().ToLowerInvariant() ?? string.Empty);
}

public sealed record SkeletonAgentProfileOutcome
{
    public required bool Succeeded { get; init; }
    public string FailureReason { get; init; } = string.Empty;
    public SkeletonAgentProfile? Profile { get; init; }
}

public sealed record SkeletonAgentProfileValidationIssue
{
    public required string Code { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

public sealed record SkeletonAgentProfileDraft
{
    public required SkeletonAgentRole Role { get; init; }
    public required long Revision { get; init; }
    public required long BasePublishedVersion { get; init; }
    public required SkeletonAgentProfileUpdate Values { get; init; }
    public required bool IsValid { get; init; }
    public IReadOnlyList<SkeletonAgentProfileValidationIssue> ValidationIssues { get; init; } = [];
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record SkeletonAgentProfileDraftWriteRequest
{
    public required long ExpectedRevision { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; }
    public string Skill { get; init; } = string.Empty;
    public string Personality { get; init; } = string.Empty;

    public SkeletonAgentProfileUpdate ToUpdate() => new()
    {
        Provider = Provider,
        Model = Model,
        TimeoutSeconds = TimeoutSeconds,
        Skill = Skill,
        Personality = Personality
    };
}

public sealed record SkeletonAgentProfilePublishRequest
{
    public required long ExpectedRevision { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record SkeletonAgentProfilePublishedVersion
{
    public required long Version { get; init; }
    public required SkeletonAgentRole Role { get; init; }
    public required SkeletonAgentProfileUpdate Values { get; init; }
    public required string Reason { get; init; }
    public required int ActorUserId { get; init; }
    public required DateTimeOffset PublishedAtUtc { get; init; }
}

public sealed record SkeletonAgentProfileRunUsage
{
    public required string RunId { get; init; }
    public required int ProjectId { get; init; }
    public required long WorkItemId { get; init; }
    public required DateTimeOffset CapturedAtUtc { get; init; }
}

public sealed record SkeletonAgentProfileHistoryView
{
    public required SkeletonAgentProfilePublishedVersion Version { get; init; }
    public IReadOnlyList<SkeletonAgentProfileRunUsage> RunUsage { get; init; } = [];
    public string UsageBoundary { get; init; } =
        "Usage is reconstructed from durable configuration snapshots in the 50 most recent project runs. " +
        "Runs created before profile-version linkage are not attributed by guesswork.";
}

public sealed record SkeletonAgentProfileDraftOutcome
{
    public required bool Succeeded { get; init; }
    public string Code { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
    public required long CurrentRevision { get; init; }
    public SkeletonAgentProfileDraft? Draft { get; init; }
    public SkeletonAgentProfilePublishedVersion? PublishedVersion { get; init; }
    public SkeletonAgentProfile? Profile { get; init; }
}

public sealed record SkeletonAgentProfileDraftTestOutcome
{
    public required bool Succeeded { get; init; }
    public required string Status { get; init; }
    public string FailureReason { get; init; } = string.Empty;
    public IReadOnlyList<SkeletonAgentProfileValidationIssue> ValidationIssues { get; init; } = [];
    public required DateTimeOffset ExecutedAtUtc { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string Boundary { get; init; } = SkeletonAgentProfile.BoundaryText;
}

public static class SkeletonAgentProfileResetScopes
{
    public const string Field = "Field";
    public const string Agent = "Agent";
    public const string BuiltIn = "BuiltIn";
    public const string Project = "Project";
    public const string Tenant = "Tenant";
}

public sealed record SkeletonAgentProfileResetRequest
{
    public required long ExpectedRevision { get; init; }
    public string Scope { get; init; } = SkeletonAgentProfileResetScopes.Agent;
    public string Field { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed record SkeletonAgentProfileRestoreRequest
{
    public required long ExpectedRevision { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record SkeletonAgentProfileFieldSource
{
    public required string Field { get; init; }
    public required string SourceLayer { get; init; }
    public required string SourceLabel { get; init; }
    public string? Version { get; init; }
    public required bool Inherited { get; init; }
    public string Detail { get; init; } = string.Empty;
}

public sealed record EffectiveSkeletonAgentProfile
{
    public required SkeletonAgentRole Role { get; init; }
    public required string DisplayName { get; init; }
    public string AiConnectionId { get; init; } = "deployment-default";
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public required int TimeoutSeconds { get; init; }
    public required string EffectiveSkill { get; init; }
    public required string EffectivePersonality { get; init; }
    public IReadOnlyList<SkeletonAgentProfileFieldSource> FieldSources { get; init; } = [];
    public string BuiltInDefaultVersion { get; init; } = string.Empty;
    public string? TenantProfileVersion { get; init; }
    public string? ProjectProfileVersion { get; init; }
    public long? PublishedVersion { get; init; }
    public required string EffectiveHash { get; init; }
    public string Boundary { get; init; } = SkeletonAgentProfile.BoundaryText;
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
    Task<IReadOnlyList<EffectiveSkeletonAgentProfile>> ListEffectiveAsync(
        int tenantId,
        int? projectId = null,
        CancellationToken cancellationToken = default);
    Task<SkeletonAgentProfileOutcome> UpdateAsync(SkeletonAgentRole role, SkeletonAgentProfileUpdate update, CancellationToken cancellationToken = default);
    Task<SkeletonAgentProfileDraft> GetDraftAsync(SkeletonAgentRole role, CancellationToken cancellationToken = default);
    Task<SkeletonAgentProfileDraftOutcome> SaveDraftAsync(SkeletonAgentRole role, SkeletonAgentProfileDraftWriteRequest request, CancellationToken cancellationToken = default);
    Task<SkeletonAgentProfileDraftTestOutcome> TestDraftAsync(SkeletonAgentRole role, CancellationToken cancellationToken = default);
    Task<SkeletonAgentProfileDraftOutcome> PublishDraftAsync(SkeletonAgentRole role, SkeletonAgentProfilePublishRequest request, int actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SkeletonAgentProfilePublishedVersion>> ListHistoryAsync(SkeletonAgentRole role, CancellationToken cancellationToken = default);
    Task<SkeletonAgentProfileDraftOutcome> ResetAsync(SkeletonAgentRole role, SkeletonAgentProfileResetRequest request, int actorUserId, CancellationToken cancellationToken = default);
    Task<SkeletonAgentProfileDraftOutcome> RestoreAsync(SkeletonAgentRole role, long version, SkeletonAgentProfileRestoreRequest request, int actorUserId, CancellationToken cancellationToken = default);
}
