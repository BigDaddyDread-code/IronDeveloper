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

/// <summary>
/// Stable machine-readable identifiers for provisioning checks. Labels may change;
/// clients select behavior from these codes and render unknown future codes honestly.
/// </summary>
public static class ProvisioningCheckCodes
{
    public const string RepositoryAccess = "RepositoryAccess";
    public const string RootSafety = "RootSafety";
    public const string GitRepository = "GitRepository";
    public const string WorkTree = "WorkTree";
    public const string BuildCommand = "BuildCommand";
    public const string TestCommand = "TestCommand";
    public const string ProjectProfile = "ProjectProfile";
    public const string CodeIndex = "CodeIndex";
    public const string BuilderApplyPermission = "BuilderApplyPermission";
    public const string DetectionWarning = "DetectionWarning";
    public const string WorkspaceRoot = "WorkspaceRoot";
    public const string EvidenceRoot = "EvidenceRoot";
    public const string Unknown = "Unknown";
}

public static class ProvisioningActionKinds
{
    public const string None = "None";
    public const string ChangeRepository = "ChangeRepository";
    public const string ConfirmBuildCommand = "ConfirmBuildCommand";
    public const string ConfirmTestCommand = "ConfirmTestCommand";
    public const string ConfirmProjectProfile = "ConfirmProjectProfile";
    public const string RecheckSetup = "RecheckSetup";
    public const string ResolveAdditionalSetup = "ResolveAdditionalSetup";
    public const string IndexProject = "IndexProject";
    public const string EnableBuilderApply = "EnableBuilderApply";
    public const string DisableBuilderApply = "DisableBuilderApply";
    public const string OpenBoard = "OpenBoard";

    public static string ForCheck(string code, bool blocking) => blocking
        ? code switch
        {
            ProvisioningCheckCodes.RepositoryAccess or ProvisioningCheckCodes.RootSafety => ChangeRepository,
            ProvisioningCheckCodes.BuildCommand => ConfirmBuildCommand,
            ProvisioningCheckCodes.TestCommand => ConfirmTestCommand,
            ProvisioningCheckCodes.ProjectProfile => ConfirmProjectProfile,
            ProvisioningCheckCodes.WorkTree => RecheckSetup,
            ProvisioningCheckCodes.CodeIndex => IndexProject,
            ProvisioningCheckCodes.BuilderApplyPermission => EnableBuilderApply,
            _ => ResolveAdditionalSetup
        }
        : None;
}

public static class ProjectSetupCapabilities
{
    public const string ManageProjectSafety = "project.setup.safety.manage";
}

public static class ProjectProvisioningActionStatuses
{
    public const string Succeeded = "Succeeded";
    public const string ProjectNotFound = "ProjectNotFound";
    public const string Forbidden = "Forbidden";
    public const string MissingRepositoryPath = "MissingRepositoryPath";
    public const string UnsafeRepositoryPath = "UnsafeRepositoryPath";
    public const string MissingProjectProfile = "MissingProjectProfile";
    public const string IndexFailed = "IndexFailed";
}

public static class ProjectProvisioningActionReasonCodes
{
    public const string ProjectNotFound = "project_setup_project_not_found";
    public const string CapabilityRequired = "project_setup_safety_capability_required";
    public const string RepositoryPathMissing = "project_setup_repository_path_missing";
    public const string RepositoryPathUnsafe = "project_setup_repository_path_unsafe";
    public const string ProjectProfileMissing = "project_setup_profile_missing";
    public const string CodeIndexFailed = "project_setup_code_index_failed";
}

public sealed record ProjectProvisioningActionResult
{
    public bool Allowed { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ReasonCode { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Capability { get; init; } = ProjectSetupCapabilities.ManageProjectSafety;
    public bool Changed { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public CodeIndexResult? IndexResult { get; init; }
    public ProjectProfile? Profile { get; init; }
    public ProjectProvisioningReadiness? Readiness { get; init; }
}

public interface IProjectProvisioningActionService
{
    Task<ProjectProvisioningActionResult> IndexProjectAsync(
        int projectId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<ProjectProvisioningActionResult> SetBuilderWorkspacePermissionAsync(
        int projectId,
        int actorUserId,
        bool enabled,
        CancellationToken cancellationToken = default);
}

/// <summary>The spec's readiness blocker vocabulary (future-ux-product-spec §8.3).</summary>
public static class ProvisioningBlockedStates
{
    public const string MissingRepoPath = "BlockedMissingRepoPath";
    public const string UnsafeRepoPath = "BlockedUnsafeRepoPath";
    public const string MissingBuildCommand = "BlockedMissingBuildCommand";
    public const string MissingTestCommand = "BlockedMissingTestCommand";
    public const string UnknownArchitecture = "BlockedUnknownArchitecture";
    public const string DirtyRepo = "BlockedDirtyRepo";

    // DOGFOOD-2 finding F-E: provisioning said ReadyToRun while the Builder's own
    // readiness still refused for these two — two disjoint readiness truths. One
    // truth: provisioning readiness includes every requirement the run start
    // enforces, so isReady=true means the governed loop may actually be attempted.
    public const string ProjectNotIndexed = "BlockedProjectNotIndexed";
    public const string BuilderApplyDisabled = "BlockedBuilderApplyDisabled";
}

/// <summary>DOGFOOD-2 entry criterion: what the git working tree looked like when readiness was evaluated.</summary>
public static class ProvisioningWorkTreeStates
{
    public const string Clean = "Clean";
    public const string Dirty = "Dirty";
    public const string NotAGitRepository = "NotAGitRepository";

    /// <summary>git could not answer — the state is named, never guessed.</summary>
    public const string Unknown = "Unknown";
}

/// <summary>One provisioning check: what was looked at, what state it is in, and the named remedy.</summary>
public sealed record ProvisioningCheck
{
    /// <summary>Stable machine-readable code. Display labels must never be used for behavior.</summary>
    public string Code { get; init; } = ProvisioningCheckCodes.Unknown;

    public string Name { get; init; } = string.Empty;

    public string Label => Name;

    /// <summary>One of ProvisioningCheckStates.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>What the check saw — honest, specific, in backend words.</summary>
    public string Evidence { get; init; } = string.Empty;

    public string Summary => Evidence;

    /// <summary>The named remedy. A blocked check without a remedy is a dead end.</summary>
    public string Remedy { get; init; } = string.Empty;

    public bool Blocking { get; init; }

    /// <summary>The detected candidate value for the wizard to prefill — a proposal, never a confirmation.</summary>
    public string DetectedValue { get; init; } = string.Empty;

    public string ActionKind => ProvisioningActionKinds.ForCheck(Code, Blocking);
}

public sealed record ProvisioningNextAction
{
    public string Kind { get; init; } = ProvisioningActionKinds.ResolveAdditionalSetup;
    public string? CheckCode { get; init; }
    public bool Allowed { get; init; }
    public string? ReasonCode { get; init; }
    public string Label { get; init; } = string.Empty;
    public string NextSafeAction { get; init; } = string.Empty;
}

public sealed record ProjectProvisioningReadiness
{
    public const string BoundaryText =
        "Readiness is computed from stored truth and scan evidence. Detection proposes; only a human " +
        "confirms. Ready means the governed loop may be attempted — it approves nothing.";

    public int ProjectId { get; init; }
    public bool IsReady { get; init; }
    public int BlockedCount { get; init; }
    public IReadOnlyList<string> BlockedStates { get; init; } = [];
    public IReadOnlyList<ProvisioningCheck> Checks { get; init; } = [];
    public ProvisioningNextAction NextAction { get; init; } = new();

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

    /// <summary>One of ProvisioningWorkTreeStates. Gathered by the service via git status --porcelain.</summary>
    public string WorkTreeState { get; init; } = ProvisioningWorkTreeStates.Unknown;

    /// <summary>Bounded evidence for the work-tree state — changed-path count/examples, or the git error.</summary>
    public string WorkTreeDetail { get; init; } = string.Empty;

    public ProjectProfile? StoredProfile { get; init; }
    public ProjectCommand? StoredBuildCommand { get; init; }
    public ProjectCommand? StoredTestCommand { get; init; }
    public string DetectedBuildCommand { get; init; } = string.Empty;
    public string DetectedTestCommand { get; init; } = string.Empty;
    public ProjectProfile? DetectedProfile { get; init; }
    public IReadOnlyList<string> DetectionFacts { get; init; } = [];
    public IReadOnlyList<string> DetectionWarnings { get; init; } = [];

    // F-E: the Builder-readiness requirements the run start enforces, gathered
    // from the same stored truth BuilderReadinessService reads.
    public bool HasCodeIndex { get; init; }
    public string? IndexingStatus { get; init; }
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
                Code = ProvisioningCheckCodes.RepositoryAccess,
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
                Code = ProvisioningCheckCodes.RepositoryAccess,
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
                Code = ProvisioningCheckCodes.RootSafety,
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
                Code = ProvisioningCheckCodes.RepositoryAccess,
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
                Code = ProvisioningCheckCodes.GitRepository,
                Name = "Git repository",
                State = input.IsGitRepository ? ProvisioningCheckStates.Confirmed : ProvisioningCheckStates.NeedsConfirmation,
                Evidence = input.IsGitRepository
                    ? "A .git directory is present."
                    : "No .git directory found — the dirty-repo policy cannot apply, and apply-time drift detection loses its baseline.",
                Remedy = input.IsGitRepository ? string.Empty : "Initialize or clone a git repository at the configured path.",
                Blocking = false
            });

            // DOGFOOD-2 entry criterion: a governed run must start from an unambiguous
            // source tree. Dirty blocks with the reason named; an unanswerable git is
            // honestly Unknown/NotEvaluated — named, never guessed, never blocking.
            if (input.IsGitRepository)
            {
                switch (input.WorkTreeState)
                {
                    case ProvisioningWorkTreeStates.Clean:
                        checks.Add(new ProvisioningCheck
                        {
                            Code = ProvisioningCheckCodes.WorkTree,
                            Name = "Dirty-repo state",
                            State = ProvisioningCheckStates.Confirmed,
                            Evidence = "Working tree is clean (git status --porcelain reported no changes).",
                            Remedy = string.Empty,
                            Blocking = false
                        });
                        break;

                    case ProvisioningWorkTreeStates.Dirty:
                        checks.Add(new ProvisioningCheck
                        {
                            Code = ProvisioningCheckCodes.WorkTree,
                            Name = "Dirty-repo state",
                            State = ProvisioningCheckStates.NeedsConfirmation,
                            Evidence = $"Working tree has uncommitted changes: {input.WorkTreeDetail}",
                            Remedy = "Commit or stash local changes before governed work — a run must start from an unambiguous source tree. A dirty-repo allow policy is a future project-safety setting; until it exists, dirty blocks.",
                            Blocking = true
                        });
                        blocked.Add(ProvisioningBlockedStates.DirtyRepo);
                        break;

                    default:
                        checks.Add(new ProvisioningCheck
                        {
                            Code = ProvisioningCheckCodes.WorkTree,
                            Name = "Dirty-repo state",
                            State = ProvisioningCheckStates.NotEvaluated,
                            Evidence = string.IsNullOrWhiteSpace(input.WorkTreeDetail)
                                ? "git could not report the working-tree state."
                                : $"git could not report the working-tree state: {input.WorkTreeDetail}",
                            Remedy = "Verify git is installed and the path is a working tree, then re-evaluate.",
                            Blocking = false
                        });
                        break;
                }
            }
        }

        // 3. Build command — stored default confirms; a detected candidate only proposes.
        AddCommandCheck(
            checks, blocked,
            code: ProvisioningCheckCodes.BuildCommand,
            name: "Build command",
            stored: input.StoredBuildCommand,
            detected: input.DetectedBuildCommand,
            blockedState: ProvisioningBlockedStates.MissingBuildCommand,
            confirmHint: "POST /api/projects/{projectId}/profile/commands with CommandType=Build");

        // 4. Test command.
        AddCommandCheck(
            checks, blocked,
            code: ProvisioningCheckCodes.TestCommand,
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
                Code = ProvisioningCheckCodes.ProjectProfile,
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
                Code = ProvisioningCheckCodes.ProjectProfile,
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
                Code = ProvisioningCheckCodes.ProjectProfile,
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

        // 6. Code index — the Builder's readiness refuses to run without it (F-E:
        // this used to live only behind the run start, so provisioning could say
        // ready while the run refused).
        if (pathUsable)
        {
            if (!input.HasCodeIndex)
            {
                checks.Add(new ProvisioningCheck
                {
                    Code = ProvisioningCheckCodes.CodeIndex,
                    Name = "Code index",
                    State = ProvisioningCheckStates.Missing,
                    Evidence = "The project has never been indexed — the Builder's readiness gate will refuse to start a run.",
                    Remedy = "Use Index project to index the configured source tree.",
                    Blocking = true
                });
                blocked.Add(ProvisioningBlockedStates.ProjectNotIndexed);
            }
            else if (!string.Equals(input.IndexingStatus, "Ready", StringComparison.OrdinalIgnoreCase))
            {
                checks.Add(new ProvisioningCheck
                {
                    Code = ProvisioningCheckCodes.CodeIndex,
                    Name = "Code index",
                    State = ProvisioningCheckStates.NeedsConfirmation,
                    Evidence = $"The code index is not ready: {(string.IsNullOrWhiteSpace(input.IndexingStatus) ? "no status recorded" : input.IndexingStatus)}.",
                    Remedy = "Use Index project to safely re-index the configured source tree.",
                    Blocking = true
                });
                blocked.Add(ProvisioningBlockedStates.ProjectNotIndexed);
            }
            else
            {
                checks.Add(new ProvisioningCheck
                {
                    Code = ProvisioningCheckCodes.CodeIndex,
                    Name = "Code index",
                    State = ProvisioningCheckStates.Confirmed,
                    Evidence = "The project is indexed and the index reports Ready.",
                    Remedy = string.Empty,
                    Blocking = false
                });
            }
        }

        // 7. Builder apply permission — off by default, and the run start refuses
        // while it is off (F-E). Confirming it is a deliberate human act; readiness
        // names it instead of letting the run's refusal be the first mention.
        if (storedProfileMeaningful)
        {
            if (input.StoredProfile!.AllowBuilderApply)
            {
                checks.Add(new ProvisioningCheck
                {
                    Code = ProvisioningCheckCodes.BuilderApplyPermission,
                    Name = "Builder apply permission",
                    State = ProvisioningCheckStates.Confirmed,
                    Evidence = "AllowBuilderApply is enabled on the stored profile. It permits governed workspace writes only — copy-only apply stays behind the full gate chain.",
                    Remedy = string.Empty,
                    Blocking = false
                });
            }
            else
            {
                checks.Add(new ProvisioningCheck
                {
                    Code = ProvisioningCheckCodes.BuilderApplyPermission,
                    Name = "Builder apply permission",
                    State = ProvisioningCheckStates.NeedsConfirmation,
                    Evidence = "AllowBuilderApply is false on the stored profile — the Builder's readiness gate will refuse to start a run.",
                    Remedy = "Deliberately enable governed Builder workspace writes. This does not approve or apply changes to the source repository.",
                    Blocking = true
                });
                blocked.Add(ProvisioningBlockedStates.BuilderApplyDisabled);
            }
        }

        // 8. Detection facts and warnings ride along as evidence, never as judgment.
        foreach (var warning in input.DetectionWarnings)
        {
            checks.Add(new ProvisioningCheck
            {
                Code = ProvisioningCheckCodes.DetectionWarning,
                Name = "Detection warning",
                State = ProvisioningCheckStates.NeedsConfirmation,
                Evidence = warning,
                Remedy = "Review during profile confirmation.",
                Blocking = false
            });
        }

        var isReady = blocked.Count == 0;
        return new ProjectProvisioningReadiness
        {
            ProjectId = input.ProjectId,
            IsReady = isReady,
            BlockedCount = checks.Count(check => check.Blocking),
            BlockedStates = blocked,
            Checks = checks,
            ProposedProfile = proposedProfile,
            NextAction = CreateNextAction(isReady, checks)
        };
    }

    private static void AddCommandCheck(
        List<ProvisioningCheck> checks,
        List<string> blocked,
        string code,
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
                Code = code,
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
                Code = code,
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
            Code = code,
            Name = name,
            State = ProvisioningCheckStates.Missing,
            Evidence = "No stored default and detection found no candidate.",
            Remedy = $"Supply it: {confirmHint}.",
            Blocking = true
        });
        blocked.Add(blockedState);
    }

    private static ProvisioningNextAction CreateNextAction(
        bool isReady,
        IReadOnlyList<ProvisioningCheck> checks)
    {
        if (isReady)
        {
            return new ProvisioningNextAction
            {
                Kind = ProvisioningActionKinds.OpenBoard,
                Allowed = true,
                Label = "Open Board",
                NextSafeAction = "Open the project Board."
            };
        }

        var priority = new[]
        {
            ProvisioningCheckCodes.RepositoryAccess,
            ProvisioningCheckCodes.RootSafety,
            ProvisioningCheckCodes.BuildCommand,
            ProvisioningCheckCodes.TestCommand,
            ProvisioningCheckCodes.ProjectProfile,
            ProvisioningCheckCodes.WorkTree,
            ProvisioningCheckCodes.CodeIndex,
            ProvisioningCheckCodes.BuilderApplyPermission
        };
        var next = priority
            .Select(code => checks.FirstOrDefault(check => check.Blocking && check.Code == code))
            .FirstOrDefault(check => check is not null)
            ?? checks.First(check => check.Blocking);

        return new ProvisioningNextAction
        {
            Kind = next.ActionKind,
            CheckCode = next.Code,
            Allowed = true,
            ReasonCode = ReasonCodeFor(next.Code),
            Label = ActionLabel(next.ActionKind),
            NextSafeAction = next.Remedy
        };
    }

    private static string ActionLabel(string actionKind) => actionKind switch
    {
        ProvisioningActionKinds.ChangeRepository => "Change repository",
        ProvisioningActionKinds.ConfirmBuildCommand => "Confirm build command",
        ProvisioningActionKinds.ConfirmTestCommand => "Confirm test command",
        ProvisioningActionKinds.ConfirmProjectProfile => "Confirm project structure",
        ProvisioningActionKinds.RecheckSetup => "Re-check setup",
        ProvisioningActionKinds.IndexProject => "Index project",
        ProvisioningActionKinds.EnableBuilderApply => "Enable governed Builder writes",
        ProvisioningActionKinds.DisableBuilderApply => "Disable governed Builder writes",
        _ => "Complete required setup"
    };

    private static string? ReasonCodeFor(string checkCode) => checkCode switch
    {
        ProvisioningCheckCodes.RepositoryAccess => ProvisioningBlockedStates.MissingRepoPath,
        ProvisioningCheckCodes.RootSafety => ProvisioningBlockedStates.UnsafeRepoPath,
        ProvisioningCheckCodes.BuildCommand => ProvisioningBlockedStates.MissingBuildCommand,
        ProvisioningCheckCodes.TestCommand => ProvisioningBlockedStates.MissingTestCommand,
        ProvisioningCheckCodes.ProjectProfile => ProvisioningBlockedStates.UnknownArchitecture,
        ProvisioningCheckCodes.WorkTree => ProvisioningBlockedStates.DirtyRepo,
        ProvisioningCheckCodes.CodeIndex => ProvisioningBlockedStates.ProjectNotIndexed,
        ProvisioningCheckCodes.BuilderApplyPermission => ProvisioningBlockedStates.BuilderApplyDisabled,
        _ => null
    };

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
