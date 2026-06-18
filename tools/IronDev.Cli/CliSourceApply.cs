using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;
using IronDev.Core.SourceApply;
using SourceApplyDryRunResult = IronDev.Core.SourceApply.SourceApplyDryRunResult;
using SourceApplyRequest = IronDev.Core.SourceApply.SourceApplyRequest;

namespace IronDev.Cli;

public static partial class IronDevCliSourceApply
{
    private const string DefaultRunsFolderName = "irondev-patch-runs";
    private const string GovernanceEventsArtifactName = "governance-events.jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions JsonLineOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool IsSourceApplyCommand(string[] args) =>
        args.Length >= 1 && string.Equals(args[0], "source-apply", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "source-apply requires a subcommand: approval-template, prepare, status, decision-template, apply, rollback-template, rollback, or applied-status.");

        return args[1].ToLowerInvariant() switch
        {
            "approval-template" => await HandleApprovalTemplateAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "prepare" => await HandlePrepareAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "status" => await HandleStatusAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "decision-template" => await HandleDecisionTemplateAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "apply" => await HandleApplyAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "rollback-template" => await HandleRollbackTemplateAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "rollback" => await HandleRollbackAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "applied-status" => await HandleAppliedStatusAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "commit" or "push" or "pr" or "merge" or "release" or "deploy" => Usage(error, $"source-apply {args[1]} is intentionally unsupported in Block AG."),
            _ => Usage(error, $"unsupported source-apply subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandleApprovalTemplateAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseApprovalTemplate(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "source-apply approval-template", parsed.Error);

        var loaded = LoadRun(parsed.Run!, parsed.RunsRootPath);
        if (loaded.Run is null)
            return Failure(output, error, parsed.Json, "source-apply approval-template", $"run metadata was not found: {Path.Combine(loaded.RunPath, "run.json")}");

        var verification = PatchArtifactVerifier.Verify(ToMetadata(loaded.Run, loaded.RunPath));
        var request = BuildRequest(loaded.Run, loaded.RunPath, verification);
        var template = new SourceApplyApprovalEvidence
        {
            ApprovalEvidenceId = $"approval_evidence_{Guid.NewGuid():N}",
            RunId = request.RunId,
            SourceRepoIdentity = request.SourceRepoIdentity,
            BaseCommit = request.BaseCommit,
            PatchSha256 = request.PatchSha256,
            ApprovedChangedFiles = request.ChangedFiles,
            ApprovedBy = string.Empty,
            ApprovedAtUtc = DateTimeOffset.UtcNow,
            ApprovalText = "HUMAN_REVIEW_REQUIRED: replace this text with explicit human approval for dry-run readiness evaluation only.",
            HumanReviewRequired = true,
            Boundary = SourceApplyBoundary.None
        };

        await WriteJsonFileAsync(Path.Combine(loaded.RunPath, "source-apply-approval-template.json"), template, cancellationToken).ConfigureAwait(false);
        await WriteJsonFileAsync(parsed.OutPath!, template, cancellationToken).ConfigureAwait(false);

        if (parsed.Json)
            WriteJson(output, "source-apply approval-template", "succeeded", new { template.ApprovalEvidenceId, template.RunId, parsed.OutPath, verification.Decision, boundary = SourceApplyBoundary.None }, []);
        else
        {
            output.WriteLine($"Approval evidence template: {Path.GetFullPath(parsed.OutPath!)}");
            output.WriteLine("Boundary: this file is a template only; IronDev did not approve source apply.");
        }

        return 0;
    }

    private static async Task<int> HandlePrepareAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePrepare(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "source-apply prepare", parsed.Error);

        var loaded = LoadRun(parsed.Run!, parsed.RunsRootPath);
        if (loaded.Run is null)
            return Failure(output, error, parsed.Json, "source-apply prepare", $"run metadata was not found: {Path.Combine(loaded.RunPath, "run.json")}");

        var metadata = ToMetadata(loaded.Run, loaded.RunPath);
        var verification = PatchArtifactVerifier.Verify(metadata);
        var request = BuildRequest(loaded.Run, loaded.RunPath, verification);
        await WriteJsonFileAsync(Path.Combine(loaded.RunPath, "source-apply-request.json"), request, cancellationToken).ConfigureAwait(false);
        await WriteJsonFileAsync(Path.Combine(loaded.RunPath, "patch-artifact-verification.json"), verification, cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(loaded.RunPath, request.RunId, GovernedActionKind.SourceApplyRequestCreated, request.SourceApplyRequestId, "Source-apply request evidence was created.", ["source-apply-request.json"], cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(loaded.RunPath, request.RunId, GovernedActionKind.PatchArtifactVerified, verification.PatchArtifactVerificationId, $"Patch artifact verification returned {verification.Decision}.", ["patch-artifact-verification.json"], cancellationToken).ConfigureAwait(false);

        SourceApplyApprovalEvidence? approval = null;
        if (!string.IsNullOrWhiteSpace(parsed.ApprovalPath) && File.Exists(parsed.ApprovalPath))
        {
            approval = await ReadJsonFileAsync<SourceApplyApprovalEvidence>(parsed.ApprovalPath, cancellationToken).ConfigureAwait(false);
            await WriteJsonFileAsync(Path.Combine(loaded.RunPath, "source-apply-approval-evidence.json"), approval, cancellationToken).ConfigureAwait(false);
            await RecordGovernanceEventAsync(loaded.RunPath, request.RunId, GovernedActionKind.SourceApplyApprovalEvidenceRead, approval.ApprovalEvidenceId, "Source-apply approval evidence was read for dry-run readiness evaluation.", ["source-apply-approval-evidence.json"], cancellationToken).ConfigureAwait(false);
        }

        var rollbackDraft = verification.PatchExists
            ? await RollbackPlanDraftBuilder.WriteDraftAsync(request, loaded.RunPath, cancellationToken).ConfigureAwait(false)
            : null;
        if (rollbackDraft is not null)
        {
            await WriteJsonFileAsync(Path.Combine(loaded.RunPath, "rollback-plan-draft.json"), rollbackDraft, cancellationToken).ConfigureAwait(false);
            await RecordGovernanceEventAsync(loaded.RunPath, request.RunId, GovernedActionKind.RollbackPlanDrafted, rollbackDraft.RollbackPlanDraftId, "Rollback plan draft was created for human review.", ["rollback-plan-draft.md", "rollback-plan-draft.json", "reverse-patch.diff"], cancellationToken).ConfigureAwait(false);
        }

        var sourceRepoExists = Directory.Exists(request.SourceRepoPath);
        var beforeStatus = sourceRepoExists ? await ReadGitValueAsync(request.SourceRepoPath, ["status", "--porcelain=v1"], cancellationToken).ConfigureAwait(false) : string.Empty;
        var gate = SourceApplyGate.Evaluate(
            request,
            verification,
            approval,
            sourceRepoExists,
            sourceRepoClean: sourceRepoExists && string.IsNullOrWhiteSpace(beforeStatus),
            testEvidencePresent: File.Exists(Path.Combine(loaded.RunPath, "test-output-summary.md")),
            toolEvidencePresent: File.Exists(Path.Combine(loaded.RunPath, "tool-results.jsonl")),
            governanceEvidencePresent: File.Exists(Path.Combine(loaded.RunPath, GovernanceEventsArtifactName)),
            rollbackPlanDraftPresent: rollbackDraft is not null);

        await WriteJsonFileAsync(Path.Combine(loaded.RunPath, "source-apply-gate-decision.json"), gate, cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(loaded.RunPath, request.RunId, GovernedActionKind.SourceApplyGateEvaluated, gate.SourceApplyGateDecisionId, $"Source-apply gate returned {gate.Decision}.", ["source-apply-gate-decision.json"], cancellationToken).ConfigureAwait(false);

        SourceApplyDryRunPlan? dryRunPlan = null;
        SourceApplyDryRunResult? dryRunResult = null;
        if (gate.Decision == SourceApplyGateDecisionOutcome.AllowDryRun)
        {
            var applyRoot = Path.GetFullPath(parsed.ApplyRootPath ?? Path.Combine(loaded.RunPath, "apply-rehearsal"));
            dryRunPlan = new SourceApplyDryRunPlan
            {
                SourceApplyDryRunPlanId = $"source_apply_dry_run_plan_{Guid.NewGuid():N}",
                RunId = request.RunId,
                SourceRepoPath = request.SourceRepoPath,
                ApplyRehearsalWorkspacePath = Path.Combine(applyRoot, request.RunId, "workspace"),
                PatchPath = request.PatchPath,
                PatchSha256 = request.PatchSha256,
                BaseCommit = request.BaseCommit,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Boundary = SourceApplyBoundary.None
            };
            await WriteJsonFileAsync(Path.Combine(loaded.RunPath, "source-apply-dry-run-plan.json"), dryRunPlan, cancellationToken).ConfigureAwait(false);
            await RecordGovernanceEventAsync(loaded.RunPath, request.RunId, GovernedActionKind.SourceApplyDryRunStarted, dryRunPlan.SourceApplyDryRunPlanId, "Source-apply dry-run rehearsal started in disposable apply workspace.", ["source-apply-dry-run-plan.json"], cancellationToken).ConfigureAwait(false);

            var outputDirectory = Path.Combine(loaded.RunPath, "source-apply-output");
            dryRunResult = await SourceApplyDryRun.RunAsync(dryRunPlan, outputDirectory, cancellationToken).ConfigureAwait(false);
            var afterStatus = sourceRepoExists ? await ReadGitValueAsync(request.SourceRepoPath, ["status", "--porcelain=v1"], cancellationToken).ConfigureAwait(false) : string.Empty;
            dryRunResult = dryRunResult with { SourceRepoMutated = !string.Equals(beforeStatus, afterStatus, StringComparison.Ordinal) };
            await WriteJsonFileAsync(Path.Combine(loaded.RunPath, "source-apply-dry-run-result.json"), dryRunResult, cancellationToken).ConfigureAwait(false);
            await RecordGovernanceEventAsync(loaded.RunPath, request.RunId, GovernedActionKind.SourceApplyDryRunCompleted, dryRunResult.SourceApplyDryRunResultId, $"Source-apply dry-run rehearsal completed with exit code {dryRunResult.ExitCode}.", ["source-apply-dry-run-result.json", "source-apply-output/dry-run.combined.txt"], cancellationToken, dryRunResult.Boundary).ConfigureAwait(false);
        }

        var report = BuildReadinessReport(request, verification, gate, dryRunResult, rollbackDraft);
        await WriteJsonFileAsync(Path.Combine(loaded.RunPath, "source-apply-readiness.json"), report, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(loaded.RunPath, "source-apply-readiness-report.md"), RenderReadinessReport(report, verification, gate, dryRunResult, rollbackDraft), cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(loaded.RunPath, request.RunId, GovernedActionKind.SourceApplyReadinessReportCreated, report.SourceApplyReadinessReportId, $"Source-apply readiness report returned {report.Readiness}.", ["source-apply-readiness.json", "source-apply-readiness-report.md"], cancellationToken).ConfigureAwait(false);

        if (parsed.Json)
            WriteJson(output, "source-apply prepare", report.Readiness == SourceApplyReadiness.ReadyForFutureControlledApply ? "succeeded" : "blocked", new { report, gate, dryRunResult, rollbackDraft, boundary = SourceApplyBoundary.None }, []);
        else
        {
            output.WriteLine($"Source apply readiness: {report.Readiness}");
            if (report.Reasons.Length > 0)
                output.WriteLine($"Reasons: {string.Join(", ", report.Reasons)}");
            output.WriteLine($"Report: {Path.Combine(loaded.RunPath, "source-apply-readiness-report.md")}");
            output.WriteLine("Boundary: no source apply was performed; dry-run applies only inside disposable rehearsal workspace.");
        }

        return report.Readiness == SourceApplyReadiness.ReadyForFutureControlledApply ? 0 : 1;
    }

    private static async Task<int> HandleStatusAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseStatus(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "source-apply status", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!, parsed.RunsRootPath);
        var readinessPath = Path.Combine(runPath, "source-apply-readiness.json");
        if (!File.Exists(readinessPath))
            return Failure(output, error, parsed.Json, "source-apply status", $"source-apply readiness artifact was not found: {readinessPath}");

        var report = await ReadJsonFileAsync<SourceApplyReadinessReport>(readinessPath, cancellationToken).ConfigureAwait(false);
        if (parsed.Json)
            WriteJson(output, "source-apply status", "succeeded", new { report, boundary = SourceApplyBoundary.None }, []);
        else
        {
            output.WriteLine($"Source apply readiness: {report.Readiness}");
            output.WriteLine($"Run: {report.RunId}");
            if (report.Reasons.Length > 0)
                output.WriteLine($"Reasons: {string.Join(", ", report.Reasons)}");
            output.WriteLine("Boundary: status is read-only; no source apply, approval, merge, release, or rollback was performed.");
        }

        return 0;
    }

    private static SourceApplyRequest BuildRequest(PatchRunForSourceApply run, string runPath, PatchArtifactVerificationResult verification)
    {
        var changedFiles = run.ChangedFiles.Length > 0 ? run.ChangedFiles : ReadChangedFiles(runPath);
        return new SourceApplyRequest
        {
            SourceApplyRequestId = $"source_apply_req_{Guid.NewGuid():N}",
            RunId = run.RunId,
            SourceRepoPath = run.SourceRepoPath,
            SourceRepoIdentity = run.SourceRepoIdentity,
            BaseBranch = run.BaseBranch,
            BaseCommit = run.BaseCommit,
            PatchPath = verification.PatchPath,
            PatchSha256 = verification.PatchSha256,
            ChangedFiles = changedFiles,
            RequestedBy = "IronDevCli",
            RequestedAtUtc = DateTimeOffset.UtcNow,
            EvidenceRefs =
            [
                Evidence("run.json", "PatchRunMetadata", "run.json", "Patch run metadata."),
                Evidence("patch.diff", "PatchArtifact", "patch.diff", "Patch artifact for future source apply review.", verification.PatchSha256),
                Evidence("changed-files.txt", "ChangedFiles", "changed-files.txt", "Changed files evidence."),
                Evidence("manual-apply-instructions.md", "ManualApplyInstructions", "manual-apply-instructions.md", "Manual apply instructions evidence.")
            ],
            Boundary = SourceApplyBoundary.None
        };
    }

    private static SourceApplyReadinessReport BuildReadinessReport(SourceApplyRequest request, PatchArtifactVerificationResult verification, SourceApplyGateDecision gate, SourceApplyDryRunResult? dryRunResult, RollbackPlanDraft? rollbackDraft)
    {
        var reasons = new List<string>();
        reasons.AddRange(verification.Reasons);
        reasons.AddRange(gate.Reasons);
        if (gate.Decision == SourceApplyGateDecisionOutcome.AllowDryRun && dryRunResult is null)
            reasons.Add("DryRunMissing");
        if (dryRunResult is not null && !dryRunResult.PatchAppliedInRehearsalWorkspace)
            reasons.Add("DryRunFailed");
        if (dryRunResult is not null && dryRunResult.SourceRepoMutated)
            reasons.Add("SourceRepoMutated");
        if (rollbackDraft is null)
            reasons.Add("RollbackPlanMissing");

        var ready = reasons.Count == 0 && dryRunResult?.PatchAppliedInRehearsalWorkspace == true;
        return new SourceApplyReadinessReport
        {
            SourceApplyReadinessReportId = $"source_apply_readiness_{Guid.NewGuid():N}",
            RunId = request.RunId,
            SourceApplyRequestId = request.SourceApplyRequestId,
            PatchArtifactVerificationId = verification.PatchArtifactVerificationId,
            SourceApplyGateDecisionId = gate.SourceApplyGateDecisionId,
            SourceApplyDryRunResultId = dryRunResult?.SourceApplyDryRunResultId,
            RollbackPlanDraftId = rollbackDraft?.RollbackPlanDraftId,
            Readiness = ready ? SourceApplyReadiness.ReadyForFutureControlledApply : SourceApplyReadiness.Blocked,
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            EvidenceRefs = request.EvidenceRefs,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = SourceApplyBoundary.None
        };
    }

    private static string RenderReadinessReport(SourceApplyReadinessReport report, PatchArtifactVerificationResult verification, SourceApplyGateDecision gate, SourceApplyDryRunResult? dryRunResult, RollbackPlanDraft? rollbackDraft)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Source Apply Readiness Report");
        builder.AppendLine();
        builder.AppendLine($"Run: `{report.RunId}`");
        builder.AppendLine($"Readiness: `{report.Readiness}`");
        builder.AppendLine($"Patch verification: `{verification.Decision}`");
        builder.AppendLine($"Gate: `{gate.Decision}`");
        builder.AppendLine($"Dry-run rehearsal applied patch: `{dryRunResult?.PatchAppliedInRehearsalWorkspace ?? false}`");
        builder.AppendLine($"Rollback draft created: `{rollbackDraft is not null}`");
        builder.AppendLine();
        builder.AppendLine("## Boundary");
        builder.AppendLine();
        builder.AppendLine("This block creates the controlled source-apply foundation.");
        builder.AppendLine("It does not apply source.");
        builder.AppendLine("It does not mutate the source repository.");
        builder.AppendLine("It does not execute rollback.");
        builder.AppendLine("It does not create git commits.");
        builder.AppendLine("It does not push.");
        builder.AppendLine("It does not create pull requests.");
        builder.AppendLine("It does not merge.");
        builder.AppendLine("It does not approve release.");
        builder.AppendLine("It does not approve deployment.");
        builder.AppendLine("It does not satisfy policy.");
        builder.AppendLine("It does not continue workflow.");
        builder.AppendLine("It does not promote memory.");
        builder.AppendLine("It does not dispatch agents.");
        builder.AppendLine("It does not add API, SQL, UI, scheduler, worker, or autonomous runtime behavior.");
        builder.AppendLine("Dry-run apply occurs only in a disposable apply rehearsal workspace.");
        builder.AppendLine("A successful dry-run is evidence only.");
        builder.AppendLine("A successful dry-run is not source apply.");
        builder.AppendLine("A successful dry-run is not approval.");
        builder.AppendLine("A successful dry-run is not release readiness.");
        builder.AppendLine("A successful dry-run is not merge readiness.");
        builder.AppendLine();
        if (report.Reasons.Length > 0)
        {
            builder.AppendLine("## Reasons");
            builder.AppendLine();
            foreach (var reason in report.Reasons)
                builder.AppendLine($"- `{reason}`");
            builder.AppendLine();
        }

        builder.AppendLine("## Next human step");
        builder.AppendLine();
        builder.AppendLine("A human may inspect this evidence and decide outside this block whether a future controlled source-apply path should be requested.");
        return builder.ToString();
    }

    private static SourceApplyRunMetadata ToMetadata(PatchRunForSourceApply run, string runPath) =>
        new()
        {
            RunId = run.RunId,
            RunPath = runPath,
            SourceRepoPath = run.SourceRepoPath,
            SourceRepoIdentity = run.SourceRepoIdentity,
            BaseBranch = run.BaseBranch,
            BaseCommit = run.BaseCommit,
            PatchSha256 = run.PatchSha256,
            ChangedFiles = run.ChangedFiles.Length > 0 ? run.ChangedFiles : ReadChangedFiles(runPath)
        };

    private static string[] ReadChangedFiles(string runPath)
    {
        var changedFilesPath = Path.Combine(runPath, "changed-files.txt");
        return File.Exists(changedFilesPath)
            ? File.ReadAllLines(changedFilesPath).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).ToArray()
            : [];
    }

    private static SourceApplyEvidenceRef Evidence(string refId, string kind, string path, string summary, string? sha = null) =>
        new() { RefId = refId, EvidenceKind = kind, Path = path, SafeSummary = summary, Sha256 = sha };

    private static (PatchRunForSourceApply? Run, string RunPath) LoadRun(string run, string? runsRoot)
    {
        var runPath = ResolveRunPath(run, runsRoot);
        var path = Path.Combine(runPath, "run.json");
        if (!File.Exists(path))
            return (null, runPath);

        var loaded = JsonSerializer.Deserialize<PatchRunForSourceApply>(File.ReadAllText(path), JsonOptions);
        return (loaded, runPath);
    }

    private static async Task RecordGovernanceEventAsync(string runPath, string runId, GovernedActionKind actionKind, string subjectId, string message, string[] evidenceRefs, CancellationToken cancellationToken, SourceApplyBoundary? sourceBoundary = null)
    {
        var boundary = sourceBoundary is null
            ? GovernedActionBoundary.None
            : new GovernedActionBoundary
            {
                SourceRepoMutated = sourceBoundary.SourceRepoMutated,
                SourceApplied = sourceBoundary.SourceApplied,
                GitCommitCreated = sourceBoundary.GitCommitCreated,
                GitPushPerformed = sourceBoundary.GitPushPerformed,
                PullRequestCreated = sourceBoundary.PullRequestCreated,
                ApprovalGranted = sourceBoundary.ApprovalGranted,
                PolicySatisfied = sourceBoundary.PolicySatisfied,
                ReleaseApproved = sourceBoundary.ReleaseApproved,
                WorkflowContinued = sourceBoundary.WorkflowContinued,
                MemoryPromoted = sourceBoundary.MemoryPromoted,
                AgentDispatched = sourceBoundary.AgentDispatched,
                ModelCalled = sourceBoundary.ModelCalled
            };

        var action = GovernedAction.Create(actionKind, "SourceApplyFoundation", subjectId, "IronDevCli", "IronDev.Cli.source-apply", runId, evidenceRefs) with { Boundary = boundary };
        var evt = RunScopedGovernanceEvent.FromAction(action, "ActionRecorded", message);
        await AppendJsonLineAsync(Path.Combine(runPath, GovernanceEventsArtifactName), evt, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadGitValueAsync(string workingDirectory, string[] arguments, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync("git", arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.Stdout : string.Empty;
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);
        if (process is null)
            return new ProcessResult(-1, string.Empty, $"could not start process: {fileName}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private static ParsedApprovalTemplateCommand ParseApprovalTemplate(string[] args)
    {
        string? run = null;
        string? runsRoot = null;
        string? outPath = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run": if (!TryRead(args, ref index, out run)) return ParsedApprovalTemplateCommand.Fail(json, "--run requires a value."); break;
                case "--runs-root": if (!TryRead(args, ref index, out runsRoot)) return ParsedApprovalTemplateCommand.Fail(json, "--runs-root requires a value."); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedApprovalTemplateCommand.Fail(json, "--out requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedApprovalTemplateCommand.Fail(json, $"unsupported source-apply approval-template option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(run)) return ParsedApprovalTemplateCommand.Fail(json, "--run is required.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedApprovalTemplateCommand.Fail(json, "--out is required.");
        return new(run, runsRoot, outPath, json, null);
    }

    private static ParsedPrepareCommand ParsePrepare(string[] args)
    {
        string? run = null;
        string? runsRoot = null;
        string? approval = null;
        string? applyRoot = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run": if (!TryRead(args, ref index, out run)) return ParsedPrepareCommand.Fail(json, "--run requires a value."); break;
                case "--runs-root": if (!TryRead(args, ref index, out runsRoot)) return ParsedPrepareCommand.Fail(json, "--runs-root requires a value."); break;
                case "--approval": if (!TryRead(args, ref index, out approval)) return ParsedPrepareCommand.Fail(json, "--approval requires a value."); break;
                case "--apply-root": if (!TryRead(args, ref index, out applyRoot)) return ParsedPrepareCommand.Fail(json, "--apply-root requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedPrepareCommand.Fail(json, $"unsupported source-apply prepare option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(run) ? ParsedPrepareCommand.Fail(json, "--run is required.") : new(run, runsRoot, approval, applyRoot, json, null);
    }

    private static ParsedStatusCommand ParseStatus(string[] args)
    {
        string? run = null;
        string? runsRoot = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run": if (!TryRead(args, ref index, out run)) return ParsedStatusCommand.Fail(json, "--run requires a value."); break;
                case "--runs-root": if (!TryRead(args, ref index, out runsRoot)) return ParsedStatusCommand.Fail(json, "--runs-root requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedStatusCommand.Fail(json, $"unsupported source-apply status option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(run) ? ParsedStatusCommand.Fail(json, "--run is required.") : new(run, runsRoot, json, null);
    }

    private static bool TryRead(string[] args, ref int index, out string? value)
    {
        value = null;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
            return false;
        value = args[++index];
        return true;
    }

    private static string ResolveRunPath(string run, string? runsRootPath)
    {
        var candidate = Path.GetFullPath(run);
        if (Directory.Exists(candidate) || File.Exists(Path.Combine(candidate, "run.json")) || Path.IsPathRooted(run))
            return candidate;
        return Path.Combine(Path.GetFullPath(runsRootPath ?? DefaultRunsRoot()), run.Trim());
    }

    private static string DefaultRunsRoot() => Path.Combine(Path.GetTempPath(), DefaultRunsFolderName);

    private static async Task WriteJsonFileAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory());
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadJsonFileAsync<T>(string path, CancellationToken cancellationToken) =>
        JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), JsonOptions) ??
        throw new InvalidOperationException($"JSON file did not contain {typeof(T).Name}: {path}");

    private static async Task AppendJsonLineAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory());
        await File.AppendAllTextAsync(path, JsonSerializer.Serialize(value, JsonLineOptions) + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    private static void WriteJson(TextWriter output, string command, string status, object? data, string[] errors) =>
        output.WriteLine(JsonSerializer.Serialize(new { ok = errors.Length == 0, command, status, data, errors }, JsonOptions));

    private static int Failure(TextWriter output, TextWriter error, bool json, string command, string message)
    {
        if (json)
            WriteJson(output, command, "failed", null, [message]);
        else
            error.WriteLine(message);
        return 1;
    }

    private static int Usage(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine("Usage:");
        error.WriteLine("  irondev source-apply approval-template --run <run-id-or-path> --out <approval.json> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev source-apply prepare --run <run-id-or-path> [--approval <approval.json>] [--apply-root <path>] [--runs-root <path>] [--json]");
        error.WriteLine("  irondev source-apply status --run <run-id-or-path> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev source-apply decision-template --run <run-id-or-path> --out <decision.json> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev source-apply apply --run <run-id-or-path> --decision <decision.json> --thought-ledger-ref <ref> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev source-apply rollback-template --run <run-id-or-path> --out <decision.json> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev source-apply rollback --run <run-id-or-path> --decision <decision.json> --thought-ledger-ref <ref> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev source-apply applied-status --run <run-id-or-path> [--runs-root <path>] [--json]");
        return 2;
    }

    private sealed record ParsedApprovalTemplateCommand(string? Run, string? RunsRootPath, string? OutPath, bool Json, string? Error)
    {
        public static ParsedApprovalTemplateCommand Fail(bool json, string error) => new(null, null, null, json, error);
    }

    private sealed record ParsedPrepareCommand(string? Run, string? RunsRootPath, string? ApprovalPath, string? ApplyRootPath, bool Json, string? Error)
    {
        public static ParsedPrepareCommand Fail(bool json, string error) => new(null, null, null, null, json, error);
    }

    private sealed record ParsedStatusCommand(string? Run, string? RunsRootPath, bool Json, string? Error)
    {
        public static ParsedStatusCommand Fail(bool json, string error) => new(null, null, json, error);
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

    private sealed class PatchRunForSourceApply
    {
        public string RunId { get; set; } = string.Empty;
        public string SourceRepoPath { get; set; } = string.Empty;
        public string SourceRepoIdentity { get; set; } = string.Empty;
        public string BaseBranch { get; set; } = string.Empty;
        public string BaseCommit { get; set; } = string.Empty;
        public string? PatchSha256 { get; set; }
        public string[] ChangedFiles { get; set; } = [];
    }
}
