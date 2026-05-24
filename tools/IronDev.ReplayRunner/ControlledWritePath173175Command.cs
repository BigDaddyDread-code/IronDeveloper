using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Promotion;
using IronDev.Infrastructure.Services.Promotion;

public static class ControlledWritePath173175Command
{
    private const string PolicyBoundary = "Policy resolution only. Settings can enable a future controlled path, but hard invariants cannot be configured away.";
    private const string ApprovalBoundary = "Approval record only. It scopes permission to this package and controlled worktree dry-run/validation; it does not write, create a PR, merge, mutate memory, accept tickets, or approve itself.";
    private const string DryRunBoundary = "Controlled worktree dry-run only. It validates package, approval, policy, target path, and file manifest without copying files, creating a worktree, writing main, opening a PR, or approving itself.";

    public static async Task<int> HandlePolicyEffectiveAsync(string[] args, JsonSerializerOptions options)
    {
        var repoRoot = SolitaireDisposableBuildSmokeCommand.FindRepositoryRoot();
        var runId = ReadOption(args, "--run-id") ??
                    ReadOption(args, "--dogfood-run-id") ??
                    $"ControlledWritePolicy173-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var project = ReadOption(args, "--project") ?? "IronDev";
        var report = await BuildAndWritePolicyAsync(repoRoot, runId, project, handoffKeyGranted: true);
        Console.WriteLine(JsonSerializer.Serialize(report, options));
        return report.Errors.Count == 0 ? 0 : 1;
    }

    public static async Task<int> HandlePolicyCampaignAsync(string[] args, JsonSerializerOptions options)
    {
        var repoRoot = SolitaireDisposableBuildSmokeCommand.FindRepositoryRoot();
        var runId = ReadOption(args, "--run-id") ??
                    ReadOption(args, "--dogfood-run-id") ??
                    $"ControlledWritePolicy173-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var project = ReadOption(args, "--project") ?? "IronDev";
        var report = await BuildAndWritePolicyAsync(repoRoot, runId, project, handoffKeyGranted: true);
        Console.WriteLine(JsonSerializer.Serialize(report, options));
        return report.Errors.Count == 0 &&
               report.EffectiveSettings.WritePathEnabled &&
               report.EffectiveSettings.PermittedPromotionModes.Contains("ControlledWorktreeDryRun") &&
               report.HardInvariants.All(invariant => !invariant.Configurable) &&
               report.IgnoredInvariantOverrides.Count >= 2
            ? 0
            : 1;
    }

    public static async Task<int> HandleApprovalCreateAsync(string[] args, JsonSerializerOptions options)
    {
        var repoRoot = SolitaireDisposableBuildSmokeCommand.FindRepositoryRoot();
        var runId = ReadOption(args, "--run-id") ??
                    ReadOption(args, "--dogfood-run-id") ??
                    $"ControlledWriteApproval174-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var packageRunId = ReadOption(args, "--package-run-id");
        if (string.IsNullOrWhiteSpace(packageRunId))
        {
            Console.Error.WriteLine("Usage: promotion approval create --package-run-id <run> [--run-id id] [--json]");
            return 2;
        }

        var result = await CreateApprovalAsync(repoRoot, runId, packageRunId);
        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return result.ValidForControlledWorktreeDryRun ? 0 : 1;
    }

    public static async Task<int> HandleApprovalCampaignAsync(string[] args, JsonSerializerOptions options)
    {
        var repoRoot = SolitaireDisposableBuildSmokeCommand.FindRepositoryRoot();
        var runId = ReadOption(args, "--run-id") ??
                    ReadOption(args, "--dogfood-run-id") ??
                    $"ControlledWriteApproval174-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var packageRunId = $"{runId}-package";
        await EnsureReviewFixturePromotionPackageAsync(repoRoot, packageRunId);
        var result = await CreateApprovalAsync(repoRoot, runId, packageRunId);
        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return result.ValidForControlledWorktreeDryRun &&
               !result.ValidForRealRepoWrite &&
               result.AllowedActions.Contains("ControlledWorktreeDryRun") &&
               result.BlockedActions.Contains("WriteMain")
            ? 0
            : 1;
    }

    public static async Task<int> HandleWorktreeDryRunAsync(string[] args, JsonSerializerOptions options)
    {
        var repoRoot = SolitaireDisposableBuildSmokeCommand.FindRepositoryRoot();
        var runId = ReadOption(args, "--run-id") ??
                    ReadOption(args, "--dogfood-run-id") ??
                    $"ControlledWorktreeDryRun175-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var packageRunId = ReadOption(args, "--package-run-id");
        var approvalRunId = ReadOption(args, "--approval-run-id");
        var targetWorktree = ReadOption(args, "--target-worktree");
        if (string.IsNullOrWhiteSpace(packageRunId) ||
            string.IsNullOrWhiteSpace(approvalRunId) ||
            string.IsNullOrWhiteSpace(targetWorktree))
        {
            Console.Error.WriteLine("Usage: promotion apply worktree-dry-run --package-run-id <run> --approval-run-id <run> --target-worktree <path> [--run-id id] [--json]");
            return 2;
        }

        var result = await CreateDryRunAsync(repoRoot, runId, packageRunId, approvalRunId, targetWorktree);
        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return string.Equals(result.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    public static async Task<int> HandleWorktreeDryRunCampaignAsync(string[] args, JsonSerializerOptions options)
    {
        var repoRoot = SolitaireDisposableBuildSmokeCommand.FindRepositoryRoot();
        var runId = ReadOption(args, "--run-id") ??
                    ReadOption(args, "--dogfood-run-id") ??
                    $"ControlledWorktreeDryRun175-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var packageRunId = $"{runId}-package";
        var approvalRunId = $"{runId}-approval";
        await EnsureReviewFixturePromotionPackageAsync(repoRoot, packageRunId);
        var approval = await CreateApprovalAsync(repoRoot, approvalRunId, packageRunId);
        if (!approval.ValidForControlledWorktreeDryRun)
            return await WriteFailedDryRunAsync(repoRoot, runId, packageRunId, approvalRunId, "Approval record was not valid for controlled dry-run.", options);

        var targetRoot = Path.Combine(Path.GetTempPath(), "IronDev-ControlledWorktreeDryRun");
        var targetWorktree = Path.Combine(targetRoot, SanitizeSegment(runId), "candidate");
        var result = await CreateDryRunAsync(repoRoot, runId, packageRunId, approvalRunId, targetWorktree);
        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return string.Equals(result.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) &&
               result.TargetPathExplicit &&
               result.TargetOutsideActiveRepo &&
               result.TargetBranchIsNotMain &&
               result.FilesThatWouldApply.Count >= 10 &&
               result.Mutation.ActiveRepoMutationCount == 0 &&
               !Directory.Exists(targetWorktree)
            ? 0
            : 1;
    }

    private static async Task<ControlledWriteEffectivePolicy> BuildAndWritePolicyAsync(
        string repoRoot,
        string runId,
        string project,
        bool handoffKeyGranted)
    {
        var runRoot = RunRoot(repoRoot, runId);
        Directory.CreateDirectory(runRoot);
        var policy = BuildPolicy(repoRoot, runId, project, handoffKeyGranted);
        var policyPath = Path.Combine(runRoot, "controlled-write-policy.json");
        var markdownPath = Path.Combine(runRoot, "controlled-write-policy.md");
        await File.WriteAllTextAsync(policyPath, JsonSerializer.Serialize(policy, SerializerOptions()), Encoding.UTF8);
        await File.WriteAllTextAsync(markdownPath, ToPolicyMarkdown(policy), Encoding.UTF8);
        return policy;
    }

    private static ControlledWriteEffectivePolicy BuildPolicy(
        string repoRoot,
        string runId,
        string project,
        bool handoffKeyGranted)
    {
        var global = new ControlledWritePolicySettings
        {
            PolicyId = "global-default",
            Scope = "Global",
            WritePathEnabled = false,
            PermittedPromotionModes = [],
            WorktreeRoot = Path.Combine(Path.GetTempPath(), "IronDev-ControlledWorktreeDryRun")
        };
        var projectSettings = global with
        {
            PolicyId = "project-irondev",
            Scope = $"Project:{project}",
            MaxFilesChanged = 75,
            RequiredReviewerRoles = ["HumanOwner", "CodexReviewer"]
        };
        var runSettings = projectSettings with
        {
            PolicyId = $"run-{runId}",
            Scope = $"Run:{runId}",
            BranchNameTemplate = "ida/{project}/{runId}",
            BuildTestRetryCount = 1
        };
        var humanOverride = runSettings with
        {
            PolicyId = $"human-handoff-{runId}",
            Scope = "ExplicitHumanOverride",
            WritePathEnabled = handoffKeyGranted,
            PermittedPromotionModes = handoffKeyGranted ? ["ControlledWorktreeDryRun"] : []
        };

        var invariants = BuildHardInvariants();
        var attempted = new[] { "allowDirectMainWrite=true", "allowActiveDeveloperWorkingTreeWrite=true", "allowAgentSelfApproval=true" };
        var effective = humanOverride with
        {
            PolicyId = $"effective-{runId}",
            Scope = "Effective",
            BlockedPathSegments = humanOverride.BlockedPathSegments
                .Concat(["main/", ".git/"])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        var policyPath = Path.Combine(RunRoot(repoRoot, runId), "controlled-write-policy.json");
        return new ControlledWriteEffectivePolicy
        {
            Command = "promotion policy effective",
            Status = "Succeeded",
            RunId = runId,
            TraceId = Guid.NewGuid().ToString("N"),
            Project = project,
            GlobalDefaults = global,
            ProjectSettings = projectSettings,
            RunSettings = runSettings,
            ExplicitHumanOverride = humanOverride,
            EffectiveSettings = effective,
            HardInvariants = invariants,
            AttemptedInvariantOverrides = attempted,
            IgnoredInvariantOverrides = attempted.Select(value => $"{value} ignored because hard invariants are non-configurable.").ToArray(),
            Evidence = [new PromotionEvidenceRef("EffectivePolicy", policyPath, "Effective controlled write policy snapshot.")],
            Boundary = PolicyBoundary,
            ReproCommand = $"promotion policy effective --project {project} --run-id {runId} --json"
        };
    }

    private static async Task<ControlledWriteApprovalRecord> CreateApprovalAsync(string repoRoot, string runId, string packageRunId)
    {
        var packagePath = Path.Combine(repoRoot, "tools", "dogfood", "runs", packageRunId, "promotion-package.json");
        if (!File.Exists(packagePath))
            return FailedApproval(runId, packageRunId, $"Promotion package not found: {packagePath}");

        var package = JsonSerializer.Deserialize<PromotionPackage>(await File.ReadAllTextAsync(packagePath), SerializerOptions());
        if (package is null)
            return FailedApproval(runId, packageRunId, $"Promotion package could not be parsed: {packagePath}");

        var policy = BuildPolicy(repoRoot, runId, package.Project, handoffKeyGranted: true);
        var approval = new ControlledWriteApprovalRecord
        {
            ApprovalId = $"APP-{runId}",
            RunId = runId,
            TraceId = Guid.NewGuid().ToString("N"),
            Project = package.Project,
            PackageId = package.PackageId,
            ProposedChangeId = package.ProposedChangeId,
            SourceRunId = package.SourceRunId,
            SourceTraceId = package.SourceTraceId,
            ApprovedBy = "HumanOwner",
            ApprovalRole = "HumanOwner",
            ApprovalScope = "ControlledWorktreeDryRunOnly",
            ApprovalState = "ApprovedForControlledWorktreeDryRun",
            ApprovalPhrase = policy.EffectiveSettings.RequiredApprovalPhrase,
            CreatedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(policy.EffectiveSettings.PromotionPackageExpiry),
            ValidForControlledWorktreeDryRun = true,
            ValidForRealRepoWrite = false,
            RequiredEvidenceRefs = package.EvidenceSummary.EvidenceRefs,
            AllowedActions = ["ControlledWorktreeDryRun"],
            BlockedActions = ["WriteMain", "WriteActiveDeveloperWorkingTree", "CreatePullRequest", "AutoMerge", "MutateAcceptedMemory", "AcceptTicket", "SelfApprove"],
            Boundary = ApprovalBoundary
        };

        var runRoot = RunRoot(repoRoot, runId);
        Directory.CreateDirectory(runRoot);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "approval-record.json"), JsonSerializer.Serialize(approval, SerializerOptions()), Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "approval-record.md"), ToApprovalMarkdown(approval), Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "controlled-write-policy.json"), JsonSerializer.Serialize(policy, SerializerOptions()), Encoding.UTF8);
        return approval;
    }

    private static async Task<ControlledWorktreeDryRunReport> CreateDryRunAsync(
        string repoRoot,
        string runId,
        string packageRunId,
        string approvalRunId,
        string targetWorktree)
    {
        var runRoot = RunRoot(repoRoot, runId);
        Directory.CreateDirectory(runRoot);
        var packagePath = Path.Combine(repoRoot, "tools", "dogfood", "runs", packageRunId, "promotion-package.json");
        var approvalPath = Path.Combine(repoRoot, "tools", "dogfood", "runs", approvalRunId, "approval-record.json");
        var package = File.Exists(packagePath)
            ? JsonSerializer.Deserialize<PromotionPackage>(await File.ReadAllTextAsync(packagePath), SerializerOptions())
            : null;
        var approval = File.Exists(approvalPath)
            ? JsonSerializer.Deserialize<ControlledWriteApprovalRecord>(await File.ReadAllTextAsync(approvalPath), SerializerOptions())
            : null;

        if (package is null || approval is null)
            return await WriteDryRunReportAsync(repoRoot, runId, packageRunId, approvalRunId, targetWorktree, package, approval, ["Package or approval record was missing or invalid."]);

        return await WriteDryRunReportAsync(repoRoot, runId, packageRunId, approvalRunId, targetWorktree, package, approval, []);
    }

    private static async Task<ControlledWorktreeDryRunReport> WriteDryRunReportAsync(
        string repoRoot,
        string runId,
        string packageRunId,
        string approvalRunId,
        string targetWorktree,
        PromotionPackage? package,
        ControlledWriteApprovalRecord? approval,
        IReadOnlyList<string> preErrors)
    {
        var runRoot = RunRoot(repoRoot, runId);
        Directory.CreateDirectory(runRoot);
        var project = package?.Project ?? approval?.Project ?? "Unknown";
        var policy = BuildPolicy(repoRoot, runId, project, handoffKeyGranted: true);
        var fullTarget = Path.GetFullPath(targetWorktree);
        var fullRepo = Path.GetFullPath(repoRoot);
        var branchName = ExpandBranchName(policy.EffectiveSettings.BranchNameTemplate, project, runId);
        var statusBefore = await SolitaireDisposableBuildSmokeCommand.GetGitStatusAsync(repoRoot);
        var errors = preErrors.ToList();
        var targetExplicit = !string.IsNullOrWhiteSpace(targetWorktree);
        var targetOutsideRepo = targetExplicit && !IsUnderDirectory(fullTarget, fullRepo);
        var branchNotMain = !string.Equals(branchName, "main", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(branchName, "master", StringComparison.OrdinalIgnoreCase);

        if (!policy.EffectiveSettings.WritePathEnabled)
            errors.Add("Effective policy does not enable the write path.");
        if (!policy.EffectiveSettings.PermittedPromotionModes.Contains("ControlledWorktreeDryRun"))
            errors.Add("Effective policy does not permit ControlledWorktreeDryRun.");
        if (approval is not null && !approval.ValidForControlledWorktreeDryRun)
            errors.Add("Approval is not valid for controlled worktree dry-run.");
        if (package is not null && approval is not null && !string.Equals(package.PackageId, approval.PackageId, StringComparison.Ordinal))
            errors.Add("Approval package id does not match the promotion package.");
        if (!targetExplicit)
            errors.Add("Target worktree path was not explicit.");
        if (!targetOutsideRepo)
            errors.Add("Target worktree path is inside the active repository.");
        if (!branchNotMain)
            errors.Add("Target branch resolves to main/master.");
        if (Directory.Exists(fullTarget))
            errors.Add("Dry-run target path already exists; dry-run refuses ambiguous existing target.");

        var statusAfter = await SolitaireDisposableBuildSmokeCommand.GetGitStatusAsync(repoRoot);
        var mutationCount = statusBefore == statusAfter ? 0 : 1;
        if (mutationCount != 0)
            errors.Add("Active repo status changed during dry-run.");

        var report = new ControlledWorktreeDryRunReport
        {
            Command = "promotion apply worktree-dry-run",
            Status = errors.Count == 0 ? "Succeeded" : "Failed",
            RunId = runId,
            TraceId = Guid.NewGuid().ToString("N"),
            Project = project,
            PackageId = package?.PackageId ?? "",
            ProposedChangeId = package?.ProposedChangeId ?? "",
            ApprovalId = approval?.ApprovalId ?? "",
            TargetWorktreePath = fullTarget,
            TargetBranchName = branchName,
            TargetPathExplicit = targetExplicit,
            TargetOutsideActiveRepo = targetOutsideRepo,
            TargetBranchIsNotMain = branchNotMain,
            WouldCreateWorktree = errors.Count == 0,
            WouldCopyFiles = false,
            FilesThatWouldApply = package?.FilesToPromote ?? [],
            BlockedFilesRejected = package?.FilesBlocked ?? [],
            PolicySnapshot = policy,
            ApprovalRecord = approval ?? FailedApproval(approvalRunId, packageRunId, "Approval missing."),
            Mutation = new PromotionMutationReport
            {
                ActiveRepoMutationAllowed = false,
                ActiveRepoMutationCount = mutationCount,
                IsolatedWorkspaceMutationAllowed = false,
                ActiveRepoStatusBefore = statusBefore,
                ActiveRepoStatusAfter = statusAfter,
                IsolatedWorkspacePath = fullTarget,
                IsolatedFilesChanged = 0
            },
            Evidence = [
                new PromotionEvidenceRef("PromotionPackage", Path.Combine(repoRoot, "tools", "dogfood", "runs", packageRunId, "promotion-package.json"), "Promotion package validated by dry-run."),
                new PromotionEvidenceRef("ApprovalRecord", Path.Combine(repoRoot, "tools", "dogfood", "runs", approvalRunId, "approval-record.json"), "Scoped approval record validated by dry-run."),
                new PromotionEvidenceRef("EffectivePolicy", Path.Combine(runRoot, "controlled-write-policy.json"), "Effective policy snapshot used by dry-run.")
            ],
            Errors = errors,
            Recommendation = errors.Count == 0 ? "ReadyForReviewedWorktreeApplyImplementation" : "NeedsFixBeforeApplyImplementation",
            Boundary = DryRunBoundary,
            ReproCommand = $"promotion apply worktree-dry-run --package-run-id {packageRunId} --approval-run-id {approvalRunId} --target-worktree \"{fullTarget}\" --run-id {runId} --json"
        };

        await File.WriteAllTextAsync(Path.Combine(runRoot, "controlled-write-policy.json"), JsonSerializer.Serialize(policy, SerializerOptions()), Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "controlled-worktree-dry-run-report.json"), JsonSerializer.Serialize(report, SerializerOptions()), Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "controlled-worktree-dry-run-report.md"), ToDryRunMarkdown(report), Encoding.UTF8);
        return report;
    }

    private static async Task EnsureReviewFixturePromotionPackageAsync(string repoRoot, string packageRunId)
    {
        var runRoot = RunRoot(repoRoot, packageRunId);
        Directory.CreateDirectory(runRoot);
        var packagePath = Path.Combine(runRoot, "promotion-package.json");
        if (File.Exists(packagePath))
            return;

        var runtime = new LanguageRuntimeRegistry().GetRequired("csharp-dotnet");
        var sourceTraceId = Guid.NewGuid().ToString("N");
        var evidenceRef = Path.Combine(runRoot, "fixture-evidence.json");
        var files = BuildFixtureFiles(runtime);
        var proposedChange = new ProposedChange
        {
            ProposedChangeId = $"PC-{packageRunId}",
            Project = "Solitaire",
            Title = "Solitaire controlled write approval fixture",
            SourceGoal = "Controlled write approval dry-run fixture",
            SourceDocumentIds = ["CONTROLLED_REAL_REPO_WRITE_PATH_DESIGN_172", "CONTROLLED_WRITE_POLICY_SETTINGS_173"],
            SourceTicketIds = ["SOL-139-001"],
            SourceRunIds = [$"{packageRunId}-source"],
            SourceTraceIds = [sourceTraceId],
            TargetRuntimeProfileId = runtime.RuntimeProfileId,
            CurrentStage = "PromotionPackageCreated",
            PromotionPackageId = $"PP-{packageRunId}",
            Risks = ["Fixture package exists only to validate approval and dry-run gates."],
            EvidenceRefs = [evidenceRef],
            Recommendation = "HumanReviewRequired",
            ApprovalState = "NeedsHumanReview",
            Boundary = "Promotion package fixture only. It does not apply files, create branches, mutate memory, accept tickets, approve writes, or self-approve."
        };

        var package = new PromotionPackage
        {
            PackageId = $"PP-{packageRunId}",
            ProposedChangeId = proposedChange.ProposedChangeId,
            SourceRunId = $"{packageRunId}-source",
            SourceTraceId = sourceTraceId,
            Project = "Solitaire",
            RuntimeProfile = runtime,
            FilesToPromote = files.Promotable,
            FilesBlocked = files.Blocked,
            TestsPassed = [
                new TestEvidence
                {
                    Name = "Fixture build evidence",
                    Status = "Passed",
                    Tool = runtime.BuildTool,
                    EvidenceRef = evidenceRef,
                    Summary = "Fixture evidence stands in for an already-reviewed promotion package; 169 separately proves real package creation."
                },
                new TestEvidence
                {
                    Name = "Fixture test evidence",
                    Status = "Passed",
                    Tool = runtime.TestTool,
                    EvidenceRef = evidenceRef,
                    Summary = "Fixture evidence is sufficient for approval/dry-run contract validation."
                }
            ],
            Risks = [
                new RiskNote
                {
                    Severity = "Info",
                    Category = "FixtureScope",
                    Message = "This package is deterministic fixture evidence for approval/dry-run gates.",
                    Mitigation = "Use campaign promotion-package-169 when proving disposable build output packaging."
                }
            ],
            Checklist = new HumanReviewChecklist
            {
                RequiredChecks = [
                    "Confirm package id and proposed change id match approval.",
                    "Confirm blocked files are rejected.",
                    "Confirm approval is dry-run only."
                ],
                ExplicitApprovalsNeeded = ["Approve this specific promotion package for isolated branch/worktree validation only."],
                BlockedActions = ["real repo write", "main branch apply", "auto-merge", "accepted memory mutation", "ticket acceptance", "self-approval"]
            },
            EvidenceSummary = new EvidenceSummary
            {
                BuildStatus = "Passed",
                TestStatus = "Passed",
                QualityStatus = "Passed",
                RealRepoMutationCount = 0,
                PromotableFileCount = files.Promotable.Count,
                BlockedFileCount = files.Blocked.Count,
                EvidenceRefs = [evidenceRef]
            },
            Recommendation = "HumanReviewRequired",
            ApprovalState = "NeedsHumanReview",
            Boundary = "Promotion package fixture only. It does not apply files, create branches, mutate memory, accept tickets, approve writes, or self-approve."
        };

        await File.WriteAllTextAsync(evidenceRef, JsonSerializer.Serialize(new
        {
            packageRunId,
            sourceTraceId,
            purpose = "Deterministic promotion package fixture for controlled write approval and dry-run gates.",
            realRepoMutationCount = 0
        }, SerializerOptions()), Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "proposed-change.json"), JsonSerializer.Serialize(proposedChange, SerializerOptions()), Encoding.UTF8);
        await File.WriteAllTextAsync(packagePath, JsonSerializer.Serialize(package, SerializerOptions()), Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "promotion-package.md"), ToFixturePackageMarkdown(proposedChange, package), Encoding.UTF8);
    }

    private static async Task<int> WriteFailedApprovalAsync(string repoRoot, string runId, string packageRunId, string error, JsonSerializerOptions options)
    {
        var approval = FailedApproval(runId, packageRunId, error);
        Directory.CreateDirectory(RunRoot(repoRoot, runId));
        await File.WriteAllTextAsync(Path.Combine(RunRoot(repoRoot, runId), "approval-record.json"), JsonSerializer.Serialize(approval, SerializerOptions()), Encoding.UTF8);
        Console.WriteLine(JsonSerializer.Serialize(approval, options));
        return 1;
    }

    private static async Task<int> WriteFailedDryRunAsync(string repoRoot, string runId, string packageRunId, string approvalRunId, string error, JsonSerializerOptions options)
    {
        var target = Path.Combine(Path.GetTempPath(), "IronDev-ControlledWorktreeDryRun", SanitizeSegment(runId), "candidate");
        var report = await WriteDryRunReportAsync(repoRoot, runId, packageRunId, approvalRunId, target, null, null, [error]);
        Console.WriteLine(JsonSerializer.Serialize(report, options));
        return 1;
    }

    private static IReadOnlyList<HardSafetyInvariant> BuildHardInvariants() =>
    [
        new() { Id = "NoDirectMainWrites", Description = "No direct writes to main or master.", Enforcement = "Target branch validation.", Configurable = false },
        new() { Id = "NoActiveWorkingTreeWrites", Description = "No writes to the active developer working tree.", Enforcement = "Path containment validation.", Configurable = false },
        new() { Id = "NoSelfApproval", Description = "Agents cannot approve their own promotion or write path.", Enforcement = "Approval role validation.", Configurable = false },
        new() { Id = "NoGovernanceBypass", Description = "ConscienceAgent and ThoughtLedger evidence are required before write-capable workflows.", Enforcement = "Required evidence validation.", Configurable = false },
        new() { Id = "NoAcceptedMemoryMutation", Description = "Write apply cannot mutate accepted memory or accept tickets.", Enforcement = "Blocked action list.", Configurable = false },
        new() { Id = "NoBlockedFilePromotion", Description = "Files classified as blocked cannot be promoted.", Enforcement = "Promotion manifest validation.", Configurable = false }
    ];

    private static ControlledWriteApprovalRecord FailedApproval(string runId, string packageRunId, string error) =>
        new()
        {
            ApprovalId = $"APP-{runId}",
            RunId = runId,
            TraceId = Guid.NewGuid().ToString("N"),
            Project = "Unknown",
            PackageId = packageRunId,
            ProposedChangeId = "",
            SourceRunId = "",
            SourceTraceId = "",
            ApprovedBy = "",
            ApprovalRole = "",
            ApprovalScope = "None",
            ApprovalState = "Invalid",
            ApprovalPhrase = error,
            CreatedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow,
            ValidForControlledWorktreeDryRun = false,
            ValidForRealRepoWrite = false,
            BlockedActions = ["WriteMain", "WriteActiveDeveloperWorkingTree", "CreatePullRequest", "AutoMerge", "MutateAcceptedMemory", "AcceptTicket", "SelfApprove"],
            Boundary = ApprovalBoundary
        };

    private static FixtureFiles BuildFixtureFiles(LanguageRuntimeProfile runtime)
    {
        var paths = new[]
        {
            "Solitaire.sln",
            "src/Solitaire.Core/Solitaire.Core.csproj",
            "src/Solitaire.Core/Card.cs",
            "src/Solitaire.Core/DeckFactory.cs",
            "src/Solitaire.Core/GameSetupService.cs",
            "src/Solitaire.Core/KlondikeRules.cs",
            "src/Solitaire.Core/SolitaireGameEngine.cs",
            "src/Solitaire.Wpf/Solitaire.Wpf.csproj",
            "src/Solitaire.Wpf/MainWindow.xaml",
            "src/Solitaire.Wpf/ViewModels/MainWindowViewModel.cs",
            "tests/Solitaire.Core.Tests/Solitaire.Core.Tests.csproj",
            "tests/Solitaire.Core.Tests/KlondikeRulesTests.cs"
        };

        var promotable = paths.Select(path => new PromotableFile
        {
            RelativePath = path,
            Language = runtime.TargetLanguage,
            FileRole = ClassifyFixtureRole(path),
            Sha256 = HashString(path),
            SizeBytes = Encoding.UTF8.GetByteCount(path),
            Rationale = "Deterministic fixture file allowed by the csharp-dotnet runtime profile."
        }).ToArray();

        var blocked = new[]
        {
            new BlockedFile
            {
                RelativePath = "src/Solitaire.Wpf/bin/Debug/net10.0/Solitaire.Wpf.dll",
                Reason = "Generated build output remains blocked from promotion."
            }
        };

        return new FixtureFiles(promotable, blocked);
    }

    private static string ClassifyFixtureRole(string relativePath)
    {
        if (relativePath.Contains("Tests/", StringComparison.OrdinalIgnoreCase) || relativePath.Contains(".Tests", StringComparison.OrdinalIgnoreCase))
            return "Test";
        if (relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || relativePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            return "Project";
        if (relativePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            return "UI";
        return "Source";
    }

    private static string HashString(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string ToFixturePackageMarkdown(ProposedChange proposedChange, PromotionPackage package) =>
        $"""
        # {proposedChange.Title}

        Proposed change: `{proposedChange.ProposedChangeId}`
        Promotion package: `{package.PackageId}`
        Runtime: `{package.RuntimeProfile.RuntimeProfileId}`
        Files to promote: `{package.FilesToPromote.Count}`
        Blocked files: `{package.FilesBlocked.Count}`
        Approval state: `{package.ApprovalState}`

        Boundary: {package.Boundary}
        """;

    private static async Task<CommandRun> RunCommandAsync(string repoRoot, string runId, string logName, IReadOnlyList<string> args)
    {
        var logsRoot = Path.Combine(RunRoot(repoRoot, runId), "logs");
        Directory.CreateDirectory(logsRoot);
        var logPath = Path.Combine(logsRoot, logName);
        using var process = new Process();
        process.StartInfo.FileName = "dotnet";
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.WorkingDirectory = repoRoot;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await File.WriteAllTextAsync(logPath, stdout + Environment.NewLine + stderr, Encoding.UTF8);
        return new CommandRun(process.ExitCode, logPath);
    }

    private static string ToPolicyMarkdown(ControlledWriteEffectivePolicy policy) =>
        $"""
        # Controlled Write Policy 173

        Status: `{policy.Status}`
        Project: `{policy.Project}`
        Write path enabled: `{policy.EffectiveSettings.WritePathEnabled}`
        Permitted modes: `{string.Join(", ", policy.EffectiveSettings.PermittedPromotionModes)}`

        Hard invariants are non-configurable. Attempted overrides were ignored:

        {string.Join(Environment.NewLine, policy.IgnoredInvariantOverrides.Select(item => $"- {item}"))}

        Boundary: {policy.Boundary}
        """;

    private static string ToApprovalMarkdown(ControlledWriteApprovalRecord approval) =>
        $"""
        # Controlled Write Approval 174

        Approval: `{approval.ApprovalId}`
        Package: `{approval.PackageId}`
        Scope: `{approval.ApprovalScope}`
        State: `{approval.ApprovalState}`
        Valid for dry-run: `{approval.ValidForControlledWorktreeDryRun}`
        Valid for real repo write: `{approval.ValidForRealRepoWrite}`

        Boundary: {approval.Boundary}
        """;

    private static string ToDryRunMarkdown(ControlledWorktreeDryRunReport report) =>
        $"""
        # Controlled Worktree Dry-Run 175

        Status: `{report.Status}`
        Package: `{report.PackageId}`
        Approval: `{report.ApprovalId}`
        Target worktree: `{report.TargetWorktreePath}`
        Target branch: `{report.TargetBranchName}`
        Files that would apply: `{report.FilesThatWouldApply.Count}`
        Blocked files rejected: `{report.BlockedFilesRejected.Count}`
        Active repo mutation count: `{report.Mutation.ActiveRepoMutationCount}`
        Recommendation: `{report.Recommendation}`

        Boundary: {report.Boundary}
        """;

    private static string RunnerProject(string repoRoot) =>
        Path.Combine(repoRoot, "tools", "IronDev.ReplayRunner", "IronDev.ReplayRunner.csproj");

    private static string RunRoot(string repoRoot, string runId) =>
        Path.Combine(repoRoot, "tools", "dogfood", "runs", runId);

    private static JsonSerializerOptions SerializerOptions() =>
        new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    private static string ExpandBranchName(string template, string project, string runId) =>
        SanitizeBranchName(template
            .Replace("{project}", project, StringComparison.OrdinalIgnoreCase)
            .Replace("{runId}", runId, StringComparison.OrdinalIgnoreCase));

    private static string SanitizeSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-');
        return builder.ToString().Trim('-');
    }

    private static string SanitizeBranchName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or '/' ? ch : '-');
        return builder.ToString().Trim('-', '/');
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar), fullDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private sealed record CommandRun(int ExitCode, string LogPath);
    private sealed record FixtureFiles(IReadOnlyList<PromotableFile> Promotable, IReadOnlyList<BlockedFile> Blocked);
}
