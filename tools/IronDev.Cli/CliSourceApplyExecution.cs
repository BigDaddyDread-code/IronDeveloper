using System.Text;
using System.Text.Json;
using IronDev.Core.Governance;
using IronDev.Core.SourceApply;
using SourceApplyDryRunResult = IronDev.Core.SourceApply.SourceApplyDryRunResult;
using SourceApplyRequest = IronDev.Core.SourceApply.SourceApplyRequest;
using SourceApplyReceipt = IronDev.Core.SourceApply.SourceApplyReceipt;

namespace IronDev.Cli;

public static partial class IronDevCliSourceApply
{
    private static async Task<int> HandleDecisionTemplateAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseTemplate(args, "decision-template");
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "source-apply decision-template", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!, parsed.RunsRootPath);
        var sourceApply = await ReadRequiredArtifactAsync<SourceApplyRequest>(runPath, "source-apply-request.json", cancellationToken).ConfigureAwait(false);
        if (sourceApply is null)
            return Failure(output, error, parsed.Json, "source-apply decision-template", "source-apply-request.json was not found. Run source-apply prepare first.");

        var template = SourceApplyDecisionTemplates.SourceApply(sourceApply.RunId, sourceApply.SourceApplyRequestId);
        await WriteJsonFileAsync(parsed.OutPath!, template, cancellationToken).ConfigureAwait(false);

        if (parsed.Json)
            WriteJson(output, "source-apply decision-template", "succeeded", new { template.DecisionId, template.ActionKind, template.SubjectId, parsed.OutPath }, []);
        else
            output.WriteLine($"Source apply decision template: {Path.GetFullPath(parsed.OutPath!)}");

        return 0;
    }

    private static async Task<int> HandleRollbackTemplateAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseTemplate(args, "rollback-template");
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "source-apply rollback-template", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!, parsed.RunsRootPath);
        var receipt = await ReadRequiredArtifactAsync<SourceApplyReceipt>(runPath, "source-apply-receipt.json", cancellationToken).ConfigureAwait(false);
        if (receipt is null || receipt.Decision != SourceApplyReceiptDecision.AppliedToWorkingTree)
            return Failure(output, error, parsed.Json, "source-apply rollback-template", "source-apply-receipt.json with AppliedToWorkingTree decision was not found.");

        var template = SourceApplyDecisionTemplates.SourceRollback(receipt.RunId, receipt.SourceApplyReceiptId);
        await WriteJsonFileAsync(parsed.OutPath!, template, cancellationToken).ConfigureAwait(false);

        if (parsed.Json)
            WriteJson(output, "source-apply rollback-template", "succeeded", new { template.DecisionId, template.ActionKind, template.SubjectId, parsed.OutPath }, []);
        else
            output.WriteLine($"Source rollback decision template: {Path.GetFullPath(parsed.OutPath!)}");

        return 0;
    }

    private static async Task<int> HandleApplyAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseExecute(args, "apply");
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "source-apply apply", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!, parsed.RunsRootPath);
        var sourceApply = await ReadRequiredArtifactAsync<SourceApplyRequest>(runPath, "source-apply-request.json", cancellationToken).ConfigureAwait(false);
        var verification = await ReadRequiredArtifactAsync<PatchArtifactVerificationResult>(runPath, "patch-artifact-verification.json", cancellationToken).ConfigureAwait(false);
        var approval = await ReadRequiredArtifactAsync<SourceApplyApprovalEvidence>(runPath, "source-apply-approval-evidence.json", cancellationToken).ConfigureAwait(false);
        var readiness = await ReadRequiredArtifactAsync<SourceApplyReadinessReport>(runPath, "source-apply-readiness.json", cancellationToken).ConfigureAwait(false);
        var dryRun = await ReadRequiredArtifactAsync<SourceApplyDryRunResult>(runPath, "source-apply-dry-run-result.json", cancellationToken).ConfigureAwait(false);
        var rollbackDraft = await ReadRequiredArtifactAsync<RollbackPlanDraft>(runPath, "rollback-plan-draft.json", cancellationToken).ConfigureAwait(false);
        var decision = await ReadJsonFileAsync<ConscienceDecision>(parsed.DecisionPath!, cancellationToken).ConfigureAwait(false);

        if (sourceApply is null)
            return Failure(output, error, parsed.Json, "source-apply apply", "source-apply-request.json was not found. Run source-apply prepare first.");

        var request = new SourceApplyExecutionRequest
        {
            SourceApplyExecutionRequestId = $"source_apply_exec_req_{Guid.NewGuid():N}",
            RunId = sourceApply.RunId,
            SourceApplyRequestId = sourceApply.SourceApplyRequestId,
            SourceRepoPath = sourceApply.SourceRepoPath,
            SourceRepoIdentity = sourceApply.SourceRepoIdentity,
            BaseCommit = sourceApply.BaseCommit,
            PatchPath = sourceApply.PatchPath,
            PatchSha256 = sourceApply.PatchSha256,
            ChangedFiles = sourceApply.ChangedFiles,
            RequestedBy = "IronDevCli",
            RequestedAtUtc = DateTimeOffset.UtcNow,
            ConscienceDecisionId = decision.DecisionId,
            ThoughtLedgerRef = parsed.ThoughtLedgerRef!,
            EvidenceRefs = sourceApply.EvidenceRefs,
            Boundary = SourceApplyBoundary.None
        };

        await WriteJsonFileAsync(Path.Combine(runPath, "source-apply-execution-request.json"), request, cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(runPath, request.RunId, GovernedActionKind.SourceApplyExecutionRequested, request.SourceApplyExecutionRequestId, "Controlled source-apply execution was requested.", ["source-apply-execution-request.json"], cancellationToken).ConfigureAwait(false);

        var pre = await SourceSnapshotBuilder.CaptureAsync(request.RunId, request.SourceRepoPath, cancellationToken).ConfigureAwait(false);
        await WriteJsonFileAsync(Path.Combine(runPath, "source-apply-pre-source-snapshot.json"), pre.Snapshot, cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(runPath, request.RunId, GovernedActionKind.SourceApplyPreSnapshotCaptured, pre.Snapshot.SourceSnapshotId, "Pre-apply source repository snapshot was captured.", ["source-apply-pre-source-snapshot.json"], cancellationToken).ConfigureAwait(false);

        var gate = SourceApplyExecutionGate.Evaluate(request, sourceApply, verification, approval, readiness, dryRun, rollbackDraft, pre.Snapshot, decision, parsed.ThoughtLedgerRef);
        await WriteJsonFileAsync(Path.Combine(runPath, "source-apply-execution-gate-decision.json"), gate, cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(runPath, request.RunId, GovernedActionKind.SourceApplyExecutionGateEvaluated, gate.SourceApplyExecutionGateDecisionId, $"Controlled source-apply execution gate returned {gate.Decision}.", ["source-apply-execution-gate-decision.json"], cancellationToken).ConfigureAwait(false);

        if (gate.Decision != SourceApplyExecutionGateDecisionOutcome.AllowApplyToWorkingTree)
        {
            var blocked = BuildApplyReceipt(request, gate, null, pre.Snapshot, null, SourceApplyReceiptDecision.Blocked, gate.Reasons, null);
            await WriteApplyReceiptAsync(runPath, blocked, cancellationToken).ConfigureAwait(false);
            await RecordGovernanceEventAsync(runPath, request.RunId, GovernedActionKind.SourceApplyReceiptCreated, blocked.SourceApplyReceiptId, $"Controlled source-apply receipt returned {blocked.Decision}.", ["source-apply-receipt.json", "source-apply-receipt.md"], cancellationToken).ConfigureAwait(false);
            return Failure(output, error, parsed.Json, "source-apply apply", $"source apply execution gate blocked: {string.Join(", ", gate.Reasons)}");
        }

        var command = await SourceApplyCommandExecutor.ApplyAsync(request, Path.Combine(runPath, "source-apply-output"), cancellationToken).ConfigureAwait(false);
        await WriteJsonFileAsync(Path.Combine(runPath, "source-apply-command-result.json"), command, cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(runPath, request.RunId, GovernedActionKind.SourceApplyCommandExecuted, command.SourceApplyCommandResultId, $"Controlled source-apply command exited {command.ExitCode}.", ["source-apply-command-result.json", "source-apply-output/apply.combined.txt"], cancellationToken, command.Boundary).ConfigureAwait(false);

        var post = await SourceSnapshotBuilder.CaptureAsync(request.RunId, request.SourceRepoPath, cancellationToken).ConfigureAwait(false);
        await WriteJsonFileAsync(Path.Combine(runPath, "source-apply-post-source-snapshot.json"), post.Snapshot, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "source-apply-diff-after.diff"), post.DiffText, cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(runPath, request.RunId, GovernedActionKind.SourceApplyPostSnapshotCaptured, post.Snapshot.SourceSnapshotId, "Post-apply source repository snapshot was captured.", ["source-apply-post-source-snapshot.json", "source-apply-diff-after.diff"], cancellationToken).ConfigureAwait(false);

        var receipt = BuildApplyReceipt(
            request,
            gate,
            command,
            pre.Snapshot,
            post.Snapshot,
            command.SourceAppliedToWorkingTree ? SourceApplyReceiptDecision.AppliedToWorkingTree : SourceApplyReceiptDecision.Failed,
            command.SourceAppliedToWorkingTree ? [] : ["SourceApplyCommandFailed"],
            post.Snapshot.DiffSha256);
        await WriteApplyReceiptAsync(runPath, receipt, cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(runPath, request.RunId, GovernedActionKind.SourceApplyReceiptCreated, receipt.SourceApplyReceiptId, $"Controlled source-apply receipt returned {receipt.Decision}.", ["source-apply-receipt.json", "source-apply-receipt.md"], cancellationToken, receipt.Boundary).ConfigureAwait(false);

        if (parsed.Json)
            WriteJson(output, "source-apply apply", receipt.Decision == SourceApplyReceiptDecision.AppliedToWorkingTree ? "succeeded" : "failed", new { receipt, boundary = receipt.Boundary }, []);
        else
        {
            output.WriteLine($"Source apply decision: {receipt.Decision}");
            output.WriteLine("Boundary: source working tree may now contain uncommitted changes. No commit, push, PR, merge, release, deploy, or workflow continuation was performed.");
        }

        return receipt.Decision == SourceApplyReceiptDecision.AppliedToWorkingTree ? 0 : 1;
    }

    private static async Task<int> HandleRollbackAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseExecute(args, "rollback");
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "source-apply rollback", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!, parsed.RunsRootPath);
        var applyReceipt = await ReadRequiredArtifactAsync<SourceApplyReceipt>(runPath, "source-apply-receipt.json", cancellationToken).ConfigureAwait(false);
        var sourceApply = await ReadRequiredArtifactAsync<SourceApplyRequest>(runPath, "source-apply-request.json", cancellationToken).ConfigureAwait(false);
        var decision = await ReadJsonFileAsync<ConscienceDecision>(parsed.DecisionPath!, cancellationToken).ConfigureAwait(false);
        if (applyReceipt is null || sourceApply is null)
            return Failure(output, error, parsed.Json, "source-apply rollback", "source apply receipt/request artifacts are required before rollback.");

        var current = await SourceSnapshotBuilder.CaptureAsync(applyReceipt.RunId, sourceApply.SourceRepoPath, cancellationToken).ConfigureAwait(false);
        var request = new SourceRollbackRequest
        {
            SourceRollbackRequestId = $"source_rollback_req_{Guid.NewGuid():N}",
            RunId = applyReceipt.RunId,
            SourceApplyReceiptId = applyReceipt.SourceApplyReceiptId,
            SourceRepoPath = sourceApply.SourceRepoPath,
            BaseCommit = applyReceipt.BaseCommit,
            PatchPath = sourceApply.PatchPath,
            ExpectedPostApplyDiffSha256 = applyReceipt.PostApplyDiffSha256 ?? string.Empty,
            CurrentDiffSha256 = current.Snapshot.DiffSha256,
            ConscienceDecisionId = decision.DecisionId,
            ThoughtLedgerRef = parsed.ThoughtLedgerRef!,
            RequestedAtUtc = DateTimeOffset.UtcNow,
            Boundary = SourceApplyBoundary.None
        };

        await WriteJsonFileAsync(Path.Combine(runPath, "source-rollback-request.json"), request, cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(runPath, request.RunId, GovernedActionKind.SourceRollbackRequested, request.SourceRollbackRequestId, "Controlled source rollback was requested.", ["source-rollback-request.json"], cancellationToken).ConfigureAwait(false);

        var reverseCheck = await SourceRollbackCommandExecutor.ReverseCheckAsync(request, cancellationToken).ConfigureAwait(false);
        var gate = SourceRollbackGate.Evaluate(request, applyReceipt, current.Snapshot, decision, reverseCheck);
        await WriteJsonFileAsync(Path.Combine(runPath, "source-rollback-gate-decision.json"), gate, cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(runPath, request.RunId, GovernedActionKind.SourceRollbackGateEvaluated, gate.SourceRollbackGateDecisionId, $"Controlled source rollback gate returned {gate.Decision}.", ["source-rollback-gate-decision.json"], cancellationToken).ConfigureAwait(false);

        if (gate.Decision != SourceRollbackGateDecisionOutcome.AllowRollback)
        {
            var blocked = BuildRollbackReceipt(request, gate, null, SourceRollbackReceiptDecision.Blocked, gate.Reasons, null);
            await WriteRollbackReceiptAsync(runPath, blocked, cancellationToken).ConfigureAwait(false);
            await RecordGovernanceEventAsync(runPath, request.RunId, GovernedActionKind.SourceRollbackReceiptCreated, blocked.SourceRollbackReceiptId, $"Controlled source rollback receipt returned {blocked.Decision}.", ["source-rollback-receipt.json", "source-rollback-receipt.md"], cancellationToken).ConfigureAwait(false);
            return Failure(output, error, parsed.Json, "source-apply rollback", $"source rollback gate blocked: {string.Join(", ", gate.Reasons)}");
        }

        var command = await SourceRollbackCommandExecutor.RollbackAsync(request, Path.Combine(runPath, "source-rollback-output"), cancellationToken).ConfigureAwait(false);
        await WriteJsonFileAsync(Path.Combine(runPath, "source-rollback-command-result.json"), command, cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(runPath, request.RunId, GovernedActionKind.SourceRollbackCommandExecuted, command.SourceRollbackCommandResultId, $"Controlled source rollback command exited {command.ExitCode}.", ["source-rollback-command-result.json", "source-rollback-output/rollback.combined.txt"], cancellationToken, command.Boundary).ConfigureAwait(false);

        var after = await SourceSnapshotBuilder.CaptureAsync(request.RunId, request.SourceRepoPath, cancellationToken).ConfigureAwait(false);
        var receipt = BuildRollbackReceipt(
            request,
            gate,
            command,
            command.RolledBackWorkingTree ? SourceRollbackReceiptDecision.RolledBackWorkingTree : SourceRollbackReceiptDecision.Failed,
            command.RolledBackWorkingTree ? [] : ["SourceRollbackCommandFailed"],
            after.Snapshot.DiffSha256);
        await WriteRollbackReceiptAsync(runPath, receipt, cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(runPath, request.RunId, GovernedActionKind.SourceRollbackReceiptCreated, receipt.SourceRollbackReceiptId, $"Controlled source rollback receipt returned {receipt.Decision}.", ["source-rollback-receipt.json", "source-rollback-receipt.md"], cancellationToken, receipt.Boundary).ConfigureAwait(false);

        if (parsed.Json)
            WriteJson(output, "source-apply rollback", receipt.Decision == SourceRollbackReceiptDecision.RolledBackWorkingTree ? "succeeded" : "failed", new { receipt, boundary = receipt.Boundary }, []);
        else
        {
            output.WriteLine($"Source rollback decision: {receipt.Decision}");
            output.WriteLine("Boundary: rollback only mutates the uncommitted working tree. No commit, push, PR, merge, release, deploy, or workflow continuation was performed.");
        }

        return receipt.Decision == SourceRollbackReceiptDecision.RolledBackWorkingTree ? 0 : 1;
    }

    private static async Task<int> HandleAppliedStatusAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseStatus(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "source-apply applied-status", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!, parsed.RunsRootPath);
        var apply = await ReadRequiredArtifactAsync<SourceApplyReceipt>(runPath, "source-apply-receipt.json", cancellationToken).ConfigureAwait(false);
        var rollback = await ReadRequiredArtifactAsync<SourceRollbackReceipt>(runPath, "source-rollback-receipt.json", cancellationToken).ConfigureAwait(false);
        var applied = apply?.Decision == SourceApplyReceiptDecision.AppliedToWorkingTree && rollback?.Decision != SourceRollbackReceiptDecision.RolledBackWorkingTree;

        if (parsed.Json)
            WriteJson(output, "source-apply applied-status", "succeeded", new { runId = parsed.Run, applied, applyDecision = apply?.Decision.ToString(), rollbackDecision = rollback?.Decision.ToString(), boundary = SourceApplyBoundary.None }, []);
        else
        {
            output.WriteLine($"Source apply currently applied: {applied}");
            output.WriteLine("Boundary: applied-status is read-only and does not apply, rollback, commit, push, merge, release, or continue workflow.");
        }

        return 0;
    }

    private static SourceApplyReceipt BuildApplyReceipt(SourceApplyExecutionRequest request, SourceApplyExecutionGateDecision gate, SourceApplyCommandResult? command, SourceSnapshot? pre, SourceSnapshot? post, SourceApplyReceiptDecision decision, string[] reasons, string? postDiffSha) =>
        new()
        {
            SourceApplyReceiptId = $"source_apply_receipt_{Guid.NewGuid():N}",
            RunId = request.RunId,
            SourceApplyExecutionRequestId = request.SourceApplyExecutionRequestId,
            SourceApplyRequestId = request.SourceApplyRequestId,
            SourceApplyExecutionGateDecisionId = gate.SourceApplyExecutionGateDecisionId,
            SourceApplyCommandResultId = command?.SourceApplyCommandResultId,
            PreSourceSnapshotId = pre?.SourceSnapshotId,
            PostSourceSnapshotId = post?.SourceSnapshotId,
            Decision = decision,
            Reasons = reasons,
            BaseCommit = request.BaseCommit,
            PatchSha256 = request.PatchSha256,
            PostApplyDiffSha256 = postDiffSha,
            SourceRepoMutated = command?.SourceAppliedToWorkingTree == true,
            SourceAppliedToWorkingTree = command?.SourceAppliedToWorkingTree == true,
            GitCommitCreated = false,
            GitPushPerformed = false,
            PullRequestCreated = false,
            WorkflowContinued = false,
            ReleaseApproved = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = new SourceApplyBoundary { SourceRepoMutated = command?.SourceAppliedToWorkingTree == true, SourceApplied = command?.SourceAppliedToWorkingTree == true }
        };

    private static SourceRollbackReceipt BuildRollbackReceipt(SourceRollbackRequest request, SourceRollbackGateDecision gate, SourceRollbackCommandResult? command, SourceRollbackReceiptDecision decision, string[] reasons, string? postDiffSha) =>
        new()
        {
            SourceRollbackReceiptId = $"source_rollback_receipt_{Guid.NewGuid():N}",
            RunId = request.RunId,
            SourceRollbackRequestId = request.SourceRollbackRequestId,
            SourceApplyReceiptId = request.SourceApplyReceiptId,
            SourceRollbackGateDecisionId = gate.SourceRollbackGateDecisionId,
            SourceRollbackCommandResultId = command?.SourceRollbackCommandResultId,
            Decision = decision,
            Reasons = reasons,
            BaseCommit = request.BaseCommit,
            PreRollbackDiffSha256 = request.CurrentDiffSha256,
            PostRollbackDiffSha256 = postDiffSha,
            SourceRepoMutated = command?.RolledBackWorkingTree == true,
            RolledBackWorkingTree = command?.RolledBackWorkingTree == true,
            GitCommitCreated = false,
            GitPushPerformed = false,
            PullRequestCreated = false,
            WorkflowContinued = false,
            ReleaseApproved = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = new SourceApplyBoundary { SourceRepoMutated = command?.RolledBackWorkingTree == true, RollbackExecuted = command?.RolledBackWorkingTree == true }
        };

    private static async Task WriteApplyReceiptAsync(string runPath, SourceApplyReceipt receipt, CancellationToken cancellationToken)
    {
        await WriteJsonFileAsync(Path.Combine(runPath, "source-apply-receipt.json"), receipt, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "source-apply-receipt.md"), RenderApplyReceipt(receipt), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteRollbackReceiptAsync(string runPath, SourceRollbackReceipt receipt, CancellationToken cancellationToken)
    {
        await WriteJsonFileAsync(Path.Combine(runPath, "source-rollback-receipt.json"), receipt, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "source-rollback-receipt.md"), RenderRollbackReceipt(receipt), cancellationToken).ConfigureAwait(false);
    }

    private static string RenderApplyReceipt(SourceApplyReceipt receipt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Source Apply Receipt");
        builder.AppendLine();
        builder.AppendLine($"Run: `{receipt.RunId}`");
        builder.AppendLine($"Decision: `{receipt.Decision}`");
        builder.AppendLine($"Source applied to working tree: `{receipt.SourceAppliedToWorkingTree}`");
        builder.AppendLine($"Git commit created: `{receipt.GitCommitCreated}`");
        builder.AppendLine($"Git push performed: `{receipt.GitPushPerformed}`");
        builder.AppendLine($"Pull request created: `{receipt.PullRequestCreated}`");
        builder.AppendLine();
        builder.AppendLine("Boundary: this receipt records uncommitted working-tree source mutation only. It is not commit, push, PR creation, merge, release approval, deployment approval, workflow continuation, policy satisfaction, or memory promotion.");
        foreach (var reason in receipt.Reasons)
            builder.AppendLine($"- `{reason}`");
        return builder.ToString();
    }

    private static string RenderRollbackReceipt(SourceRollbackReceipt receipt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Source Rollback Receipt");
        builder.AppendLine();
        builder.AppendLine($"Run: `{receipt.RunId}`");
        builder.AppendLine($"Decision: `{receipt.Decision}`");
        builder.AppendLine($"Rolled back working tree: `{receipt.RolledBackWorkingTree}`");
        builder.AppendLine($"Git commit created: `{receipt.GitCommitCreated}`");
        builder.AppendLine($"Git push performed: `{receipt.GitPushPerformed}`");
        builder.AppendLine($"Pull request created: `{receipt.PullRequestCreated}`");
        builder.AppendLine();
        builder.AppendLine("Boundary: this receipt records controlled working-tree rollback only. It is not cleanup certification, commit, push, PR creation, merge, release approval, deployment approval, workflow continuation, policy satisfaction, or memory promotion.");
        foreach (var reason in receipt.Reasons)
            builder.AppendLine($"- `{reason}`");
        return builder.ToString();
    }

    private static async Task<T?> ReadRequiredArtifactAsync<T>(string runPath, string artifactName, CancellationToken cancellationToken) where T : class
    {
        var path = Path.Combine(runPath, artifactName);
        return File.Exists(path) ? await ReadJsonFileAsync<T>(path, cancellationToken).ConfigureAwait(false) : null;
    }

    private static ParsedTemplateCommand ParseTemplate(string[] args, string commandName)
    {
        string? run = null;
        string? runsRoot = null;
        string? outPath = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run": if (!TryRead(args, ref index, out run)) return ParsedTemplateCommand.Fail(json, "--run requires a value."); break;
                case "--runs-root": if (!TryRead(args, ref index, out runsRoot)) return ParsedTemplateCommand.Fail(json, "--runs-root requires a value."); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedTemplateCommand.Fail(json, "--out requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedTemplateCommand.Fail(json, $"unsupported source-apply {commandName} option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(run)) return ParsedTemplateCommand.Fail(json, "--run is required.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedTemplateCommand.Fail(json, "--out is required.");
        return new(run, runsRoot, outPath, json, null);
    }

    private static ParsedExecuteCommand ParseExecute(string[] args, string commandName)
    {
        string? run = null;
        string? runsRoot = null;
        string? decision = null;
        string? thoughtLedger = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run": if (!TryRead(args, ref index, out run)) return ParsedExecuteCommand.Fail(json, "--run requires a value."); break;
                case "--runs-root": if (!TryRead(args, ref index, out runsRoot)) return ParsedExecuteCommand.Fail(json, "--runs-root requires a value."); break;
                case "--decision": if (!TryRead(args, ref index, out decision)) return ParsedExecuteCommand.Fail(json, "--decision requires a value."); break;
                case "--thought-ledger-ref": if (!TryRead(args, ref index, out thoughtLedger)) return ParsedExecuteCommand.Fail(json, "--thought-ledger-ref requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedExecuteCommand.Fail(json, $"unsupported source-apply {commandName} option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(run)) return ParsedExecuteCommand.Fail(json, "--run is required.");
        if (string.IsNullOrWhiteSpace(decision)) return ParsedExecuteCommand.Fail(json, "--decision is required.");
        if (string.IsNullOrWhiteSpace(thoughtLedger)) return ParsedExecuteCommand.Fail(json, "--thought-ledger-ref is required.");
        return new(run, runsRoot, decision, thoughtLedger, json, null);
    }

    private sealed record ParsedTemplateCommand(string? Run, string? RunsRootPath, string? OutPath, bool Json, string? Error)
    {
        public static ParsedTemplateCommand Fail(bool json, string error) => new(null, null, null, json, error);
    }

    private sealed record ParsedExecuteCommand(string? Run, string? RunsRootPath, string? DecisionPath, string? ThoughtLedgerRef, bool Json, string? Error)
    {
        public static ParsedExecuteCommand Fail(bool json, string error) => new(null, null, null, null, json, error);
    }
}
