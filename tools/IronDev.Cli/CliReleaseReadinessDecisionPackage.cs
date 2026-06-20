using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.Cli;

internal static class IronDevCliReleaseReadinessDecisionPackage
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

    public static bool IsReleaseReadinessCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "release-readiness", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "release-readiness requires a subcommand: package, inspect, status, or records.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"release-readiness {args[1]} is intentionally unsupported; Block BA only writes release-readiness decision package evidence.");

        return subcommand switch
        {
            "package" => await HandlePackageAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "inspect" => HandleRead(args, output, error, "inspect"),
            "status" => HandleRead(args, output, error, "status"),
            "records" => HandleRead(args, output, error, "records"),
            _ => Usage(error, $"unsupported release-readiness subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandlePackageAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePackage(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "release-readiness package", parsed.Error);

        var releaseCandidate = ReadJson<ReleaseCandidatePackage>(Path.GetFullPath(parsed.ReleaseCandidatePackagePath!));
        if (releaseCandidate is null)
            return Failure(output, error, parsed.Json, "release-readiness package", $"release candidate package is missing or invalid: {parsed.ReleaseCandidatePackagePath}");

        var sourceState = ReadJson<CurrentReleaseSourceState>(Path.GetFullPath(parsed.SourceStatePath!));
        if (sourceState is null)
            return Failure(output, error, parsed.Json, "release-readiness package", $"current release source state is missing or invalid: {parsed.SourceStatePath}");

        var tagReleaseState = ReadJson<CurrentTagReleaseState>(Path.GetFullPath(parsed.TagReleaseStatePath!));
        if (tagReleaseState is null)
            return Failure(output, error, parsed.Json, "release-readiness package", $"current tag/release state is missing or invalid: {parsed.TagReleaseStatePath}");

        var validationReceipt = ReadJson<ValidationRunReceipt>(Path.GetFullPath(parsed.ValidationReceiptPath!));
        if (validationReceipt is null)
            return Failure(output, error, parsed.Json, "release-readiness package", $"validation receipt is missing or invalid: {parsed.ValidationReceiptPath}");

        var artifactReadiness = ReadJson<ReleaseArtifactReadinessEvidence>(Path.GetFullPath(parsed.ArtifactReadinessPath!));
        if (artifactReadiness is null)
            return Failure(output, error, parsed.Json, "release-readiness package", $"artifact readiness evidence is missing or invalid: {parsed.ArtifactReadinessPath}");

        var decision = new ReleaseReadinessDecisionEvidence
        {
            ReleaseReadinessDecisionId = $"release_readiness_decision_{Guid.NewGuid():N}",
            Decision = ParseDecision(parsed.Decision!),
            DecisionMadeBy = parsed.DecisionBy!,
            DecisionMadeAtUtc = DateTimeOffset.UtcNow,
            DecisionRationale = parsed.DecisionRationale!,
            ExpectedRepository = parsed.Repository!,
            ExpectedCandidateCommitSha = parsed.CandidateCommit!,
            ExpectedVersion = parsed.Version!,
            ExpectedTagName = parsed.Tag!,
            ExpectedReleaseSourceBranch = parsed.SourceBranch!,
            ExpectedReleaseChannel = parsed.Channel!,
            ExpectedArtifactManifestId = artifactReadiness.ArtifactManifestId,
            ExpectedReleaseCandidatePackageId = releaseCandidate.ReleaseCandidatePackageId
        };

        var artifacts = ReleaseReadinessDecisionPackageBuilder.Build(new ReleaseReadinessDecisionPackageInput
        {
            ReleaseCandidatePackage = releaseCandidate,
            CurrentReleaseSourceState = sourceState,
            CurrentTagReleaseState = tagReleaseState,
            FinalReleaseValidationEvidence = ReleaseReadinessDecisionPackageBuilder.FromValidationReceipt(validationReceipt),
            ReleaseArtifactReadinessEvidence = artifactReadiness,
            ReleaseReadinessDecision = decision,
            Repository = parsed.Repository!,
            ReleaseSourceBranch = parsed.SourceBranch!,
            CandidateCommitSha = parsed.CandidateCommit!,
            CandidateVersion = parsed.Version!,
            CandidateTagName = parsed.Tag!,
            ReleaseChannel = parsed.Channel!,
            CreatedBy = parsed.CreatedBy!,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        var outDirectory = ResolveOutputDirectory(parsed.OutPath!);
        Directory.CreateDirectory(outDirectory);
        await WriteArtifactsAsync(outDirectory, artifacts, cancellationToken).ConfigureAwait(false);
        RecordEvent(outDirectory, artifacts.Package);

        if (parsed.Json)
            WriteJson(output, "release-readiness package", artifacts.Package.PackageVerdict == ReleaseReadinessDecisionPackageVerdict.PackageReadyForReleaseExecutor ? "succeeded" : "blocked", new { outDirectory, artifacts.Package, artifacts.Receipt }, artifacts.Package.PackageIssues);
        else
        {
            output.WriteLine($"Release readiness package: {artifacts.Package.ReleaseReadinessDecisionPackageId}");
            output.WriteLine($"Verdict: {artifacts.Package.PackageVerdict}");
            output.WriteLine($"Can release for future executor: {artifacts.Package.CanReleaseForExecutor}");
            output.WriteLine("Boundary: package evidence does not tag, release, publish, deploy, promote memory, or continue workflow.");
        }

        return artifacts.Package.PackageVerdict == ReleaseReadinessDecisionPackageVerdict.PackageReadyForReleaseExecutor ? 0 : 1;
    }

    private static int HandleRead(string[] args, TextWriter output, TextWriter error, string mode)
    {
        var parsed = ParseRead(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, $"release-readiness {mode}", parsed.Error);

        var package = ReadJson<ReleaseReadinessDecisionPackage>(ResolvePackagePath(parsed.PackagePath!));
        if (package is null)
            return Failure(output, error, parsed.Json, $"release-readiness {mode}", "release readiness decision package is missing or invalid.");

        if (parsed.Json)
        {
            object data = mode == "records"
                ? new { package.CurrentReleaseSourceState, package.CurrentTagReleaseState, package.FinalReleaseValidationEvidence, package.ReleaseArtifactReadinessEvidence, package.ReleaseReadinessDecision, package.BlockReasons, package.PackageIssues }
                : new { package.ReleaseReadinessDecisionPackageId, package.Repository, package.CandidateCommitSha, package.CandidateVersion, package.CandidateTagName, package.ReleaseChannel, package.PackageVerdict, package.CanReleaseForExecutor, package.Boundary };
            WriteJson(output, $"release-readiness {mode}", "succeeded", data, []);
        }
        else if (mode == "records")
        {
            output.WriteLine($"Decision: {package.ReleaseReadinessDecision?.Decision}");
            output.WriteLine($"Decision maker: {package.ReleaseReadinessDecision?.DecisionMadeBy ?? "none"}");
            output.WriteLine($"Version: {package.CandidateVersion}");
            output.WriteLine($"Tag: {package.CandidateTagName}");
            output.WriteLine($"Block reasons: {RenderInline(package.BlockReasons.Select(item => item.ToString()))}");
            output.WriteLine($"Issues: {RenderInline(package.PackageIssues)}");
        }
        else
        {
            output.WriteLine($"Release readiness package: {package.ReleaseReadinessDecisionPackageId}");
            output.WriteLine($"Verdict: {package.PackageVerdict}");
            output.WriteLine($"Can release for future executor: {package.CanReleaseForExecutor}");
            output.WriteLine("Boundary: package evidence grants no tag, release, publish, deploy, memory-promotion, or continuation authority.");
        }

        return 0;
    }

    private static async Task WriteArtifactsAsync(string outDirectory, ReleaseReadinessDecisionPackageArtifacts artifacts, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "release-readiness-decision-package.json"), JsonSerializer.Serialize(artifacts.Package, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "release-readiness-decision-package-receipt.json"), JsonSerializer.Serialize(artifacts.Receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "release-readiness-summary.md"), RenderSummary(artifacts), cancellationToken).ConfigureAwait(false);
        var records = new[]
        {
            JsonSerializer.Serialize(artifacts.Package.CurrentReleaseSourceState, JsonOptions),
            JsonSerializer.Serialize(artifacts.Package.CurrentTagReleaseState, JsonOptions),
            JsonSerializer.Serialize(artifacts.Package.FinalReleaseValidationEvidence, JsonOptions),
            JsonSerializer.Serialize(artifacts.Package.ReleaseArtifactReadinessEvidence, JsonOptions),
            JsonSerializer.Serialize(artifacts.Package.ReleaseReadinessDecision, JsonOptions),
            JsonSerializer.Serialize(artifacts.Receipt, JsonOptions)
        };
        await File.WriteAllLinesAsync(Path.Combine(outDirectory, "release-readiness-evidence.jsonl"), records, cancellationToken).ConfigureAwait(false);
    }

    private static string RenderSummary(ReleaseReadinessDecisionPackageArtifacts artifacts) => $"""
        # Release Readiness Decision Package

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

        Release candidate package is not release readiness decision.
        Release readiness decision package is not release execution.
        Release execution is not deployment.
        Release is not deployment.
        Validation evidence is not release authority.
        Release notes are not release authority.
        Version selection is not tag creation.
        Artifact readiness is not publication.
        No hidden tag creation.
        No hidden release creation.
        No hidden publication.
        No hidden deployment.
        No hidden memory promotion.
        No hidden workflow continuation.

        Boundary: BA produces release-readiness decision package evidence only. It does not create a tag, create a GitHub release, upload artifacts, publish, deploy, promote memory, commit, push, mutate source, or continue workflow.
        """;

    private static void RecordEvent(string outDirectory, ReleaseReadinessDecisionPackage package) =>
        new FileBackedGovernanceEventStore(outDirectory).Append(
            package.ReleaseReadinessDecisionPackageId,
            package.ReleaseReadinessDecisionPackageId,
            GovernanceKernelEventKind.ReleaseReadinessDecisionPackageCreated,
            "ReleaseReadinessDecisionPackage",
            package.ReleaseReadinessDecisionPackageId,
            "Release readiness decision package was created.",
            ["release-readiness-decision-package.json", "release-readiness-decision-package-receipt.json"]);

    private static ParsedPackage ParsePackage(string[] args)
    {
        string? releaseCandidatePackage = null;
        string? repo = null;
        string? sourceBranch = null;
        string? candidateCommit = null;
        string? version = null;
        string? tag = null;
        string? channel = null;
        string? sourceState = null;
        string? tagReleaseState = null;
        string? validation = null;
        string? artifactReadiness = null;
        string? decision = null;
        string? decisionBy = null;
        string? decisionRationale = null;
        string? createdBy = null;
        string? outPath = null;
        var json = false;

        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--release-candidate-package": if (!TryRead(args, ref index, out releaseCandidatePackage)) return ParsedPackage.Fail(json, "--release-candidate-package requires a value."); break;
                case "--repo": if (!TryRead(args, ref index, out repo)) return ParsedPackage.Fail(json, "--repo requires a value."); break;
                case "--source-branch": if (!TryRead(args, ref index, out sourceBranch)) return ParsedPackage.Fail(json, "--source-branch requires a value."); break;
                case "--candidate-commit": if (!TryRead(args, ref index, out candidateCommit)) return ParsedPackage.Fail(json, "--candidate-commit requires a value."); break;
                case "--version": if (!TryRead(args, ref index, out version)) return ParsedPackage.Fail(json, "--version requires a value."); break;
                case "--tag": if (!TryRead(args, ref index, out tag)) return ParsedPackage.Fail(json, "--tag requires a value."); break;
                case "--channel": if (!TryRead(args, ref index, out channel)) return ParsedPackage.Fail(json, "--channel requires a value."); break;
                case "--source-state": if (!TryRead(args, ref index, out sourceState)) return ParsedPackage.Fail(json, "--source-state requires a value."); break;
                case "--tag-release-state": if (!TryRead(args, ref index, out tagReleaseState)) return ParsedPackage.Fail(json, "--tag-release-state requires a value."); break;
                case "--validation": if (!TryRead(args, ref index, out validation)) return ParsedPackage.Fail(json, "--validation requires a value."); break;
                case "--artifact-readiness": if (!TryRead(args, ref index, out artifactReadiness)) return ParsedPackage.Fail(json, "--artifact-readiness requires a value."); break;
                case "--decision": if (!TryRead(args, ref index, out decision)) return ParsedPackage.Fail(json, "--decision requires a value."); break;
                case "--decision-by": if (!TryRead(args, ref index, out decisionBy)) return ParsedPackage.Fail(json, "--decision-by requires a value."); break;
                case "--decision-rationale": if (!TryRead(args, ref index, out decisionRationale)) return ParsedPackage.Fail(json, "--decision-rationale requires a value."); break;
                case "--created-by": if (!TryRead(args, ref index, out createdBy)) return ParsedPackage.Fail(json, "--created-by requires a value."); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedPackage.Fail(json, "--out requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedPackage.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(releaseCandidatePackage)) return ParsedPackage.Fail(json, "Missing required option: --release-candidate-package <release-candidate-package.json>.");
        if (string.IsNullOrWhiteSpace(repo)) return ParsedPackage.Fail(json, "Missing required option: --repo <owner/name>.");
        if (string.IsNullOrWhiteSpace(sourceBranch)) return ParsedPackage.Fail(json, "Missing required option: --source-branch <branch>.");
        if (string.IsNullOrWhiteSpace(candidateCommit)) return ParsedPackage.Fail(json, "Missing required option: --candidate-commit <sha>.");
        if (string.IsNullOrWhiteSpace(version)) return ParsedPackage.Fail(json, "Missing required option: --version <version>.");
        if (string.IsNullOrWhiteSpace(tag)) return ParsedPackage.Fail(json, "Missing required option: --tag <tag-name>.");
        if (string.IsNullOrWhiteSpace(channel)) return ParsedPackage.Fail(json, "Missing required option: --channel <internal|preview|release-candidate|stable|hotfix>.");
        if (string.IsNullOrWhiteSpace(sourceState)) return ParsedPackage.Fail(json, "Missing required option: --source-state <release-source-state.json>.");
        if (string.IsNullOrWhiteSpace(tagReleaseState)) return ParsedPackage.Fail(json, "Missing required option: --tag-release-state <tag-release-state.json>.");
        if (string.IsNullOrWhiteSpace(validation)) return ParsedPackage.Fail(json, "Missing required option: --validation <validation-run-receipt.json>.");
        if (string.IsNullOrWhiteSpace(artifactReadiness)) return ParsedPackage.Fail(json, "Missing required option: --artifact-readiness <artifact-readiness.json>.");
        if (string.IsNullOrWhiteSpace(decision)) return ParsedPackage.Fail(json, "Missing required option: --decision <approved-for-release-executor|blocked|rejected>.");
        if (!TryParseDecision(decision, out _)) return ParsedPackage.Fail(json, "Unsupported --decision. Use approved-for-release-executor, blocked, or rejected.");
        if (string.IsNullOrWhiteSpace(decisionBy)) return ParsedPackage.Fail(json, "Missing required option: --decision-by <github-login>.");
        if (string.IsNullOrWhiteSpace(decisionRationale)) return ParsedPackage.Fail(json, "Missing required option: --decision-rationale <text>.");
        if (string.IsNullOrWhiteSpace(createdBy)) return ParsedPackage.Fail(json, "Missing required option: --created-by <github-login>.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedPackage.Fail(json, "Missing required option: --out <path>.");

        return new ParsedPackage(releaseCandidatePackage, repo, sourceBranch, candidateCommit, version, tag, channel, sourceState, tagReleaseState, validation, artifactReadiness, decision, decisionBy, decisionRationale, createdBy, outPath, json, null);
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
            ? ParsedRead.Fail(json, "Missing required option: --package <release-readiness-decision-package.json>.")
            : new ParsedRead(package, json, null);
    }

    private static bool TryRead(string[] args, ref int index, out string value)
    {
        value = string.Empty;
        if (index + 1 >= args.Length)
            return false;
        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static ReleaseReadinessDecision ParseDecision(string value) =>
        TryParseDecision(value, out var decision) ? decision : ReleaseReadinessDecision.Blocked;

    private static bool TryParseDecision(string value, out ReleaseReadinessDecision decision)
    {
        var normalized = value.Trim().Replace("_", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        switch (normalized)
        {
            case "approved-for-release-executor":
            case "approvedforreleaseexecutor":
                decision = ReleaseReadinessDecision.ApprovedForReleaseExecutor;
                return true;
            case "blocked":
                decision = ReleaseReadinessDecision.Blocked;
                return true;
            case "rejected":
                decision = ReleaseReadinessDecision.Rejected;
                return true;
            default:
                decision = ReleaseReadinessDecision.Blocked;
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
            ? Path.Combine(fullPath, "release-readiness-decision-package.json")
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
        error.WriteLine("  irondev release-readiness package --release-candidate-package <release-candidate-package.json> --repo <owner/name> --source-branch <main|release-branch> --candidate-commit <sha> --version <version> --tag <tag-name> --channel <internal|preview|release-candidate|stable|hotfix> --source-state <release-source-state.json> --tag-release-state <tag-release-state.json> --validation <validation-run-receipt.json> --artifact-readiness <artifact-readiness.json> --decision approved-for-release-executor --decision-by <github-login> --decision-rationale <text> --created-by <github-login> --out <path> [--json]");
        error.WriteLine("  irondev release-readiness inspect --package <release-readiness-decision-package.json> [--json]");
        error.WriteLine("  irondev release-readiness status --package <release-readiness-decision-package.json> [--json]");
        error.WriteLine("  irondev release-readiness records --package <release-readiness-decision-package.json> [--json]");
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
            boundary = ReleaseReadinessDecisionPackageBoundary.Evidence
        }, JsonOptions));
    }

    private sealed record ParsedPackage(
        string? ReleaseCandidatePackagePath,
        string? Repository,
        string? SourceBranch,
        string? CandidateCommit,
        string? Version,
        string? Tag,
        string? Channel,
        string? SourceStatePath,
        string? TagReleaseStatePath,
        string? ValidationReceiptPath,
        string? ArtifactReadinessPath,
        string? Decision,
        string? DecisionBy,
        string? DecisionRationale,
        string? CreatedBy,
        string? OutPath,
        bool Json,
        string? Error)
    {
        public static ParsedPackage Fail(bool json, string error) => new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, json, error);
    }

    private sealed record ParsedRead(string? PackagePath, bool Json, string? Error)
    {
        public static ParsedRead Fail(bool json, string error) => new(null, json, error);
    }
}
