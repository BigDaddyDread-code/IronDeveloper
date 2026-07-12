using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> StateLocks = new(StringComparer.OrdinalIgnoreCase);
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
        var issues = Validate(update);
        if (issues.Count > 0)
            return new SkeletonAgentProfileOutcome { Succeeded = false, FailureReason = issues[0].Message };

        await WriteProfileAsync(role, update, cancellationToken).ConfigureAwait(false);

        return new SkeletonAgentProfileOutcome
        {
            Succeeded = true,
            Profile = await GetAsync(role, cancellationToken).ConfigureAwait(false)
        };
    }

    public async Task<SkeletonAgentProfileDraft> GetDraftAsync(
        SkeletonAgentRole role,
        CancellationToken cancellationToken = default)
    {
        var state = await ReadStateAsync(role, cancellationToken).ConfigureAwait(false);
        if (state.Draft is not null)
            return state.Draft;

        var values = ToUpdate(await GetAsync(role, cancellationToken).ConfigureAwait(false));
        var issues = Validate(values);
        return new SkeletonAgentProfileDraft
        {
            Role = role,
            Revision = state.Revision,
            BasePublishedVersion = state.CurrentVersion,
            Values = values,
            IsValid = issues.Count == 0,
            ValidationIssues = issues,
            UpdatedAtUtc = state.UpdatedAtUtc ?? DateTimeOffset.UtcNow
        };
    }

    public async Task<SkeletonAgentProfileDraftOutcome> SaveDraftAsync(
        SkeletonAgentRole role,
        SkeletonAgentProfileDraftWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (role == SkeletonAgentRole.Orchestrator)
            return Refused("DeterministicRole", "The Orchestrator has no editable model profile.", 0);

        var gate = StateLock(role);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(role, cancellationToken).ConfigureAwait(false);
            if (request.ExpectedRevision != state.Revision)
                return Refused("StaleWrite", $"Stale draft revision {request.ExpectedRevision}; current revision is {state.Revision}. Reload and compare before saving.", state.Revision, state.Draft);

            var values = request.ToUpdate();
            var issues = Validate(values);
            var draft = new SkeletonAgentProfileDraft
            {
                Role = role,
                Revision = state.Revision + 1,
                BasePublishedVersion = state.CurrentVersion,
                Values = values,
                IsValid = issues.Count == 0,
                ValidationIssues = issues,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            var next = state with { Revision = draft.Revision, Draft = draft, UpdatedAtUtc = draft.UpdatedAtUtc };
            await WriteStateAsync(role, next, cancellationToken).ConfigureAwait(false);
            return Accepted(next.Revision, draft: draft);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<SkeletonAgentProfileDraftTestOutcome> TestDraftAsync(
        SkeletonAgentRole role,
        CancellationToken cancellationToken = default)
    {
        var draft = await GetDraftAsync(role, cancellationToken).ConfigureAwait(false);
        var issues = Validate(draft.Values);
        var passed = issues.Count == 0 && role != SkeletonAgentRole.Orchestrator;
        return new SkeletonAgentProfileDraftTestOutcome
        {
            Succeeded = passed,
            Status = passed ? "Passed" : "Refused",
            FailureReason = passed ? string.Empty : issues.FirstOrDefault()?.Message ?? "The deterministic Orchestrator has no profile to test.",
            ValidationIssues = issues,
            ExecutedAtUtc = DateTimeOffset.UtcNow,
            Summary = passed
                ? $"Draft resolves to {draft.Values.Provider} / {draft.Values.Model} with the code-owned {SkeletonAgentRoles.DisplayName(role)} boundary intact. No provider request was sent."
                : "Draft configuration test did not run.",
            Boundary = SkeletonAgentRoles.CodeOwnedBoundary(role)
        };
    }

    public async Task<SkeletonAgentProfileDraftOutcome> PublishDraftAsync(
        SkeletonAgentRole role,
        SkeletonAgentProfilePublishRequest request,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var gate = StateLock(role);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(role, cancellationToken).ConfigureAwait(false);
            if (request.ExpectedRevision != state.Revision)
                return Refused("StaleWrite", $"Stale draft revision {request.ExpectedRevision}; current revision is {state.Revision}. Reload and compare before publishing.", state.Revision, state.Draft);
            if (state.Draft is null)
                return Refused("DraftMissing", "No saved draft exists for this agent.", state.Revision);
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Refused("PublishReasonRequired", "A publish reason is required.", state.Revision, state.Draft);

            var issues = Validate(state.Draft.Values);
            if (issues.Count > 0)
                return Refused("ValidationFailed", issues[0].Message, state.Revision, state.Draft with { IsValid = false, ValidationIssues = issues });

            await WriteProfileAsync(role, state.Draft.Values, cancellationToken).ConfigureAwait(false);
            var published = new SkeletonAgentProfilePublishedVersion
            {
                Version = state.CurrentVersion + 1,
                Role = role,
                Values = state.Draft.Values,
                Reason = request.Reason.Trim(),
                ActorUserId = actorUserId,
                PublishedAtUtc = DateTimeOffset.UtcNow
            };
            var next = state with
            {
                Revision = state.Revision + 1,
                CurrentVersion = published.Version,
                Draft = null,
                History = [.. state.History, published],
                UpdatedAtUtc = published.PublishedAtUtc
            };
            await WriteStateAsync(role, next, cancellationToken).ConfigureAwait(false);
            return Accepted(next.Revision, publishedVersion: published, profile: await GetAsync(role, cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SkeletonAgentProfilePublishedVersion>> ListHistoryAsync(
        SkeletonAgentRole role,
        CancellationToken cancellationToken = default) =>
        (await ReadStateAsync(role, cancellationToken).ConfigureAwait(false)).History
            .OrderByDescending(item => item.Version)
            .ToArray();

    public async Task<SkeletonAgentProfileDraftOutcome> ResetAsync(
        SkeletonAgentRole role,
        SkeletonAgentProfileResetRequest request,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (role == SkeletonAgentRole.Orchestrator)
            return Refused("DeterministicRole", "The Orchestrator has no editable model profile.", 0);

        var gate = StateLock(role);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(role, cancellationToken).ConfigureAwait(false);
            if (request.ExpectedRevision != state.Revision)
                return Refused("StaleWrite", $"Stale profile revision {request.ExpectedRevision}; current revision is {state.Revision}. Reload and compare before resetting.", state.Revision, state.Draft);
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Refused("ResetReasonRequired", "A reset reason is required.", state.Revision, state.Draft);
            if (request.Scope.Equals(SkeletonAgentProfileResetScopes.Project, StringComparison.OrdinalIgnoreCase) ||
                request.Scope.Equals(SkeletonAgentProfileResetScopes.Tenant, StringComparison.OrdinalIgnoreCase))
                return Refused("ScopeUnavailable", $"{request.Scope} profile storage is not implemented. No reset was performed.", state.Revision, state.Draft);

            var defaults = DefaultUpdate(role);
            SkeletonAgentProfileUpdate replacement;
            if (request.Scope.Equals(SkeletonAgentProfileResetScopes.Field, StringComparison.OrdinalIgnoreCase))
            {
                var current = ToUpdate(await GetAsync(role, cancellationToken).ConfigureAwait(false));
                replacement = request.Field.Trim().ToLowerInvariant() switch
                {
                    "provider" => current with { Provider = defaults.Provider },
                    "model" => current with { Model = defaults.Model },
                    "timeoutseconds" => current with { TimeoutSeconds = defaults.TimeoutSeconds },
                    "skill" => current with { Skill = defaults.Skill },
                    "personality" => current with { Personality = defaults.Personality },
                    _ => null!
                };
                if (replacement is null)
                    return Refused("ResetFieldInvalid", "Reset field must be provider, model, timeoutSeconds, skill, or personality.", state.Revision, state.Draft);
            }
            else if (request.Scope.Equals(SkeletonAgentProfileResetScopes.Agent, StringComparison.OrdinalIgnoreCase) ||
                     request.Scope.Equals(SkeletonAgentProfileResetScopes.BuiltIn, StringComparison.OrdinalIgnoreCase))
            {
                replacement = defaults;
            }
            else
            {
                return Refused("ResetScopeInvalid", "Reset scope must be Field, Agent, BuiltIn, Project, or Tenant.", state.Revision, state.Draft);
            }

            return await PublishReplacementAsync(role, replacement, request.Reason, actorUserId, state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<SkeletonAgentProfileDraftOutcome> RestoreAsync(
        SkeletonAgentRole role,
        long version,
        SkeletonAgentProfileRestoreRequest request,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var gate = StateLock(role);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(role, cancellationToken).ConfigureAwait(false);
            if (request.ExpectedRevision != state.Revision)
                return Refused("StaleWrite", $"Stale profile revision {request.ExpectedRevision}; current revision is {state.Revision}. Reload and compare before restoring.", state.Revision, state.Draft);
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Refused("RestoreReasonRequired", "A restore reason is required.", state.Revision, state.Draft);
            var selected = state.History.SingleOrDefault(item => item.Version == version);
            if (selected is null)
                return Refused("VersionNotFound", $"Published profile version {version} was not found.", state.Revision, state.Draft);

            return await PublishReplacementAsync(role, selected.Values, request.Reason, actorUserId, state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
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

    private SemaphoreSlim StateLock(SkeletonAgentRole role) =>
        StateLocks.GetOrAdd(Path.GetFullPath(RoleDir(role)), static _ => new SemaphoreSlim(1, 1));

    private async Task<AgentProfileState> ReadStateAsync(SkeletonAgentRole role, CancellationToken cancellationToken)
    {
        var path = Path.Combine(RoleDir(role), "profile-state.json");
        if (!File.Exists(path))
            return new AgentProfileState();
        try
        {
            return JsonSerializer.Deserialize<AgentProfileState>(
                await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), JsonOptions) ?? new AgentProfileState();
        }
        catch (JsonException)
        {
            return new AgentProfileState();
        }
    }

    private async Task WriteStateAsync(SkeletonAgentRole role, AgentProfileState state, CancellationToken cancellationToken)
    {
        var dir = RoleDir(role);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "profile-state.json");
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, JsonOptions), cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    private async Task WriteProfileAsync(SkeletonAgentRole role, SkeletonAgentProfileUpdate update, CancellationToken cancellationToken)
    {
        var dir = RoleDir(role);
        Directory.CreateDirectory(dir);
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
    }

    private IReadOnlyList<SkeletonAgentProfileValidationIssue> Validate(SkeletonAgentProfileUpdate update)
    {
        var issues = new List<SkeletonAgentProfileValidationIssue>();
        var leakedField = new Dictionary<string, string?>
        {
            ["provider"] = update.Provider,
            ["model"] = update.Model,
            ["skill"] = update.Skill,
            ["personality"] = update.Personality
        }.FirstOrDefault(item => !string.IsNullOrEmpty(item.Value) && SecretMarkers.Any(marker => item.Value.Contains(marker, StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrEmpty(leakedField.Key))
            issues.Add(Issue("SecretLikeContent", leakedField.Key, "This update looks like it contains a secret. Profiles configure voice and model only; store credentials in AI Connections, never in an agent profile."));

        var provider = update.Provider?.Trim().ToLowerInvariant() ?? string.Empty;
        var fakeAllowed = string.Equals(_configuration["AgentProfiles:AllowFakeProvider"], "true", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(provider))
            issues.Add(Issue("ProviderRequired", "provider", "Provider is required."));
        else if (!SkeletonAgentProviders.IsUserSelectable(provider) && !(provider == SkeletonAgentProviders.Fake && fakeAllowed))
            issues.Add(Issue("ProviderInvalid", "provider", $"Unknown or disallowed provider '{update.Provider}'. Allowed: {string.Join(", ", SkeletonAgentProviders.UserSelectable)}. A profile cannot silently point an agent at a fake or unknown model."));
        if (string.IsNullOrWhiteSpace(update.Model))
            issues.Add(Issue("ModelRequired", "model", "Model is required."));
        if (update.TimeoutSeconds < 0)
            issues.Add(Issue("TimeoutInvalid", "timeoutSeconds", "TimeoutSeconds cannot be negative."));
        return issues;
    }

    private static SkeletonAgentProfileValidationIssue Issue(string code, string field, string message) =>
        new() { Code = code, Field = field, Message = message };

    private static SkeletonAgentProfileUpdate ToUpdate(SkeletonAgentProfile profile) => new()
    {
        Provider = profile.Provider,
        Model = profile.Model,
        TimeoutSeconds = profile.TimeoutSeconds,
        Skill = profile.Skill,
        Personality = profile.Personality
    };

    private static SkeletonAgentProfileDraftOutcome Accepted(
        long revision,
        SkeletonAgentProfileDraft? draft = null,
        SkeletonAgentProfilePublishedVersion? publishedVersion = null,
        SkeletonAgentProfile? profile = null) =>
        new() { Succeeded = true, CurrentRevision = revision, Draft = draft, PublishedVersion = publishedVersion, Profile = profile };

    private static SkeletonAgentProfileDraftOutcome Refused(
        string code,
        string reason,
        long revision,
        SkeletonAgentProfileDraft? draft = null) =>
        new() { Succeeded = false, Code = code, FailureReason = reason, CurrentRevision = revision, Draft = draft };

    private SkeletonAgentProfileUpdate DefaultUpdate(SkeletonAgentRole role)
    {
        var global = _configuration.GetSection("Ai").Get<LlmOptions>() ?? new LlmOptions();
        var builtIn = SkeletonAgentBuiltInDefaults.For(role);
        return new SkeletonAgentProfileUpdate
        {
            Provider = global.Provider?.Trim() ?? string.Empty,
            Model = global.Model?.Trim() ?? string.Empty,
            TimeoutSeconds = global.TimeoutSeconds,
            Skill = builtIn.Skill,
            Personality = builtIn.Personality
        };
    }

    private async Task<SkeletonAgentProfileDraftOutcome> PublishReplacementAsync(
        SkeletonAgentRole role,
        SkeletonAgentProfileUpdate replacement,
        string reason,
        int actorUserId,
        AgentProfileState state,
        CancellationToken cancellationToken)
    {
        var issues = Validate(replacement);
        if (issues.Count > 0)
            return Refused("ValidationFailed", issues[0].Message, state.Revision, state.Draft);

        await WriteProfileAsync(role, replacement, cancellationToken).ConfigureAwait(false);
        var published = new SkeletonAgentProfilePublishedVersion
        {
            Version = state.CurrentVersion + 1,
            Role = role,
            Values = replacement,
            Reason = reason.Trim(),
            ActorUserId = actorUserId,
            PublishedAtUtc = DateTimeOffset.UtcNow
        };
        var next = state with
        {
            Revision = state.Revision + 1,
            CurrentVersion = published.Version,
            Draft = null,
            History = [.. state.History, published],
            UpdatedAtUtc = published.PublishedAtUtc
        };
        await WriteStateAsync(role, next, cancellationToken).ConfigureAwait(false);
        return Accepted(next.Revision, publishedVersion: published, profile: await GetAsync(role, cancellationToken).ConfigureAwait(false));
    }

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
        var state = await ReadStateAsync(role, cancellationToken).ConfigureAwait(false);

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
            PublishedVersion = state.CurrentVersion > 0 ? state.CurrentVersion : null,
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

    private sealed record AgentProfileState
    {
        public long Revision { get; init; }
        public long CurrentVersion { get; init; }
        public SkeletonAgentProfileDraft? Draft { get; init; }
        public IReadOnlyList<SkeletonAgentProfilePublishedVersion> History { get; init; } = [];
        public DateTimeOffset? UpdatedAtUtc { get; init; }
    }
}
