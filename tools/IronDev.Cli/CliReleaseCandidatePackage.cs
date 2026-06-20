using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.Cli;

internal static class IronDevCliReleaseCandidatePackage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "execute",
        "release",
        "tag",
        "publish",
        "deploy",
        "promote-memory",
        "continue",
        "continue-workflow",
        "merge",
        "push",
        "commit"
    ];

    public static bool IsReleaseCandidateCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "release-candidate", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "release-candidate requires a subcommand: package, inspect, status, or records.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"release-candidate {args[1]} is intentionally unsupported; Block AZ only writes release-candidate package evidence.");

        return subcommand switch
        {
            "package" => await HandlePackageAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "inspect" => HandleRead(args, output, error, "inspect"),
            "status" => HandleRead(args, output, error, "status"),
            "records" => HandleRead(args, output, error, "records"),
            _ => Usage(error, $"unsupported release-candidate subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandlePackageAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePackage(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "release-candidate package", parsed.Error);

        var mergeReceipt = ReadJson<MergeExecutionReceipt>(Path.GetFullPath(parsed.MergeReceiptPath!));
        if (mergeReceipt is null)
            return Failure(output, error, parsed.Json, "release-candidate package", $"merge execution receipt is missing or invalid: {parsed.MergeReceiptPath}");

        var validationReceipt = ReadJson<ValidationRunReceipt>(Path.GetFullPath(parsed.ValidationReceiptPath!));
        if (validationReceipt is null)
            return Failure(output, error, parsed.Json, "release-candidate package", $"validation receipt is missing or invalid: {parsed.ValidationReceiptPath}");

        var releaseNotes = ReadJson<ReleaseNotesEvidence>(Path.GetFullPath(parsed.ReleaseNotesPath!));
        if (releaseNotes is null)
            return Failure(output, error, parsed.Json, "release-candidate package", $"release notes evidence is missing or invalid: {parsed.ReleaseNotesPath}");

        var sourceState = string.IsNullOrWhiteSpace(parsed.SourceObservationPath)
            ? CreateSourceState(parsed)
            : ReadJson<ReleaseSourceObservedState>(Path.GetFullPath(parsed.SourceObservationPath!));
        if (sourceState is null)
            return Failure(output, error, parsed.Json, "release-candidate package", $"release source observation is missing or invalid: {parsed.SourceObservationPath}");

        var versionEvidence = string.IsNullOrWhiteSpace(parsed.VersionEvidencePath)
            ? CreateVersionEvidence(parsed)
            : ReadJson<ReleaseVersionEvidence>(Path.GetFullPath(parsed.VersionEvidencePath!));
        if (versionEvidence is null)
            return Failure(output, error, parsed.Json, "release-candidate package", $"version evidence is missing or invalid: {parsed.VersionEvidencePath}");

        var artifactManifest = string.IsNullOrWhiteSpace(parsed.ArtifactManifestPath)
            ? null
            : ReadJson<ArtifactManifestEvidence>(Path.GetFullPath(parsed.ArtifactManifestPath!));
        if (!string.IsNullOrWhiteSpace(parsed.ArtifactManifestPath) && artifactManifest is null)
            return Failure(output, error, parsed.Json, "release-candidate package", $"artifact manifest evidence is missing or invalid: {parsed.ArtifactManifestPath}");

        var decision = new ReleaseCandidateDecisionRecord
        {
            ReleaseCandidateDecisionId = $"release_candidate_decision_{Guid.NewGuid():N}",
            Decision = ParseDecision(parsed.Decision!),
            DecisionMadeBy = parsed.DecisionBy!,
            DecisionMadeAtUtc = DateTimeOffset.UtcNow,
            DecisionRationale = parsed.DecisionRationale!,
            ExpectedRepository = parsed.Repository!,
            ExpectedCommitSha = parsed.CandidateCommit!,
            ExpectedVersion = versionEvidence.CandidateVersion,
            ExpectedReleaseSourceBranch = parsed.SourceBranch!,
            ExpectedReleaseChannel = parsed.Channel!
        };

        var artifacts = ReleaseCandidatePackageBuilder.Build(new ReleaseCandidatePackageInput
        {
            MergeExecutionReceipt = mergeReceipt,
            ObservedReleaseSourceState = sourceState,
            ReleaseValidationEvidence = ReleaseCandidatePackageBuilder.FromValidationReceipt(validationReceipt),
            ReleaseVersionEvidence = versionEvidence,
            ReleaseNotesEvidence = releaseNotes,
            ArtifactManifestEvidence = artifactManifest,
            ArtifactManifestRequired = parsed.ArtifactManifestRequired,
            ReleaseCandidateDecision = decision,
            Repository = parsed.Repository!,
            ReleaseSourceBranch = parsed.SourceBranch!,
            CandidateCommitSha = parsed.CandidateCommit!,
            ReleaseChannel = parsed.Channel!,
            CreatedBy = parsed.CreatedBy!,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        var outDirectory = ResolveOutputDirectory(parsed.OutPath!);
        Directory.CreateDirectory(outDirectory);
        await WriteArtifactsAsync(outDirectory, artifacts, cancellationToken).ConfigureAwait(false);
        RecordEvent(outDirectory, artifacts.Package);

        if (parsed.Json)
            WriteJson(output, "release-candidate package", artifacts.Package.PackageVerdict == ReleaseCandidatePackageVerdict.PackageReadyForReleaseExecutor ? "succeeded" : "blocked", new { outDirectory, artifacts.Package, artifacts.Receipt }, artifacts.Package.PackageIssues);
        else
        {
            output.WriteLine($"Release candidate package: {artifacts.Package.ReleaseCandidatePackageId}");
            output.WriteLine($"Verdict: {artifacts.Package.PackageVerdict}");
            output.WriteLine($"Can release for future executor: {artifacts.Package.CanReleaseForExecutor}");
            output.WriteLine("Boundary: package evidence does not tag, release, publish, deploy, promote memory, or continue workflow.");
        }

        return artifacts.Package.PackageVerdict is ReleaseCandidatePackageVerdict.PackageBlocked or ReleaseCandidatePackageVerdict.PackageRejected ? 1 : 0;
    }

    private static int HandleRead(string[] args, TextWriter output, TextWriter error, string mode)
    {
        var parsed = ParseRead(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, $"release-candidate {mode}", parsed.Error);

        var package = ReadJson<ReleaseCandidatePackage>(ResolvePackagePath(parsed.PackagePath!));
        if (package is null)
            return Failure(output, error, parsed.Json, $"release-candidate {mode}", "release candidate package is missing or invalid.");

        if (parsed.Json)
        {
            object data = mode == "records"
                ? new { package.ObservedReleaseSourceState, package.ReleaseValidationEvidence, package.ReleaseVersionEvidence, package.ReleaseNotesEvidence, package.ArtifactManifestEvidence, package.ReleaseCandidateDecision, package.BlockReasons, package.PackageIssues }
                : new { package.ReleaseCandidatePackageId, package.Repository, package.CandidateCommitSha, package.CandidateVersion, package.ReleaseChannel, package.PackageVerdict, package.CanReleaseForExecutor, package.Boundary };
            WriteJson(output, $"release-candidate {mode}", "succeeded", data, []);
        }
        else if (mode == "records")
        {
            output.WriteLine($"Decision: {package.ReleaseCandidateDecision?.Decision}");
            output.WriteLine($"Decision maker: {package.ReleaseCandidateDecision?.DecisionMadeBy ?? "none"}");
            output.WriteLine($"Version: {package.CandidateVersion}");
            output.WriteLine($"Tag: {package.CandidateTagName}");
            output.WriteLine($"Block reasons: {RenderInline(package.BlockReasons.Select(item => item.ToString()))}");
            output.WriteLine($"Issues: {RenderInline(package.PackageIssues)}");
        }
        else
        {
            output.WriteLine($"Release candidate package: {package.ReleaseCandidatePackageId}");
            output.WriteLine($"Verdict: {package.PackageVerdict}");
            output.WriteLine($"Can release for future executor: {package.CanReleaseForExecutor}");
            output.WriteLine("Boundary: package evidence grants no tag, release, publish, deploy, memory-promotion, or continuation authority.");
        }

        return 0;
    }

    private static async Task WriteArtifactsAsync(string outDirectory, ReleaseCandidatePackageArtifacts artifacts, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "release-candidate-package.json"), JsonSerializer.Serialize(artifacts.Package, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "release-candidate-package-receipt.json"), JsonSerializer.Serialize(artifacts.Receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "release-candidate-summary.md"), RenderSummary(artifacts), cancellationToken).ConfigureAwait(false);
        var records = new[]
        {
            JsonSerializer.Serialize(artifacts.Package.ObservedReleaseSourceState, JsonOptions),
            JsonSerializer.Serialize(artifacts.Package.ReleaseValidationEvidence, JsonOptions),
            JsonSerializer.Serialize(artifacts.Package.ReleaseVersionEvidence, JsonOptions),
            JsonSerializer.Serialize(artifacts.Package.ReleaseNotesEvidence, JsonOptions),
            JsonSerializer.Serialize(artifacts.Package.ArtifactManifestEvidence, JsonOptions),
            JsonSerializer.Serialize(artifacts.Package.ReleaseCandidateDecision, JsonOptions),
            JsonSerializer.Serialize(artifacts.Receipt, JsonOptions)
        };
        await File.WriteAllLinesAsync(Path.Combine(outDirectory, "release-candidate-evidence.jsonl"), records, cancellationToken).ConfigureAwait(false);
    }

    private static string RenderSummary(ReleaseCandidatePackageArtifacts artifacts) => $"""
        # Release Candidate Package

        Verdict: `{artifacts.Package.PackageVerdict}`
        Can release for future executor: `{artifacts.Package.CanReleaseForExecutor.ToString().ToLowerInvariant()}`
        Repository: `{artifacts.Package.Repository}`
        Release source branch: `{artifacts.Package.ReleaseSourceBranch}`
        Candidate commit: `{artifacts.Package.CandidateCommitSha}`
        Candidate version: `{artifacts.Package.CandidateVersion}`
        Candidate tag: `{artifacts.Package.CandidateTagName}`
        Release channel: `{artifacts.Package.ReleaseChannel}`
        Block reasons:
        {RenderBullets(artifacts.Package.BlockReasons.Select(item => item.ToString()))}
        Package issues:
        {RenderBullets(artifacts.Package.PackageIssues)}

        Merge execution is not release readiness.
        Release candidate package is not release execution.
        Release execution is not deployment.
        Release is not deployment.
        Validation evidence is not release authority.
        Release notes are not release authority.
        Version selection is not tag creation.
        No hidden publication.
        No hidden deployment.
        No hidden workflow continuation.

        Boundary: AZ produces release-candidate package evidence only. It does not create a tag, create a GitHub release, publish artifacts, deploy, promote memory, commit, push, mutate source, or continue workflow.
        """;

    private static void RecordEvent(string outDirectory, ReleaseCandidatePackage package) =>
        new FileBackedGovernanceEventStore(outDirectory).Append(
            package.ReleaseCandidatePackageId,
            package.ReleaseCandidatePackageId,
            GovernanceKernelEventKind.ReleaseCandidatePackageCreated,
            "ReleaseCandidatePackage",
            package.ReleaseCandidatePackageId,
            "Release candidate package was created.",
            ["release-candidate-package.json", "release-candidate-package-receipt.json"]);

    private static ParsedPackage ParsePackage(string[] args)
    {
        string? mergeReceipt = null;
        string? repo = null;
        string? sourceBranch = null;
        string? candidateCommit = null;
        string? observedSourceHead = null;
        string? version = null;
        string? versionScheme = "SemVer";
        string? tag = null;
        string? channel = null;
        string? validation = null;
        string? releaseNotes = null;
        string? decision = null;
        string? decisionBy = null;
        string? decisionRationale = null;
        string? createdBy = null;
        string? outPath = null;
        string? artifactManifest = null;
        string? versionEvidence = null;
        string? sourceObservation = null;
        var artifactsRequired = false;
        var json = false;

        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--merge-receipt": if (!TryRead(args, ref index, out mergeReceipt)) return ParsedPackage.Fail(json, "--merge-receipt requires a value."); break;
                case "--repo": if (!TryRead(args, ref index, out repo)) return ParsedPackage.Fail(json, "--repo requires a value."); break;
                case "--source-branch": if (!TryRead(args, ref index, out sourceBranch)) return ParsedPackage.Fail(json, "--source-branch requires a value."); break;
                case "--candidate-commit": if (!TryRead(args, ref index, out candidateCommit)) return ParsedPackage.Fail(json, "--candidate-commit requires a value."); break;
                case "--observed-source-head": if (!TryRead(args, ref index, out observedSourceHead)) return ParsedPackage.Fail(json, "--observed-source-head requires a value."); break;
                case "--version": if (!TryRead(args, ref index, out version)) return ParsedPackage.Fail(json, "--version requires a value."); break;
                case "--version-scheme": if (!TryRead(args, ref index, out versionScheme)) return ParsedPackage.Fail(json, "--version-scheme requires a value."); break;
                case "--tag": if (!TryRead(args, ref index, out tag)) return ParsedPackage.Fail(json, "--tag requires a value."); break;
                case "--channel": if (!TryRead(args, ref index, out channel)) return ParsedPackage.Fail(json, "--channel requires a value."); break;
                case "--validation": if (!TryRead(args, ref index, out validation)) return ParsedPackage.Fail(json, "--validation requires a value."); break;
                case "--release-notes": if (!TryRead(args, ref index, out releaseNotes)) return ParsedPackage.Fail(json, "--release-notes requires a value."); break;
                case "--decision": if (!TryRead(args, ref index, out decision)) return ParsedPackage.Fail(json, "--decision requires a value."); break;
                case "--decision-by": if (!TryRead(args, ref index, out decisionBy)) return ParsedPackage.Fail(json, "--decision-by requires a value."); break;
                case "--decision-rationale": if (!TryRead(args, ref index, out decisionRationale)) return ParsedPackage.Fail(json, "--decision-rationale requires a value."); break;
                case "--created-by": if (!TryRead(args, ref index, out createdBy)) return ParsedPackage.Fail(json, "--created-by requires a value."); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedPackage.Fail(json, "--out requires a value."); break;
                case "--artifact-manifest": if (!TryRead(args, ref index, out artifactManifest)) return ParsedPackage.Fail(json, "--artifact-manifest requires a value."); break;
                case "--version-evidence": if (!TryRead(args, ref index, out versionEvidence)) return ParsedPackage.Fail(json, "--version-evidence requires a value."); break;
                case "--source-observation": if (!TryRead(args, ref index, out sourceObservation)) return ParsedPackage.Fail(json, "--source-observation requires a value."); break;
                case "--artifacts-required": artifactsRequired = true; break;
                case "--json": json = true; break;
                default: return ParsedPackage.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(mergeReceipt)) return ParsedPackage.Fail(json, "Missing required option: --merge-receipt <merge-execution-receipt.json>.");
        if (string.IsNullOrWhiteSpace(repo)) return ParsedPackage.Fail(json, "Missing required option: --repo <owner/name>.");
        if (string.IsNullOrWhiteSpace(sourceBranch)) return ParsedPackage.Fail(json, "Missing required option: --source-branch <branch>.");
        if (string.IsNullOrWhiteSpace(candidateCommit)) return ParsedPackage.Fail(json, "Missing required option: --candidate-commit <sha>.");
        if (string.IsNullOrWhiteSpace(observedSourceHead) && string.IsNullOrWhiteSpace(sourceObservation)) return ParsedPackage.Fail(json, "Missing required option: --observed-source-head <sha>.");
        if (string.IsNullOrWhiteSpace(version) && string.IsNullOrWhiteSpace(versionEvidence)) return ParsedPackage.Fail(json, "Missing required option: --version <version>.");
        if (string.IsNullOrWhiteSpace(tag) && string.IsNullOrWhiteSpace(versionEvidence)) return ParsedPackage.Fail(json, "Missing required option: --tag <tag-name>.");
        if (string.IsNullOrWhiteSpace(channel)) return ParsedPackage.Fail(json, "Missing required option: --channel <internal|preview|release-candidate|stable|hotfix>.");
        if (string.IsNullOrWhiteSpace(validation)) return ParsedPackage.Fail(json, "Missing required option: --validation <validation-run-receipt.json>.");
        if (string.IsNullOrWhiteSpace(releaseNotes)) return ParsedPackage.Fail(json, "Missing required option: --release-notes <release-notes.json>.");
        if (string.IsNullOrWhiteSpace(decision)) return ParsedPackage.Fail(json, "Missing required option: --decision <approved-for-release-executor|blocked|rejected>.");
        if (!TryParseDecision(decision, out _)) return ParsedPackage.Fail(json, "Unsupported --decision. Use approved-for-release-executor, blocked, or rejected.");
        if (string.IsNullOrWhiteSpace(decisionBy)) return ParsedPackage.Fail(json, "Missing required option: --decision-by <github-login>.");
        if (string.IsNullOrWhiteSpace(decisionRationale)) return ParsedPackage.Fail(json, "Missing required option: --decision-rationale <text>.");
        if (string.IsNullOrWhiteSpace(createdBy)) return ParsedPackage.Fail(json, "Missing required option: --created-by <github-login>.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedPackage.Fail(json, "Missing required option: --out <path>.");

        return new ParsedPackage(mergeReceipt, repo, sourceBranch, candidateCommit, observedSourceHead, version, versionScheme, tag, channel, validation, releaseNotes, decision, decisionBy, decisionRationale, createdBy, outPath, artifactManifest, versionEvidence, sourceObservation, artifactsRequired, json, null);
    }

    private static ParsedRead ParseRead(string[] args)
    {
        string? package = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--package": if (!TryRead(args, ref index, out package)) return ParsedRead.Fail(json, "--package requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedRead.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(package)
            ? ParsedRead.Fail(json, "Missing required option: --package <release-candidate-package.json>.")
            : new ParsedRead(package, json, null);
    }

    private static ReleaseSourceObservedState CreateSourceState(ParsedPackage parsed) => new()
    {
        Repository = parsed.Repository!,
        ReleaseSourceBranch = parsed.SourceBranch!,
        ReleaseSourceHeadSha = parsed.ObservedSourceHeadSha!,
        ExpectedMergeCommitSha = parsed.CandidateCommit!,
        DefaultBranch = parsed.SourceBranch!,
        DefaultBranchHeadSha = parsed.ObservedSourceHeadSha!,
        CommitPresentOnReleaseSource = string.Equals(parsed.ObservedSourceHeadSha, parsed.CandidateCommit, StringComparison.OrdinalIgnoreCase),
        CommitPresentOnDefaultBranch = string.Equals(parsed.ObservedSourceHeadSha, parsed.CandidateCommit, StringComparison.OrdinalIgnoreCase),
        ObservedAtUtc = DateTimeOffset.UtcNow,
        ObservationSource = "cli-input",
        ObservationSucceeded = true
    };

    private static ReleaseVersionEvidence CreateVersionEvidence(ParsedPackage parsed) => new()
    {
        CandidateVersion = parsed.Version!,
        VersionScheme = parsed.VersionScheme ?? "SemVer",
        VersionSource = "cli-input",
        VersionDecisionBy = parsed.DecisionBy!,
        VersionDecisionAtUtc = DateTimeOffset.UtcNow,
        VersionRationale = parsed.DecisionRationale!,
        TagName = parsed.Tag!,
        ExistingTagFound = false,
        ExistingReleaseFound = false
    };

    private static bool TryRead(string[] args, ref int index, out string value)
    {
        value = string.Empty;
        if (index + 1 >= args.Length)
            return false;
        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static ReleaseCandidateDecision ParseDecision(string value) =>
        TryParseDecision(value, out var decision) ? decision : ReleaseCandidateDecision.Blocked;

    private static bool TryParseDecision(string value, out ReleaseCandidateDecision decision)
    {
        var normalized = value.Trim().Replace("_", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        switch (normalized)
        {
            case "approved-for-release-executor":
            case "approvedforreleaseexecutor":
                decision = ReleaseCandidateDecision.ApprovedForReleaseExecutor;
                return true;
            case "blocked":
                decision = ReleaseCandidateDecision.Blocked;
                return true;
            case "rejected":
                decision = ReleaseCandidateDecision.Rejected;
                return true;
            default:
                decision = ReleaseCandidateDecision.Blocked;
                return false;
        }
    }

    private static string ResolveOutputDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Path.HasExtension(fullPath) && string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory()
            : fullPath;
    }

    private static string ResolvePackagePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Directory.Exists(fullPath) || !Path.HasExtension(fullPath)
            ? Path.Combine(fullPath, "release-candidate-package.json")
            : fullPath;
    }

    private static T? ReadJson<T>(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : default;
        }
        catch
        {
            return default;
        }
    }

    private static string RenderBullets(IEnumerable<string> values)
    {
        var items = values.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        return items.Length == 0 ? "- none" : string.Join(Environment.NewLine, items.Select(item => $"- {item}"));
    }

    private static string RenderInline(IEnumerable<string> values)
    {
        var items = values.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        return items.Length == 0 ? "none" : string.Join(", ", items);
    }

    private static int Usage(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine("Usage:");
        error.WriteLine("  irondev release-candidate package --merge-receipt <merge-execution-receipt.json> --repo <owner/name> --source-branch <main|release-branch> --candidate-commit <sha> --observed-source-head <sha> --version <version> --tag <tag-name> --channel <internal|preview|release-candidate|stable|hotfix> --validation <validation-run-receipt.json> --release-notes <release-notes.json> --decision approved-for-release-executor --decision-by <github-login> --decision-rationale <text> --created-by <github-login> --out <path> [--artifact-manifest <artifact-manifest.json>] [--version-evidence <version-evidence.json>] [--source-observation <release-source-state.json>] [--artifacts-required] [--json]");
        error.WriteLine("  irondev release-candidate inspect --package <release-candidate-package.json> [--json]");
        error.WriteLine("  irondev release-candidate status --package <release-candidate-package.json> [--json]");
        error.WriteLine("  irondev release-candidate records --package <release-candidate-package.json> [--json]");
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
            ok = errors.Length == 0 && status != "failed" && status != "blocked",
            command,
            status,
            data,
            errors,
            boundary = ReleaseCandidatePackageBoundary.Evidence
        }, JsonOptions));
    }

    private sealed record ParsedPackage(
        string? MergeReceiptPath,
        string? Repository,
        string? SourceBranch,
        string? CandidateCommit,
        string? ObservedSourceHeadSha,
        string? Version,
        string? VersionScheme,
        string? Tag,
        string? Channel,
        string? ValidationReceiptPath,
        string? ReleaseNotesPath,
        string? Decision,
        string? DecisionBy,
        string? DecisionRationale,
        string? CreatedBy,
        string? OutPath,
        string? ArtifactManifestPath,
        string? VersionEvidencePath,
        string? SourceObservationPath,
        bool ArtifactManifestRequired,
        bool Json,
        string? Error)
    {
        public static ParsedPackage Fail(bool json, string error) => new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, false, json, error);
    }

    private sealed record ParsedRead(string? PackagePath, bool Json, string? Error)
    {
        public static ParsedRead Fail(bool json, string error) => new(null, json, error);
    }
}
