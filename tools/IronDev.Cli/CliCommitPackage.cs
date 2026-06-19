using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

internal static class IronDevCliCommitPackage
{
    private const string DefaultRunsFolderName = "irondev-patch-runs";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "stage",
        "commit",
        "push",
        "pr",
        "pull-request",
        "create-pr",
        "merge",
        "release",
        "deploy",
        "continue",
        "continue-workflow",
        "approve",
        "execute",
        "apply",
        "rollback",
        "promote-memory"
    ];

    private static readonly string[] StatusArtifacts =
    [
        "commit-package-request.json",
        "commit-package-request.md",
        "commit-file-manifest.json",
        "commit-file-manifest.md",
        "commit-staging-plan.json",
        "commit-staging-plan.md",
        "commit-evidence-bundle.json",
        "commit-evidence-bundle.md",
        "commit-message-proposal.json",
        "commit-message-proposal.md",
        "commit-readiness-review.json",
        "commit-readiness-review.md",
        "commit-package-risk-report.json",
        "commit-package-risk-report.md",
        "commit-package-boundary-report.json",
        "commit-package-boundary-report.md",
        "commit-package-bypass-report.json",
        "commit-package-bypass-report.md",
        "governance-events.jsonl"
    ];

    public static bool IsCommitPackageCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "commit-package", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "commit-package requires a subcommand: request, manifest, evidence, message, review, or status.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"commit-package {args[1]} is intentionally unsupported; Block AL prepares evidence only.");

        return subcommand switch
        {
            "request" => await HandleRequestAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "manifest" => await HandleManifestAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "evidence" => await HandleEvidenceAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "message" => await HandleMessageAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "review" => await HandleReviewAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "status" => HandleStatus(args, output, error),
            _ => Usage(error, $"unsupported commit-package subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandleRequestAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseSourceRepoCommand(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "commit-package request", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var sourceRepoPath = Path.GetFullPath(parsed.SourceRepo!);
        var repoIdentity = await ReadGitValueAsync(sourceRepoPath, ["rev-parse", "--show-toplevel"], cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(repoIdentity))
            return Failure(output, error, parsed.Json, "commit-package request", "--source-repo must point at a git repository.");

        Directory.CreateDirectory(runPath);
        var runInfo = ReadRunInfo(runPath);
        var currentHead = await ReadGitValueAsync(sourceRepoPath, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
        var postApplyDiffHash = await ComputeRepositoryStateHashAsync(sourceRepoPath, cancellationToken).ConfigureAwait(false);
        var request = CommitPackageRequestWriter.Create(new CommitPackageRequestInput
        {
            RunId = RunId(runPath),
            ProjectId = runInfo.ProjectId,
            SourceRepoIdentity = NormalizePath(repoIdentity),
            SourceRepoPath = NormalizePath(sourceRepoPath),
            BaseCommit = string.IsNullOrWhiteSpace(runInfo.BaseCommit) ? currentHead : runInfo.BaseCommit,
            CurrentHeadCommit = currentHead,
            SourceApplyReceiptId = ReadSourceApplyReceiptId(runPath),
            PatchHash = HashFile(Path.Combine(runPath, "patch.diff")) ?? "missing-patch-diff",
            PostApplyDiffHash = postApplyDiffHash,
            RequestedBy = "IronDevCli",
            Reason = runInfo.TaskSummary,
            EvidenceRefs = ReadArtifactNames(runPath)
        });

        await WriteJsonAsync(runPath, "commit-package-request.json", request, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "commit-package-request.md"), RenderRequest(request), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.CommitPackageRequestCreated, request.CommitPackageRequestId, "Commit package request was created.", ["commit-package-request.json", "commit-package-request.md"]);

        if (parsed.Json)
            WriteJson(output, "commit-package request", "succeeded", new { runPath, request }, []);
        else
        {
            output.WriteLine($"Commit package request: {request.CommitPackageRequestId}");
            output.WriteLine($"Run path: {runPath}");
            output.WriteLine("Boundary: request evidence does not stage, commit, push, create PRs, merge, release, deploy, or continue workflow.");
        }

        return 0;
    }

    private static async Task<int> HandleManifestAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseSourceRepoCommand(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "commit-package manifest", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var request = ReadJson<CommitPackageRequest>(Path.Combine(runPath, "commit-package-request.json"));
        if (request is null)
            return Failure(output, error, parsed.Json, "commit-package manifest", "commit-package-request.json is missing; run 'irondev commit-package request' first.");

        var sourceRepoPath = Path.GetFullPath(parsed.SourceRepo!);
        var currentHead = await ReadGitValueAsync(sourceRepoPath, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
        var actualChangedFiles = await ReadChangedFilesAsync(sourceRepoPath, cancellationToken).ConfigureAwait(false);
        var fileHashes = actualChangedFiles
            .Select(file => BuildFileHash(sourceRepoPath, file))
            .Where(item => item is not null)
            .Cast<CommitFileHash>()
            .ToArray();

        var manifest = CommitFileManifestBuilder.Build(new CommitFileManifestInput
        {
            RunId = request.RunId,
            SourceRepoIdentity = request.SourceRepoIdentity,
            SourceRepoPath = NormalizePath(sourceRepoPath),
            BaseCommit = request.BaseCommit,
            CurrentHeadCommit = string.IsNullOrWhiteSpace(currentHead) ? request.CurrentHeadCommit : currentHead,
            KnownChangedFiles = ReadChangedFilesArtifact(runPath),
            ActualChangedFiles = actualChangedFiles,
            ExplicitExcludedFiles = ReadLinesIfExists(Path.Combine(runPath, "commit-excluded-files.txt")),
            FileHashes = fileHashes,
            DiffHash = await ComputeRepositoryStateHashAsync(sourceRepoPath, cancellationToken).ConfigureAwait(false)
        });
        var stagingPlan = CommitFileManifestBuilder.BuildStagingPlan(manifest);

        await WriteJsonAsync(runPath, "commit-file-manifest.json", manifest, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "commit-file-manifest.md"), RenderManifest(manifest), cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "commit-staging-plan.json", stagingPlan, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "commit-staging-plan.md"), RenderStagingPlan(stagingPlan), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.CommitFileManifestCreated, manifest.CommitFileManifestId, "Commit file manifest and human staging plan were created.", ["commit-file-manifest.json", "commit-staging-plan.json"]);

        if (parsed.Json)
            WriteJson(output, "commit-package manifest", "succeeded", new { runPath, manifest, stagingPlan }, []);
        else
        {
            output.WriteLine($"Commit file manifest: {manifest.CommitFileManifestId}");
            output.WriteLine($"Included files: {manifest.IncludedFiles.Length}");
            output.WriteLine("Boundary: staging plan is written for a human; no files were staged.");
        }

        return 0;
    }

    private static async Task<int> HandleEvidenceAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunOnlyCommand(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "commit-package evidence", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var request = ReadJson<CommitPackageRequest>(Path.Combine(runPath, "commit-package-request.json"));
        var manifest = ReadJson<CommitFileManifest>(Path.Combine(runPath, "commit-file-manifest.json"));
        if (request is null || manifest is null)
            return Failure(output, error, parsed.Json, "commit-package evidence", "commit package request and manifest artifacts are required first.");

        var bundle = CommitEvidenceBundleBuilder.Build(new CommitEvidenceBundleInput
        {
            RunId = request.RunId,
            CommitPackageRequestId = request.CommitPackageRequestId,
            CommitFileManifestId = manifest.CommitFileManifestId,
            AvailableArtifactNames = ReadArtifactNames(runPath)
        });

        await WriteJsonAsync(runPath, "commit-evidence-bundle.json", bundle, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "commit-evidence-bundle.md"), RenderEvidence(bundle), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.CommitEvidenceBundleCreated, bundle.CommitEvidenceBundleId, "Commit evidence bundle was created.", ["commit-evidence-bundle.json", "commit-evidence-bundle.md"]);

        if (parsed.Json)
            WriteJson(output, "commit-package evidence", "succeeded", new { runPath, bundle }, []);
        else
        {
            output.WriteLine($"Commit evidence bundle: {bundle.CommitEvidenceBundleId}");
            output.WriteLine($"Missing evidence: {bundle.MissingEvidence.Length}");
            output.WriteLine("Boundary: evidence bundle cannot approve or create a commit.");
        }

        return 0;
    }

    private static async Task<int> HandleMessageAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunOnlyCommand(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "commit-package message", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var request = ReadJson<CommitPackageRequest>(Path.Combine(runPath, "commit-package-request.json"));
        var manifest = ReadJson<CommitFileManifest>(Path.Combine(runPath, "commit-file-manifest.json"));
        var bundle = ReadJson<CommitEvidenceBundle>(Path.Combine(runPath, "commit-evidence-bundle.json"));
        if (request is null || manifest is null || bundle is null)
            return Failure(output, error, parsed.Json, "commit-package message", "commit package request, manifest, and evidence bundle artifacts are required first.");

        var message = CommitMessageProposalBuilder.Build(bundle, manifest, request.Reason);
        await WriteJsonAsync(runPath, "commit-message-proposal.json", message, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "commit-message-proposal.md"), RenderMessage(message), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.CommitMessageProposalCreated, message.CommitMessageProposalId, "Commit message proposal was created.", ["commit-message-proposal.json", "commit-message-proposal.md"]);

        if (parsed.Json)
            WriteJson(output, "commit-package message", "succeeded", new { runPath, message }, []);
        else
        {
            output.WriteLine($"Commit message proposal: {message.CommitMessageProposalId}");
            output.WriteLine("Boundary: message proposal is draft text only; a human must edit and commit manually.");
        }

        return 0;
    }

    private static async Task<int> HandleReviewAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunOnlyCommand(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "commit-package review", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var request = ReadJson<CommitPackageRequest>(Path.Combine(runPath, "commit-package-request.json"));
        var manifest = ReadJson<CommitFileManifest>(Path.Combine(runPath, "commit-file-manifest.json"));
        var stagingPlan = ReadJson<CommitStagingPlan>(Path.Combine(runPath, "commit-staging-plan.json"));
        var bundle = ReadJson<CommitEvidenceBundle>(Path.Combine(runPath, "commit-evidence-bundle.json"));
        var message = ReadJson<CommitMessageProposal>(Path.Combine(runPath, "commit-message-proposal.json"));
        if (request is null || manifest is null || stagingPlan is null || bundle is null || message is null)
            return Failure(output, error, parsed.Json, "commit-package review", "commit package request, manifest, staging plan, evidence, and message artifacts are required first.");

        var unsafeFindings = ReadUnsafeMaterialFindings(runPath);
        var review = CommitReadinessReviewer.Review(request, manifest, stagingPlan, bundle, message, unsafeFindings);
        var risk = CommitReadinessReviewer.BuildRiskReport(review);
        var boundary = CommitReadinessReviewer.BuildBoundaryReport(request.RunId);
        var bypass = CommitPackageBypassEvaluator.Evaluate(request.RunId, ["request", "manifest", "staging plan", "evidence bundle", "message proposal", "readiness review", "test pass", "build pass", "diff-check pass"]);

        await WriteJsonAsync(runPath, "commit-readiness-review.json", review, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "commit-readiness-review.md"), RenderReview(review), cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "commit-package-risk-report.json", risk, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "commit-package-risk-report.md"), RenderRisk(risk), cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "commit-package-boundary-report.json", boundary, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "commit-package-boundary-report.md"), RenderBoundary(boundary), cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "commit-package-bypass-report.json", bypass, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "commit-package-bypass-report.md"), RenderBypass(bypass), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.CommitReadinessReviewCreated, review.CommitReadinessReviewId, "Commit readiness review was created.", ["commit-readiness-review.json", "commit-package-risk-report.json"]);
        RecordEvent(runPath, GovernanceKernelEventKind.CommitPackageBoundaryReportCreated, boundary.CommitPackageBoundaryReportId, "Commit package boundary report was created.", ["commit-package-boundary-report.json"]);
        RecordEvent(runPath, GovernanceKernelEventKind.CommitPackageBypassReportCreated, bypass.CommitPackageBypassReportId, "Commit package bypass report was created.", ["commit-package-bypass-report.json"]);

        if (parsed.Json)
            WriteJson(output, "commit-package review", "succeeded", new { runPath, review, risk, boundary, bypass }, []);
        else
        {
            output.WriteLine($"Commit readiness review: {review.Decision}");
            output.WriteLine("Boundary: readiness review is not approval, staging, commit creation, push, PR creation, merge, release, deployment, or workflow continuation.");
        }

        return 0;
    }

    private static int HandleStatus(string[] args, TextWriter output, TextWriter error)
    {
        var parsed = ParseRunOnlyCommand(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "commit-package status", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var artifacts = StatusArtifacts
            .Select(name => new { name, exists = File.Exists(Path.Combine(runPath, name)) })
            .ToArray();

        if (parsed.Json)
            WriteJson(output, "commit-package status", "succeeded", new { runPath, artifacts }, []);
        else
        {
            output.WriteLine($"Commit package artifacts: {runPath}");
            foreach (var artifact in artifacts)
                output.WriteLine($"- {artifact.name}: {(artifact.exists ? "present" : "missing")}");
            output.WriteLine("Boundary: status is read-only and grants no commit authority.");
        }

        return 0;
    }

    private static ParsedSourceRepoCommand ParseSourceRepoCommand(string[] args)
    {
        string? run = null;
        string? sourceRepo = null;
        var json = false;

        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run":
                    if (!TryRead(args, ref index, out run)) return ParsedSourceRepoCommand.Fail(json, "--run requires a value.");
                    break;
                case "--source-repo":
                    if (!TryRead(args, ref index, out sourceRepo)) return ParsedSourceRepoCommand.Fail(json, "--source-repo requires a value.");
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    return ParsedSourceRepoCommand.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(run))
            return ParsedSourceRepoCommand.Fail(json, "Missing required option: --run <run-id-or-path>.");
        if (string.IsNullOrWhiteSpace(sourceRepo))
            return ParsedSourceRepoCommand.Fail(json, "Missing required option: --source-repo <path>.");

        return new ParsedSourceRepoCommand(run, sourceRepo, json, null);
    }

    private static ParsedRunOnlyCommand ParseRunOnlyCommand(string[] args)
    {
        string? run = null;
        var json = false;

        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run":
                    if (!TryRead(args, ref index, out run)) return ParsedRunOnlyCommand.Fail(json, "--run requires a value.");
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    return ParsedRunOnlyCommand.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(run)
            ? ParsedRunOnlyCommand.Fail(json, "Missing required option: --run <run-id-or-path>.")
            : new ParsedRunOnlyCommand(run, json, null);
    }

    private static bool TryRead(string[] args, ref int index, out string value)
    {
        value = string.Empty;
        if (index + 1 >= args.Length)
            return false;
        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static async Task<string[]> ReadChangedFilesAsync(string sourceRepoPath, CancellationToken cancellationToken)
    {
        var status = await RunGitAsync(sourceRepoPath, ["status", "--porcelain=v1", "--untracked-files=all"], cancellationToken).ConfigureAwait(false);
        if (status.ExitCode != 0)
            return [];

        return status.Stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseStatusPath)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ParseStatusPath(string line)
    {
        if (line.Length <= 3)
            return string.Empty;

        var path = line[3..].Trim();
        var arrow = path.LastIndexOf(" -> ", StringComparison.Ordinal);
        if (arrow >= 0)
            path = path[(arrow + 4)..].Trim();
        return path.Trim('"');
    }

    private static string[] ReadChangedFilesArtifact(string runPath)
    {
        var preferred = Path.Combine(runPath, "changed-files.txt");
        if (File.Exists(preferred))
            return ReadLinesIfExists(preferred);

        var manifest = Path.Combine(runPath, "changed-files.json");
        if (!File.Exists(manifest))
            return [];

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            if (document.RootElement.ValueKind == JsonValueKind.Array)
                return document.RootElement.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        }
        catch
        {
            return [];
        }

        return [];
    }

    private static string[] ReadLinesIfExists(string path) =>
        File.Exists(path)
            ? File.ReadAllLines(path).Select(item => item.Trim()).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray()
            : [];

    private static CommitFileHash? BuildFileHash(string sourceRepoPath, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(sourceRepoPath, relativePath));
        var root = EnsureTrailingSeparator(Path.GetFullPath(sourceRepoPath));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            return null;

        return new CommitFileHash
        {
            Path = NormalizePath(relativePath),
            ContentHash = HashBytes(File.ReadAllBytes(fullPath))
        };
    }

    private static async Task<string> ComputeRepositoryStateHashAsync(string sourceRepoPath, CancellationToken cancellationToken)
    {
        var status = await RunGitAsync(sourceRepoPath, ["status", "--porcelain=v1", "--untracked-files=all"], cancellationToken).ConfigureAwait(false);
        var diff = await RunGitAsync(sourceRepoPath, ["diff", "--binary"], cancellationToken).ConfigureAwait(false);
        var cachedDiff = await RunGitAsync(sourceRepoPath, ["diff", "--cached", "--binary"], cancellationToken).ConfigureAwait(false);
        var changedFiles = await ReadChangedFilesAsync(sourceRepoPath, cancellationToken).ConfigureAwait(false);
        var fileHashes = changedFiles
            .Select(file => BuildFileHash(sourceRepoPath, file))
            .Where(item => item is not null)
            .Select(item => $"{item!.Path}:{item.ContentHash}");
        var hashMaterial = new List<string> { status.Stdout, diff.Stdout, cachedDiff.Stdout };
        hashMaterial.AddRange(fileHashes);

        return HashText(string.Join("\n", hashMaterial));
    }

    private static RunInfo ReadRunInfo(string runPath)
    {
        var runJson = Path.Combine(runPath, "run.json");
        if (!File.Exists(runJson))
            return new RunInfo("project-unknown", string.Empty, $"Prepare controlled commit package for {RunId(runPath)}.");

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(runJson));
            var root = document.RootElement;
            return new RunInfo(
                ReadStringProperty(root, "projectId", "ProjectId", "project") ?? "project-unknown",
                ReadStringProperty(root, "baseCommit", "BaseCommit", "base_commit") ?? string.Empty,
                ReadStringProperty(root, "taskSummary", "TaskSummary", "summary", "title") ?? $"Prepare controlled commit package for {RunId(runPath)}.");
        }
        catch
        {
            return new RunInfo("project-unknown", string.Empty, $"Prepare controlled commit package for {RunId(runPath)}.");
        }
    }

    private static string ReadSourceApplyReceiptId(string runPath)
    {
        var receipt = Path.Combine(runPath, "source-apply-receipt.json");
        if (!File.Exists(receipt))
            return "missing-source-apply-receipt";

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(receipt));
            return ReadStringProperty(document.RootElement, "sourceApplyReceiptId", "SourceApplyReceiptId", "receiptId", "ReceiptId", "id") ?? "source-apply-receipt";
        }
        catch
        {
            return "source-apply-receipt";
        }
    }

    private static string[] ReadUnsafeMaterialFindings(string runPath)
    {
        var findingsPath = Path.Combine(runPath, "unsafe-material-findings.jsonl");
        if (File.Exists(findingsPath))
        {
            var findings = ReadLinesIfExists(findingsPath);
            if (findings.Length > 0)
                return findings;
        }

        var reportPath = Path.Combine(runPath, "unsafe-material-report.json");
        if (!File.Exists(reportPath))
            return [];

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            if (document.RootElement.TryGetProperty("findings", out var findings) && findings.ValueKind == JsonValueKind.Array)
                return findings.GetArrayLength() == 0 ? [] : findings.EnumerateArray().Select(item => item.ToString()).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        }
        catch
        {
            return [];
        }

        return [];
    }

    private static string? ReadStringProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
                return property.GetString();
        }

        return null;
    }

    private static string[] ReadArtifactNames(string runPath) =>
        Directory.Exists(runPath)
            ? Directory.EnumerateFiles(runPath).Select(Path.GetFileName).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];

    private static string RenderRequest(CommitPackageRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Commit Package Request");
        builder.AppendLine();
        builder.AppendLine($"Request: `{request.CommitPackageRequestId}`");
        builder.AppendLine($"Run: `{request.RunId}`");
        builder.AppendLine($"Project: `{request.ProjectId}`");
        builder.AppendLine($"Source repo: `{request.SourceRepoIdentity}`");
        builder.AppendLine($"Base commit: `{request.BaseCommit}`");
        builder.AppendLine($"Current HEAD: `{request.CurrentHeadCommit}`");
        builder.AppendLine($"Patch hash: `{request.PatchHash}`");
        builder.AppendLine($"Post-apply diff hash: `{request.PostApplyDiffHash}`");
        builder.AppendLine();
        builder.AppendLine("Boundary: this request prepares evidence only. It does not stage, commit, push, create PRs, merge, release, deploy, or continue workflow.");
        return builder.ToString();
    }

    private static string RenderManifest(CommitFileManifest manifest)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Commit File Manifest");
        builder.AppendLine();
        builder.AppendLine($"Manifest: `{manifest.CommitFileManifestId}`");
        builder.AppendLine($"Diff hash: `{manifest.DiffHash}`");
        builder.AppendLine();
        builder.AppendLine("## Included Files");
        foreach (var file in manifest.IncludedFiles.DefaultIfEmpty("(none)"))
            builder.AppendLine($"- `{file}`");
        builder.AppendLine();
        builder.AppendLine("## Excluded Files");
        foreach (var file in manifest.ExcludedFiles)
            builder.AppendLine($"- `{file.Path}`: {file.Reason}");
        builder.AppendLine();
        builder.AppendLine("Boundary: manifest is inspection evidence only. It does not stage files.");
        return builder.ToString();
    }

    private static string RenderStagingPlan(CommitStagingPlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Human Staging Plan");
        builder.AppendLine();
        builder.AppendLine($"Plan: `{plan.CommitStagingPlanId}`");
        foreach (var command in plan.StagingCommandsForHuman)
            builder.AppendLine($"- `{command}`");
        foreach (var warning in plan.Warnings)
            builder.AppendLine($"- Warning: {warning}");
        builder.AppendLine();
        builder.AppendLine("Boundary: these commands are text for a human. Block AL did not run them.");
        return builder.ToString();
    }

    private static string RenderEvidence(CommitEvidenceBundle bundle)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Commit Evidence Bundle");
        builder.AppendLine();
        builder.AppendLine($"Bundle: `{bundle.CommitEvidenceBundleId}`");
        builder.AppendLine("## Missing Evidence");
        foreach (var item in bundle.MissingEvidence.DefaultIfEmpty("(none)"))
            builder.AppendLine($"- `{item}`");
        builder.AppendLine("## Validation Evidence");
        foreach (var item in bundle.ValidationEvidenceRefs.DefaultIfEmpty("(none)"))
            builder.AppendLine($"- `{item}`");
        builder.AppendLine();
        builder.AppendLine("Boundary: evidence cannot approve, stage, commit, push, create PRs, merge, release, deploy, or continue workflow.");
        return builder.ToString();
    }

    private static string RenderMessage(CommitMessageProposal message)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Commit Message Proposal");
        builder.AppendLine();
        builder.AppendLine($"Proposal: `{message.CommitMessageProposalId}`");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(message.ProposedTitle);
        builder.AppendLine();
        builder.AppendLine(message.ProposedBody);
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("Boundary: draft text only. Human edit and review are required before any future commit.");
        return builder.ToString();
    }

    private static string RenderReview(CommitReadinessReview review)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Commit Readiness Review");
        builder.AppendLine();
        builder.AppendLine($"Decision: `{review.Decision}`");
        foreach (var finding in review.Findings)
            builder.AppendLine($"- `{finding}`");
        builder.AppendLine();
        builder.AppendLine("Boundary: review is evidence only and cannot approve or create a commit.");
        return builder.ToString();
    }

    private static string RenderRisk(CommitPackageRiskReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Commit Package Risk Report");
        foreach (var risk in report.Risks)
            builder.AppendLine($"- {risk}");
        builder.AppendLine("Boundary: risk report cannot satisfy policy.");
        return builder.ToString();
    }

    private static string RenderBoundary(CommitPackageBoundaryReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Commit Package Boundary Report");
        builder.AppendLine();
        builder.AppendLine(report.BoundaryText);
        builder.AppendLine("It does not approve commits.");
        builder.AppendLine("It does not satisfy policy.");
        return builder.ToString();
    }

    private static string RenderBypass(CommitPackageBypassReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Commit Package Bypass Report");
        foreach (var subject in report.EvidenceSubjects)
            builder.AppendLine($"- `{subject}` cannot stage, commit, push, create PRs, merge, release, deploy, or continue workflow.");
        return builder.ToString();
    }

    private static async Task WriteJsonAsync<T>(string runPath, string artifactName, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(runPath);
        await File.WriteAllTextAsync(Path.Combine(runPath, artifactName), JsonSerializer.Serialize(value, JsonOptions), cancellationToken).ConfigureAwait(false);
    }

    private static T? ReadJson<T>(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : default;

    private static void RecordEvent(string runPath, GovernanceKernelEventKind kind, string subjectId, string summary, string[] evidenceRefs) =>
        new FileBackedGovernanceEventStore(runPath).Append(
            runId: RunId(runPath),
            actionId: subjectId,
            eventKind: kind,
            subjectKind: "ControlledCommitPackage",
            subjectId: subjectId,
            summary: summary,
            evidenceRefs: evidenceRefs);

    private static async Task<string> ReadGitValueAsync(string workingDirectory, string[] arguments, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.Stdout.Trim() : string.Empty;
    }

    private static Task<ProcessResult> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        RunProcessAsync("git", arguments, workingDirectory, cancellationToken);

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

    private static string ResolveRunPath(string run)
    {
        var candidate = Path.GetFullPath(run.Trim());
        if (Path.IsPathRooted(run) || Directory.Exists(candidate) || File.Exists(Path.Combine(candidate, "run.json")))
            return candidate;
        return Path.Combine(Path.GetTempPath(), DefaultRunsFolderName, run.Trim());
    }

    private static string RunId(string runPath) => Path.GetFileName(Path.GetFullPath(runPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static string NormalizePath(string value) => value.Trim().Replace('\\', '/');

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static string? HashFile(string path) => File.Exists(path) ? HashBytes(File.ReadAllBytes(path)) : null;

    private static string HashText(string value) => HashBytes(Encoding.UTF8.GetBytes(value));

    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static int Usage(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine("Usage:");
        error.WriteLine("  irondev commit-package request --run <run-id-or-path> --source-repo <path> [--json]");
        error.WriteLine("  irondev commit-package manifest --run <run-id-or-path> --source-repo <path> [--json]");
        error.WriteLine("  irondev commit-package evidence --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev commit-package message --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev commit-package review --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev commit-package status --run <run-id-or-path> [--json]");
        return 2;
    }

    private static int Failure(TextWriter output, TextWriter error, bool json, string command, string message)
    {
        if (json)
            WriteJson(output, command, "failed", null, [message]);
        else
            error.WriteLine(message);
        return 1;
    }

    private static void WriteJson(TextWriter output, string command, string status, object? data, string[] errors)
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            ok = errors.Length == 0,
            command,
            status,
            data,
            errors,
            boundary = new CommitPackageBoundary()
        }, JsonOptions));
    }

    private sealed record ParsedSourceRepoCommand(string? Run, string? SourceRepo, bool Json, string? Error)
    {
        public static ParsedSourceRepoCommand Fail(bool json, string error) => new(null, null, json, error);
    }

    private sealed record ParsedRunOnlyCommand(string? Run, bool Json, string? Error)
    {
        public static ParsedRunOnlyCommand Fail(bool json, string error) => new(null, json, error);
    }

    private sealed record RunInfo(string ProjectId, string BaseCommit, string TaskSummary);

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
