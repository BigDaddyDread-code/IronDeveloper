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
            profiles.Add(await GetEffectiveAsync(role, tenantId, projectId, cancellationToken).ConfigureAwait(false));
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

    public Task<SkeletonAgentProfileDraft> GetDraftAsync(SkeletonAgentRole role, CancellationToken cancellationToken = default) =>
        GetDraftInternalAsync(role, null, cancellationToken);

    public Task<SkeletonAgentProfileDraft> GetDraftAsync(SkeletonAgentRole role, SkeletonAgentProfileScope scope, CancellationToken cancellationToken = default) =>
        GetDraftInternalAsync(role, scope, cancellationToken);

    private async Task<SkeletonAgentProfileDraft> GetDraftInternalAsync(
        SkeletonAgentRole role,
        SkeletonAgentProfileScope? scope,
        CancellationToken cancellationToken)
    {
        var state = await ReadStateAsync(role, scope, cancellationToken).ConfigureAwait(false);
        if (state.Draft is not null)
            return state.Draft;

        var values = scope is null
            ? ToUpdate(await GetAsync(role, cancellationToken).ConfigureAwait(false))
            : ToUpdate((await ListEffectiveAsync(scope.TenantId, scope.ProjectId, cancellationToken).ConfigureAwait(false)).Single(profile => profile.Role == role));
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

    public Task<SkeletonAgentProfileDraftOutcome> SaveDraftAsync(SkeletonAgentRole role, SkeletonAgentProfileDraftWriteRequest request, CancellationToken cancellationToken = default) =>
        SaveDraftInternalAsync(role, null, request, cancellationToken);

    public Task<SkeletonAgentProfileDraftOutcome> SaveDraftAsync(SkeletonAgentRole role, SkeletonAgentProfileScope scope, SkeletonAgentProfileDraftWriteRequest request, CancellationToken cancellationToken = default) =>
        SaveDraftInternalAsync(role, scope, request, cancellationToken);

    private async Task<SkeletonAgentProfileDraftOutcome> SaveDraftInternalAsync(
        SkeletonAgentRole role,
        SkeletonAgentProfileScope? scope,
        SkeletonAgentProfileDraftWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (role == SkeletonAgentRole.Orchestrator)
            return Refused("DeterministicRole", "The Orchestrator has no editable model profile.", 0);

        var gate = StateLock(role, scope);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(role, scope, cancellationToken).ConfigureAwait(false);
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
            await WriteStateAsync(role, scope, next, cancellationToken).ConfigureAwait(false);
            return Accepted(next.Revision, draft: draft);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<SkeletonAgentProfileDraftTestOutcome> TestDraftAsync(SkeletonAgentRole role, CancellationToken cancellationToken = default) =>
        TestDraftInternalAsync(role, null, cancellationToken);

    public Task<SkeletonAgentProfileDraftTestOutcome> TestDraftAsync(SkeletonAgentRole role, SkeletonAgentProfileScope scope, CancellationToken cancellationToken = default) =>
        TestDraftInternalAsync(role, scope, cancellationToken);

    private async Task<SkeletonAgentProfileDraftTestOutcome> TestDraftInternalAsync(
        SkeletonAgentRole role,
        SkeletonAgentProfileScope? scope,
        CancellationToken cancellationToken)
    {
        var draft = await GetDraftInternalAsync(role, scope, cancellationToken).ConfigureAwait(false);
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

    public Task<SkeletonAgentProfileDraftOutcome> PublishDraftAsync(SkeletonAgentRole role, SkeletonAgentProfilePublishRequest request, int actorUserId, CancellationToken cancellationToken = default) =>
        PublishDraftInternalAsync(role, null, request, actorUserId, cancellationToken);

    public Task<SkeletonAgentProfileDraftOutcome> PublishDraftAsync(SkeletonAgentRole role, SkeletonAgentProfileScope scope, SkeletonAgentProfilePublishRequest request, int actorUserId, CancellationToken cancellationToken = default) =>
        PublishDraftInternalAsync(role, scope, request, actorUserId, cancellationToken);

    private async Task<SkeletonAgentProfileDraftOutcome> PublishDraftInternalAsync(
        SkeletonAgentRole role,
        SkeletonAgentProfileScope? scope,
        SkeletonAgentProfilePublishRequest request,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        var gate = StateLock(role, scope);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(role, scope, cancellationToken).ConfigureAwait(false);
            if (request.ExpectedRevision != state.Revision)
                return Refused("StaleWrite", $"Stale draft revision {request.ExpectedRevision}; current revision is {state.Revision}. Reload and compare before publishing.", state.Revision, state.Draft);
            if (state.Draft is null)
                return Refused("DraftMissing", "No saved draft exists for this agent.", state.Revision);
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Refused("PublishReasonRequired", "A publish reason is required.", state.Revision, state.Draft);

            var issues = Validate(state.Draft.Values);
            if (issues.Count > 0)
                return Refused("ValidationFailed", issues[0].Message, state.Revision, state.Draft with { IsValid = false, ValidationIssues = issues });

            await WriteProfileAsync(role, scope, state.Draft.Values, cancellationToken).ConfigureAwait(false);
            var published = new SkeletonAgentProfilePublishedVersion
            {
                Version = state.CurrentVersion + 1,
                Role = role,
                Values = state.Draft.Values,
                Reason = request.Reason.Trim(),
                ActorUserId = actorUserId,
                PublishedAtUtc = DateTimeOffset.UtcNow,
                ScopeLayer = scope?.Layer ?? "LegacyRole",
                TenantId = scope?.TenantId,
                ProjectId = scope?.ProjectId
            };
            var next = state with
            {
                Revision = state.Revision + 1,
                CurrentVersion = published.Version,
                Draft = null,
                History = [.. state.History, published],
                UpdatedAtUtc = published.PublishedAtUtc
            };
            await WriteStateAsync(role, scope, next, cancellationToken).ConfigureAwait(false);
            return Accepted(next.Revision, publishedVersion: published, profile: await GetOutcomeProfileAsync(role, scope, cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<IReadOnlyList<SkeletonAgentProfilePublishedVersion>> ListHistoryAsync(SkeletonAgentRole role, CancellationToken cancellationToken = default) =>
        ListHistoryInternalAsync(role, null, cancellationToken);

    public Task<IReadOnlyList<SkeletonAgentProfilePublishedVersion>> ListHistoryAsync(SkeletonAgentRole role, SkeletonAgentProfileScope scope, CancellationToken cancellationToken = default) =>
        ListHistoryInternalAsync(role, scope, cancellationToken);

    private async Task<IReadOnlyList<SkeletonAgentProfilePublishedVersion>> ListHistoryInternalAsync(
        SkeletonAgentRole role,
        SkeletonAgentProfileScope? scope,
        CancellationToken cancellationToken) =>
        (await ReadStateAsync(role, scope, cancellationToken).ConfigureAwait(false)).History
            .OrderByDescending(item => item.Version)
            .ToArray();

    public Task<SkeletonAgentProfileDraftOutcome> ResetAsync(SkeletonAgentRole role, SkeletonAgentProfileResetRequest request, int actorUserId, CancellationToken cancellationToken = default) =>
        ResetInternalAsync(role, null, request, actorUserId, cancellationToken);

    public Task<SkeletonAgentProfileDraftOutcome> ResetAsync(SkeletonAgentRole role, SkeletonAgentProfileScope scope, SkeletonAgentProfileResetRequest request, int actorUserId, CancellationToken cancellationToken = default) =>
        ResetInternalAsync(role, scope, request, actorUserId, cancellationToken);

    private async Task<SkeletonAgentProfileDraftOutcome> ResetInternalAsync(
        SkeletonAgentRole role,
        SkeletonAgentProfileScope? scope,
        SkeletonAgentProfileResetRequest request,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        if (role == SkeletonAgentRole.Orchestrator)
            return Refused("DeterministicRole", "The Orchestrator has no editable model profile.", 0);

        var gate = StateLock(role, scope);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(role, scope, cancellationToken).ConfigureAwait(false);
            if (request.ExpectedRevision != state.Revision)
                return Refused("StaleWrite", $"Stale profile revision {request.ExpectedRevision}; current revision is {state.Revision}. Reload and compare before resetting.", state.Revision, state.Draft);
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Refused("ResetReasonRequired", "A reset reason is required.", state.Revision, state.Draft);
            if (scope is null &&
                (request.Scope.Equals(SkeletonAgentProfileResetScopes.Project, StringComparison.OrdinalIgnoreCase) ||
                 request.Scope.Equals(SkeletonAgentProfileResetScopes.Tenant, StringComparison.OrdinalIgnoreCase)))
                return Refused("ScopeUnavailable", "A tenant or project reset requires an explicit scoped profile endpoint. No reset was performed.", state.Revision, state.Draft);
            if (request.Scope.Equals(SkeletonAgentProfileResetScopes.Project, StringComparison.OrdinalIgnoreCase) && scope?.ProjectId is not > 0)
                return Refused("ScopeMismatch", "Project reset requires a project profile scope.", state.Revision, state.Draft);
            if (request.Scope.Equals(SkeletonAgentProfileResetScopes.Tenant, StringComparison.OrdinalIgnoreCase) && scope?.ProjectId is > 0)
                return Refused("ScopeMismatch", "Tenant reset requires a tenant profile scope.", state.Revision, state.Draft);

            if (scope is not null && request.Scope.Equals(SkeletonAgentProfileResetScopes.Field, StringComparison.OrdinalIgnoreCase))
            {
                if (!await RemoveScopedFieldAsync(role, scope, request.Field, cancellationToken).ConfigureAwait(false))
                    return Refused("ResetFieldInvalid", "Reset field must be provider, model, timeoutSeconds, skill, or personality.", state.Revision, state.Draft);
                var inherited = ToUpdate((await ListEffectiveAsync(scope.TenantId, scope.ProjectId, cancellationToken).ConfigureAwait(false)).Single(profile => profile.Role == role));
                return await PublishStateOnlyAsync(role, scope, inherited, request.Reason, actorUserId, state, cancellationToken).ConfigureAwait(false);
            }

            if (scope is not null &&
                (request.Scope.Equals(SkeletonAgentProfileResetScopes.Agent, StringComparison.OrdinalIgnoreCase) ||
                 request.Scope.Equals(SkeletonAgentProfileResetScopes.Project, StringComparison.OrdinalIgnoreCase) ||
                 request.Scope.Equals(SkeletonAgentProfileResetScopes.Tenant, StringComparison.OrdinalIgnoreCase)))
            {
                RemoveScopedProfileFiles(role, scope);
                var inherited = ToUpdate((await ListEffectiveAsync(scope.TenantId, scope.ProjectId, cancellationToken).ConfigureAwait(false)).Single(profile => profile.Role == role));
                return await PublishStateOnlyAsync(role, scope, inherited, request.Reason, actorUserId, state, cancellationToken).ConfigureAwait(false);
            }

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

            return await PublishReplacementAsync(role, scope, replacement, request.Reason, actorUserId, state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<SkeletonAgentProfileDraftOutcome> RestoreAsync(SkeletonAgentRole role, long version, SkeletonAgentProfileRestoreRequest request, int actorUserId, CancellationToken cancellationToken = default) =>
        RestoreInternalAsync(role, null, version, request, actorUserId, cancellationToken);

    public Task<SkeletonAgentProfileDraftOutcome> RestoreAsync(SkeletonAgentRole role, SkeletonAgentProfileScope scope, long version, SkeletonAgentProfileRestoreRequest request, int actorUserId, CancellationToken cancellationToken = default) =>
        RestoreInternalAsync(role, scope, version, request, actorUserId, cancellationToken);

    private async Task<SkeletonAgentProfileDraftOutcome> RestoreInternalAsync(
        SkeletonAgentRole role,
        SkeletonAgentProfileScope? scope,
        long version,
        SkeletonAgentProfileRestoreRequest request,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        var gate = StateLock(role, scope);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(role, scope, cancellationToken).ConfigureAwait(false);
            if (request.ExpectedRevision != state.Revision)
                return Refused("StaleWrite", $"Stale profile revision {request.ExpectedRevision}; current revision is {state.Revision}. Reload and compare before restoring.", state.Revision, state.Draft);
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Refused("RestoreReasonRequired", "A restore reason is required.", state.Revision, state.Draft);
            var selected = state.History.SingleOrDefault(item => item.Version == version);
            if (selected is null)
                return Refused("VersionNotFound", $"Published profile version {version} was not found.", state.Revision, state.Draft);

            return await PublishReplacementAsync(role, scope, selected.Values, request.Reason, actorUserId, state, cancellationToken).ConfigureAwait(false);
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

    private string RoleDir(SkeletonAgentRole role, SkeletonAgentProfileScope? scope) =>
        scope is null
            ? RoleDir(role)
            : scope.ProjectId is > 0
                ? Path.Combine(ProfilesRoot(), "tenants", scope.TenantId.ToString(), "projects", scope.ProjectId.Value.ToString(), role.ToString().ToLowerInvariant())
                : Path.Combine(ProfilesRoot(), "tenants", scope.TenantId.ToString(), role.ToString().ToLowerInvariant());

    private SemaphoreSlim StateLock(SkeletonAgentRole role) =>
        StateLocks.GetOrAdd(Path.GetFullPath(RoleDir(role)), static _ => new SemaphoreSlim(1, 1));

    private SemaphoreSlim StateLock(SkeletonAgentRole role, SkeletonAgentProfileScope? scope) =>
        StateLocks.GetOrAdd(Path.GetFullPath(RoleDir(role, scope)), static _ => new SemaphoreSlim(1, 1));

    private async Task<AgentProfileState> ReadStateAsync(SkeletonAgentRole role, CancellationToken cancellationToken)
        => await ReadStateAsync(role, null, cancellationToken).ConfigureAwait(false);

    private async Task<AgentProfileState> ReadStateAsync(SkeletonAgentRole role, SkeletonAgentProfileScope? scope, CancellationToken cancellationToken)
    {
        var path = Path.Combine(RoleDir(role, scope), "profile-state.json");
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
        => await WriteStateAsync(role, null, state, cancellationToken).ConfigureAwait(false);

    private async Task WriteStateAsync(SkeletonAgentRole role, SkeletonAgentProfileScope? scope, AgentProfileState state, CancellationToken cancellationToken)
    {
        var dir = RoleDir(role, scope);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "profile-state.json");
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, JsonOptions), cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    private async Task WriteProfileAsync(SkeletonAgentRole role, SkeletonAgentProfileUpdate update, CancellationToken cancellationToken)
        => await WriteProfileAsync(role, null, update, cancellationToken).ConfigureAwait(false);

    private async Task WriteProfileAsync(SkeletonAgentRole role, SkeletonAgentProfileScope? scope, SkeletonAgentProfileUpdate update, CancellationToken cancellationToken)
    {
        var dir = RoleDir(role, scope);
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

    private static SkeletonAgentProfileUpdate ToUpdate(EffectiveSkeletonAgentProfile profile) => new()
    {
        Provider = profile.Provider,
        Model = profile.Model,
        TimeoutSeconds = profile.TimeoutSeconds,
        Skill = profile.EffectiveSkill,
        Personality = profile.EffectivePersonality
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
        SkeletonAgentProfileScope? scope,
        SkeletonAgentProfileUpdate replacement,
        string reason,
        int actorUserId,
        AgentProfileState state,
        CancellationToken cancellationToken)
    {
        var issues = Validate(replacement);
        if (issues.Count > 0)
            return Refused("ValidationFailed", issues[0].Message, state.Revision, state.Draft);

        await WriteProfileAsync(role, scope, replacement, cancellationToken).ConfigureAwait(false);
        var published = new SkeletonAgentProfilePublishedVersion
        {
            Version = state.CurrentVersion + 1,
            Role = role,
            Values = replacement,
            Reason = reason.Trim(),
            ActorUserId = actorUserId,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            ScopeLayer = scope?.Layer ?? "LegacyRole",
            TenantId = scope?.TenantId,
            ProjectId = scope?.ProjectId
        };
        var next = state with
        {
            Revision = state.Revision + 1,
            CurrentVersion = published.Version,
            Draft = null,
            History = [.. state.History, published],
            UpdatedAtUtc = published.PublishedAtUtc
        };
        await WriteStateAsync(role, scope, next, cancellationToken).ConfigureAwait(false);
        return Accepted(next.Revision, publishedVersion: published, profile: await GetOutcomeProfileAsync(role, scope, cancellationToken).ConfigureAwait(false));
    }

    private async Task<SkeletonAgentProfileDraftOutcome> PublishStateOnlyAsync(
        SkeletonAgentRole role,
        SkeletonAgentProfileScope scope,
        SkeletonAgentProfileUpdate effectiveValues,
        string reason,
        int actorUserId,
        AgentProfileState state,
        CancellationToken cancellationToken)
    {
        var published = new SkeletonAgentProfilePublishedVersion
        {
            Version = state.CurrentVersion + 1,
            Role = role,
            Values = effectiveValues,
            Reason = reason.Trim(),
            ActorUserId = actorUserId,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            ScopeLayer = scope.Layer,
            TenantId = scope.TenantId,
            ProjectId = scope.ProjectId
        };
        var next = state with
        {
            Revision = state.Revision + 1,
            CurrentVersion = published.Version,
            Draft = null,
            History = [.. state.History, published],
            UpdatedAtUtc = published.PublishedAtUtc
        };
        await WriteStateAsync(role, scope, next, cancellationToken).ConfigureAwait(false);
        return Accepted(next.Revision, publishedVersion: published, profile: await GetOutcomeProfileAsync(role, scope, cancellationToken).ConfigureAwait(false));
    }

    private async Task<bool> RemoveScopedFieldAsync(
        SkeletonAgentRole role,
        SkeletonAgentProfileScope scope,
        string field,
        CancellationToken cancellationToken)
    {
        var directory = RoleDir(role, scope);
        var normalized = field.Trim().ToLowerInvariant();
        if (normalized is "skill" or "personality")
        {
            var textPath = Path.Combine(directory, normalized + ".md");
            if (File.Exists(textPath))
                File.Delete(textPath);
            return true;
        }
        if (normalized is not ("provider" or "model" or "timeoutseconds"))
            return false;

        var settingsPath = Path.Combine(directory, "agent.json");
        var settings = await ReadSettingsAsync(settingsPath, cancellationToken).ConfigureAwait(false) ?? new AgentSettingsFile();
        settings = normalized switch
        {
            "provider" => settings with { Provider = string.Empty },
            "model" => settings with { Model = string.Empty },
            _ => settings with { TimeoutSeconds = null }
        };
        if (string.IsNullOrWhiteSpace(settings.Provider) && string.IsNullOrWhiteSpace(settings.Model) && settings.TimeoutSeconds is not > 0)
        {
            if (File.Exists(settingsPath))
                File.Delete(settingsPath);
        }
        else
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(settings, JsonOptions), cancellationToken).ConfigureAwait(false);
        }
        return true;
    }

    private void RemoveScopedProfileFiles(SkeletonAgentRole role, SkeletonAgentProfileScope scope)
    {
        var directory = RoleDir(role, scope);
        foreach (var name in new[] { "agent.json", "skill.md", "personality.md" })
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private async Task<SkeletonAgentProfile> GetOutcomeProfileAsync(
        SkeletonAgentRole role,
        SkeletonAgentProfileScope? scope,
        CancellationToken cancellationToken)
    {
        if (scope is null)
            return await GetAsync(role, cancellationToken).ConfigureAwait(false);
        var effective = (await ListEffectiveAsync(scope.TenantId, scope.ProjectId, cancellationToken).ConfigureAwait(false))
            .Single(profile => profile.Role == role);
        return new SkeletonAgentProfile
        {
            Role = role,
            DisplayName = effective.DisplayName,
            BuiltInDefaultName = SkeletonAgentBuiltInDefaults.Name,
            BuiltInDefaultVersion = effective.BuiltInDefaultVersion,
            Provider = effective.Provider,
            Model = effective.Model,
            TimeoutSeconds = effective.TimeoutSeconds,
            Skill = effective.EffectiveSkill,
            Personality = effective.EffectivePersonality,
            Boundary = effective.Boundary
        };
    }

    private async Task<EffectiveSkeletonAgentProfile> GetEffectiveAsync(
        SkeletonAgentRole role,
        int tenantId,
        int? projectId,
        CancellationToken cancellationToken)
    {
        var legacyDir = RoleDir(role);
        var legacySettings = await ReadSettingsAsync(Path.Combine(legacyDir, "agent.json"), cancellationToken).ConfigureAwait(false);
        var global = _configuration.GetSection("Ai").Get<LlmOptions>() ?? new LlmOptions();
        var builtIn = SkeletonAgentBuiltInDefaults.For(role);
        var isDeterministic = role == SkeletonAgentRole.Orchestrator;
        var legacyState = await ReadStateAsync(role, cancellationToken).ConfigureAwait(false);
        var tenantScope = new SkeletonAgentProfileScope { TenantId = tenantId };
        var tenantDir = RoleDir(role, tenantScope);
        var tenantSettings = await ReadSettingsAsync(Path.Combine(tenantDir, "agent.json"), cancellationToken).ConfigureAwait(false);
        var tenantState = await ReadStateAsync(role, tenantScope, cancellationToken).ConfigureAwait(false);
        SkeletonAgentProfileScope? projectScope = projectId is > 0
            ? new SkeletonAgentProfileScope { TenantId = tenantId, ProjectId = projectId }
            : null;
        var projectDir = projectScope is null ? string.Empty : RoleDir(role, projectScope);
        var projectSettings = projectScope is null
            ? null
            : await ReadSettingsAsync(Path.Combine(projectDir, "agent.json"), cancellationToken).ConfigureAwait(false);
        var projectState = projectScope is null
            ? new AgentProfileState()
            : await ReadStateAsync(role, projectScope, cancellationToken).ConfigureAwait(false);

        var provider = isDeterministic
            ? string.Empty
            : Coalesce(projectSettings?.Provider, Coalesce(tenantSettings?.Provider, Coalesce(legacySettings?.Provider, global.Provider)));
        var model = isDeterministic
            ? string.Empty
            : Coalesce(projectSettings?.Model, Coalesce(tenantSettings?.Model, Coalesce(legacySettings?.Model, global.Model)));
        var timeout = isDeterministic
            ? 0
            : projectSettings?.TimeoutSeconds is > 0 ? projectSettings.TimeoutSeconds.Value
            : tenantSettings?.TimeoutSeconds is > 0 ? tenantSettings.TimeoutSeconds.Value
            : legacySettings?.TimeoutSeconds is > 0 ? legacySettings.TimeoutSeconds.Value
            : global.TimeoutSeconds;
        var legacySkillPath = Path.Combine(legacyDir, "skill.md");
        var tenantSkillPath = Path.Combine(tenantDir, "skill.md");
        var projectSkillPath = projectScope is null ? string.Empty : Path.Combine(projectDir, "skill.md");
        var legacyPersonalityPath = Path.Combine(legacyDir, "personality.md");
        var tenantPersonalityPath = Path.Combine(tenantDir, "personality.md");
        var projectPersonalityPath = projectScope is null ? string.Empty : Path.Combine(projectDir, "personality.md");
        var skill = projectScope is not null && File.Exists(projectSkillPath) ? await ReadTextAsync(projectSkillPath, cancellationToken).ConfigureAwait(false)
            : File.Exists(tenantSkillPath) ? await ReadTextAsync(tenantSkillPath, cancellationToken).ConfigureAwait(false)
            : await ReadTextOrDefaultAsync(legacySkillPath, builtIn.Skill, cancellationToken).ConfigureAwait(false);
        var personality = projectScope is not null && File.Exists(projectPersonalityPath) ? await ReadTextAsync(projectPersonalityPath, cancellationToken).ConfigureAwait(false)
            : File.Exists(tenantPersonalityPath) ? await ReadTextAsync(tenantPersonalityPath, cancellationToken).ConfigureAwait(false)
            : await ReadTextOrDefaultAsync(legacyPersonalityPath, builtIn.Personality, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<SkeletonAgentProfileFieldSource> fieldSources;
        if (isDeterministic)
        {
            fieldSources = DeterministicSources(role);
        }
        else
        {
            var sources = new List<SkeletonAgentProfileFieldSource>
            {
                Source(
                    "provider",
                    string.IsNullOrWhiteSpace(legacySettings?.Provider) ? "DeploymentDefault" : "RoleOverride",
                    string.IsNullOrWhiteSpace(legacySettings?.Provider) ? "Ai:Provider" : $"{ProfileRelativePath(role)}/agent.json",
                    inherited: string.IsNullOrWhiteSpace(legacySettings?.Provider)),
                Source(
                    "model",
                    string.IsNullOrWhiteSpace(legacySettings?.Model) ? "DeploymentDefault" : "RoleOverride",
                    string.IsNullOrWhiteSpace(legacySettings?.Model) ? "Ai:Model" : $"{ProfileRelativePath(role)}/agent.json",
                    inherited: string.IsNullOrWhiteSpace(legacySettings?.Model)),
                Source(
                    "timeoutSeconds",
                    legacySettings?.TimeoutSeconds is > 0 ? "RoleOverride" : "DeploymentDefault",
                    legacySettings?.TimeoutSeconds is > 0 ? $"{ProfileRelativePath(role)}/agent.json" : "Ai:TimeoutSeconds",
                    inherited: legacySettings?.TimeoutSeconds is not > 0),
                Source(
                    "effectiveSkill",
                    File.Exists(legacySkillPath) ? "RoleOverride" : "BuiltInDefault",
                    File.Exists(legacySkillPath) ? $"{ProfileRelativePath(role)}/skill.md" : SkeletonAgentBuiltInDefaults.Name,
                    inherited: !File.Exists(legacySkillPath),
                    version: File.Exists(legacySkillPath) ? null : builtIn.Version),
                Source(
                    "effectivePersonality",
                    File.Exists(legacyPersonalityPath) ? "RoleOverride" : "BuiltInDefault",
                    File.Exists(legacyPersonalityPath) ? $"{ProfileRelativePath(role)}/personality.md" : SkeletonAgentBuiltInDefaults.Name,
                    inherited: !File.Exists(legacyPersonalityPath),
                    version: File.Exists(legacyPersonalityPath) ? null : builtIn.Version)
            };

            ApplyScopeSources(sources, tenantSettings, tenantDir, tenantState, "TenantDefault");
            if (projectScope is not null)
                ApplyScopeSources(sources, projectSettings, projectDir, projectState, "ProjectOverride");
            fieldSources = sources;
        }

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
            TenantProfileVersion = tenantState.CurrentVersion > 0 ? tenantState.CurrentVersion.ToString() : null,
            ProjectProfileVersion = projectState.CurrentVersion > 0 ? projectState.CurrentVersion.ToString() : null,
            PublishedVersion = projectState.CurrentVersion > 0 ? projectState.CurrentVersion
                : tenantState.CurrentVersion > 0 ? tenantState.CurrentVersion
                : legacyState.CurrentVersion > 0 ? legacyState.CurrentVersion : null,
            PublishedScopeLayer = projectState.CurrentVersion > 0 ? "Project"
                : tenantState.CurrentVersion > 0 ? "Tenant"
                : legacyState.CurrentVersion > 0 ? "LegacyRole" : string.Empty,
            EffectiveHash = EffectiveHash(role, provider, model, timeout, skill, personality, fieldSources, builtIn.Version),
            Boundary = SkeletonAgentRoles.CodeOwnedBoundary(role)
        };
    }

    private static void ApplyScopeSources(
        List<SkeletonAgentProfileFieldSource> sources,
        AgentSettingsFile? settings,
        string directory,
        AgentProfileState state,
        string layer)
    {
        var version = state.CurrentVersion > 0 ? state.CurrentVersion.ToString() : null;
        ReplaceSource(sources, "provider", !string.IsNullOrWhiteSpace(settings?.Provider), layer, Path.Combine(directory, "agent.json"), version);
        ReplaceSource(sources, "model", !string.IsNullOrWhiteSpace(settings?.Model), layer, Path.Combine(directory, "agent.json"), version);
        ReplaceSource(sources, "timeoutSeconds", settings?.TimeoutSeconds is > 0, layer, Path.Combine(directory, "agent.json"), version);
        ReplaceSource(sources, "effectiveSkill", File.Exists(Path.Combine(directory, "skill.md")), layer, Path.Combine(directory, "skill.md"), version);
        ReplaceSource(sources, "effectivePersonality", File.Exists(Path.Combine(directory, "personality.md")), layer, Path.Combine(directory, "personality.md"), version);
    }

    private static void ReplaceSource(
        List<SkeletonAgentProfileFieldSource> sources,
        string field,
        bool overridden,
        string layer,
        string sourceLabel,
        string? version)
    {
        if (!overridden)
            return;
        var index = sources.FindIndex(source => source.Field == field);
        sources[index] = Source(field, layer, sourceLabel, inherited: false, version: version);
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
