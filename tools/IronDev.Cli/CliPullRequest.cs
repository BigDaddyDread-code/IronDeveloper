using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

internal static class IronDevCliPullRequest
{
    private const string DefaultRunsFolderName = "irondev-patch-runs";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "create",
        "create-ready",
        "ready",
        "request-reviewers",
        "approve",
        "merge",
        "release",
        "deploy",
        "push",
        "commit",
        "stage",
        "continue",
        "continue-workflow",
        "mark-ready",
        "reviewers"
    ];

    private static readonly string[] StatusArtifacts =
    [
        "pull-request-creation-request.json",
        "pull-request-creation-request.md",
        "pull-request-branch-validation.json",
        "pull-request-branch-validation.md",
        "pull-request-evidence-validation.json",
        "pull-request-evidence-validation.md",
        "pull-request-text-proposal.json",
        "pull-request-text-proposal.md",
        "pull-request-creation-gate.json",
        "pull-request-creation-gate.md",
        "pull-request-created-receipt.json",
        "pull-request-created-receipt.md",
        "pull-request-status.json",
        "pull-request-status.md",
        "pull-request-creation-bypass-report.json",
        "pull-request-creation-bypass-report.md",
        "governance-events.jsonl"
    ];

    public static bool IsPullRequestCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "pull-request", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "pull-request requires a subcommand: request, validate, text, gate, create-draft, or status.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"pull-request {args[1]} is intentionally unsupported; Block AM creates gated draft PRs only.");

        return subcommand switch
        {
            "request" => await HandleRequestAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "validate" => await HandleValidateAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "text" => await HandleTextAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "gate" => await HandleGateAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "create-draft" => await HandleCreateDraftAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "status" => HandleStatus(args, output, error),
            _ => Usage(error, $"unsupported pull-request subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandleRequestAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRequest(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "pull-request request", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var commitPackageRequest = ReadJson<CommitPackageRequest>(Path.Combine(runPath, "commit-package-request.json"));
        var commitReview = ReadJson<CommitReadinessReview>(Path.Combine(runPath, "commit-readiness-review.json"));
        if (commitPackageRequest is null || commitReview is null)
            return Failure(output, error, parsed.Json, "pull-request request", "commit package request and readiness review artifacts are required first.");

        var request = PullRequestCreationRequestWriter.Create(new PullRequestCreationRequestInput
        {
            RunId = RunId(runPath),
            ProjectId = commitPackageRequest.ProjectId,
            RepositoryFullName = parsed.RepositoryFullName!,
            BaseBranch = parsed.BaseBranch!,
            HeadBranch = parsed.HeadBranch!,
            ExpectedHeadSha = parsed.ExpectedHeadSha!,
            CommitPackageRequestId = commitPackageRequest.CommitPackageRequestId,
            CommitReadinessReviewId = commitReview.CommitReadinessReviewId,
            RequestedBy = "IronDevCli",
            Reason = commitPackageRequest.Reason,
            EvidenceRefs = ReadArtifactNames(runPath)
        });

        await WriteJsonAsync(runPath, "pull-request-creation-request.json", request, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "pull-request-creation-request.md"), RenderRequest(request), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.PullRequestCreationRequestCreated, request.PullRequestCreationRequestId, "Draft pull request creation was requested.", ["pull-request-creation-request.json"]);

        if (parsed.Json)
            WriteJson(output, "pull-request request", "succeeded", new { runPath, request }, []);
        else
        {
            output.WriteLine($"Pull request creation request: {request.PullRequestCreationRequestId}");
            output.WriteLine("Boundary: request is not pull request creation.");
        }

        return 0;
    }

    private static async Task<int> HandleValidateAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunOnly(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "pull-request validate", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var request = ReadJson<PullRequestCreationRequest>(Path.Combine(runPath, "pull-request-creation-request.json"));
        if (request is null)
            return Failure(output, error, parsed.Json, "pull-request validate", "pull-request-creation-request.json is missing; run 'irondev pull-request request' first.");

        var head = await ReadRemoteBranchAsync(request.RepositoryFullName, request.HeadBranch, cancellationToken).ConfigureAwait(false);
        var baseBranch = await ReadRemoteBranchAsync(request.RepositoryFullName, request.BaseBranch, cancellationToken).ConfigureAwait(false);
        var existingPr = await ReadExistingPullRequestAsync(request.RepositoryFullName, request.HeadBranch, cancellationToken).ConfigureAwait(false);
        var branchReport = PullRequestBranchValidator.Validate(request, head, baseBranch, existingPr);

        var evidenceReport = PullRequestEvidenceValidator.Validate(
            request,
            ReadJson<CommitReadinessReview>(Path.Combine(runPath, "commit-readiness-review.json")),
            ReadJson<CommitEvidenceBundle>(Path.Combine(runPath, "commit-evidence-bundle.json")),
            ReadJson<CommitPackageBoundaryReport>(Path.Combine(runPath, "commit-package-boundary-report.json")),
            ReadUnsafeMaterialFindings(runPath),
            ReadArtifactConsistencyBlockers(runPath));

        await WriteJsonAsync(runPath, "pull-request-branch-validation.json", branchReport, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "pull-request-branch-validation.md"), RenderBranchValidation(branchReport), cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "pull-request-evidence-validation.json", evidenceReport, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "pull-request-evidence-validation.md"), RenderEvidenceValidation(evidenceReport), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.PullRequestBranchValidationCreated, branchReport.PullRequestBranchValidationReportId, "Pull request branch validation was created.", ["pull-request-branch-validation.json"]);
        RecordEvent(runPath, GovernanceKernelEventKind.PullRequestEvidenceValidationCreated, evidenceReport.PullRequestEvidenceValidationReportId, "Pull request commit evidence validation was created.", ["pull-request-evidence-validation.json"]);

        if (parsed.Json)
            WriteJson(output, "pull-request validate", branchReport.Passed && evidenceReport.Passed ? "succeeded" : "blocked", new { runPath, branchReport, evidenceReport }, []);
        else
        {
            output.WriteLine($"Branch validation: {(branchReport.Passed ? "passed" : "blocked")}");
            output.WriteLine($"Evidence validation: {(evidenceReport.Passed ? "passed" : "blocked")}");
            output.WriteLine("Boundary: validation does not create a pull request.");
        }

        return branchReport.Passed && evidenceReport.Passed ? 0 : 1;
    }

    private static async Task<int> HandleTextAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunOnly(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "pull-request text", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var request = ReadJson<PullRequestCreationRequest>(Path.Combine(runPath, "pull-request-creation-request.json"));
        if (request is null)
            return Failure(output, error, parsed.Json, "pull-request text", "pull-request-creation-request.json is missing; run 'irondev pull-request request' first.");

        var proposal = PullRequestTextProposalBuilder.Build(
            request,
            ReadJson<CommitFileManifest>(Path.Combine(runPath, "commit-file-manifest.json")),
            ReadJson<CommitEvidenceBundle>(Path.Combine(runPath, "commit-evidence-bundle.json")),
            ReadJson<CommitReadinessReview>(Path.Combine(runPath, "commit-readiness-review.json")),
            ReadRiskNotes(runPath));

        await WriteJsonAsync(runPath, "pull-request-text-proposal.json", proposal, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "pull-request-text-proposal.md"), RenderTextProposal(proposal), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.PullRequestTextProposalCreated, proposal.PullRequestTextProposalId, "Pull request title/body proposal was created.", ["pull-request-text-proposal.json"]);

        if (parsed.Json)
            WriteJson(output, "pull-request text", "succeeded", new { runPath, proposal }, []);
        else
        {
            output.WriteLine($"Pull request text proposal: {proposal.PullRequestTextProposalId}");
            output.WriteLine("Boundary: proposal text does not create a pull request.");
        }

        return 0;
    }

    private static async Task<int> HandleGateAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseGate(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "pull-request gate", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var request = ReadJson<PullRequestCreationRequest>(Path.Combine(runPath, "pull-request-creation-request.json"));
        if (request is null)
            return Failure(output, error, parsed.Json, "pull-request gate", "pull-request-creation-request.json is missing; run 'irondev pull-request request' first.");

        var decision = ReadJson<ConscienceDecision>(Path.GetFullPath(parsed.DecisionPath!));
        var gate = PullRequestCreationGateBuilder.Build(
            request,
            ReadJson<PullRequestBranchValidationReport>(Path.Combine(runPath, "pull-request-branch-validation.json")),
            ReadJson<PullRequestEvidenceValidationReport>(Path.Combine(runPath, "pull-request-evidence-validation.json")),
            ReadJson<PullRequestTextProposal>(Path.Combine(runPath, "pull-request-text-proposal.json")),
            ReadJson<CommitReadinessReview>(Path.Combine(runPath, "commit-readiness-review.json")),
            decision,
            parsed.ThoughtLedgerRef);

        await WriteJsonAsync(runPath, "pull-request-creation-gate.json", gate, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "pull-request-creation-gate.md"), RenderGate(gate), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.PullRequestCreationGateCreated, gate.PullRequestCreationGateId, "Draft pull request creation gate was created.", ["pull-request-creation-gate.json"]);

        if (parsed.Json)
            WriteJson(output, "pull-request gate", gate.Decision == PullRequestCreationGateDecision.CreateDraftPullRequest ? "succeeded" : "blocked", new { runPath, gate }, []);
        else
        {
            output.WriteLine($"Pull request creation gate: {gate.Decision}");
            output.WriteLine("Boundary: gate allows draft PR creation only, and does not create the PR.");
        }

        return gate.Decision == PullRequestCreationGateDecision.CreateDraftPullRequest ? 0 : 1;
    }

    private static async Task<int> HandleCreateDraftAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunOnly(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "pull-request create-draft", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var request = ReadJson<PullRequestCreationRequest>(Path.Combine(runPath, "pull-request-creation-request.json"));
        var gate = ReadJson<PullRequestCreationGate>(Path.Combine(runPath, "pull-request-creation-gate.json"));
        var proposal = ReadJson<PullRequestTextProposal>(Path.Combine(runPath, "pull-request-text-proposal.json"));
        if (request is null || gate is null || proposal is null)
            return Failure(output, error, parsed.Json, "pull-request create-draft", "request, gate, and text proposal artifacts are required first.");

        var head = await ReadRemoteBranchAsync(request.RepositoryFullName, request.HeadBranch, cancellationToken).ConfigureAwait(false);
        var existingPr = await ReadExistingPullRequestAsync(request.RepositoryFullName, request.HeadBranch, cancellationToken).ConfigureAwait(false);
        var result = await DraftPullRequestExecutor.CreateDraftAsync(request, gate, proposal, head, existingPr, new GhDraftPullRequestCreator(), "IronDevCli", cancellationToken).ConfigureAwait(false);
        if (result.Status != PullRequestCreationExecutionStatus.Created || result.Receipt is null || result.StatusReport is null)
            return Failure(output, error, parsed.Json, "pull-request create-draft", string.Join(",", result.Issues));

        await WriteJsonAsync(runPath, "pull-request-created-receipt.json", result.Receipt, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "pull-request-created-receipt.md"), RenderReceipt(result.Receipt), cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "pull-request-status.json", result.StatusReport, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "pull-request-status.md"), RenderStatusReport(result.StatusReport), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.DraftPullRequestCreated, result.Receipt.PullRequestCreationReceiptId, "Controlled draft pull request was created.", ["pull-request-created-receipt.json", "pull-request-status.json"]);

        if (parsed.Json)
            WriteJson(output, "pull-request create-draft", "succeeded", new { runPath, receipt = result.Receipt, status = result.StatusReport }, []);
        else
        {
            output.WriteLine($"Draft pull request created: {result.Receipt.PullRequestUrl}");
            output.WriteLine("Boundary: draft PR creation did not commit, push, mark ready, request reviewers, merge, release, deploy, or continue workflow.");
        }

        return 0;
    }

    private static int HandleStatus(string[] args, TextWriter output, TextWriter error)
    {
        var parsed = ParseRunOnly(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "pull-request status", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var artifacts = StatusArtifacts.Select(name => new { name, exists = File.Exists(Path.Combine(runPath, name)) }).ToArray();
        var bypass = PullRequestCreationBypassEvaluator.Evaluate(RunId(runPath), ["commit package request", "commit readiness review", "PR text proposal", "branch validation", "test success", "build success", "artifact consistency report", "release readiness report", "chat text", "AI review text", "memory plan text", "human-looking approval text"]);
        WriteJsonAsync(runPath, "pull-request-creation-bypass-report.json", bypass, CancellationToken.None).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(runPath, "pull-request-creation-bypass-report.md"), RenderBypass(bypass));
        RecordEvent(runPath, GovernanceKernelEventKind.PullRequestCreationBypassReportCreated, bypass.PullRequestCreationBypassReportId, "Pull request creation bypass report was created.", ["pull-request-creation-bypass-report.json"]);

        if (parsed.Json)
            WriteJson(output, "pull-request status", "succeeded", new { runPath, artifacts, bypass }, []);
        else
        {
            output.WriteLine($"Pull request artifacts: {runPath}");
            foreach (var artifact in artifacts)
                output.WriteLine($"- {artifact.name}: {(artifact.exists ? "present" : "missing")}");
            output.WriteLine("Boundary: status is read-only and grants no PR authority.");
        }

        return 0;
    }

    private static async Task<RemoteBranchState> ReadRemoteBranchAsync(string repositoryFullName, string branch, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync("git", ["ls-remote", $"https://github.com/{repositoryFullName}.git", $"refs/heads/{branch}"], Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);
        var sha = result.ExitCode == 0
            ? result.Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Split('\t').FirstOrDefault()
            : null;
        return new RemoteBranchState
        {
            RepositoryFullName = repositoryFullName,
            BranchName = branch,
            Exists = !string.IsNullOrWhiteSpace(sha),
            HeadSha = string.IsNullOrWhiteSpace(sha) ? null : sha.Trim()
        };
    }

    private static async Task<ExistingPullRequestState> ReadExistingPullRequestAsync(string repositoryFullName, string headBranch, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync("gh", ["pr", "list", "--repo", repositoryFullName, "--head", headBranch, "--state", "open", "--json", "number,url,isDraft", "--limit", "1"], Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
            return new ExistingPullRequestState();

        try
        {
            using var document = JsonDocument.Parse(result.Stdout);
            var first = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().FirstOrDefault() : default;
            if (first.ValueKind != JsonValueKind.Object)
                return new ExistingPullRequestState();
            return new ExistingPullRequestState
            {
                Exists = true,
                PullRequestNumber = first.TryGetProperty("number", out var number) ? number.GetInt32() : null,
                Url = first.TryGetProperty("url", out var url) ? url.GetString() : null,
                Draft = first.TryGetProperty("isDraft", out var draft) && draft.GetBoolean()
            };
        }
        catch
        {
            return new ExistingPullRequestState();
        }
    }

    private static string[] ReadUnsafeMaterialFindings(string runPath)
    {
        var findingsPath = Path.Combine(runPath, "unsafe-material-findings.jsonl");
        if (File.Exists(findingsPath))
        {
            var findings = File.ReadAllLines(findingsPath).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            if (findings.Length > 0)
                return findings;
        }

        return ReadJsonArrayStrings(Path.Combine(runPath, "unsafe-material-report.json"), "findings");
    }

    private static string[] ReadArtifactConsistencyBlockers(string runPath)
    {
        var issues = ReadJsonArrayStrings(Path.Combine(runPath, "artifact-consistency-report.json"), "issues");
        if (issues.Length > 0)
            return issues;

        var blockers = ReadJsonArrayStrings(Path.Combine(runPath, "artifact-consistency-report.json"), "blockingIssues");
        if (blockers.Length > 0)
            return blockers;

        var reportPath = Path.Combine(runPath, "artifact-consistency-report.json");
        if (!File.Exists(reportPath))
            return [];

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            if (document.RootElement.TryGetProperty("outcome", out var outcome) && outcome.ValueKind == JsonValueKind.String && !string.Equals(outcome.GetString(), "Pass", StringComparison.OrdinalIgnoreCase))
                return [$"ArtifactConsistencyOutcome:{outcome.GetString()}"];
        }
        catch
        {
            return [];
        }

        return [];
    }

    private static string[] ReadJsonArrayStrings(string path, string propertyName)
    {
        if (!File.Exists(path))
            return [];
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty(propertyName, out var values) && values.ValueKind == JsonValueKind.Array)
                return values.EnumerateArray().Select(item => item.ToString()).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        }
        catch
        {
            return [];
        }

        return [];
    }

    private static string[] ReadRiskNotes(string runPath)
    {
        var path = Path.Combine(runPath, "known-risks.md");
        return File.Exists(path) ? File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray() : [];
    }

    private static string[] ReadArtifactNames(string runPath) =>
        Directory.Exists(runPath)
            ? Directory.EnumerateFiles(runPath).Select(Path.GetFileName).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];

    private static string RenderRequest(PullRequestCreationRequest request) => $"""
        # Pull Request Creation Request

        Request: `{request.PullRequestCreationRequestId}`
        Repository: `{request.RepositoryFullName}`
        Base: `{request.BaseBranch}`
        Head: `{request.HeadBranch}`
        Expected head SHA: `{request.ExpectedHeadSha}`

        Boundary: AM1 creates a draft PR creation request. It does not create a pull request.
        """;

    private static string RenderBranchValidation(PullRequestBranchValidationReport report) => $"""
        # Pull Request Branch Validation

        Passed: `{report.Passed}`
        Observed head SHA: `{report.ObservedHeadSha ?? "missing"}`
        Issues:
        {RenderBullets(report.Issues)}

        Boundary: AM2 validates branch evidence. It does not create a pull request.
        """;

    private static string RenderEvidenceValidation(PullRequestEvidenceValidationReport report) => $"""
        # Pull Request Evidence Validation

        Passed: `{report.Passed}`
        Issues:
        {RenderBullets(report.Issues)}

        Boundary: AM2 validates commit package evidence. It does not create a pull request.
        """;

    private static string RenderTextProposal(PullRequestTextProposal proposal) => $"""
        # Pull Request Text Proposal

        Title: `{proposal.ProposedTitle}`

        {proposal.ProposedBody}

        Boundary: AM3 proposes draft PR text. It does not create a pull request.
        """;

    private static string RenderGate(PullRequestCreationGate gate) => $"""
        # Draft PR Creation Gate

        Decision: `{gate.Decision}`
        Allowed operation: `{gate.AllowedOperation ?? "none"}`
        Reasons:
        {RenderBullets(gate.Reasons)}

        Boundary: AM4 gates draft PR creation only. It does not create the pull request.
        """;

    private static string RenderReceipt(PullRequestCreationReceipt receipt) => $"""
        # Pull Request Created Receipt

        PR: `{receipt.PullRequestNumber}`
        URL: {receipt.PullRequestUrl}
        Draft: `{receipt.Draft}`
        Expected head SHA: `{receipt.ExpectedHeadSha}`
        Observed head SHA: `{receipt.ObservedHeadSha}`

        Boundary: AM5 created one gated draft pull request. It did not commit, push, mark ready, request review, merge, release, deploy, or continue workflow.
        """;

    private static string RenderStatusReport(PullRequestStatusReport status) => $"""
        # Pull Request Status

        PR: `{status.PullRequestNumber?.ToString() ?? "none"}`
        Draft: `{status.Draft}`
        Marked ready: `{status.MarkedReadyForReview}`
        Reviewers requested: `{status.ReviewersRequested}`
        Merged: `{status.Merged}`

        Boundary: status is evidence only.
        """;

    private static string RenderBypass(PullRequestCreationBypassReport report) => $"""
        # Pull Request Creation Bypass Report

        {RenderBullets(report.EvidenceSubjects.Select(subject => $"{subject} cannot create a pull request, mark ready, request review, merge, release, deploy, or continue workflow."))}
        """;

    private static string RenderBullets(IEnumerable<string> values) =>
        string.Join(Environment.NewLine, values.DefaultIfEmpty("(none)").Select(item => $"- {item}"));

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
            subjectKind: "ControlledPullRequestCreation",
            subjectId: subjectId,
            summary: summary,
            evidenceRefs: evidenceRefs);

    private static ParsedRequest ParseRequest(string[] args)
    {
        string? run = null;
        string? repo = null;
        string? baseBranch = null;
        string? headBranch = null;
        string? expectedHead = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run": if (!TryRead(args, ref index, out run)) return ParsedRequest.Fail(json, "--run requires a value."); break;
                case "--repo": if (!TryRead(args, ref index, out repo)) return ParsedRequest.Fail(json, "--repo requires a value."); break;
                case "--base": if (!TryRead(args, ref index, out baseBranch)) return ParsedRequest.Fail(json, "--base requires a value."); break;
                case "--head": if (!TryRead(args, ref index, out headBranch)) return ParsedRequest.Fail(json, "--head requires a value."); break;
                case "--expected-head": if (!TryRead(args, ref index, out expectedHead)) return ParsedRequest.Fail(json, "--expected-head requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedRequest.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(run)) return ParsedRequest.Fail(json, "Missing required option: --run <run-id-or-path>.");
        if (string.IsNullOrWhiteSpace(repo)) return ParsedRequest.Fail(json, "Missing required option: --repo <owner/name>.");
        if (string.IsNullOrWhiteSpace(baseBranch)) return ParsedRequest.Fail(json, "Missing required option: --base <branch>.");
        if (string.IsNullOrWhiteSpace(headBranch)) return ParsedRequest.Fail(json, "Missing required option: --head <branch>.");
        if (string.IsNullOrWhiteSpace(expectedHead)) return ParsedRequest.Fail(json, "Missing required option: --expected-head <sha>.");
        return new ParsedRequest(run, repo, baseBranch, headBranch, expectedHead, json, null);
    }

    private static ParsedGate ParseGate(string[] args)
    {
        var parsed = ParseRunOnly(args);
        if (parsed.Error is not null)
            return ParsedGate.Fail(parsed.Json, parsed.Error);
        string? decision = null;
        string? thoughtLedger = null;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run": index++; break;
                case "--decision": if (!TryRead(args, ref index, out decision)) return ParsedGate.Fail(parsed.Json, "--decision requires a value."); break;
                case "--thought-ledger-ref": if (!TryRead(args, ref index, out thoughtLedger)) return ParsedGate.Fail(parsed.Json, "--thought-ledger-ref requires a value."); break;
                case "--json": break;
                default: return ParsedGate.Fail(parsed.Json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(decision)) return ParsedGate.Fail(parsed.Json, "Missing required option: --decision <decision.json>.");
        if (string.IsNullOrWhiteSpace(thoughtLedger)) return ParsedGate.Fail(parsed.Json, "Missing required option: --thought-ledger-ref <ref>.");
        return new ParsedGate(parsed.Run, decision, thoughtLedger, parsed.Json, null);
    }

    private static ParsedRunOnly ParseRunOnly(string[] args)
    {
        string? run = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run": if (!TryRead(args, ref index, out run)) return ParsedRunOnly.Fail(json, "--run requires a value."); break;
                case "--json": json = true; break;
                case "--decision": index++; break;
                case "--thought-ledger-ref": index++; break;
                default: return ParsedRunOnly.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(run) ? ParsedRunOnly.Fail(json, "Missing required option: --run <run-id-or-path>.") : new ParsedRunOnly(run, json, null);
    }

    private static bool TryRead(string[] args, ref int index, out string value)
    {
        value = string.Empty;
        if (index + 1 >= args.Length)
            return false;
        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
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

    private static string ResolveRunPath(string run)
    {
        var candidate = Path.GetFullPath(run.Trim());
        if (Path.IsPathRooted(run) || Directory.Exists(candidate) || File.Exists(Path.Combine(candidate, "run.json")))
            return candidate;
        return Path.Combine(Path.GetTempPath(), DefaultRunsFolderName, run.Trim());
    }

    private static string RunId(string runPath) => Path.GetFileName(Path.GetFullPath(runPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static int Usage(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine("Usage:");
        error.WriteLine("  irondev pull-request request --run <run-id-or-path> --repo <owner/name> --base <branch> --head <branch> --expected-head <sha> [--json]");
        error.WriteLine("  irondev pull-request validate --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev pull-request text --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev pull-request gate --run <run-id-or-path> --decision <decision.json> --thought-ledger-ref <ref> [--json]");
        error.WriteLine("  irondev pull-request create-draft --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev pull-request status --run <run-id-or-path> [--json]");
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
            boundary = PullRequestBoundary.Evidence
        }, JsonOptions));
    }

    private sealed class GhDraftPullRequestCreator : IDraftPullRequestCreator
    {
        public async Task<PullRequestCreatedResult> CreateDraftPullRequestAsync(PullRequestDraftCreateCommand command, CancellationToken cancellationToken)
        {
            if (!command.Draft)
                throw new InvalidOperationException("Block AM can create draft pull requests only.");

            var bodyPath = Path.Combine(Path.GetTempPath(), $"irondev-pr-body-{Guid.NewGuid():N}.md");
            await File.WriteAllTextAsync(bodyPath, command.Body, cancellationToken).ConfigureAwait(false);
            try
            {
                var create = await RunProcessAsync("gh", ["pr", "create", "--repo", command.RepositoryFullName, "--base", command.BaseBranch, "--head", command.HeadBranch, "--title", command.Title, "--body-file", bodyPath, "--draft"], Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);
                if (create.ExitCode != 0)
                    throw new InvalidOperationException(create.Stderr);
                var url = create.Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? string.Empty;
                var view = await RunProcessAsync("gh", ["pr", "view", url, "--json", "number,url,isDraft"], Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);
                if (view.ExitCode == 0)
                {
                    using var document = JsonDocument.Parse(view.Stdout);
                    return new PullRequestCreatedResult
                    {
                        Number = document.RootElement.GetProperty("number").GetInt32(),
                        Url = document.RootElement.GetProperty("url").GetString() ?? url,
                        Draft = document.RootElement.GetProperty("isDraft").GetBoolean()
                    };
                }

                return new PullRequestCreatedResult
                {
                    Number = int.TryParse(url.Split('/').LastOrDefault(), out var number) ? number : 0,
                    Url = url,
                    Draft = true
                };
            }
            finally
            {
                try
                {
                    if (File.Exists(bodyPath))
                        File.Delete(bodyPath);
                }
                catch
                {
                    // Best-effort cleanup for a temporary PR body file.
                }
            }
        }
    }

    private sealed record ParsedRequest(string? Run, string? RepositoryFullName, string? BaseBranch, string? HeadBranch, string? ExpectedHeadSha, bool Json, string? Error)
    {
        public static ParsedRequest Fail(bool json, string error) => new(null, null, null, null, null, json, error);
    }

    private sealed record ParsedRunOnly(string? Run, bool Json, string? Error)
    {
        public static ParsedRunOnly Fail(bool json, string error) => new(null, json, error);
    }

    private sealed record ParsedGate(string? Run, string? DecisionPath, string? ThoughtLedgerRef, bool Json, string? Error)
    {
        public static ParsedGate Fail(bool json, string error) => new(null, null, null, json, error);
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
