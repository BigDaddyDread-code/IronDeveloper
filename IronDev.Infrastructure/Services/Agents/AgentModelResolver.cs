using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class AgentModelResolver : IAgentModelResolver
{
    private readonly Dictionary<string, ModelProfile> _profiles;

    public AgentModelResolver(IEnumerable<ModelProfile>? profiles = null)
    {
        _profiles = (profiles ?? AgentModelDefaults.CreateDefaultProfiles())
            .Select(ValidateProfile)
            .ToDictionary(profile => profile.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ModelProfile> ListProfiles() => _profiles.Values.OrderBy(profile => profile.Name).ToArray();

    public ModelProfile ResolveProfile(string profileName)
    {
        if (_profiles.TryGetValue(profileName, out var profile))
            return profile;

        throw new InvalidOperationException($"Unknown agent model profile: {profileName}");
    }

    public ModelProfile ResolveForAgent(AgentDefinition definition) => ResolveProfile(definition.DefaultModelProfile);

    private static ModelProfile ValidateProfile(ModelProfile profile)
    {
        if (!string.Equals(profile.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"014 supports OpenAI model profiles only. Profile '{profile.Name}' uses '{profile.Provider}'.");

        return profile;
    }
}
