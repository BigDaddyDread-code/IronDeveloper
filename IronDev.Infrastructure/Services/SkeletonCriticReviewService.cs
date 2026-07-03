using System.Text.Json;
using IronDev.Core;
using IronDev.Core.Agents.Concrete;
using IronDev.Core.Builder;
using IronDev.Core.RunReports;
using IronDev.Core.Runs;
using IronDev.Services;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// P1-1 — the critic actually reviews. Pulls the skeleton run's critic package
/// from durable evidence (never from the requester), prompts a live model to
/// attack it, and records the review through the stored manual-critic execution
/// path — which validates the review-only surface (no approval claims, no
/// authority claims, verdict/finding consistency) and persists the audit
/// envelope. The run ↔ review link is published as a durable run event.
///
/// Boundary: this service is advisory-only. It can create critic findings and
/// nothing else — it holds no reference to approvals, executors, or memory, and
/// a finding is not a veto. Review is not approval; the human gate stays separate.
/// The prompt is built from the package and its evidence alone: outside memory
/// is not outside evidence.
/// </summary>
public sealed class SkeletonCriticReviewService : ISkeletonCriticReviewService
{
    public const string CriticSpecialisationId = "builtin.critic.code-review";
    private const int MaxDiffCharsPerChange = 6000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ITicketService _tickets;
    private readonly IRunStore _runs;
    private readonly IRunEventStore _events;
    private readonly ILLMService _llm;
    private readonly IStoredManualIndependentCriticAgentService _storedCritic;
    private readonly IConfiguration _configuration;

    public SkeletonCriticReviewService(
        ITicketService tickets,
        IRunStore runs,
        IRunEventStore events,
        ILLMService llm,
        IStoredManualIndependentCriticAgentService storedCritic,
        IConfiguration configuration)
    {
        _tickets = tickets;
        _runs = runs;
        _events = events;
        _llm = llm;
        _storedCritic = storedCritic;
        _configuration = configuration;
    }

    public async Task<SkeletonCriticReviewOutcome?> ReviewAsync(
        SkeletonCriticReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(request.TicketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || ticket.ProjectId != request.ProjectId)
            return null;

        var run = await _runs.GetAsync(request.RunId, cancellationToken).ConfigureAwait(false);
        if (run is null || run.ProjectId != request.ProjectId || run.TicketId != request.TicketId)
            return null;

        // The critic pulls its subject from durable evidence by run id. The
        // requester never supplies the package — a curated copy is not reviewable.
        var packagePath = Path.Combine(ResolveEvidenceRoot(), request.RunId, "evidence", "critic-package.json");
        if (!File.Exists(packagePath))
        {
            return await FailAsync(request,
                "The critic package evidence is missing on disk. A run must halt with a prepared package before it can be reviewed.",
                cancellationToken).ConfigureAwait(false);
        }

        var packageBytes = await File.ReadAllBytesAsync(packagePath, cancellationToken).ConfigureAwait(false);
        var packageHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packageBytes)).ToLowerInvariant();
        SkeletonCriticPackage? package;
        try
        {
            package = JsonSerializer.Deserialize<SkeletonCriticPackage>(
                System.Text.Encoding.UTF8.GetString(packageBytes),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            package = null;
        }

        if (package is null)
        {
            return await FailAsync(request,
                "The critic package evidence on disk could not be parsed.",
                cancellationToken).ConfigureAwait(false);
        }

        string response;
        try
        {
            response = await _llm.GetResponseAsync(BuildPrompt(package), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await FailAsync(request,
                $"Critic model call failed: {exception.Message}",
                cancellationToken).ConfigureAwait(false);
        }

        var parsed = TryParse(response);
        if (!parsed.Succeeded)
        {
            return await FailAsync(request, parsed.FailureReason, cancellationToken).ConfigureAwait(false);
        }

        var packageEvidenceRefs = new[] { packagePath, $"critic-package-sha256:{packageHash}" };
        var reviewRequestId = $"skeleton-critic-{request.RunId}-{Guid.NewGuid():N}";
        var stored = _storedCritic.ExecuteAndStore(
            new ManualCriticReviewRequest
            {
                ReviewRequestId = reviewRequestId,
                TenantId = ticket.TenantId.ToString(),
                ProjectId = request.ProjectId.ToString(),
                CampaignId = $"skeleton-run-{request.RunId}",
                RunId = request.RunId,
                SubjectType = CriticReviewSubjectType.WorkPackage,
                SubjectId = package.PackageId,
                RequestedByUserId = request.RequestedByUserId,
                CorrelationId = request.RunId,
                RequestSummary =
                    $"Independent critic review of skeleton work package {package.PackageId} " +
                    $"({package.Changes.Count} change(s), {package.AuthoredTests.Count} authored test(s)). " +
                    "The package was pulled from durable evidence by the critic; findings are advisory.",
                Inputs =
                [
                    new ManualCriticReviewInputRef
                    {
                        InputRefId = $"input-{reviewRequestId}-001",
                        RefType = nameof(CriticReviewSubjectType.WorkPackage),
                        RefId = package.PackageId,
                        Source = "Durable skeleton-run evidence, pulled by the critic service.",
                        Summary =
                            $"Ticket '{package.TicketTitle}': {package.ProposalSummary} " +
                            $"Workspace build/test succeeded: {package.WorkspaceRunSucceeded}.",
                        EvidenceRefs = packageEvidenceRefs,
                        ContainsRawPrivateReasoning = false,
                        IsAuthoritativeForAction = false
                    }
                ],
                FindingDrafts = parsed.Findings
                    .Select(finding => new ManualCriticFindingDraft
                    {
                        Severity = finding.Severity,
                        Title = finding.Title,
                        Problem = finding.Problem,
                        WhyItMatters = finding.WhyItMatters,
                        RequiredFix = finding.RequiredFix,
                        EvidenceRefs = packageEvidenceRefs,
                        BlocksMerge = finding.BlocksMerge,
                        RequiresHumanReview = true
                    })
                    .ToList(),
                RequestedVerdict = parsed.Verdict
            },
            new ManualAgentExecutionSpecialisationSelection
            {
                SpecialisationId = CriticSpecialisationId,
                RequestedByUserId = request.RequestedByUserId,
                Reason = "P1-1 skeleton critic review. This selection grants no approval, governance, execution, source apply, or memory authority."
            },
            DateTimeOffset.UtcNow);

        if (stored.Status is not (StoredManualAgentExecutionStatus.Stored or StoredManualAgentExecutionStatus.AlreadyStored) ||
            stored.Output is null)
        {
            var issueSummary = string.Join(" | ", stored.Issues.Select(issue => $"{issue.Code}: {issue.Message}"));
            return await FailAsync(request,
                $"The critic review was rejected by the review-only validation surface: {issueSummary}",
                cancellationToken).ConfigureAwait(false);
        }

        var output = stored.Output;
        var blockingCount = output.Findings.Count(finding => finding.BlocksMerge);
        await PublishAsync(request, "SkeletonCriticReviewRecorded",
            $"Independent critic review recorded with verdict {output.Verdict}: {output.Findings.Count} finding(s), {blockingCount} blocking. " +
            "Findings are advisory — a finding is not a veto, review is not approval, and the human gate remains separate.",
            new Dictionary<string, string>
            {
                ["criticAgentRunId"] = stored.AgentRunId,
                ["reviewId"] = output.ReviewResultId,
                ["verdict"] = output.Verdict.ToString(),
                ["findingCount"] = output.Findings.Count.ToString(),
                ["blockingFindingCount"] = blockingCount.ToString(),
                ["packageSha256"] = packageHash,
                ["requestedByUserId"] = request.RequestedByUserId
            }, cancellationToken).ConfigureAwait(false);

        return new SkeletonCriticReviewOutcome
        {
            Succeeded = true,
            CriticAgentRunId = stored.AgentRunId,
            ReviewId = output.ReviewResultId,
            Verdict = output.Verdict.ToString(),
            Findings = output.Findings
                .Select(finding => new SkeletonCriticReviewFindingDto
                {
                    Severity = finding.Severity.ToString(),
                    Title = finding.Title,
                    Problem = finding.Problem,
                    WhyItMatters = finding.WhyItMatters,
                    RequiredFix = finding.RequiredFix,
                    BlocksMerge = finding.BlocksMerge
                })
                .ToList()
        };
    }

    /// <summary>
    /// The critic's prompt is built from the work package and its evidence alone.
    /// It carries no team memory, no prior conversation, and no builder narrative
    /// beyond what the package itself records at full fidelity.
    /// </summary>
    private static string BuildPrompt(SkeletonCriticPackage package)
    {
        var lines = new List<string>
        {
            "You are an INDEPENDENT CRITIC reviewing a work package from outside the team that built it.",
            "You are deliberately given only the package and its evidence — no team memory, no prior discussion.",
            "Your job is to attack the work: find where it fails the acceptance criteria, where the tests do not",
            "prove what they claim, and where the evidence contradicts the summary. You are not there to be nice.",
            "Your findings are advisory: you cannot approve, block, execute, or change anything.",
            string.Empty,
            $"Ticket: {package.TicketTitle}",
            $"Acceptance criteria:\n{package.AcceptanceCriteria}",
            $"Builder's summary: {package.ProposalSummary}",
            $"Builder's rationale: {package.ProposalRationale}",
            $"Workspace build/test succeeded: {package.WorkspaceRunSucceeded}",
            string.Empty,
            "Proposed changes (exact diffs):"
        };

        foreach (var change in package.Changes)
        {
            var diff = string.IsNullOrEmpty(change.Diff) ? change.FullContentAfter ?? string.Empty : change.Diff;
            if (diff.Length > MaxDiffCharsPerChange)
                diff = diff[..MaxDiffCharsPerChange] + "\n[diff truncated for prompt length; the full diff is in the package evidence]";
            lines.Add($"--- {change.FilePath}{(change.IsNewFile ? " (new)" : change.IsDeletion ? " (deleted)" : "")}");
            lines.Add(diff);
        }

        lines.Add(string.Empty);
        lines.Add("Tests authored from the acceptance criteria (blind to the diff):");
        if (package.AuthoredTests.Count == 0)
        {
            lines.Add("NONE — the criterion-to-test matrix has no cells. Treat missing coverage as review material.");
        }
        else
        {
            foreach (var test in package.AuthoredTests)
                lines.Add($"- {test.RelativePath} covers: {test.CoversCriterion}");
        }

        lines.Add(string.Empty);
        lines.Add("Build/test command results:");
        foreach (var command in package.CommandResults)
            lines.Add($"- {command.DisplayName}: exit {command.ExitCode}{(command.TimedOut ? " (timed out)" : "")}");

        lines.Add(string.Empty);
        lines.Add("Respond with ONLY a JSON object, no prose, no code fences:");
        lines.Add("{\"verdict\":\"NoObjection|CommentOnly|RequestChanges|RecommendBlock\",");
        lines.Add(" \"findings\":[{\"severity\":\"Critical|High|Medium|Low\",\"title\":\"...\",\"problem\":\"...\",");
        lines.Add("   \"whyItMatters\":\"...\",\"requiredFix\":\"...\",\"blocksMerge\":true|false}]}");
        lines.Add("Rules: NoObjection requires an empty findings list with nothing blocking.");
        lines.Add("RecommendBlock requires at least one finding with blocksMerge=true.");
        lines.Add("Every finding must name a concrete problem in THIS package, not a generic best practice.");

        return string.Join('\n', lines);
    }

    /// <summary>Parses the model response. Tolerates code fences; anything else fails explicitly.</summary>
    public static ParsedSkeletonCriticReview TryParse(string response)
    {
        var text = response.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                text = text[(firstNewline + 1)..lastFence].Trim();
        }

        SkeletonCriticJson? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SkeletonCriticJson>(text, JsonOptions);
        }
        catch (JsonException exception)
        {
            return ParsedSkeletonCriticReview.Failure($"Critic response was not valid JSON: {exception.Message}");
        }

        if (payload is null)
            return ParsedSkeletonCriticReview.Failure("Critic response was empty.");

        if (!Enum.TryParse<CriticReviewVerdict>(payload.Verdict, ignoreCase: true, out var verdict) || !Enum.IsDefined(verdict))
            return ParsedSkeletonCriticReview.Failure($"Critic verdict '{payload.Verdict}' is not a known verdict.");

        var findings = new List<ParsedSkeletonCriticFinding>();
        foreach (var finding in payload.Findings)
        {
            if (string.IsNullOrWhiteSpace(finding.Title) ||
                string.IsNullOrWhiteSpace(finding.Problem) ||
                string.IsNullOrWhiteSpace(finding.WhyItMatters) ||
                string.IsNullOrWhiteSpace(finding.RequiredFix))
            {
                return ParsedSkeletonCriticReview.Failure(
                    "A critic finding is missing title, problem, whyItMatters, or requiredFix — partial findings are not recorded.");
            }

            var severity = Enum.TryParse<CriticSeverity>(finding.Severity, ignoreCase: true, out var parsedSeverity) && Enum.IsDefined(parsedSeverity)
                ? parsedSeverity
                : CriticSeverity.Medium;

            findings.Add(new ParsedSkeletonCriticFinding(
                severity, finding.Title.Trim(), finding.Problem.Trim(), finding.WhyItMatters.Trim(), finding.RequiredFix.Trim(), finding.BlocksMerge));
        }

        return ParsedSkeletonCriticReview.Success(verdict, findings);
    }

    private async Task<SkeletonCriticReviewOutcome> FailAsync(
        SkeletonCriticReviewRequest request,
        string reason,
        CancellationToken cancellationToken)
    {
        await PublishAsync(request, "SkeletonCriticReviewFailed",
            $"Critic review failed and was NOT recorded: {reason}",
            new Dictionary<string, string>
            {
                ["failureReason"] = reason,
                ["requestedByUserId"] = request.RequestedByUserId
            }, cancellationToken).ConfigureAwait(false);

        return new SkeletonCriticReviewOutcome { Succeeded = false, FailureReason = reason };
    }

    private Task PublishAsync(
        SkeletonCriticReviewRequest request,
        string eventType,
        string message,
        IReadOnlyDictionary<string, string> payload,
        CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, string>(payload, StringComparer.OrdinalIgnoreCase)
        {
            ["projectId"] = request.ProjectId.ToString(),
            ["ticketId"] = request.TicketId.ToString(),
            ["skeletonRun"] = "true",
            ["currentNode"] = "SkeletonCriticReview"
        };

        return _events.PublishAsync(new RunEventDto
        {
            RunId = request.RunId,
            EventType = eventType,
            Message = message,
            Payload = merged
        }, cancellationToken);
    }

    private string ResolveEvidenceRoot()
    {
        var configured = _configuration["DisposableBuild:EvidenceRoot"] ?? _configuration["LocalTest:LogsRoot"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "IronDevDisposableEvidence")
            : configured;
        return Path.Combine(root, "runs");
    }

    private sealed record SkeletonCriticJson
    {
        public string Verdict { get; init; } = string.Empty;
        public IReadOnlyList<SkeletonCriticFindingJson> Findings { get; init; } = [];
    }

    private sealed record SkeletonCriticFindingJson
    {
        public string Severity { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Problem { get; init; } = string.Empty;
        public string WhyItMatters { get; init; } = string.Empty;
        public string RequiredFix { get; init; } = string.Empty;
        public bool BlocksMerge { get; init; }
    }
}

public sealed record ParsedSkeletonCriticFinding(
    CriticSeverity Severity,
    string Title,
    string Problem,
    string WhyItMatters,
    string RequiredFix,
    bool BlocksMerge);

public sealed record ParsedSkeletonCriticReview
{
    public required bool Succeeded { get; init; }
    public string FailureReason { get; init; } = string.Empty;
    public CriticReviewVerdict Verdict { get; init; }
    public IReadOnlyList<ParsedSkeletonCriticFinding> Findings { get; init; } = [];

    public static ParsedSkeletonCriticReview Success(CriticReviewVerdict verdict, IReadOnlyList<ParsedSkeletonCriticFinding> findings) =>
        new() { Succeeded = true, Verdict = verdict, Findings = findings };

    public static ParsedSkeletonCriticReview Failure(string reason) =>
        new() { Succeeded = false, FailureReason = reason };
}
