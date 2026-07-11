using System.Security.Cryptography;
using System.Text;
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

    // If an update's provider/skill/personality contains one of these, it is
    // almost certainly a leaked secret — refuse rather than persist it. Built
    // from fragments so no literal secret token lands in source (the secret
    // scanner would rightly flag a real one).
    private static readonly string[] SecretMarkers =
    [
        "api" + "_key", "api" + "key", "api" + "-key", "sec" + "ret", "bea" + "rer ",
        "-----" + "begin", "pass" + "word", "sk" + "-", "gh" + "p_", "xox" + "b-"
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

        var settings = await ReadSettingsAsync(settingsPath, cancellationToken).ConfigureAwait(false);

        var global = _configuration.GetSection("Ai").Get<LlmOptions>() ?? new LlmOptions();
        var builtIn = SkeletonAgentBuiltInDefaults.For(role);

        return new SkeletonAgentProfile
        {
            Role = role,
            DisplayName = SkeletonAgentRoles.DisplayName(role),
            BuiltInDefaultName = builtIn.Name,
            BuiltInDefaultVersion = builtIn.Version,
            Provider = Coalesce(settings?.Provider, global.Provider),
            Model = Coalesce(settings?.Model, global.Model),
            // BaseUrl is always the deployment's global value — never taken from
            // the profile file, so no edit (API or hand) can redirect outbound calls.
            BaseUrl = global.BaseUrl ?? string.Empty,
            TimeoutSeconds = settings?.TimeoutSeconds is > 0 ? settings.TimeoutSeconds!.Value : global.TimeoutSeconds,
            Skill = await ReadTextOrDefaultAsync(Path.Combine(dir, "skill.md"), builtIn.Skill, cancellationToken).ConfigureAwait(false),
            Personality = await ReadTextOrDefaultAsync(Path.Combine(dir, "personality.md"), builtIn.Personality, cancellationToken).ConfigureAwait(false),
            Boundary = SkeletonAgentRoles.CodeOwnedBoundary(role)
        };
    }

    public async Task<IReadOnlyList<SkeletonAgentProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        var profiles = new List<SkeletonAgentProfile>();
        foreach (var role in SkeletonAgentRoles.ProfileOrder)
            profiles.Add(await GetAsync(role, cancellationToken).ConfigureAwait(false));
        return profiles;
    }

    public async Task<IReadOnlyList<EffectiveSkeletonAgentProfile>> ListEffectiveAsync(
        int tenantId,
        int? projectId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (tenantId <= 0)
        {
            return [];
        }

        var profiles = new List<EffectiveSkeletonAgentProfile>();
        foreach (var role in SkeletonAgentRoles.ProfileOrder)
        {
            profiles.Add(await GetEffectiveAsync(role, projectId, cancellationToken).ConfigureAwait(false));
        }

        return profiles;
    }

    public async Task<SkeletonAgentProfileOutcome> UpdateAsync(
        SkeletonAgentRole role,
        SkeletonAgentProfileUpdate update,
        CancellationToken cancellationToken = default)
    {
        var leaked = new[] { update.Provider, update.Model, update.Skill, update.Personality }
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

        // Fail closed on provider: only known user-selectable providers, so a
        // typo or a hostile edit cannot silently downgrade an agent to a fake or
        // unknown model. "fake" is test/local only, behind an explicit config flag.
        var provider = update.Provider?.Trim().ToLowerInvariant() ?? string.Empty;
        var fakeAllowed = string.Equals(_configuration["AgentProfiles:AllowFakeProvider"], "true", StringComparison.OrdinalIgnoreCase);
        var providerAllowed = SkeletonAgentProviders.IsUserSelectable(provider) ||
                              (provider == SkeletonAgentProviders.Fake && fakeAllowed);
        if (!string.IsNullOrEmpty(provider) && !providerAllowed)
        {
            return new SkeletonAgentProfileOutcome
            {
                Succeeded = false,
                FailureReason =
                    $"Unknown or disallowed provider '{update.Provider}'. Allowed: {string.Join(", ", SkeletonAgentProviders.UserSelectable)}. " +
                    "A profile cannot silently point an agent at a fake or unknown model."
            };
        }

        if (update.TimeoutSeconds < 0)
        {
            return new SkeletonAgentProfileOutcome { Succeeded = false, FailureReason = "TimeoutSeconds cannot be negative." };
        }

        var dir = RoleDir(role);
        Directory.CreateDirectory(dir);

        // BaseUrl is intentionally NOT written from the update — the outbound
        // endpoint is deployment config, never a user-editable field.
        var settings = new AgentSettingsFile
        {
            Provider = update.Provider?.Trim() ?? string.Empty,
            Model = update.Model?.Trim() ?? string.Empty,
            BaseUrl = string.Empty,
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

    private async Task<EffectiveSkeletonAgentProfile> GetEffectiveAsync(
        SkeletonAgentRole role,
        int? projectId,
        CancellationToken cancellationToken)
    {
        var dir = RoleDir(role);
        var settingsPath = Path.Combine(dir, "agent.json");
        var skillPath = Path.Combine(dir, "skill.md");
        var personalityPath = Path.Combine(dir, "personality.md");
        var settings = await ReadSettingsAsync(settingsPath, cancellationToken).ConfigureAwait(false);
        var global = _configuration.GetSection("Ai").Get<LlmOptions>() ?? new LlmOptions();
        var builtIn = SkeletonAgentBuiltInDefaults.For(role);
        var isDeterministic = role == SkeletonAgentRole.Orchestrator;

        var provider = isDeterministic
            ? string.Empty
            : Coalesce(settings?.Provider, global.Provider);
        var model = isDeterministic
            ? string.Empty
            : Coalesce(settings?.Model, global.Model);
        var timeout = isDeterministic
            ? 0
            : settings?.TimeoutSeconds is > 0 ? settings.TimeoutSeconds!.Value : global.TimeoutSeconds;
        var skillFromOverride = File.Exists(skillPath);
        var personalityFromOverride = File.Exists(personalityPath);
        var skill = await ReadTextOrDefaultAsync(skillPath, builtIn.Skill, cancellationToken).ConfigureAwait(false);
        var personality = await ReadTextOrDefaultAsync(personalityPath, builtIn.Personality, cancellationToken).ConfigureAwait(false);
        var fieldSources = isDeterministic
            ? DeterministicSources(role)
            :
            [
                Source(
                    "provider",
                    string.IsNullOrWhiteSpace(settings?.Provider) ? "DeploymentDefault" : "RoleOverride",
                    string.IsNullOrWhiteSpace(settings?.Provider) ? "Ai:Provider" : $"{ProfileRelativePath(role)}/agent.json",
                    inherited: string.IsNullOrWhiteSpace(settings?.Provider)),
                Source(
                    "model",
                    string.IsNullOrWhiteSpace(settings?.Model) ? "DeploymentDefault" : "RoleOverride",
                    string.IsNullOrWhiteSpace(settings?.Model) ? "Ai:Model" : $"{ProfileRelativePath(role)}/agent.json",
                    inherited: string.IsNullOrWhiteSpace(settings?.Model)),
                Source(
                    "timeoutSeconds",
                    settings?.TimeoutSeconds is > 0 ? "RoleOverride" : "DeploymentDefault",
                    settings?.TimeoutSeconds is > 0 ? $"{ProfileRelativePath(role)}/agent.json" : "Ai:TimeoutSeconds",
                    inherited: settings?.TimeoutSeconds is not > 0),
                Source(
                    "effectiveSkill",
                    skillFromOverride ? "RoleOverride" : "BuiltInDefault",
                    skillFromOverride ? $"{ProfileRelativePath(role)}/skill.md" : SkeletonAgentBuiltInDefaults.Name,
                    inherited: !skillFromOverride,
                    version: skillFromOverride ? null : builtIn.Version),
                Source(
                    "effectivePersonality",
                    personalityFromOverride ? "RoleOverride" : "BuiltInDefault",
                    personalityFromOverride ? $"{ProfileRelativePath(role)}/personality.md" : SkeletonAgentBuiltInDefaults.Name,
                    inherited: !personalityFromOverride,
                    version: personalityFromOverride ? null : builtIn.Version)
            ];

        return new EffectiveSkeletonAgentProfile
        {
            Role = role,
            DisplayName = SkeletonAgentRoles.DisplayName(role),
            AiConnectionId = isDeterministic ? string.Empty : "deployment-default",
            Provider = provider,
            Model = model,
            TimeoutSeconds = timeout,
            EffectiveSkill = skill,
            EffectivePersonality = personality,
            FieldSources = fieldSources,
            BuiltInDefaultVersion = builtIn.Version,
            TenantProfileVersion = null,
            ProjectProfileVersion = null,
            EffectiveHash = EffectiveHash(role, provider, model, timeout, skill, personality, fieldSources, builtIn.Version),
            Boundary = SkeletonAgentRoles.CodeOwnedBoundary(role)
        };
    }

    private static async Task<AgentSettingsFile?> ReadSettingsAsync(string settingsPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AgentSettingsFile>(
                await File.ReadAllTextAsync(settingsPath, cancellationToken).ConfigureAwait(false), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<SkeletonAgentProfileFieldSource> DeterministicSources(SkeletonAgentRole role) =>
    [
        Source("provider", "DeterministicRole", role.ToString(), inherited: true, detail: "This role runs no model."),
        Source("model", "DeterministicRole", role.ToString(), inherited: true, detail: "This role runs no model."),
        Source("timeoutSeconds", "DeterministicRole", role.ToString(), inherited: true, detail: "This role runs no model."),
        Source("effectiveSkill", "DeterministicRole", role.ToString(), inherited: true, detail: "This role has no editable skill."),
        Source("effectivePersonality", "DeterministicRole", role.ToString(), inherited: true, detail: "This role has no editable personality.")
    ];

    private static SkeletonAgentProfileFieldSource Source(
        string field,
        string sourceLayer,
        string sourceLabel,
        bool inherited,
        string? version = null,
        string detail = "") =>
        new()
        {
            Field = field,
            SourceLayer = sourceLayer,
            SourceLabel = sourceLabel,
            Version = version,
            Inherited = inherited,
            Detail = detail
        };

    private static string ProfileRelativePath(SkeletonAgentRole role) =>
        $"AgentProfiles/{role.ToString().ToLowerInvariant()}";

    private static string EffectiveHash(
        SkeletonAgentRole role,
        string provider,
        string model,
        int timeoutSeconds,
        string skill,
        string personality,
        IReadOnlyList<SkeletonAgentProfileFieldSource> fieldSources,
        string builtInDefaultVersion)
    {
        var payload = JsonSerializer.Serialize(new
        {
            role,
            provider,
            model,
            timeoutSeconds,
            skill,
            personality,
            builtInDefaultVersion,
            fieldSources
        }, JsonOptions);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken) =>
        File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false) : string.Empty;

    private static async Task<string> ReadTextOrDefaultAsync(string path, string fallback, CancellationToken cancellationToken) =>
        File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false) : fallback;

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
