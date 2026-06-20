using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

internal static class IronDevCliDeploymentReadinessDecision
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "deploy",
        "execute",
        "publish",
        "publish-package",
        "promote",
        "promote-memory",
        "continue",
        "continue-workflow",
        "dispatch",
        "trigger-pipeline",
        "commit",
        "push",
        "merge",
        "source-apply",
        "rollback",
        "rollback-execute"
    ];

    public static bool IsDeploymentReadinessDecisionCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "deployment-readiness-decision", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "deployment-readiness-decision requires a subcommand: package, inspect, status, or records.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"deployment-readiness-decision {args[1]} is intentionally unsupported; Block BD only writes deployment-readiness decision package evidence.");

        return subcommand switch
        {
            "package" => await HandlePackageAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "inspect" => HandleRead(args, output, error, "inspect"),
            "status" => HandleRead(args, output, error, "status"),
            "records" => HandleRead(args, output, error, "records"),
            _ => Usage(error, $"unsupported deployment-readiness-decision subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandlePackageAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePackage(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        var separationPackage = ReadJson<DeploymentReadinessSeparationPackage>(Path.GetFullPath(parsed.SeparationPackagePath!));
        if (separationPackage is null)
            return Failure(output, error, parsed.Json, "deployment-readiness-decision package", $"deployment readiness separation package is missing or invalid: {parsed.SeparationPackagePath}");

        var decision = new DeploymentReadinessDecisionEvidence
        {
            DeploymentReadinessDecisionId = $"deployment_readiness_decision_{Guid.NewGuid():N}",
            Decision = ParseDecision(parsed.Decision!),
            DecisionMadeBy = parsed.DecisionBy!,
            DecisionMadeAtUtc = DateTimeOffset.UtcNow,
            DecisionRationale = parsed.DecisionRationale!,
            ExpectedDeploymentReadinessSeparationPackageId = separationPackage.DeploymentReadinessSeparationPackageId,
            ExpectedRepository = parsed.Repository!,
            ExpectedCandidateCommitSha = parsed.CandidateCommit!,
            ExpectedVersion = parsed.Version!,
            ExpectedTagName = parsed.Tag!,
            ExpectedReleaseChannel = parsed.Channel!,
            ExpectedDeploymentTarget = parsed.DeploymentTarget!,
            ExpectedDeploymentEnvironment = parsed.Environment!,
            ExpectedDeploymentArtifactName = parsed.ArtifactName!,
            ExpectedDeploymentArtifactSha256 = parsed.ArtifactSha256!
        };

        var artifacts = DeploymentReadinessDecisionPackageBuilder.Build(new DeploymentReadinessDecisionPackageInput
        {
            DeploymentReadinessSeparationPackage = separationPackage,
            DeploymentReadinessDecision = decision,
            Repository = parsed.Repository!,
            CandidateCommitSha = parsed.CandidateCommit!,
            CandidateVersion = parsed.Version!,
            CandidateTagName = parsed.Tag!,
            ReleaseChannel = parsed.Channel!,
            DeploymentTarget = parsed.DeploymentTarget!,
            DeploymentEnvironment = parsed.Environment!,
            DeploymentArtifactName = parsed.ArtifactName!,
            DeploymentArtifactSha256 = parsed.ArtifactSha256!,
            CreatedBy = parsed.CreatedBy!,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        var outDirectory = ResolveOutputDirectory(parsed.OutPath!);
        Directory.CreateDirectory(outDirectory);
        await WriteArtifactsAsync(outDirectory, artifacts, cancellationToken).ConfigureAwait(false);
        RecordEvent(outDirectory, artifacts.Package);

        if (parsed.Json)
            WriteJson(
                output,
                "deployment-readiness-decision package",
                artifacts.Package.PackageVerdict == DeploymentReadinessDecisionPackageVerdict.PackageReadyForControlledDeploymentExecutor ? "succeeded" : "blocked",
                new { outDirectory, artifacts.Package, artifacts.Receipt },
                artifacts.Package.PackageIssues);
        else
        {
            output.WriteLine($"Deployment readiness decision package: {artifacts.Package.DeploymentReadinessDecisionPackageId}");
            output.WriteLine($"Verdict: {artifacts.Package.PackageVerdict}");
            output.WriteLine($"Can proceed to future controlled deployment executor: {artifacts.Package.CanProceedToControlledDeploymentExecutor}");
            output.WriteLine("Boundary: package evidence does not deploy, publish packages, promote memory, mutate environments, execute rollback, mutate source, or continue workflow.");
        }

        return artifacts.Package.PackageVerdict == DeploymentReadinessDecisionPackageVerdict.PackageReadyForControlledDeploymentExecutor ? 0 : 1;
    }

    private static int HandleRead(string[] args, TextWriter output, TextWriter error, string mode)
    {
        var parsed = ParseRead(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        var package = ReadJson<DeploymentReadinessDecisionPackage>(ResolvePackagePath(parsed.PackagePath!));
        if (package is null)
            return Failure(output, error, parsed.Json, $"deployment-readiness-decision {mode}", "deployment readiness decision package is missing or invalid.");

        if (parsed.Json)
        {
            object data = mode == "records"
                ? new { package.SourceDeploymentReadinessSeparationPackageId, package.Decision, package.DecisionMadeBy, package.DecisionMadeAtUtc, package.DecisionRationale, package.BlockReasons, package.PackageIssues, package.Boundary }
                : new { package.DeploymentReadinessDecisionPackageId, package.Repository, package.CandidateCommitSha, package.CandidateVersion, package.CandidateTagName, package.ReleaseChannel, package.DeploymentTarget, package.DeploymentEnvironment, package.DeploymentArtifactName, package.DeploymentArtifactSha256, package.PackageVerdict, package.CanProceedToControlledDeploymentExecutor, package.Boundary };
            WriteJson(output, $"deployment-readiness-decision {mode}", "succeeded", data, []);
        }
        else if (mode == "records")
        {
            output.WriteLine($"Source BC package: {package.SourceDeploymentReadinessSeparationPackageId}");
            output.WriteLine($"Decision: {package.Decision}");
            output.WriteLine($"Decision maker: {package.DecisionMadeBy}");
            output.WriteLine($"Block reasons: {RenderInline(package.BlockReasons.Select(item => item.ToString()))}");
            output.WriteLine($"Issues: {RenderInline(package.PackageIssues)}");
        }
        else
        {
            output.WriteLine($"Deployment readiness decision package: {package.DeploymentReadinessDecisionPackageId}");
            output.WriteLine($"Verdict: {package.PackageVerdict}");
            output.WriteLine($"Can proceed to future controlled deployment executor: {package.CanProceedToControlledDeploymentExecutor}");
            output.WriteLine("Boundary: package evidence grants no deployment, package-publication, memory-promotion, environment-mutation, rollback, pipeline-dispatch, source-mutation, or continuation authority.");
        }

        return 0;
    }

    private static async Task WriteArtifactsAsync(string outDirectory, DeploymentReadinessDecisionPackageArtifacts artifacts, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "deployment-readiness-decision-package.json"), JsonSerializer.Serialize(artifacts.Package, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "deployment-readiness-decision-package-receipt.json"), JsonSerializer.Serialize(artifacts.Receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "deployment-readiness-decision-summary.md"), RenderSummary(artifacts), cancellationToken).ConfigureAwait(false);
        var records = new[]
        {
            JsonSerializer.Serialize(artifacts.Package, JsonOptions),
            JsonSerializer.Serialize(artifacts.Receipt, JsonOptions)
        };
        await File.WriteAllLinesAsync(Path.Combine(outDirectory, "deployment-readiness-decision-evidence.jsonl"), records, cancellationToken).ConfigureAwait(false);
    }

    private static string RenderSummary(DeploymentReadinessDecisionPackageArtifacts artifacts) => $"""
        # Deployment Readiness Decision Package

        Verdict: `{artifacts.Package.PackageVerdict}`
        Can proceed to future controlled deployment executor: `{artifacts.Package.CanProceedToControlledDeploymentExecutor.ToString().ToLowerInvariant()}`
        Repository: `{artifacts.Package.Repository}`
        Candidate commit: `{artifacts.Package.CandidateCommitSha}`
        Candidate version: `{artifacts.Package.CandidateVersion}`
        Candidate tag: `{artifacts.Package.CandidateTagName}`
        Release channel: `{artifacts.Package.ReleaseChannel}`
        Deployment target: `{artifacts.Package.DeploymentTarget}`
        Deployment environment: `{artifacts.Package.DeploymentEnvironment}`
        Deployment artifact: `{artifacts.Package.DeploymentArtifactName}`
        Source BC package: `{artifacts.Package.SourceDeploymentReadinessSeparationPackageId}`
        Block reasons:
        {RenderBullets(artifacts.Package.BlockReasons.Select(item => item.ToString()))}
        Package issues:
        {RenderBullets(artifacts.Package.PackageIssues)}

        BD consumes BC deployment-readiness separation evidence.
        BC separation package is not deployment readiness decision.
        Deployment readiness decision package is not deployment execution.
        BD does not deploy.
        BD does not publish packages.
        BD does not promote memory.
        BD does not continue workflow.
        BD does not mutate environments.
        BD does not mutate source.
        BD does not execute rollback.
        CanProceedToControlledDeploymentExecutor is not deployment execution.

        Boundary: BD produces deployment-readiness decision package evidence only. It does not deploy, publish packages, promote memory, mutate environments, mutate source, execute rollback, dispatch pipelines, or continue workflow.
        """;

    private static void RecordEvent(string outDirectory, DeploymentReadinessDecisionPackage package) =>
        new FileBackedGovernanceEventStore(outDirectory).Append(
            package.DeploymentReadinessDecisionPackageId,
            package.DeploymentReadinessDecisionPackageId,
            GovernanceKernelEventKind.DeploymentReadinessDecisionPackageCreated,
            "DeploymentReadinessDecisionPackage",
            package.DeploymentReadinessDecisionPackageId,
            "Deployment readiness decision package was created.",
            ["deployment-readiness-decision-package.json", "deployment-readiness-decision-package-receipt.json"]);

    private static ParsedPackage ParsePackage(string[] args)
    {
        string? separationPackage = null;
        string? repo = null;
        string? candidateCommit = null;
        string? version = null;
        string? tag = null;
        string? channel = null;
        string? deploymentTarget = null;
        string? environment = null;
        string? artifactName = null;
        string? artifactSha256 = null;
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
                case "--deployment-readiness-separation-package": if (!TryRead(args, ref index, out separationPackage)) return ParsedPackage.Fail(json, "--deployment-readiness-separation-package requires a value."); break;
                case "--repo": if (!TryRead(args, ref index, out repo)) return ParsedPackage.Fail(json, "--repo requires a value."); break;
                case "--candidate-commit": if (!TryRead(args, ref index, out candidateCommit)) return ParsedPackage.Fail(json, "--candidate-commit requires a value."); break;
                case "--version": if (!TryRead(args, ref index, out version)) return ParsedPackage.Fail(json, "--version requires a value."); break;
                case "--tag": if (!TryRead(args, ref index, out tag)) return ParsedPackage.Fail(json, "--tag requires a value."); break;
                case "--channel": if (!TryRead(args, ref index, out channel)) return ParsedPackage.Fail(json, "--channel requires a value."); break;
                case "--deployment-target": if (!TryRead(args, ref index, out deploymentTarget)) return ParsedPackage.Fail(json, "--deployment-target requires a value."); break;
                case "--deployment-environment": if (!TryRead(args, ref index, out environment)) return ParsedPackage.Fail(json, "--deployment-environment requires a value."); break;
                case "--artifact-name": if (!TryRead(args, ref index, out artifactName)) return ParsedPackage.Fail(json, "--artifact-name requires a value."); break;
                case "--artifact-sha256": if (!TryRead(args, ref index, out artifactSha256)) return ParsedPackage.Fail(json, "--artifact-sha256 requires a value."); break;
                case "--decision": if (!TryRead(args, ref index, out decision)) return ParsedPackage.Fail(json, "--decision requires a value."); break;
                case "--decision-by": if (!TryRead(args, ref index, out decisionBy)) return ParsedPackage.Fail(json, "--decision-by requires a value."); break;
                case "--decision-rationale": if (!TryRead(args, ref index, out decisionRationale)) return ParsedPackage.Fail(json, "--decision-rationale requires a value."); break;
                case "--created-by": if (!TryRead(args, ref index, out createdBy)) return ParsedPackage.Fail(json, "--created-by requires a value."); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedPackage.Fail(json, "--out requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedPackage.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(separationPackage)) return ParsedPackage.Fail(json, "Missing required option: --deployment-readiness-separation-package <deployment-readiness-separation-package.json>.");
        if (string.IsNullOrWhiteSpace(repo)) return ParsedPackage.Fail(json, "Missing required option: --repo <owner/name>.");
        if (string.IsNullOrWhiteSpace(candidateCommit)) return ParsedPackage.Fail(json, "Missing required option: --candidate-commit <sha>.");
        if (string.IsNullOrWhiteSpace(version)) return ParsedPackage.Fail(json, "Missing required option: --version <version>.");
        if (string.IsNullOrWhiteSpace(tag)) return ParsedPackage.Fail(json, "Missing required option: --tag <tag-name>.");
        if (string.IsNullOrWhiteSpace(channel)) return ParsedPackage.Fail(json, "Missing required option: --channel <internal|preview|release-candidate|stable|hotfix>.");
        if (string.IsNullOrWhiteSpace(deploymentTarget)) return ParsedPackage.Fail(json, "Missing required option: --deployment-target <target-name>.");
        if (string.IsNullOrWhiteSpace(environment)) return ParsedPackage.Fail(json, "Missing required option: --deployment-environment <environment>.");
        if (string.IsNullOrWhiteSpace(artifactName)) return ParsedPackage.Fail(json, "Missing required option: --artifact-name <name>.");
        if (string.IsNullOrWhiteSpace(artifactSha256)) return ParsedPackage.Fail(json, "Missing required option: --artifact-sha256 <sha256>.");
        if (string.IsNullOrWhiteSpace(decision)) return ParsedPackage.Fail(json, "Missing required option: --decision <approved-for-controlled-deployment-executor|rejected|needs-more-evidence>.");
        if (!TryParseDecision(decision, out _)) return ParsedPackage.Fail(json, "Unsupported --decision. Use approved-for-controlled-deployment-executor, rejected, or needs-more-evidence.");
        if (string.IsNullOrWhiteSpace(decisionBy)) return ParsedPackage.Fail(json, "Missing required option: --decision-by <github-login>.");
        if (string.IsNullOrWhiteSpace(decisionRationale)) return ParsedPackage.Fail(json, "Missing required option: --decision-rationale <text>.");
        if (string.IsNullOrWhiteSpace(createdBy)) return ParsedPackage.Fail(json, "Missing required option: --created-by <github-login>.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedPackage.Fail(json, "Missing required option: --out <path>.");

        return new ParsedPackage(separationPackage, repo, candidateCommit, version, tag, channel, deploymentTarget, environment, artifactName, artifactSha256, decision, decisionBy, decisionRationale, createdBy, outPath, json, null);
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
            ? ParsedRead.Fail(json, "Missing required option: --package <deployment-readiness-decision-package.json>.")
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

    private static DeploymentReadinessDecision ParseDecision(string value) =>
        TryParseDecision(value, out var decision) ? decision : DeploymentReadinessDecision.NeedsMoreEvidence;

    private static bool TryParseDecision(string value, out DeploymentReadinessDecision decision)
    {
        var normalized = value.Trim().Replace("_", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        switch (normalized)
        {
            case "approved-for-controlled-deployment-executor":
            case "approvedforcontrolleddeploymentexecutor":
                decision = DeploymentReadinessDecision.ApprovedForControlledDeploymentExecutor;
                return true;
            case "rejected":
                decision = DeploymentReadinessDecision.Rejected;
                return true;
            case "needs-more-evidence":
            case "needsmoreevidence":
                decision = DeploymentReadinessDecision.NeedsMoreEvidence;
                return true;
            default:
                decision = DeploymentReadinessDecision.NeedsMoreEvidence;
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
            ? Path.Combine(fullPath, "deployment-readiness-decision-package.json")
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
        error.WriteLine("  irondev deployment-readiness-decision package --deployment-readiness-separation-package <deployment-readiness-separation-package.json> --repo <owner/name> --candidate-commit <sha> --version <version> --tag <tag-name> --channel <internal|preview|release-candidate|stable|hotfix> --deployment-target <target-name> --deployment-environment <environment> --artifact-name <name> --artifact-sha256 <sha256> --decision approved-for-controlled-deployment-executor --decision-by <github-login> --decision-rationale <text> --created-by <github-login> --out <path> [--json]");
        error.WriteLine("  irondev deployment-readiness-decision inspect --package <deployment-readiness-decision-package.json> [--json]");
        error.WriteLine("  irondev deployment-readiness-decision status --package <deployment-readiness-decision-package.json> [--json]");
        error.WriteLine("  irondev deployment-readiness-decision records --package <deployment-readiness-decision-package.json> [--json]");
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
            boundary = DeploymentReadinessDecisionPackageBoundary.Evidence
        }, JsonOptions));
    }

    private sealed record ParsedPackage(
        string? SeparationPackagePath,
        string? Repository,
        string? CandidateCommit,
        string? Version,
        string? Tag,
        string? Channel,
        string? DeploymentTarget,
        string? Environment,
        string? ArtifactName,
        string? ArtifactSha256,
        string? Decision,
        string? DecisionBy,
        string? DecisionRationale,
        string? CreatedBy,
        string? OutPath,
        bool Json,
        string? Error)
    {
        public static ParsedPackage Fail(bool json, string error) => new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, json, error);
    }

    private sealed record ParsedRead(string? PackagePath, bool Json, string? Error)
    {
        public static ParsedRead Fail(bool json, string error) => new(null, json, error);
    }
}
