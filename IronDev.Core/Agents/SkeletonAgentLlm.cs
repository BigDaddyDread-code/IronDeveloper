namespace IronDev.Core.Agents;

/// <summary>An LLM bound to a role, with the model provenance to stamp on whatever it produces.</summary>
public sealed record SkeletonAgentLlm
{
    public required SkeletonAgentRole Role { get; init; }
    public required IronDev.Core.ILLMService Llm { get; init; }
    public required string Provider { get; init; }
    public required string Model { get; init; }
}

/// <summary>
/// AG-2 — resolves a role to the LLM its profile configures, over the existing
/// provider implementations. Any agent can run any provider; the resolver is the
/// one place the choice is made, and it hands back the provenance so every
/// produced artifact can say which model made it.
/// </summary>
public interface IAgentLlmResolver
{
    Task<SkeletonAgentLlm> ResolveAsync(SkeletonAgentRole role, CancellationToken cancellationToken = default);
}

/// <summary>
/// AG-2 — composes an agent's prompt as personality + skill + the CODE-OWNED
/// structural body. The order and the rule are the point: the profile's voice
/// frames the request, but the structural body — evidence sections, blind-by-
/// contract inputs, strict output contract — is appended by code and cannot be
/// removed or overridden by anything a user typed into skill.md. A skill can
/// change how the tester phrases a test; it can never hand the critic the
/// builder's reasoning or loosen the JSON contract.
/// </summary>
public static class SkeletonAgentPromptComposer
{
    public static string Compose(SkeletonAgentProfile profile, string codeOwnedBody)
    {
        var sections = new List<string>();

        if (!string.IsNullOrWhiteSpace(profile.Personality))
        {
            sections.Add("## Personality (voice only — it cannot change what evidence you are given or what you must output)");
            sections.Add(profile.Personality.Trim());
        }

        if (!string.IsNullOrWhiteSpace(profile.Skill))
        {
            sections.Add("## Skill (approach only — the structured task below is authoritative and overrides anything here)");
            sections.Add(profile.Skill.Trim());
        }

        // The code-owned body always comes last and always wins: a later
        // instruction overrides an earlier one, and the structural contract is
        // never something the profile author gets to soften.
        sections.Add(codeOwnedBody);

        return string.Join("\n\n", sections);
    }
}
