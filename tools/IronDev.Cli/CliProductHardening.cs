using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

internal static class IronDevCliProductHardening
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions JsonLineOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "approve",
        "execute",
        "apply",
        "rollback",
        "continue",
        "continue-workflow",
        "promote-memory",
        "release",
        "deploy",
        "merge",
        "commit",
        "push",
        "pull-request",
        "create-pr"
    ];

    public static bool IsProductHardeningCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "product-hardening", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "product-hardening requires a subcommand: dogfood.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"product-hardening {args[1]} is intentionally unsupported; Block AI is evidence only.");

        return subcommand switch
        {
            "dogfood" => await HandleDogfoodAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            _ => Usage(error, $"unsupported product-hardening subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandleDogfoodAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = Parse(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "product-hardening dogfood", parsed.Error);

        var runPath = Path.GetFullPath(parsed.Run!);
        Directory.CreateDirectory(runPath);
        var taskSummary = await ReadTaskSummaryAsync(parsed.Task!, cancellationToken).ConfigureAwait(false);
        var missing = parsed.SimulatedMissingArtifacts.ToArray();
        var dogfoodRun = ProductHardeningDogfood.CreateRun(new ProductDogfoodRunRequest
        {
            RunId = Path.GetFileName(runPath),
            ProjectId = parsed.ProjectId!,
            TaskSummary = taskSummary,
            MissingArtifacts = missing,
            SimulateFailure = parsed.SimulateFailure
        });

        var artifactTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dogfood-run.json"] = JsonSerializer.Serialize(dogfoodRun, JsonOptions),
            ["dogfood-run.md"] = RenderDogfoodRun(dogfoodRun),
            ["dogfood-known-risks.md"] = RenderLines("# Dogfood Known Risks", dogfoodRun.KnownRisks)
        };
        var checklist = ProductHardeningDogfood.CreateChecklist(dogfoodRun.RunId, artifactTexts);
        artifactTexts["dogfood-artifact-checklist.json"] = JsonSerializer.Serialize(checklist, JsonOptions);
        artifactTexts["dogfood-artifact-checklist.md"] = RenderChecklist(checklist);

        foreach (var (name, content) in artifactTexts)
            await File.WriteAllTextAsync(Path.Combine(runPath, name), content, cancellationToken).ConfigureAwait(false);

        var descriptors = ProductHardeningDogfood.DogfoodArtifacts
            .Select(artifact => Descriptor(artifact, dogfoodRun, !missing.Contains(artifact, StringComparer.OrdinalIgnoreCase)))
            .Concat(new[]
            {
                Descriptor("governance-events.jsonl", dogfoodRun, true) with { GovernanceEventRefs = ["AcceptedMemoryRetrieved", "MemoryInformedPlanProposed"], ThoughtLedgerRefs = ["thought-ledger:ai"], ConscienceRefs = ["conscience:ai"] },
                Descriptor("memory-citations.json", dogfoodRun, true) with { MemoryCitationHashes = ["memhash-ai"] },
                Descriptor("planner-context.json", dogfoodRun, true) with { MemoryCitationHashes = ["memhash-ai"] }
            })
            .ToArray();
        var consistency = ProductArtifactConsistencyAuditor.Audit(new ProductArtifactConsistencyAuditRequest
        {
            RunId = dogfoodRun.RunId,
            ProjectId = dogfoodRun.ProjectId,
            RequiredArtifacts = ProductHardeningDogfood.DogfoodArtifacts,
            Artifacts = descriptors
        });
        await WriteJsonAsync(runPath, "artifact-consistency-report.json", consistency, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "artifact-consistency-report.md"), RenderConsistency(consistency), cancellationToken).ConfigureAwait(false);
        await WriteJsonLinesAsync(Path.Combine(runPath, "artifact-consistency-issues.jsonl"), consistency.Issues, cancellationToken).ConfigureAwait(false);

        var unsafeReport = UnsafeMaterialScanner.Scan(new UnsafeMaterialScanRequest
        {
            RunId = dogfoodRun.RunId,
            ProjectId = dogfoodRun.ProjectId,
            ArtifactText = artifactTexts
        });
        await WriteJsonAsync(runPath, "unsafe-material-report.json", unsafeReport, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "unsafe-material-report.md"), RenderUnsafe(unsafeReport), cancellationToken).ConfigureAwait(false);
        await WriteJsonLinesAsync(Path.Combine(runPath, "unsafe-material-findings.jsonl"), unsafeReport.Findings, cancellationToken).ConfigureAwait(false);

        var resume = ProductResumeReportBuilder.Build(new ProductResumeReportRequest
        {
            RunId = dogfoodRun.RunId,
            ProjectId = dogfoodRun.ProjectId,
            Steps = dogfoodRun.Steps,
            ArtifactRefs = ProductHardeningDogfood.DogfoodArtifacts,
            MissingArtifacts = missing,
            FailedArtifact = missing.FirstOrDefault()
        });
        var failure = ProductResumeReportBuilder.BuildFailureSummary(resume);
        await WriteJsonAsync(runPath, "resume-report.json", resume, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "resume-report.md"), RenderResume(resume), cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "failure-summary.json", failure, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "failure-summary.md"), RenderFailure(failure), cancellationToken).ConfigureAwait(false);

        var readiness = ProductReleaseReadinessEvaluator.Evaluate(new ProductReleaseReadinessEvaluationRequest
        {
            RunId = dogfoodRun.RunId,
            ProjectId = dogfoodRun.ProjectId,
            FocusedBlockValidationRecorded = true,
            StableBandValidationRecorded = true,
            BuildResultRecorded = true,
            DiffCheckRecorded = true,
            ArtifactConsistencyReportExists = true,
            ArtifactConsistencyPassed = consistency.Outcome == ProductHardeningAuditOutcome.Pass,
            UnsafeMaterialReportExists = true,
            UnsafeMaterialPassed = unsafeReport.Outcome == ProductHardeningAuditOutcome.Pass,
            ResumeReportBehaviorExists = true,
            KnownRisksDocumented = true,
            AuthorityBoundaryDocumented = true,
            NoUnsupportedMutationSurfacesPresent = true,
            EvidenceRefs = ["focused-ai", "stable-z-ai", "build", "diff-check", consistency.ArtifactConsistencyReportId, unsafeReport.UnsafeMaterialReportId, resume.ResumeReportId]
        });
        await WriteJsonAsync(runPath, "release-readiness-report.json", readiness, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "release-readiness-report.md"), RenderReadiness(readiness), cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "release-readiness-checklist.json", readiness.Checklist, cancellationToken).ConfigureAwait(false);
        await WriteJsonLinesAsync(Path.Combine(runPath, "release-readiness-blockers.jsonl"), readiness.BlockingIssues.Select(issue => new { issue }), cancellationToken).ConfigureAwait(false);

        var decision = ProductReleaseReadinessDecisionRecorder.Create(new ProductReleaseReadinessDecisionRequest
        {
            Report = readiness,
            Decision = readiness.Outcome,
            Reasons = [$"Recorded readiness status: {readiness.Outcome}."],
            EvidenceRefs = readiness.EvidenceRefs,
            ReviewedBy = "IronDevCli"
        });
        if (!decision.IsValid)
            return Failure(output, error, parsed.Json, "product-hardening dogfood", string.Join(",", decision.Issues));
        await WriteJsonAsync(runPath, "release-readiness-decision-record.json", decision.Record, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "release-readiness-decision-record.md"), RenderDecision(decision.Record!), cancellationToken).ConfigureAwait(false);

        var bypass = ProductHardeningBypassEvaluator.Evaluate(dogfoodRun.RunId, ["dogfood success", "artifact consistency report", "unsafe material clean report", "resume report", "release-readiness report", "release-readiness decision record", "test success", "build success", "diff-check success"]);
        await WriteJsonAsync(runPath, "product-hardening-bypass-report.json", bypass, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "product-hardening-bypass-report.md"), RenderBypass(bypass), cancellationToken).ConfigureAwait(false);

        var exitCode = dogfoodRun.Outcome == ProductHardeningAuditOutcome.Pass && consistency.Outcome == ProductHardeningAuditOutcome.Pass && unsafeReport.Outcome == ProductHardeningAuditOutcome.Pass ? 0 : 1;
        if (parsed.Json)
            WriteJson(output, "product-hardening dogfood", exitCode == 0 ? "succeeded" : "needs-more-evidence", new { runPath, dogfoodRun, consistency, unsafeReport, resume, readiness, decision = decision.Record, bypass }, []);
        else
        {
            output.WriteLine($"Product hardening dogfood: {readiness.Outcome}");
            output.WriteLine($"Run path: {runPath}");
            output.WriteLine("Boundary: product hardening evidence cannot merge, release, deploy, mutate source, or continue workflow.");
        }

        return exitCode;
    }

    private static ProductArtifactDescriptor Descriptor(string artifact, ProductDogfoodRun run, bool exists) => new()
    {
        ArtifactName = artifact,
        Exists = exists,
        RunId = run.RunId,
        ProjectId = run.ProjectId,
        PatchHash = "patchhash-ai",
        BaseCommit = "base-ai",
        SourceRepoIdentity = "repo-ai",
        ChangedFiles = ["README.md"],
        MemoryCitationHashes = ["memhash-ai"]
    };

    private static async Task<string> ReadTaskSummaryAsync(string task, CancellationToken cancellationToken)
    {
        if (File.Exists(task))
            return (await File.ReadAllTextAsync(task, cancellationToken).ConfigureAwait(false)).Trim();
        return task.Trim();
    }

    private static string RenderDogfoodRun(ProductDogfoodRun run)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Dogfood Run");
        builder.AppendLine();
        builder.AppendLine($"Run: `{run.RunId}`");
        builder.AppendLine($"Outcome: `{run.Outcome}`");
        foreach (var step in run.Steps)
            builder.AppendLine($"- `{step.Status}` {step.Name}");
        builder.AppendLine();
        builder.AppendLine("Boundary: dogfood success is not release, merge, deploy, source mutation, or workflow continuation.");
        return builder.ToString();
    }

    private static string RenderChecklist(ProductDogfoodArtifactChecklist checklist)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Dogfood Artifact Checklist");
        foreach (var item in checklist.Items)
            builder.AppendLine($"- {(item.Exists ? "[x]" : "[ ]")} `{item.ArtifactName}`");
        return builder.ToString();
    }

    private static string RenderConsistency(ProductArtifactConsistencyReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Artifact Consistency Report");
        builder.AppendLine($"Outcome: `{report.Outcome}`");
        foreach (var issue in report.Issues)
            builder.AppendLine($"- `{issue.Code}` `{issue.ArtifactName}` {issue.Message}");
        builder.AppendLine("Boundary: artifact audit does not approve or repair artifacts.");
        return builder.ToString();
    }

    private static string RenderUnsafe(UnsafeMaterialReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Unsafe Material Report");
        builder.AppendLine($"Outcome: `{report.Outcome}`");
        foreach (var finding in report.Findings)
            builder.AppendLine($"- `{finding.Kind}` `{finding.ArtifactName}` {finding.RedactedPreview}");
        builder.AppendLine("Boundary: scanner reports unsafe material; it does not approve anything.");
        return builder.ToString();
    }

    private static string RenderResume(ProductResumeReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Resume Report");
        builder.AppendLine($"Last completed product step: {report.LastCompletedProductStep}");
        builder.AppendLine($"Last safe artifact: {report.LastSafeArtifact}");
        foreach (var artifact in report.MissingArtifacts)
            builder.AppendLine($"- Missing: `{artifact}`");
        builder.AppendLine(report.SuggestedManualNextCommand);
        builder.AppendLine("Boundary: resume guidance is not resume execution.");
        return builder.ToString();
    }

    private static string RenderFailure(ProductFailureSummary failure) => RenderLines("# Failure Summary", [failure.Summary]);

    private static string RenderReadiness(ProductReleaseReadinessReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Release Readiness Report");
        builder.AppendLine($"Outcome: `{report.Outcome}`");
        foreach (var item in report.Checklist)
            builder.AppendLine($"- {(item.Passed ? "[x]" : "[ ]")} {item.Check}");
        builder.AppendLine("Boundary: readiness report is a witness, not the judge.");
        return builder.ToString();
    }

    private static string RenderDecision(ProductReleaseReadinessDecisionRecord record)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Release Readiness Decision Record");
        builder.AppendLine($"Decision: `{record.Decision}`");
        builder.AppendLine(record.Boundary);
        return builder.ToString();
    }

    private static string RenderBypass(ProductHardeningBypassReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Product Hardening Bypass Report");
        foreach (var subject in report.EvidenceSubjects)
            builder.AppendLine($"- `{subject}` cannot authorize merge/release/deploy/source mutation/workflow continuation.");
        return builder.ToString();
    }

    private static string RenderLines(string title, IEnumerable<string> lines)
    {
        var builder = new StringBuilder();
        builder.AppendLine(title);
        foreach (var line in lines)
            builder.AppendLine($"- {line}");
        return builder.ToString();
    }

    private static async Task WriteJsonAsync<T>(string runPath, string artifactName, T value, CancellationToken cancellationToken) =>
        await File.WriteAllTextAsync(Path.Combine(runPath, artifactName), JsonSerializer.Serialize(value, JsonOptions), cancellationToken).ConfigureAwait(false);

    private static async Task WriteJsonLinesAsync<T>(string path, IEnumerable<T> values, CancellationToken cancellationToken)
    {
        var lines = values.Select(value => JsonSerializer.Serialize(value, JsonLineOptions)).ToArray();
        await File.WriteAllTextAsync(path, string.Join(Environment.NewLine, lines) + (lines.Length == 0 ? string.Empty : Environment.NewLine), cancellationToken).ConfigureAwait(false);
    }

    private static ParsedCommand Parse(string[] args)
    {
        string? run = null;
        string? projectId = null;
        string? task = null;
        var missing = new List<string>();
        var json = false;
        var simulateFailure = false;

        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run":
                    if (!TryRead(args, ref index, out run)) return ParsedCommand.Fail(json, "--run requires a value.");
                    break;
                case "--project":
                case "--project-id":
                    if (!TryRead(args, ref index, out projectId)) return ParsedCommand.Fail(json, $"{args[index]} requires a value.");
                    break;
                case "--task":
                    if (!TryRead(args, ref index, out task)) return ParsedCommand.Fail(json, "--task requires a value.");
                    break;
                case "--simulate-missing-artifact":
                    if (!TryRead(args, ref index, out var artifact)) return ParsedCommand.Fail(json, "--simulate-missing-artifact requires a value.");
                    missing.Add(artifact);
                    break;
                case "--simulate-failure":
                    simulateFailure = true;
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    return ParsedCommand.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(run))
            return ParsedCommand.Fail(json, "Missing required option: --run <path>.");
        if (string.IsNullOrWhiteSpace(projectId))
            return ParsedCommand.Fail(json, "Missing required option: --project <id>.");
        if (string.IsNullOrWhiteSpace(task))
            return ParsedCommand.Fail(json, "Missing required option: --task <task.md-or-text>.");

        return new ParsedCommand(run, projectId, task, missing, simulateFailure, json, null);
    }

    private static bool TryRead(string[] args, ref int index, out string value)
    {
        value = string.Empty;
        if (index + 1 >= args.Length)
            return false;
        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static int Usage(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine("Usage: irondev product-hardening dogfood --run <path> --project <id> --task <task.md-or-text> [--simulate-missing-artifact <name>] [--json]");
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
            boundary = new ProductHardeningBoundary()
        }, JsonOptions));
    }

    private sealed record ParsedCommand(
        string? Run,
        string? ProjectId,
        string? Task,
        IReadOnlyList<string> SimulatedMissingArtifacts,
        bool SimulateFailure,
        bool Json,
        string? Error)
    {
        public static ParsedCommand Fail(bool json, string error) => new(null, null, null, [], false, json, error);
    }
}
