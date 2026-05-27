using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Builder;
using IronDev.Core.Promotion;
using IronDev.Core.RunReports;
using IronDev.Core.Tools;
using IronDev.Infrastructure.Tools.CodeStandards;

namespace IronDev.Infrastructure.Services.Promotion;

public sealed class PatchProposalService : IPatchProposalService
{
    private readonly IGovernedToolRegistry _tools;

    public PatchProposalService(IGovernedToolRegistry tools)
    {
        _tools = tools;
    }

    public async Task<PatchProposal> CreateAsync(
        PatchProposalRequest request,
        CancellationToken cancellationToken = default)
    {
        var unifiedDiff = BuildUnifiedDiff(request.Proposal);
        var hash = Sha256(unifiedDiff);
        var codeStandards = await _tools.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
            new GovernedToolRequest<CodeStandardsAnalysisInput>
            {
                RequestId = $"code-standards-{request.RunId}",
                ToolName = CodeStandardsAnalysisTool.ToolName,
                RequestedBy = request.RequestedBy,
                Input = new CodeStandardsAnalysisInput
                {
                    PatchText = unifiedDiff,
                    ChangedFiles = request.Proposal.FileChanges.Select(change => new CodeStandardsChangedFile
                    {
                        Path = change.FilePath,
                        Patch = change.Patch,
                        Content = change.FullContentAfter
                    }).ToArray()
                },
                Reason = "Analyse patch proposal before promotion packaging."
            },
            cancellationToken).ConfigureAwait(false);

        var codeStandardsOutput = codeStandards.Output ?? new CodeStandardsAnalysisResult
        {
            Status = codeStandards.Status.ToString(),
            Summary = codeStandards.Summary
        };
        var warnings = new List<string>();
        if (!request.PatchValidation.AllValid)
            warnings.Add(request.PatchValidation.Summary);
        if (request.BuildResult.ExitCode != 0)
            warnings.Add(request.BuildResult.Summary);
        if (request.TestResult.ExitCode != 0)
            warnings.Add(request.TestResult.Summary);
        if (codeStandardsOutput.HasBlockingFindings)
            warnings.Add(codeStandardsOutput.Summary);

        return new PatchProposal
        {
            RunId = request.RunId,
            TicketId = request.TicketId,
            PatchProposalId = $"patch-{hash[..12]}",
            UnifiedDiff = unifiedDiff,
            PatchSha256 = hash,
            ChangedFiles = request.Proposal.FileChanges.Select(change => change.FilePath).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            CodeStandardsStatus = codeStandardsOutput.Status,
            CodeStandardsSummary = codeStandardsOutput.Summary,
            CodeStandardsFindingCount = codeStandardsOutput.Findings.Count,
            CodeStandardsBlockingFindingCount = codeStandardsOutput.Findings.Count(finding =>
                string.Equals(finding.Severity, "High", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(finding.Severity, "Critical", StringComparison.OrdinalIgnoreCase)),
            PatchValidation = request.PatchValidation,
            BuildResult = request.BuildResult,
            TestResult = request.TestResult,
            Warnings = warnings,
            EvidenceLinks =
            [
                new("CodeStandards", codeStandards.RequestId, codeStandards.Summary),
                new("Build", request.BuildResult.LogPath, request.BuildResult.Summary),
                new("Test", request.TestResult.LogPath, request.TestResult.Summary)
            ],
            RiskSummary = warnings.Count == 0
                ? "No blocking validation warnings were recorded."
                : string.Join(" ", warnings)
        };
    }

    private static string BuildUnifiedDiff(CodeChangeProposal proposal)
    {
        var patches = proposal.FileChanges
            .Select(change => change.Patch)
            .Where(patch => !string.IsNullOrWhiteSpace(patch))
            .Select(patch => patch.ReplaceLineEndings("\n").TrimEnd('\r', '\n'))
            .ToArray();

        return patches.Length == 0
            ? string.Empty
            : string.Join('\n', patches) + "\n";
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public sealed class PromotionPackageService : IPromotionPackageService
{
    public PromotionPackage CreatePackage(PromotionPackageRequest request)
    {
        var proposal = request.PatchProposal;
        var files = proposal.ChangedFiles.Select(path => new PromotableFile
        {
            RelativePath = path,
            Language = request.RuntimeProfile.TargetLanguage,
            FileRole = IsTestFile(path) ? "Test" : "Source",
            Sha256 = proposal.PatchSha256,
            SizeBytes = Encoding.UTF8.GetByteCount(proposal.UnifiedDiff),
            Rationale = "Included in validated patch proposal."
        }).ToArray();

        var blocked = proposal.ChangedFiles
            .Where(path => request.RuntimeProfile.ForbiddenPathSegments.Any(segment =>
                path.Replace('\\', '/').Contains(segment, StringComparison.OrdinalIgnoreCase)))
            .Select(path => new BlockedFile
            {
                RelativePath = path,
                Reason = "Path matches a forbidden runtime-profile segment."
            })
            .ToArray();

        return new PromotionPackage
        {
            PackageId = $"pkg-{proposal.PatchSha256[..12]}",
            ProposedChangeId = proposal.PatchProposalId,
            SourceRunId = proposal.RunId,
            SourceTraceId = request.SourceTraceId,
            Project = request.Project,
            PatchSha256 = proposal.PatchSha256,
            UnifiedDiff = proposal.UnifiedDiff,
            RuntimeProfile = request.RuntimeProfile,
            FilesToPromote = files.Where(file => blocked.All(blockedFile =>
                !string.Equals(blockedFile.RelativePath, file.RelativePath, StringComparison.OrdinalIgnoreCase))).ToArray(),
            FilesBlocked = blocked,
            TestsPassed =
            [
                new()
                {
                    Name = "Build",
                    Status = proposal.BuildResult.Status,
                    Tool = proposal.BuildResult.Command,
                    EvidenceRef = proposal.BuildResult.LogPath,
                    Summary = proposal.BuildResult.Summary
                },
                new()
                {
                    Name = "Test",
                    Status = proposal.TestResult.Status,
                    Tool = proposal.TestResult.Command,
                    EvidenceRef = proposal.TestResult.LogPath,
                    Summary = proposal.TestResult.Summary
                }
            ],
            Risks = proposal.Warnings.Select(warning => new RiskNote
            {
                Severity = "Medium",
                Category = "Validation",
                Message = warning,
                Mitigation = "Resolve before approval or record explicit human acceptance."
            }).ToArray(),
            Checklist = new HumanReviewChecklist
            {
                RequiredChecks = ["Review exact patch hash", "Review build/test evidence", "Review blocked files"],
                ExplicitApprovalsNeeded = ["Human approval before controlled worktree apply"],
                BlockedActions = ["real repository write without matching approval", "auto-merge", "self-approval"]
            },
            EvidenceSummary = new EvidenceSummary
            {
                BuildStatus = proposal.BuildResult.Status,
                TestStatus = proposal.TestResult.Status,
                QualityStatus = proposal.CodeStandardsStatus,
                RealRepoMutationCount = 0,
                PromotableFileCount = files.Length - blocked.Length,
                BlockedFileCount = blocked.Length,
                EvidenceRefs = proposal.EvidenceLinks.Select(evidence => evidence.Path).ToArray()
            },
            Recommendation = proposal.Warnings.Count == 0 && blocked.Length == 0
                ? "ReadyForHumanReview"
                : "HumanReviewRequired",
            ApprovalState = "NeedsHumanReview",
            Boundary = "Promotion package is review evidence. It does not approve or apply the patch."
        };
    }

    private static bool IsTestFile(string path) =>
        path.Contains("test", StringComparison.OrdinalIgnoreCase);
}

public sealed class ControlledWriteApprovalService : IControlledWriteApprovalService
{
    public ControlledWriteApprovalRecord ApproveForControlledWorktreeApply(ControlledWriteApprovalRequest request)
    {
        var expectedPhrase = "Approve this specific promotion package for controlled worktree apply only.";
        var approved = string.Equals(request.ApprovalPhrase, expectedPhrase, StringComparison.Ordinal);
        var created = request.CreatedUtc;

        return new ControlledWriteApprovalRecord
        {
            ApprovalId = $"approval-{Guid.NewGuid():N}",
            RunId = request.RunId,
            TraceId = request.TraceId,
            Project = request.Project,
            PackageId = request.Package.PackageId,
            ProposedChangeId = request.Package.ProposedChangeId,
            SourceRunId = request.Package.SourceRunId,
            SourceTraceId = request.Package.SourceTraceId,
            PatchSha256 = request.Package.PatchSha256,
            ApprovedBy = request.ApprovedBy,
            ApprovalRole = request.ApprovalRole,
            ApprovalScope = "ControlledWorktreeApply",
            ApprovalState = approved ? "Approved" : "Rejected",
            ApprovalPhrase = request.ApprovalPhrase,
            CreatedUtc = created,
            ExpiresUtc = created.AddDays(7),
            ValidForControlledWorktreeDryRun = approved,
            ValidForRealRepoWrite = false,
            RequiredEvidenceRefs = request.Package.EvidenceSummary.EvidenceRefs,
            AllowedActions = approved ? ["ControlledWorktreeApply"] : [],
            BlockedActions = approved
                ? ["direct main write", "auto-merge", "accepted memory mutation", "ticket acceptance", "self-approval"]
                : ["ControlledWorktreeApply"],
            Boundary = "Approval is scoped to one package and exact patch hash. It does not approve real repo main writes."
        };
    }
}

public sealed class ControlledWorktreeApplyService : IControlledWorktreeApplyService
{
    private readonly IRunEventStore _events;

    public ControlledWorktreeApplyService(IRunEventStore events)
    {
        _events = events;
    }

    public async Task<ControlledWorktreeDryRunReport> ApplyAsync(
        ControlledWorktreeApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = Validate(request).ToList();
        var activeStatusBefore = await GitAsync(request.ActiveRepoPath, ["status", "--porcelain"], cancellationToken).ConfigureAwait(false);
        if (!request.ForceDirtyActiveRepo && !string.IsNullOrWhiteSpace(activeStatusBefore.Stdout))
            errors.Add("Active repository has uncommitted changes.");

        if (errors.Count == 0)
            await CreateWorktreeAsync(request, cancellationToken).ConfigureAwait(false);

        RuntimeCommandEvidence dryRun = new()
        {
            Command = "git apply --check",
            ExitCode = 1,
            Status = "Blocked",
            LogPath = request.TargetWorktreePath,
            Summary = "Dry-run was blocked before git apply --check."
        };
        RuntimeCommandEvidence apply = dryRun with
        {
            Command = "git apply",
            Summary = "Apply was blocked before git apply."
        };

        if (errors.Count == 0)
        {
            dryRun = await RunGitApplyAsync(request, checkOnly: true, cancellationToken).ConfigureAwait(false);
            if (dryRun.ExitCode != 0)
                errors.Add(dryRun.Summary);
        }

        if (errors.Count == 0)
        {
            apply = await RunGitApplyAsync(request, checkOnly: false, cancellationToken).ConfigureAwait(false);
            if (apply.ExitCode != 0)
                errors.Add(apply.Summary);
        }

        var activeStatusAfter = await GitAsync(request.ActiveRepoPath, ["status", "--porcelain"], cancellationToken).ConfigureAwait(false);
        var success = errors.Count == 0;
        var report = new ControlledWorktreeDryRunReport
        {
            Command = "promotion apply controlled-worktree",
            Status = success ? "Applied" : "Blocked",
            RunId = request.RunId,
            TraceId = request.TraceId,
            Project = request.Package.Project,
            PackageId = request.Package.PackageId,
            ProposedChangeId = request.Package.ProposedChangeId,
            ApprovalId = request.Approval.ApprovalId,
            PatchSha256 = request.Package.PatchSha256,
            TargetWorktreePath = request.TargetWorktreePath,
            TargetBranchName = request.TargetBranchName,
            TargetPathExplicit = !string.IsNullOrWhiteSpace(request.TargetWorktreePath),
            TargetOutsideActiveRepo = IsOutside(request.ActiveRepoPath, request.TargetWorktreePath),
            TargetBranchIsNotMain = IsSafeBranch(request.TargetBranchName),
            WouldCreateWorktree = success,
            WouldCopyFiles = success,
            FilesThatWouldApply = request.Package.FilesToPromote,
            BlockedFilesRejected = request.Package.FilesBlocked,
            PolicySnapshot = request.PolicySnapshot ?? BuildDefaultPolicy(request),
            ApprovalRecord = request.Approval,
            Mutation = new PromotionMutationReport
            {
                ActiveRepoMutationAllowed = false,
                ActiveRepoMutationCount = string.Equals(activeStatusBefore.Stdout, activeStatusAfter.Stdout, StringComparison.Ordinal) ? 0 : 1,
                IsolatedWorkspaceMutationAllowed = true,
                ActiveRepoStatusBefore = activeStatusBefore.Stdout,
                ActiveRepoStatusAfter = activeStatusAfter.Stdout,
                IsolatedWorkspacePath = request.TargetWorktreePath,
                IsolatedFilesChanged = success ? request.Package.FilesToPromote.Count : 0
            },
            Evidence =
            [
                new("DryRun", dryRun.LogPath, dryRun.Summary),
                new("Apply", apply.LogPath, apply.Summary)
            ],
            Errors = errors,
            Recommendation = success ? "ReviewAppliedWorktree" : "ResolveBlockedApply",
            Boundary = "Controlled apply writes only to the isolated target worktree after approval, hash match, dirty-tree check, and git apply dry-run.",
            ReproCommand = $"git -C \"{request.TargetWorktreePath}\" apply < approved.patch"
        };

        await _events.PublishAsync(new RunEventDto
        {
            RunId = request.RunId,
            EventType = success ? "ControlledWorktreeApplyCompleted" : "ControlledWorktreeApplyBlocked",
            Message = report.Status,
            Payload = new Dictionary<string, string>
            {
                ["packageId"] = request.Package.PackageId,
                ["approvalId"] = request.Approval.ApprovalId,
                ["patchSha256"] = request.Package.PatchSha256,
                ["status"] = report.Status
            }
        }, cancellationToken).ConfigureAwait(false);

        return report;
    }

    private static IEnumerable<string> Validate(ControlledWorktreeApplyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Package.UnifiedDiff))
            yield return "Promotion package has no unified diff.";
        if (request.Package.FilesBlocked.Count > 0)
            yield return "Promotion package contains blocked files.";
        if (!string.Equals(request.Approval.ApprovalState, "Approved", StringComparison.OrdinalIgnoreCase))
            yield return "Approval record is not approved.";
        if (!string.Equals(request.Approval.PackageId, request.Package.PackageId, StringComparison.Ordinal))
            yield return "Approval package id does not match package.";
        if (!string.Equals(request.Approval.PatchSha256, request.Package.PatchSha256, StringComparison.Ordinal))
            yield return "Approval patch hash does not match package patch hash.";
        if (!request.Approval.AllowedActions.Contains("ControlledWorktreeApply", StringComparer.OrdinalIgnoreCase))
            yield return "Approval does not allow controlled worktree apply.";
        if (!IsOutside(request.ActiveRepoPath, request.TargetWorktreePath))
            yield return "Target worktree must be outside the active repository.";
        if (!IsSafeBranch(request.TargetBranchName))
            yield return "Target branch must not be main or master.";
    }

    private static async Task CreateWorktreeAsync(ControlledWorktreeApplyRequest request, CancellationToken cancellationToken)
    {
        if (Directory.Exists(request.TargetWorktreePath))
            Directory.Delete(request.TargetWorktreePath, recursive: true);

        var result = await GitAsync(request.ActiveRepoPath, ["worktree", "add", "-B", request.TargetBranchName, request.TargetWorktreePath, "HEAD"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"git worktree add failed: {result.Stderr}");
    }

    private static async Task<RuntimeCommandEvidence> RunGitApplyAsync(
        ControlledWorktreeApplyRequest request,
        bool checkOnly,
        CancellationToken cancellationToken)
    {
        var patchPath = Path.Combine(Path.GetTempPath(), $"irondev-approved-{Guid.NewGuid():N}.patch");
        var logRoot = Path.GetDirectoryName(request.TargetWorktreePath) ?? Path.GetTempPath();
        var logPath = Path.Combine(logRoot, $"irondev-{(checkOnly ? "apply-check" : "apply")}-{Guid.NewGuid():N}.log");
        await File.WriteAllTextAsync(patchPath, request.Package.UnifiedDiff, cancellationToken).ConfigureAwait(false);
        try
        {
            var args = checkOnly ? new[] { "apply", "--check", patchPath } : ["apply", patchPath];
            var result = await GitAsync(request.TargetWorktreePath, args, cancellationToken).ConfigureAwait(false);
            var summary = result.ExitCode == 0
                ? $"{(checkOnly ? "Dry-run" : "Apply")} succeeded."
                : $"{(checkOnly ? "Dry-run" : "Apply")} failed: {result.Stderr}";
            await File.WriteAllTextAsync(
                logPath,
                $"Command: git {string.Join(' ', args)}{Environment.NewLine}ExitCode: {result.ExitCode}{Environment.NewLine}Stdout:{Environment.NewLine}{result.Stdout}{Environment.NewLine}Stderr:{Environment.NewLine}{result.Stderr}",
                cancellationToken).ConfigureAwait(false);
            return new RuntimeCommandEvidence
            {
                Command = checkOnly ? "git apply --check" : "git apply",
                ExitCode = result.ExitCode,
                Status = result.ExitCode == 0 ? "Succeeded" : "Failed",
                LogPath = logPath,
                Summary = summary
            };
        }
        finally
        {
            if (File.Exists(patchPath))
                File.Delete(patchPath);
        }
    }

    private static ControlledWriteEffectivePolicy BuildDefaultPolicy(ControlledWorktreeApplyRequest request) => new()
    {
        Command = "promotion policy effective",
        Status = "Effective",
        RunId = request.RunId,
        TraceId = request.TraceId,
        Project = request.Package.Project,
        GlobalDefaults = DefaultSettings("global"),
        ProjectSettings = DefaultSettings("project"),
        RunSettings = DefaultSettings("run"),
        ExplicitHumanOverride = DefaultSettings("human"),
        EffectiveSettings = DefaultSettings("effective") with
        {
            WritePathEnabled = true,
            PermittedPromotionModes = ["ControlledWorktreeApply"],
            WorktreeRoot = Path.GetDirectoryName(request.TargetWorktreePath) ?? string.Empty
        },
        Boundary = "Effective policy snapshot for controlled worktree apply.",
        ReproCommand = "promotion policy effective"
    };

    private static ControlledWritePolicySettings DefaultSettings(string scope) => new()
    {
        PolicyId = $"irondev-{scope}",
        Scope = scope
    };

    private static async Task<ProcessResult> GitAsync(string workingDirectory, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static bool IsOutside(string activeRepoPath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return false;

        var active = Path.GetFullPath(activeRepoPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !target.Equals(active, StringComparison.OrdinalIgnoreCase) &&
               !target.StartsWith(active + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeBranch(string branchName) =>
        !string.IsNullOrWhiteSpace(branchName) &&
        !string.Equals(branchName, "main", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(branchName, "master", StringComparison.OrdinalIgnoreCase);

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
