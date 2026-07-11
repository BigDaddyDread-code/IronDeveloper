using IronDev.Core.Builder;
using IronDev.Core.Workspaces;
using IronDev.Infrastructure.Services.Workspaces;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// Drives the proven governed workspace apply spine, in-process, in the exact stage
/// order the CLI and its E2E proof use: prepare → materialize approved changes →
/// diff → promotion-package → promotion-approval → apply-preflight → apply-dry-run →
/// apply-copy → apply-verify. Every stage writes its evidence file into the workspace
/// run directory, and apply-copy refuses to run unless the whole upstream chain
/// exists — the chain is the receipt.
///
/// Boundary: the promotion-approval stage RECORDS a decision already made through the
/// accepted-approvals surface; it does not decide. This spine is copy-only: no commit,
/// no push, no release.
/// </summary>
public sealed class SkeletonApplySpine
{
    public sealed record StageOutcome(string Stage, bool Succeeded, string Summary, IReadOnlyList<string> Errors);

    public sealed record SpineResult
    {
        public required bool Succeeded { get; init; }
        public required string WorkspacePath { get; init; }
        public required IReadOnlyList<StageOutcome> Stages { get; init; }
        public string? FailedStage => Stages.LastOrDefault(stage => !stage.Succeeded)?.Stage;
    }

    public async Task<SpineResult> RunAsync(
        string applyRunId,
        string sourceRepo,
        string workspaceRoot,
        SkeletonCriticPackage approvedPackage,
        string approvedByActorId,
        string approvalReason,
        Func<string, Task>? onStageStarted = null,
        Func<StageOutcome, Task>? onStageCompleted = null,
        CancellationToken cancellationToken = default)
    {
        var stages = new List<StageOutcome>();
        var workspacePath = Path.Combine(workspaceRoot, applyRunId);

        async Task StartStageAsync(string stage)
        {
            if (onStageStarted is not null)
                await onStageStarted(stage).ConfigureAwait(false);
        }

        async Task<bool> CompleteStageAsync(StageOutcome stage)
        {
            stages.Add(stage);
            if (onStageCompleted is not null)
                await onStageCompleted(stage).ConfigureAwait(false);
            return stage.Succeeded;
        }

        await StartStageAsync("prepare").ConfigureAwait(false);
        var prepare = await new DisposableWorkspacePrepareService(new DisposableWorkspaceReadinessService()).PrepareAsync(new DisposableWorkspacePrepareRequest
        {
            RunId = applyRunId,
            SourceRepo = sourceRepo,
            WorkspaceRoot = workspaceRoot
        }, cancellationToken).ConfigureAwait(false);
        if (!await CompleteStageAsync(new StageOutcome("prepare", prepare.ExitCode == 0, prepare.Summary, prepare.Errors)).ConfigureAwait(false))
            return Result(false, workspacePath, stages);

        await StartStageAsync("materialize-approved-changes").ConfigureAwait(false);
        var materialize = MaterializeApprovedChanges(workspacePath, approvedPackage);
        if (!await CompleteStageAsync(materialize).ConfigureAwait(false))
            return Result(false, workspacePath, stages);

        // Validation runs against the materialized workspace: what gets validated is
        // exactly what will be applied.
        await StartStageAsync("validate").ConfigureAwait(false);
        var validation = await new DisposableWorkspaceValidationService(new DisposableWorkspaceCommandService()).ValidateAsync(new DisposableWorkspaceValidationRequest
        {
            RunId = applyRunId,
            WorkspacePath = workspacePath,
            ProfileId = "dotnet-build-test"
        }, cancellationToken).ConfigureAwait(false);
        if (!await CompleteStageAsync(new StageOutcome("validate", validation.ExitCode == 0, validation.Summary, validation.Errors)).ConfigureAwait(false))
            return Result(false, workspacePath, stages);

        await StartStageAsync("diff").ConfigureAwait(false);
        var diff = await new DisposableWorkspaceDiffService().DiffAsync(new DisposableWorkspaceDiffRequest
        {
            RunId = applyRunId,
            WorkspacePath = workspacePath
        }, cancellationToken).ConfigureAwait(false);
        if (!await CompleteStageAsync(new StageOutcome("diff", diff.ExitCode == 0, diff.Summary, diff.Errors)).ConfigureAwait(false))
            return Result(false, workspacePath, stages);

        await StartStageAsync("promotion-package").ConfigureAwait(false);
        var package = await new DisposableWorkspacePromotionPackageService().CreateAsync(new DisposableWorkspacePromotionPackageRequest
        {
            RunId = applyRunId,
            WorkspacePath = workspacePath
        }, cancellationToken).ConfigureAwait(false);
        if (!await CompleteStageAsync(new StageOutcome("promotion-package", package.ExitCode == 0, package.Summary, package.Errors)).ConfigureAwait(false))
            return Result(false, workspacePath, stages);

        // Records the human decision already made through the accepted-approvals
        // surface into workspace evidence. This stage records; it does not decide.
        await StartStageAsync("promotion-approval").ConfigureAwait(false);
        var approval = await new DisposableWorkspacePromotionApprovalService().CreateAsync(new DisposableWorkspacePromotionApprovalRequest
        {
            RunId = applyRunId,
            WorkspacePath = workspacePath,
            Decision = "approved",
            ApprovedBy = approvedByActorId,
            Reason = approvalReason
        }, cancellationToken).ConfigureAwait(false);
        if (!await CompleteStageAsync(new StageOutcome("promotion-approval", approval.ExitCode == 0, approval.Summary, approval.Errors)).ConfigureAwait(false))
            return Result(false, workspacePath, stages);

        await StartStageAsync("apply-preflight").ConfigureAwait(false);
        var preflight = await new DisposableWorkspaceApplyPreflightService().CheckAsync(new DisposableWorkspaceApplyPreflightRequest
        {
            RunId = applyRunId,
            WorkspacePath = workspacePath
        }, cancellationToken).ConfigureAwait(false);
        if (!await CompleteStageAsync(new StageOutcome("apply-preflight", preflight.ExitCode == 0, preflight.Summary, preflight.Errors)).ConfigureAwait(false))
            return Result(false, workspacePath, stages);

        await StartStageAsync("apply-dry-run").ConfigureAwait(false);
        var dryRun = await new DisposableWorkspaceApplyDryRunService().CheckAsync(new DisposableWorkspaceApplyDryRunRequest
        {
            RunId = applyRunId,
            WorkspacePath = workspacePath
        }, cancellationToken).ConfigureAwait(false);
        if (!await CompleteStageAsync(new StageOutcome("apply-dry-run", dryRun.ExitCode == 0, dryRun.Summary, dryRun.Errors)).ConfigureAwait(false))
            return Result(false, workspacePath, stages);

        await StartStageAsync("apply-copy").ConfigureAwait(false);
        var copy = await new DisposableWorkspaceApplyCopyService().ApplyAsync(new DisposableWorkspaceApplyCopyRequest
        {
            RunId = applyRunId,
            WorkspacePath = workspacePath
        }, cancellationToken).ConfigureAwait(false);
        if (!await CompleteStageAsync(new StageOutcome("apply-copy", copy.ExitCode == 0, copy.Summary, copy.Errors)).ConfigureAwait(false))
            return Result(false, workspacePath, stages);

        await StartStageAsync("apply-verify").ConfigureAwait(false);
        var verify = await new DisposableWorkspaceApplyVerifyService().VerifyAsync(new DisposableWorkspaceApplyVerifyRequest
        {
            RunId = applyRunId,
            WorkspacePath = workspacePath
        }, cancellationToken).ConfigureAwait(false);
        await CompleteStageAsync(new StageOutcome("apply-verify", verify.ExitCode == 0, verify.Summary, verify.Errors)).ConfigureAwait(false);

        return Result(verify.ExitCode == 0, workspacePath, stages);
    }

    /// <summary>
    /// Writes the approval-bound package's changes into the prepared spine workspace.
    /// Only the hash-verified package contents are materialized — never the live
    /// proposal object — so what lands is exactly what the critic reviewed and the
    /// human approved.
    /// </summary>
    private static StageOutcome MaterializeApprovedChanges(string workspacePath, SkeletonCriticPackage package)
    {
        var errors = new List<string>();
        var written = 0;
        var fullWorkspace = Path.GetFullPath(workspacePath);

        foreach (var change in package.Changes)
        {
            if (string.IsNullOrWhiteSpace(change.FilePath) || Path.IsPathRooted(change.FilePath))
            {
                errors.Add($"Change path must be relative: '{change.FilePath}'.");
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(fullWorkspace, change.FilePath));
            if (!targetPath.StartsWith(fullWorkspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Change path must stay inside the workspace: '{change.FilePath}'.");
                continue;
            }

            if (change.IsDeletion)
            {
                if (File.Exists(targetPath))
                    File.Delete(targetPath);
                written++;
                continue;
            }

            if (change.FullContentAfter is null)
            {
                errors.Add($"Change has no content to materialize: '{change.FilePath}'.");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, change.FullContentAfter);
            written++;
        }

        return errors.Count > 0
            ? new StageOutcome("materialize-approved-changes", false, $"Materialization blocked with {errors.Count} error(s).", errors)
            : new StageOutcome("materialize-approved-changes", true, $"Materialized {written} approved change(s) into the workspace.", []);
    }

    private static SpineResult Result(bool succeeded, string workspacePath, List<StageOutcome> stages) => new()
    {
        Succeeded = succeeded,
        WorkspacePath = workspacePath,
        Stages = stages
    };
}
