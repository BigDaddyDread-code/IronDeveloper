using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Models;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// AG-1 — file-backed per-role agent profiles under AgentProfiles/{role}/:
/// agent.json (provider/model/baseUrl/timeout), skill.md, personality.md. All
/// three are literally editable by hand; the Settings UI edits the same files
/// through the governed endpoints.
///
/// Read returns defaults (the global Ai:* config) when a profile is absent, so
/// the loop always has a model to run. Update writes only the voice/model
/// surface and REFUSES anything that looks like a secret — an API key never
/// belongs in a profile the UI can read back.
///
/// Boundary: a profile configures voice and model, never authority. This service
/// reads and writes those files and nothing else — it grants no capability,
/// touches no gate, and stores no secret.
/// </summary>
public sealed class SkeletonAgentProfileService : ISkeletonAgentProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // If an update's provider/baseUrl/skill/personality contains one of these,
    // it is almost certainly a leaked secret — refuse rather than persist it.
    private static readonly string[] SecretMarkers =
    [
        "api_key", "apikey", "api-key", "secret", "bearer ", "-----begin", "password", "sk-"
    ];

    private readonly IConfiguration _configuration;

    public SkeletonAgentProfileService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<SkeletonAgentProfile> GetAsync(SkeletonAgentRole role, CancellationToken cancellationToken = default)
    {
        var dir = RoleDir(role);
        var settingsPath = Path.Combine(dir, "agent.json");

        AgentSettingsFile? settings = null;
        if (File.Exists(settingsPath))
        {
            try
            {
                settings = JsonSerializer.Deserialize<AgentSettingsFile>(
                    await File.ReadAllTextAsync(settingsPath, cancellationToken).ConfigureAwait(false), JsonOptions);
            }
            catch (JsonException)
            {
                settings = null;
            }
        }

        var global = _configuration.GetSection("Ai").Get<LlmOptions>() ?? new LlmOptions();

        return new SkeletonAgentProfile
        {
            Role = role,
            Provider = Coalesce(settings?.Provider, global.Provider),
            Model = Coalesce(settings?.Model, global.Model),
            BaseUrl = Coalesce(settings?.BaseUrl, global.BaseUrl),
            TimeoutSeconds = settings?.TimeoutSeconds is > 0 ? settings.TimeoutSeconds!.Value : global.TimeoutSeconds,
            Skill = await ReadTextAsync(Path.Combine(dir, "skill.md"), cancellationToken).ConfigureAwait(false),
            Personality = await ReadTextAsync(Path.Combine(dir, "personality.md"), cancellationToken).ConfigureAwait(false)
        };
    }

    public async Task<IReadOnlyList<SkeletonAgentProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        var profiles = new List<SkeletonAgentProfile>();
        foreach (var role in Enum.GetValues<SkeletonAgentRole>())
            profiles.Add(await GetAsync(role, cancellationToken).ConfigureAwait(false));
        return profiles;
    }

    public async Task<SkeletonAgentProfileOutcome> UpdateAsync(
        SkeletonAgentRole role,
        SkeletonAgentProfileUpdate update,
        CancellationToken cancellationToken = default)
    {
        var leaked = new[] { update.Provider, update.BaseUrl, update.Model, update.Skill, update.Personality }
            .FirstOrDefault(value => !string.IsNullOrEmpty(value) &&
                SecretMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)));
        if (leaked is not null)
        {
            return new SkeletonAgentProfileOutcome
            {
                Succeeded = false,
                FailureReason =
                    "This update looks like it contains a secret (API key / token / password). A profile configures " +
                    "voice and model only — keep secrets in environment or provider config, never in an agent profile."
            };
        }

        if (update.TimeoutSeconds < 0)
        {
            return new SkeletonAgentProfileOutcome { Succeeded = false, FailureReason = "TimeoutSeconds cannot be negative." };
        }

        var dir = RoleDir(role);
        Directory.CreateDirectory(dir);

        var settings = new AgentSettingsFile
        {
            Provider = update.Provider?.Trim() ?? string.Empty,
            Model = update.Model?.Trim() ?? string.Empty,
            BaseUrl = update.BaseUrl?.Trim() ?? string.Empty,
            TimeoutSeconds = update.TimeoutSeconds
        };
        await File.WriteAllTextAsync(Path.Combine(dir, "agent.json"), JsonSerializer.Serialize(settings, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(dir, "skill.md"), update.Skill ?? string.Empty, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(dir, "personality.md"), update.Personality ?? string.Empty, cancellationToken).ConfigureAwait(false);

        return new SkeletonAgentProfileOutcome
        {
            Succeeded = true,
            Profile = await GetAsync(role, cancellationToken).ConfigureAwait(false)
        };
    }

    private string ProfilesRoot()
    {
        var configured = _configuration["AgentProfiles:Root"];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "AgentProfiles")
            : configured;
    }

    private string RoleDir(SkeletonAgentRole role) =>
        Path.Combine(ProfilesRoot(), role.ToString().ToLowerInvariant());

    private static async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken) =>
        File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false) : string.Empty;

    private static string Coalesce(string? value, string? fallback) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback?.Trim() ?? string.Empty;

    private sealed record AgentSettingsFile
    {
        public string Provider { get; init; } = string.Empty;
        public string Model { get; init; } = string.Empty;
        public string BaseUrl { get; init; } = string.Empty;
        public int? TimeoutSeconds { get; init; }
    }
}
