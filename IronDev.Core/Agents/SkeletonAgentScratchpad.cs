namespace IronDev.Core.Agents;

/// <summary>
/// AG-4 — a per-agent, per-run ephemeral scratchpad. Each agent gets its own
/// private working memory, keyed by (runId, role); it is unshared, ungoverned,
/// and dies with the run. This is deliberately the ONLY agent memory the loop
/// has today: there is no global/collective memory feeding any role (shared
/// beliefs would collapse the spec↔code↔tests independence), and no DURABLE
/// private memory (durable private memory is the least-governed surface in the
/// system and must wait for collective-grade governance).
///
/// The critic exception is structural, not polite: the Critic gets NO memory,
/// ever — remembering its own past verdicts or a project narrative erodes the
/// fresh-eyes statelessness that is its entire value. A scratchpad scoped to the
/// Critic is refused by construction, and a hostile test proves it.
/// </summary>
public sealed class SkeletonAgentMemoryForbiddenException : InvalidOperationException
{
    public SkeletonAgentMemoryForbiddenException(SkeletonAgentRole role)
        : base($"The {role} agent is memory-blind by design: it may hold no scratchpad. " +
               "Remembering past verdicts or project narrative would erode the fresh-eyes independence that is its value.")
    {
        Role = role;
    }

    public SkeletonAgentRole Role { get; }
}

/// <summary>
/// Ephemeral per-agent scratchpad. Writes and reads for the Critic role throw
/// <see cref="SkeletonAgentMemoryForbiddenException"/> — the exclusion cannot be
/// forgotten because it fails loudly. Everything here is in-memory and per-run.
/// </summary>
public interface ISkeletonAgentScratchpad
{
    /// <summary>True for every role except Critic. The one place the exception is decided.</summary>
    static bool RoleMayHoldMemory(SkeletonAgentRole role) => role != SkeletonAgentRole.Critic;

    void Write(string runId, SkeletonAgentRole role, string key, string value);

    string? Read(string runId, SkeletonAgentRole role, string key);

    IReadOnlyDictionary<string, string> ReadAll(string runId, SkeletonAgentRole role);

    /// <summary>The scratchpad dies with the run — releasing it is explicit, not hoped-for.</summary>
    void ClearRun(string runId);
}
