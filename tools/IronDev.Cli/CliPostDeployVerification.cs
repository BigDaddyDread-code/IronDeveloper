using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

public static class IronDevCliPostDeployVerification
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "deploy",
        "retry-deployment",
        "rollback",
        "rollback-execute",
        "rollback-decision",
        "publish",
        "publish-package",
        "promote-memory",
        "continue",
        "continue-workflow",
        "dispatch",
        "trigger-pipeline",
        "commit",
        "push",
        "merge",
        "source-apply",
        "tag",
        "release"
    ];

    public static bool IsPostDeployVerificationCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "post-deploy-verification", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "post-deploy-verification requires a subcommand: package, inspect, status, or records.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"post-deploy-verification {args[1]} is intentionally unsupported; Block BF packages verification and rollback separation evidence only.");

        return subcommand switch
        {
            "package" => await HandlePackageAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "inspect" => HandleRead(args, output, error, "inspect"),
            "status" => HandleRead(args, output, error, "status"),
            "records" => HandleRead(args, output, error, "records"),
            _ => Usage(error, $"unsupported post-deploy-verification subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandlePackageAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePackage(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        var receipt = ReadJson<DeploymentExecutionReceipt>(Path.GetFullPath(parsed.DeploymentExecutionReceiptPath!));
        if (receipt is null)
            return Failure(output, error, parsed.Json, "post-deploy-verification package", $"deployment execution receipt is missing or invalid: {parsed.DeploymentExecutionReceiptPath}");

        var observation = ReadJson<PostDeployObservationEvidence>(Path.GetFullPath(parsed.ObservationPath!));
        if (observation is null)
            return Failure(output, error, parsed.Json, "post-deploy-verification package", $"post-deploy observation is missing or invalid: {parsed.ObservationPath}");

        var artifacts = PostDeployVerificationPackageBuilder.Build(new PostDeployVerificationPackageInput
        {
            DeploymentExecutionReceipt = receipt,
            Observation = observation,
            Repository = parsed.Repository!,
            CandidateCommitSha = parsed.CandidateCommit!,
            CandidateVersion = parsed.Version!,
            CandidateTagName = parsed.Tag!,
            ReleaseChannel = parsed.Channel!,
            DeploymentTarget = parsed.DeploymentTarget!,
            DeploymentEnvironment = parsed.DeploymentEnvironment!,
            ExpectedArtifactName = parsed.ArtifactName!,
            ExpectedArtifactSha256 = parsed.ArtifactSha256!,
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
                "post-deploy-verification package",
                IsSuccessfulPackageExit(artifacts.Package.PackageVerdict) ? "succeeded" : "blocked",
                new { outDirectory, artifacts.Package, artifacts.Receipt },
                artifacts.Package.PackageIssues,
                PostDeployVerificationBoundary.Evidence);
        else
        {
            output.WriteLine($"Post-deploy verification package: {artifacts.Package.PostDeployVerificationPackageId}");
            output.WriteLine($"Verdict: {artifacts.Package.PackageVerdict}");
            output.WriteLine($"Deployment verified: {artifacts.Package.DeploymentVerified}");
            output.WriteLine($"Can proceed to future rollback decision: {artifacts.Package.CanProceedToRollbackDecision}");
            output.WriteLine("Boundary: package evidence does not deploy, rollback, publish packages, promote memory, mutate environments, mutate source, dispatch pipelines, or continue workflow.");
        }

        return IsSuccessfulPackageExit(artifacts.Package.PackageVerdict) ? 0 : 1;
    }

    private static int HandleRead(string[] args, TextWriter output, TextWriter error, string mode)
    {
        var parsed = ParseRead(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        var package = ReadJson<PostDeployVerificationPackage>(ResolvePackagePath(parsed.PackagePath!));
        if (package is null)
            return Failure(output, error, parsed.Json, $"post-deploy-verification {mode}", "post-deploy verification package is missing or invalid.");

        if (parsed.Json)
        {
            object data = mode == "records"
                ? new { package.SourceDeploymentExecutionReceiptId, package.ObservedVersion, package.ObservedCommitSha, package.ObservedArtifactName, package.ObservedArtifactSha256, package.BlockReasons, package.PackageIssues, boundary = PostDeployVerificationBoundary.ReadOnly }
                : new { package.PostDeployVerificationPackageId, package.Repository, package.CandidateCommitSha, package.CandidateVersion, package.CandidateTagName, package.ReleaseChannel, package.DeploymentTarget, package.DeploymentEnvironment, package.PackageVerdict, package.DeploymentVerified, package.CanProceedToRollbackDecision, boundary = PostDeployVerificationBoundary.ReadOnly };
            WriteJson(output, $"post-deploy-verification {mode}", "succeeded", data, [], PostDeployVerificationBoundary.ReadOnly);
        }
        else if (mode == "records")
        {
            output.WriteLine($"Source BE receipt: {package.SourceDeploymentExecutionReceiptId}");
            output.WriteLine($"Observed version: {package.ObservedVersion ?? "unknown"}");
            output.WriteLine($"Observed commit: {package.ObservedCommitSha ?? "unknown"}");
            output.WriteLine($"Block reasons: {RenderInline(package.BlockReasons.Select(item => item.ToString()))}");
            output.WriteLine($"Issues: {RenderInline(package.PackageIssues)}");
        }
        else
        {
            output.WriteLine($"Post-deploy verification package: {package.PostDeployVerificationPackageId}");
            output.WriteLine($"Verdict: {package.PackageVerdict}");
            output.WriteLine($"Deployment verified: {package.DeploymentVerified}");
            output.WriteLine($"Can proceed to future rollback decision: {package.CanProceedToRollbackDecision}");
            output.WriteLine("Boundary: status is read-only and does not deploy, rollback, publish packages, promote memory, mutate source, mutate environments, dispatch pipelines, or continue workflow.");
        }

        return 0;
    }

    private static async Task WriteArtifactsAsync(
        string outDirectory,
        PostDeployVerificationPackageArtifacts artifacts,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "post-deploy-verification-package.json"), JsonSerializer.Serialize(artifacts.Package, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "post-deploy-verification-package-receipt.json"), JsonSerializer.Serialize(artifacts.Receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "post-deploy-verification-summary.md"), RenderSummary(artifacts), cancellationToken).ConfigureAwait(false);
        var records = new[]
        {
            JsonSerializer.Serialize(artifacts.Package, JsonOptions),
            JsonSerializer.Serialize(artifacts.Receipt, JsonOptions)
        };
        await File.WriteAllLinesAsync(Path.Combine(outDirectory, "post-deploy-verification-evidence.jsonl"), records, cancellationToken).ConfigureAwait(false);
    }

    private static string RenderSummary(PostDeployVerificationPackageArtifacts artifacts) => $"""
        # Post-Deploy Verification Package

        Verdict: `{artifacts.Package.PackageVerdict}`
        Deployment verified: `{artifacts.Package.DeploymentVerified.ToString().ToLowerInvariant()}`
        Can proceed to future rollback decision: `{artifacts.Package.CanProceedToRollbackDecision.ToString().ToLowerInvariant()}`
        Repository: `{artifacts.Package.Repository}`
        Candidate commit: `{artifacts.Package.CandidateCommitSha}`
        Candidate version: `{artifacts.Package.CandidateVersion}`
        Candidate tag: `{artifacts.Package.CandidateTagName}`
        Release channel: `{artifacts.Package.ReleaseChannel}`
        Deployment target: `{artifacts.Package.DeploymentTarget}`
        Deployment environment: `{artifacts.Package.DeploymentEnvironment}`
        Expected artifact: `{artifacts.Package.ExpectedArtifactName}`
        Source BE receipt: `{artifacts.Package.SourceDeploymentExecutionReceiptId}`
        Block reasons:
        {RenderBullets(artifacts.Package.BlockReasons.Select(item => item.ToString()))}
        Package issues:
        {RenderBullets(artifacts.Package.PackageIssues)}

        BF consumes BE deployment execution receipt.
        Deployment execution is not post-deployment verification.
        Failed verification is not rollback approval.
        Rollback consideration is not rollback decision.
        Rollback decision is not rollback execution.
        BF does not rollback.
        BF does not deploy again.
        BF does not retry deployment.
        BF does not publish packages.
        BF does not promote memory.
        BF does not continue workflow.
        BF does not mutate source.
        BF does not mutate environments.
        BF does not dispatch pipelines.
        CanProceedToRollbackDecision is not rollback execution.
        """;

    private static void RecordEvent(string outDirectory, PostDeployVerificationPackage package) =>
        new FileBackedGovernanceEventStore(outDirectory).Append(
            package.SourceDeploymentExecutionReceiptId,
            package.PostDeployVerificationPackageId,
            GovernanceKernelEventKind.PostDeployVerificationPackageCreated,
            "PostDeployVerificationPackage",
            package.PostDeployVerificationPackageId,
            $"Post-deploy verification package returned {package.PackageVerdict}.",
            ["post-deploy-verification-package.json", "post-deploy-verification-package-receipt.json"]);

    private static ParsedPackage ParsePackage(string[] args)
    {
        string? receipt = null;
        string? observation = null;
        string? repo = null;
        string? candidateCommit = null;
        string? version = null;
        string? tag = null;
        string? channel = null;
        string? deploymentTarget = null;
        string? deploymentEnvironment = null;
        string? artifactName = null;
        string? artifactSha256 = null;
        string? createdBy = null;
        string? outPath = null;
        var json = false;

        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--deployment-execution-receipt": if (!TryRead(args, ref index, out receipt)) return ParsedPackage.Fail(json, "--deployment-execution-receipt requires a value."); break;
                case "--observation": if (!TryRead(args, ref index, out observation)) return ParsedPackage.Fail(json, "--observation requires a value."); break;
                case "--repo": if (!TryRead(args, ref index, out repo)) return ParsedPackage.Fail(json, "--repo requires a value."); break;
                case "--candidate-commit": if (!TryRead(args, ref index, out candidateCommit)) return ParsedPackage.Fail(json, "--candidate-commit requires a value."); break;
                case "--version": if (!TryRead(args, ref index, out version)) return ParsedPackage.Fail(json, "--version requires a value."); break;
                case "--tag": if (!TryRead(args, ref index, out tag)) return ParsedPackage.Fail(json, "--tag requires a value."); break;
                case "--channel": if (!TryRead(args, ref index, out channel)) return ParsedPackage.Fail(json, "--channel requires a value."); break;
                case "--deployment-target": if (!TryRead(args, ref index, out deploymentTarget)) return ParsedPackage.Fail(json, "--deployment-target requires a value."); break;
                case "--deployment-environment": if (!TryRead(args, ref index, out deploymentEnvironment)) return ParsedPackage.Fail(json, "--deployment-environment requires a value."); break;
                case "--artifact-name": if (!TryRead(args, ref index, out artifactName)) return ParsedPackage.Fail(json, "--artifact-name requires a value."); break;
                case "--artifact-sha256": if (!TryRead(args, ref index, out artifactSha256)) return ParsedPackage.Fail(json, "--artifact-sha256 requires a value."); break;
                case "--created-by": if (!TryRead(args, ref index, out createdBy)) return ParsedPackage.Fail(json, "--created-by requires a value."); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedPackage.Fail(json, "--out requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedPackage.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(receipt)) return ParsedPackage.Fail(json, "Missing required option: --deployment-execution-receipt <deployment-execution-receipt.json>.");
        if (string.IsNullOrWhiteSpace(observation)) return ParsedPackage.Fail(json, "Missing required option: --observation <post-deploy-observation.json>.");
        if (string.IsNullOrWhiteSpace(repo)) return ParsedPackage.Fail(json, "Missing required option: --repo <owner/name>.");
        if (string.IsNullOrWhiteSpace(candidateCommit)) return ParsedPackage.Fail(json, "Missing required option: --candidate-commit <sha>.");
        if (string.IsNullOrWhiteSpace(version)) return ParsedPackage.Fail(json, "Missing required option: --version <version>.");
        if (string.IsNullOrWhiteSpace(tag)) return ParsedPackage.Fail(json, "Missing required option: --tag <tag-name>.");
        if (string.IsNullOrWhiteSpace(channel)) return ParsedPackage.Fail(json, "Missing required option: --channel <internal|preview|release-candidate|stable|hotfix>.");
        if (string.IsNullOrWhiteSpace(deploymentTarget)) return ParsedPackage.Fail(json, "Missing required option: --deployment-target <target-name>.");
        if (string.IsNullOrWhiteSpace(deploymentEnvironment)) return ParsedPackage.Fail(json, "Missing required option: --deployment-environment <environment>.");
        if (string.IsNullOrWhiteSpace(artifactName)) return ParsedPackage.Fail(json, "Missing required option: --artifact-name <artifact-name>.");
        if (string.IsNullOrWhiteSpace(artifactSha256)) return ParsedPackage.Fail(json, "Missing required option: --artifact-sha256 <sha256>.");
        if (string.IsNullOrWhiteSpace(createdBy)) return ParsedPackage.Fail(json, "Missing required option: --created-by <github-login>.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedPackage.Fail(json, "Missing required option: --out <path>.");

        return new ParsedPackage(receipt, observation, repo, candidateCommit, version, tag, channel, deploymentTarget, deploymentEnvironment, artifactName, artifactSha256, createdBy, outPath, json, null);
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
            ? ParsedRead.Fail(json, "Missing required option: --package <post-deploy-verification-package.json>.")
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

    private static bool IsSuccessfulPackageExit(PostDeployVerificationPackageVerdict verdict) =>
        verdict is PostDeployVerificationPackageVerdict.DeploymentVerified or PostDeployVerificationPackageVerdict.RollbackConsiderationRequired;

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
            ? Path.Combine(fullPath, "post-deploy-verification-package.json")
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
        error.WriteLine("  irondev post-deploy-verification package --deployment-execution-receipt <deployment-execution-receipt.json> --observation <post-deploy-observation.json> --repo <owner/name> --candidate-commit <sha> --version <version> --tag <tag-name> --channel <internal|preview|release-candidate|stable|hotfix> --deployment-target <target-name> --deployment-environment <environment> --artifact-name <artifact-name> --artifact-sha256 <sha256> --created-by <github-login> --out <path> [--json]");
        error.WriteLine("  irondev post-deploy-verification inspect --package <post-deploy-verification-package.json> [--json]");
        error.WriteLine("  irondev post-deploy-verification status --package <post-deploy-verification-package.json> [--json]");
        error.WriteLine("  irondev post-deploy-verification records --package <post-deploy-verification-package.json> [--json]");
        return 2;
    }

    private static int Failure(TextWriter output, TextWriter error, bool json, string command, string message)
    {
        if (json)
            WriteJson(output, command, "failed", null, [message], PostDeployVerificationBoundary.ReadOnly);
        else
            error.WriteLine(message);
        return 1;
    }

    private static void WriteJson(
        TextWriter output,
        string command,
        string status,
        object? data,
        string[] errors,
        PostDeployVerificationBoundary boundary)
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            ok = errors.Length == 0 && status != "failed" && status != "blocked",
            command,
            status,
            data,
            errors,
            boundary
        }, JsonOptions));
    }

    private sealed record ParsedPackage(
        string? DeploymentExecutionReceiptPath,
        string? ObservationPath,
        string? Repository,
        string? CandidateCommit,
        string? Version,
        string? Tag,
        string? Channel,
        string? DeploymentTarget,
        string? DeploymentEnvironment,
        string? ArtifactName,
        string? ArtifactSha256,
        string? CreatedBy,
        string? OutPath,
        bool Json,
        string? Error)
    {
        public static ParsedPackage Fail(bool json, string error) => new(null, null, null, null, null, null, null, null, null, null, null, null, null, json, error);
    }

    private sealed record ParsedRead(string? PackagePath, bool Json, string? Error)
    {
        public static ParsedRead Fail(bool json, string error) => new(null, json, error);
    }
}
