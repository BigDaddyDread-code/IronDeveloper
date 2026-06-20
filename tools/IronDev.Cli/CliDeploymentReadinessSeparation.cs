using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

internal static class IronDevCliDeploymentReadinessSeparation
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

    public static bool IsDeploymentReadinessSeparationCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "deployment-readiness-separation", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "deployment-readiness-separation requires a subcommand: package, inspect, status, or records.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"deployment-readiness-separation {args[1]} is intentionally unsupported; Block BC only writes deployment-readiness separation package evidence.");

        return subcommand switch
        {
            "package" => await HandlePackageAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "inspect" => HandleRead(args, output, error, "inspect"),
            "status" => HandleRead(args, output, error, "status"),
            "records" => HandleRead(args, output, error, "records"),
            _ => Usage(error, $"unsupported deployment-readiness-separation subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandlePackageAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePackage(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        var releaseReceipt = ReadJson<ReleaseExecutionReceipt>(Path.GetFullPath(parsed.ReleaseExecutionReceiptPath!));
        if (releaseReceipt is null)
            return Failure(output, error, parsed.Json, "deployment-readiness-separation package", $"release execution receipt is missing or invalid: {parsed.ReleaseExecutionReceiptPath}");

        var artifacts = DeploymentReadinessSeparationPackageBuilder.Build(new DeploymentReadinessSeparationInput
        {
            ReleaseExecutionReceipt = releaseReceipt,
            Repository = parsed.Repository!,
            CandidateCommitSha = parsed.CandidateCommit!,
            CandidateVersion = parsed.Version!,
            CandidateTagName = parsed.Tag!,
            ReleaseChannel = parsed.Channel!,
            DeploymentTarget = parsed.DeploymentTarget!,
            DeploymentReadinessScope = parsed.Scope!,
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
                "deployment-readiness-separation package",
                artifacts.Package.PackageVerdict == DeploymentReadinessSeparationVerdict.PackageReadyForDeploymentReadinessDecision ? "succeeded" : "blocked",
                new { outDirectory, artifacts.Package, artifacts.Receipt },
                artifacts.Package.PackageIssues);
        else
        {
            output.WriteLine($"Deployment readiness separation package: {artifacts.Package.DeploymentReadinessSeparationPackageId}");
            output.WriteLine($"Verdict: {artifacts.Package.PackageVerdict}");
            output.WriteLine($"Can proceed to future deployment-readiness decision: {artifacts.Package.CanProceedToDeploymentReadinessDecision}");
            output.WriteLine("Boundary: package evidence does not deploy, publish packages, promote memory, dispatch pipelines, execute rollback, mutate source, or continue workflow.");
        }

        return artifacts.Package.PackageVerdict == DeploymentReadinessSeparationVerdict.PackageReadyForDeploymentReadinessDecision ? 0 : 1;
    }

    private static int HandleRead(string[] args, TextWriter output, TextWriter error, string mode)
    {
        var parsed = ParseRead(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        var package = ReadJson<DeploymentReadinessSeparationPackage>(ResolvePackagePath(parsed.PackagePath!));
        if (package is null)
            return Failure(output, error, parsed.Json, $"deployment-readiness-separation {mode}", "deployment readiness separation package is missing or invalid.");

        if (parsed.Json)
        {
            object data = mode == "records"
                ? new { package.SourceReleaseExecutionReceiptId, package.BlockReasons, package.PackageIssues, package.Boundary }
                : new { package.DeploymentReadinessSeparationPackageId, package.Repository, package.CandidateCommitSha, package.CandidateVersion, package.CandidateTagName, package.ReleaseChannel, package.DeploymentTarget, package.DeploymentReadinessScope, package.PackageVerdict, package.CanProceedToDeploymentReadinessDecision, package.Boundary };
            WriteJson(output, $"deployment-readiness-separation {mode}", "succeeded", data, []);
        }
        else if (mode == "records")
        {
            output.WriteLine($"Source release execution receipt: {package.SourceReleaseExecutionReceiptId}");
            output.WriteLine($"Block reasons: {RenderInline(package.BlockReasons.Select(item => item.ToString()))}");
            output.WriteLine($"Issues: {RenderInline(package.PackageIssues)}");
        }
        else
        {
            output.WriteLine($"Deployment readiness separation package: {package.DeploymentReadinessSeparationPackageId}");
            output.WriteLine($"Verdict: {package.PackageVerdict}");
            output.WriteLine($"Can proceed to future deployment-readiness decision: {package.CanProceedToDeploymentReadinessDecision}");
            output.WriteLine("Boundary: package evidence grants no deployment, package-publication, memory-promotion, rollback, pipeline-dispatch, source-mutation, or continuation authority.");
        }

        return 0;
    }

    private static async Task WriteArtifactsAsync(string outDirectory, DeploymentReadinessSeparationArtifacts artifacts, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "deployment-readiness-separation-package.json"), JsonSerializer.Serialize(artifacts.Package, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "deployment-readiness-separation-receipt.json"), JsonSerializer.Serialize(artifacts.Receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "deployment-readiness-separation-summary.md"), RenderSummary(artifacts), cancellationToken).ConfigureAwait(false);
        var records = new[]
        {
            JsonSerializer.Serialize(artifacts.Package, JsonOptions),
            JsonSerializer.Serialize(artifacts.Receipt, JsonOptions)
        };
        await File.WriteAllLinesAsync(Path.Combine(outDirectory, "deployment-readiness-separation-evidence.jsonl"), records, cancellationToken).ConfigureAwait(false);
    }

    private static string RenderSummary(DeploymentReadinessSeparationArtifacts artifacts) => $"""
        # Deployment Readiness Separation Package

        Verdict: `{artifacts.Package.PackageVerdict}`
        Can proceed to future deployment-readiness decision: `{artifacts.Package.CanProceedToDeploymentReadinessDecision.ToString().ToLowerInvariant()}`
        Repository: `{artifacts.Package.Repository}`
        Candidate commit: `{artifacts.Package.CandidateCommitSha}`
        Candidate version: `{artifacts.Package.CandidateVersion}`
        Candidate tag: `{artifacts.Package.CandidateTagName}`
        Release channel: `{artifacts.Package.ReleaseChannel}`
        Deployment target: `{artifacts.Package.DeploymentTarget}`
        Deployment readiness scope: `{artifacts.Package.DeploymentReadinessScope}`
        Source release execution receipt: `{artifacts.Package.SourceReleaseExecutionReceiptId}`
        Block reasons:
        {RenderBullets(artifacts.Package.BlockReasons.Select(item => item.ToString()))}
        Package issues:
        {RenderBullets(artifacts.Package.PackageIssues)}

        BC consumes BB release execution receipt.
        Release execution is not deployment readiness.
        Release execution receipt is not deployment authority.
        Deployment readiness separation is not deployment readiness decision.
        Deployment readiness decision is not deployment execution.
        BC does not deploy.
        BC does not publish packages.
        BC does not promote memory.
        BC does not continue workflow.
        BC does not mutate source.
        BC does not dispatch deployment pipelines.
        BC does not execute rollback.
        A tag is not deployment.
        A GitHub release is not deployment.
        Uploaded release artifacts are not package publication.

        Boundary: BC produces deployment-readiness separation evidence only. It does not decide deployment readiness, deploy, publish packages, promote memory, dispatch pipelines, mutate environments, mutate source, execute rollback, or continue workflow.
        """;

    private static void RecordEvent(string outDirectory, DeploymentReadinessSeparationPackage package) =>
        new FileBackedGovernanceEventStore(outDirectory).Append(
            package.DeploymentReadinessSeparationPackageId,
            package.DeploymentReadinessSeparationPackageId,
            GovernanceKernelEventKind.DeploymentReadinessSeparationPackageCreated,
            "DeploymentReadinessSeparationPackage",
            package.DeploymentReadinessSeparationPackageId,
            "Deployment readiness separation package was created.",
            ["deployment-readiness-separation-package.json", "deployment-readiness-separation-receipt.json"]);

    private static ParsedPackage ParsePackage(string[] args)
    {
        string? releaseExecutionReceipt = null;
        string? repo = null;
        string? candidateCommit = null;
        string? version = null;
        string? tag = null;
        string? channel = null;
        string? deploymentTarget = null;
        string? scope = null;
        string? createdBy = null;
        string? outPath = null;
        var json = false;

        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--release-execution-receipt": if (!TryRead(args, ref index, out releaseExecutionReceipt)) return ParsedPackage.Fail(json, "--release-execution-receipt requires a value."); break;
                case "--repo": if (!TryRead(args, ref index, out repo)) return ParsedPackage.Fail(json, "--repo requires a value."); break;
                case "--candidate-commit": if (!TryRead(args, ref index, out candidateCommit)) return ParsedPackage.Fail(json, "--candidate-commit requires a value."); break;
                case "--version": if (!TryRead(args, ref index, out version)) return ParsedPackage.Fail(json, "--version requires a value."); break;
                case "--tag": if (!TryRead(args, ref index, out tag)) return ParsedPackage.Fail(json, "--tag requires a value."); break;
                case "--channel": if (!TryRead(args, ref index, out channel)) return ParsedPackage.Fail(json, "--channel requires a value."); break;
                case "--deployment-target": if (!TryRead(args, ref index, out deploymentTarget)) return ParsedPackage.Fail(json, "--deployment-target requires a value."); break;
                case "--scope": if (!TryRead(args, ref index, out scope)) return ParsedPackage.Fail(json, "--scope requires a value."); break;
                case "--created-by": if (!TryRead(args, ref index, out createdBy)) return ParsedPackage.Fail(json, "--created-by requires a value."); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedPackage.Fail(json, "--out requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedPackage.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(releaseExecutionReceipt)) return ParsedPackage.Fail(json, "Missing required option: --release-execution-receipt <release-execution-receipt.json>.");
        if (string.IsNullOrWhiteSpace(repo)) return ParsedPackage.Fail(json, "Missing required option: --repo <owner/name>.");
        if (string.IsNullOrWhiteSpace(candidateCommit)) return ParsedPackage.Fail(json, "Missing required option: --candidate-commit <sha>.");
        if (string.IsNullOrWhiteSpace(version)) return ParsedPackage.Fail(json, "Missing required option: --version <version>.");
        if (string.IsNullOrWhiteSpace(tag)) return ParsedPackage.Fail(json, "Missing required option: --tag <tag-name>.");
        if (string.IsNullOrWhiteSpace(channel)) return ParsedPackage.Fail(json, "Missing required option: --channel <internal|preview|release-candidate|stable|hotfix>.");
        if (string.IsNullOrWhiteSpace(deploymentTarget)) return ParsedPackage.Fail(json, "Missing required option: --deployment-target <target-name>.");
        if (string.IsNullOrWhiteSpace(scope)) return ParsedPackage.Fail(json, "Missing required option: --scope <scope>.");
        if (string.IsNullOrWhiteSpace(createdBy)) return ParsedPackage.Fail(json, "Missing required option: --created-by <github-login>.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedPackage.Fail(json, "Missing required option: --out <path>.");

        return new ParsedPackage(releaseExecutionReceipt, repo, candidateCommit, version, tag, channel, deploymentTarget, scope, createdBy, outPath, json, null);
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
            ? ParsedRead.Fail(json, "Missing required option: --package <deployment-readiness-separation-package.json>.")
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
            ? Path.Combine(fullPath, "deployment-readiness-separation-package.json")
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
        error.WriteLine("  irondev deployment-readiness-separation package --release-execution-receipt <release-execution-receipt.json> --repo <owner/name> --candidate-commit <sha> --version <version> --tag <tag-name> --channel <internal|preview|release-candidate|stable|hotfix> --deployment-target <target-name> --scope <scope> --created-by <github-login> --out <path> [--json]");
        error.WriteLine("  irondev deployment-readiness-separation inspect --package <deployment-readiness-separation-package.json> [--json]");
        error.WriteLine("  irondev deployment-readiness-separation status --package <deployment-readiness-separation-package.json> [--json]");
        error.WriteLine("  irondev deployment-readiness-separation records --package <deployment-readiness-separation-package.json> [--json]");
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
            boundary = DeploymentReadinessSeparationBoundary.Evidence
        }, JsonOptions));
    }

    private sealed record ParsedPackage(
        string? ReleaseExecutionReceiptPath,
        string? Repository,
        string? CandidateCommit,
        string? Version,
        string? Tag,
        string? Channel,
        string? DeploymentTarget,
        string? Scope,
        string? CreatedBy,
        string? OutPath,
        bool Json,
        string? Error)
    {
        public static ParsedPackage Fail(bool json, string error) => new(null, null, null, null, null, null, null, null, null, null, json, error);
    }

    private sealed record ParsedRead(string? PackagePath, bool Json, string? Error)
    {
        public static ParsedRead Fail(bool json, string error) => new(null, json, error);
    }
}
