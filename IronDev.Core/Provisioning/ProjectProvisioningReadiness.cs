using IronDev.Data.Models;

namespace IronDev.Core.Provisioning;

/// <summary>
/// PROJECT-0: the provisioning readiness contract. A folder path is not a project —
/// a project is a repo with build, test, safety, and readiness evidence. Readiness is
/// COMPUTED from stored truth (project, profile, commands) plus scan evidence; it is
/// never asserted by a client, and a detected-but-unconfirmed value still blocks:
/// detection proposes, only a human confirms.
/// </summary>
public static class ProvisioningCheckStates
{
    public const string Confirmed = "Confirmed";
    public const string Detected = "Detected";
    public const string NeedsConfirmation = "NeedsConfirmation";
    public const string Missing = "Missing";
    public const string Unsafe = "Unsafe";
    public const string NotEvaluated = "NotEvaluated";
}

/// <summary>The spec's readiness blocker vocabulary (future-ux-product-spec §8.3).</summary>
public static class ProvisioningBlockedStates
{
    public const string MissingRepoPath = "BlockedMissingRepoPath";
    public const string UnsafeRepoPath = "BlockedUnsafeRepoPath";
    public const string MissingBuildCommand = "BlockedMissingBuildCommand";
    public const string MissingTestCommand = "BlockedMissingTestCommand";
    public const string UnknownArchitecture = "BlockedUnknownArchitecture";
}

/// <summary>One provisioning check: what was looked at, what state it is in, and the named remedy.</summary>
public sealed record ProvisioningCheck
{
    public string Name { get; init; } = string.Empty;

    /// <summary>One of ProvisioningCheckStates.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>What the check saw — honest, specific, in backend words.</summary>
    public string Evidence { get; init; } = string.Empty;

    /// <summary>The named remedy. A blocked check without a remedy is a dead end.</summary>
    public string Remedy { get; init; } = string.Empty;

    public bool Blocking { get; init; }

    /// <summary>The detected candidate value for the wizard to prefill — a proposal, never a confirmation.</summary>
    public string DetectedValue { get; init; } = string.Empty;
}

public sealed record ProjectProvisioningReadiness
{
    public const string BoundaryText =
        "Readiness is computed from stored truth and scan evidence. Detection proposes; only a human " +
        "confirms. Ready means the governed loop may be attempted — it approves nothing.";

    public int ProjectId { get; init; }
    public bool IsReady { get; init; }
    public IReadOnlyList<string> BlockedStates { get; init; } = [];
    public IReadOnlyList<ProvisioningCheck> Checks { get; init; } = [];

    /// <summary>The detected architecture profile awaiting human confirmation, when one exists and none is stored.</summary>
    public ProjectProfile? ProposedProfile { get; init; }

    public string Boundary { get; init; } = BoundaryText;
}

/// <summary>Everything the evaluator needs, gathered by the service. Pure input, no I/O here.</summary>
public sealed record ProvisioningEvaluationInput
{
    public int ProjectId { get; init; }
    public string? RepoPath { get; init; }
    public bool RepoPathExists { get; init; }
    public bool RepoPathIsSafe { get; init; }
    public string RepoPathSafetyDetail { get; init; } = string.Empty;
    public bool IsGitRepository { get; init; }
    public ProjectProfile? StoredProfile { get; init; }
    public ProjectCommand? StoredBuildCommand { get; init; }
    public ProjectCommand? StoredTestCommand { get; init; }
    public string DetectedBuildCommand { get; init; } = string.Empty;
    public string DetectedTestCommand { get; init; } = string.Empty;
    public ProjectProfile? DetectedProfile { get; init; }
    public IReadOnlyList<string> DetectionFacts { get; init; } = [];
    public IReadOnlyList<string> DetectionWarnings { get; init; } = [];
}

/// <summary>
/// PROJECT-3: the pure readiness evaluator. Deterministic over its input; the service
/// owns gathering, this owns judging. Every blocking check names its remedy.
/// </summary>
public static class ProvisioningReadinessEvaluator
{
    public static ProjectProvisioningReadiness Evaluate(ProvisioningEvaluationInput input)
    {
        var checks = new List<ProvisioningCheck>();
        var blocked = new List<string>();

        // 1. Repo path — everything else depends on it.
        if (string.IsNullOrWhiteSpace(input.RepoPath))
        {
            checks.Add(new ProvisioningCheck
            {
                Name = "Repo path",
                State = ProvisioningCheckStates.Missing,
                Evidence = "No local path is set on the project.",
                Remedy = "Set the repository path: PUT /api/projects/{projectId}/local-path.",
                Blocking = true
            });
            blocked.Add(ProvisioningBlockedStates.MissingRepoPath);
        }
        else if (!input.RepoPathExists)
        {
            checks.Add(new ProvisioningCheck
            {
                Name = "Repo path",
                State = ProvisioningCheckStates.Missing,
                Evidence = $"The configured path does not exist on this machine: {input.RepoPath}",
                Remedy = "Fix the path via PUT /api/projects/{projectId}/local-path, or clone the repository to that location.",
                Blocking = true
            });
            blocked.Add(ProvisioningBlockedStates.MissingRepoPath);
        }
        else if (!input.RepoPathIsSafe)
        {
            checks.Add(new ProvisioningCheck
            {
                Name = "Repo path safety",
                State = ProvisioningCheckStates.Unsafe,
                Evidence = input.RepoPathSafetyDetail,
                Remedy = "Point the project at a dedicated repository folder, never a drive root, user-profile root, or system directory.",
                Blocking = true
            });
            blocked.Add(ProvisioningBlockedStates.UnsafeRepoPath);
        }
        else
        {
            checks.Add(new ProvisioningCheck
            {
                Name = "Repo path",
                State = ProvisioningCheckStates.Confirmed,
                Evidence = $"{input.RepoPath} exists and passed the root-safety check.",
                Remedy = string.Empty,
                Blocking = false
            });
        }

        var pathUsable = string.IsNullOrWhiteSpace(input.RepoPath) == false && input.RepoPathExists && input.RepoPathIsSafe;

        // 2. Git repository — a fact, not yet a blocker; the dirty-repo policy needs it.
        if (pathUsable)
        {
            checks.Add(new ProvisioningCheck
            {
                Name = "Git repository",
                State = input.IsGitRepository ? ProvisioningCheckStates.Confirmed : ProvisioningCheckStates.NeedsConfirmation,
                Evidence = input.IsGitRepository
                    ? "A .git directory is present."
                    : "No .git directory found — the dirty-repo policy cannot apply, and apply-time drift detection loses its baseline.",
                Remedy = input.IsGitRepository ? string.Empty : "Initialize or clone a git repository at the configured path.",
                Blocking = false
            });

            checks.Add(new ProvisioningCheck
            {
                Name = "Dirty-repo state",
                State = ProvisioningCheckStates.NotEvaluated,
                Evidence = "Working-tree cleanliness is not evaluated by this readiness check yet.",
                Remedy = "Arrives with wizard hardening; until then the run's own workspace isolation is the protection.",
                Blocking = false
            });
        }

        // 3. Build command — stored default confirms; a detected candidate only proposes.
        AddCommandCheck(
            checks, blocked,
            name: "Build command",
            stored: input.StoredBuildCommand,
            detected: input.DetectedBuildCommand,
            blockedState: ProvisioningBlockedStates.MissingBuildCommand,
            confirmHint: "POST /api/projects/{projectId}/profile/commands with CommandType=Build");

        // 4. Test command.
        AddCommandCheck(
            checks, blocked,
            name: "Test command",
            stored: input.StoredTestCommand,
            detected: input.DetectedTestCommand,
            blockedState: ProvisioningBlockedStates.MissingTestCommand,
            confirmHint: "POST /api/projects/{projectId}/profile/commands with CommandType=Test");

        // 5. Architecture profile — stored confirms; detection proposes.
        ProjectProfile? proposedProfile = null;
        var storedProfileMeaningful =
            input.StoredProfile is not null &&
            (!string.IsNullOrWhiteSpace(input.StoredProfile.PrimaryLanguage) ||
             !string.IsNullOrWhiteSpace(input.StoredProfile.ApplicationType) ||
             !string.IsNullOrWhiteSpace(input.StoredProfile.SolutionFile));

        if (storedProfileMeaningful)
        {
            checks.Add(new ProvisioningCheck
            {
                Name = "Architecture profile",
                State = ProvisioningCheckStates.Confirmed,
                Evidence =
                    $"Stored profile: {Describe(input.StoredProfile!)}",
                Remedy = string.Empty,
                Blocking = false
            });
        }
        else if (input.DetectedProfile is not null)
        {
            proposedProfile = input.DetectedProfile;
            checks.Add(new ProvisioningCheck
            {
                Name = "Architecture profile",
                State = ProvisioningCheckStates.NeedsConfirmation,
                Evidence = $"Detection proposes: {Describe(input.DetectedProfile)}. Unknowns remain until a human confirms.",
                Remedy = "Confirm or edit the proposed profile: POST /api/projects/{projectId}/profile.",
                Blocking = true,
                DetectedValue = Describe(input.DetectedProfile)
            });
            blocked.Add(ProvisioningBlockedStates.UnknownArchitecture);
        }
        else
        {
            checks.Add(new ProvisioningCheck
            {
                Name = "Architecture profile",
                State = ProvisioningCheckStates.Missing,
                Evidence = pathUsable
                    ? "No stored profile and detection produced no proposal."
                    : "No stored profile; detection could not run without a usable repo path.",
                Remedy = "Fix the repo path first if blocked, then confirm a profile: POST /api/projects/{projectId}/profile.",
                Blocking = true
            });
            blocked.Add(ProvisioningBlockedStates.UnknownArchitecture);
        }

        // 6. Detection facts and warnings ride along as evidence, never as judgment.
        foreach (var warning in input.DetectionWarnings)
        {
            checks.Add(new ProvisioningCheck
            {
                Name = "Detection warning",
                State = ProvisioningCheckStates.NeedsConfirmation,
                Evidence = warning,
                Remedy = "Review during profile confirmation.",
                Blocking = false
            });
        }

        return new ProjectProvisioningReadiness
        {
            ProjectId = input.ProjectId,
            IsReady = blocked.Count == 0,
            BlockedStates = blocked,
            Checks = checks,
            ProposedProfile = proposedProfile
        };
    }

    private static void AddCommandCheck(
        List<ProvisioningCheck> checks,
        List<string> blocked,
        string name,
        ProjectCommand? stored,
        string detected,
        string blockedState,
        string confirmHint)
    {
        if (stored is not null && !string.IsNullOrWhiteSpace(stored.CommandText))
        {
            checks.Add(new ProvisioningCheck
            {
                Name = name,
                State = ProvisioningCheckStates.Confirmed,
                Evidence = $"Stored default: {stored.CommandText}",
                Remedy = string.Empty,
                Blocking = false
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(detected))
        {
            checks.Add(new ProvisioningCheck
            {
                Name = name,
                State = ProvisioningCheckStates.NeedsConfirmation,
                Evidence = $"Detected candidate: {detected}. A detected command is a proposal — it runs nothing until confirmed.",
                Remedy = $"Confirm or edit it: {confirmHint}.",
                Blocking = true,
                DetectedValue = detected
            });
            blocked.Add(blockedState);
            return;
        }

        checks.Add(new ProvisioningCheck
        {
            Name = name,
            State = ProvisioningCheckStates.Missing,
            Evidence = "No stored default and detection found no candidate.",
            Remedy = $"Supply it: {confirmHint}.",
            Blocking = true
        });
        blocked.Add(blockedState);
    }

    private static string Describe(ProjectProfile profile)
    {
        var parts = new[]
            {
                profile.ApplicationType,
                profile.PrimaryLanguage,
                profile.Framework,
                profile.SolutionFile is null ? null : $"solution {profile.SolutionFile}"
            }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        return parts.Length > 0 ? string.Join(" · ", parts) : "(no detail)";
    }
}

/// <summary>Evaluates provisioning readiness for a project from stored truth plus scan evidence.</summary>
public interface IProjectProvisioningReadinessService
{
    /// <summary>Returns null when the project does not exist.</summary>
    Task<ProjectProvisioningReadiness?> EvaluateAsync(int projectId, CancellationToken cancellationToken = default);
}
