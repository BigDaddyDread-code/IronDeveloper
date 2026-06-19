using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.Cli;

internal static class IronDevCliFeedback
{
    private const string DefaultRunsFolderName = "irondev-patch-runs";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "fix",
        "approve",
        "apply",
        "commit",
        "push",
        "reply",
        "resolve",
        "rerun-ci",
        "ready",
        "request-reviewers",
        "merge",
        "release",
        "deploy",
        "continue",
        "continue-workflow"
    ];

    private static readonly string[] StatusArtifacts =
    [
        "feedback-loop-request.json",
        "feedback-loop-request.md",
        "ci-observation-snapshot.json",
        "ci-observation-report.md",
        "ci-failure-excerpts.jsonl",
        "review-feedback-snapshot.json",
        "review-feedback-report.md",
        "review-feedback-comments.jsonl",
        "feedback-classification-report.json",
        "feedback-classification-report.md",
        "feedback-findings.jsonl",
        "feedback-remediation-plan.json",
        "feedback-remediation-plan.md",
        "suggested-feedback-test-profile.json",
        "feedback-known-risks.md",
        "feedback-readiness-report.json",
        "feedback-readiness-report.md",
        "feedback-remediation-package.json",
        "feedback-remediation-package-receipt.json",
        "feedback-remediation-summary.md",
        "feedback-status.json",
        "feedback-loop-bypass-report.json",
        "feedback-loop-bypass-report.md",
        "governance-events.jsonl"
    ];

    public static bool IsFeedbackCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "feedback", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "feedback requires a subcommand: request, ci, review, classify, plan, readiness, package, or status.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"feedback {args[1]} is intentionally unsupported; feedback evidence is not remediation authority.");

        return subcommand switch
        {
            "request" => await HandleRequestAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "ci" => await HandleCiAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "review" => await HandleReviewAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "classify" => await HandleClassifyAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "plan" => await HandlePlanAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "readiness" => await HandleReadinessAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "package" => await HandlePackageAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "status" => HandleStatus(args, output, error),
            _ => Usage(error, $"unsupported feedback subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandleRequestAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRequest(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "feedback request", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var prReceipt = ReadJson<PullRequestCreationReceipt>(Path.Combine(runPath, "pull-request-created-receipt.json"));
        var prStatus = ReadJson<PullRequestStatusReport>(Path.Combine(runPath, "pull-request-status.json"));
        var prRequest = ReadJson<PullRequestCreationRequest>(Path.Combine(runPath, "pull-request-creation-request.json"));
        if (prReceipt is null)
            return Failure(output, error, parsed.Json, "feedback request", "pull-request-created-receipt.json is required first.");
        if (!string.Equals(prReceipt.RepositoryFullName, parsed.RepositoryFullName, StringComparison.OrdinalIgnoreCase))
            return Failure(output, error, parsed.Json, "feedback request", "repository does not match the pull request creation receipt.");
        if (prReceipt.PullRequestNumber != parsed.PullRequestNumber)
            return Failure(output, error, parsed.Json, "feedback request", "pull request number does not match the pull request creation receipt.");
        if (!string.Equals(prReceipt.ExpectedHeadSha, parsed.ExpectedHeadSha, StringComparison.OrdinalIgnoreCase))
            return Failure(output, error, parsed.Json, "feedback request", "expected head SHA does not match the pull request creation receipt.");

        var request = FeedbackLoopRequestWriter.Create(new FeedbackLoopRequestInput
        {
            RunId = RunId(runPath),
            ProjectId = prRequest?.ProjectId ?? "unknown",
            RepositoryFullName = parsed.RepositoryFullName!,
            PullRequestNumber = parsed.PullRequestNumber,
            PullRequestUrl = prReceipt.PullRequestUrl,
            BaseBranch = prReceipt.BaseBranch,
            HeadBranch = prReceipt.HeadBranch,
            ExpectedHeadSha = parsed.ExpectedHeadSha!,
            PullRequestCreationReceiptId = prReceipt.PullRequestCreationReceiptId,
            RequestedBy = "IronDevCli",
            Reason = "Observe CI and review feedback for a controlled draft PR.",
            EvidenceRefs = ReadArtifactNames(runPath)
        });

        await WriteJsonAsync(runPath, "feedback-loop-request.json", request, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "feedback-loop-request.md"), RenderRequest(request, prStatus), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.FeedbackLoopRequestCreated, request.FeedbackLoopRequestId, "Feedback loop request was created.", ["feedback-loop-request.json", "pull-request-created-receipt.json"]);

        if (parsed.Json)
            WriteJson(output, "feedback request", "succeeded", new { runPath, request }, []);
        else
        {
            output.WriteLine($"Feedback loop request: {request.FeedbackLoopRequestId}");
            output.WriteLine("Boundary: request observes feedback only and cannot update the PR.");
        }

        return 0;
    }

    private static async Task<int> HandleCiAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunOnly(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "feedback ci", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var request = ReadJson<FeedbackLoopRequest>(Path.Combine(runPath, "feedback-loop-request.json"));
        if (request is null)
            return Failure(output, error, parsed.Json, "feedback ci", "feedback-loop-request.json is missing; run 'irondev feedback request' first.");

        var observedHead = await ReadPullRequestHeadAsync(request.RepositoryFullName, request.PullRequestNumber, cancellationToken).ConfigureAwait(false);
        var checks = await ReadCiChecksAsync(request.RepositoryFullName, request.PullRequestNumber, request.ExpectedHeadSha, cancellationToken).ConfigureAwait(false);
        var snapshot = CiObservationBuilder.Build(request, observedHead, checks, []);

        await WriteJsonAsync(runPath, "ci-observation-snapshot.json", snapshot, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "ci-observation-report.md"), RenderCi(snapshot), cancellationToken).ConfigureAwait(false);
        await File.WriteAllLinesAsync(Path.Combine(runPath, "ci-failure-excerpts.jsonl"), snapshot.Failures.Select(item => JsonSerializer.Serialize(item, JsonOptions)), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.CiObservationSnapshotCreated, snapshot.CiObservationSnapshotId, "CI observation snapshot was created.", ["ci-observation-snapshot.json"]);

        if (parsed.Json)
            WriteJson(output, "feedback ci", "succeeded", new { runPath, snapshot }, []);
        else
        {
            output.WriteLine($"CI observation: {snapshot.OverallCiState}");
            output.WriteLine("Boundary: CI observation does not rerun or update CI.");
        }

        return 0;
    }

    private static async Task<int> HandleReviewAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunOnly(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "feedback review", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var request = ReadJson<FeedbackLoopRequest>(Path.Combine(runPath, "feedback-loop-request.json"));
        if (request is null)
            return Failure(output, error, parsed.Json, "feedback review", "feedback-loop-request.json is missing; run 'irondev feedback request' first.");

        var observation = await ReadReviewFeedbackAsync(request, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "review-feedback-snapshot.json", observation, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "review-feedback-report.md"), RenderReview(observation), cancellationToken).ConfigureAwait(false);
        var comments = observation.InlineComments.Concat(observation.TopLevelComments).Select(item => JsonSerializer.Serialize(item, JsonOptions));
        await File.WriteAllLinesAsync(Path.Combine(runPath, "review-feedback-comments.jsonl"), comments, cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.ReviewFeedbackSnapshotCreated, observation.ReviewFeedbackSnapshotId, "Review feedback snapshot was created.", ["review-feedback-snapshot.json"]);

        if (parsed.Json)
            WriteJson(output, "feedback review", "succeeded", new { runPath, observation }, []);
        else
        {
            output.WriteLine($"Review feedback: {observation.RequestedChanges.Length} requested change(s), {observation.InlineComments.Length + observation.TopLevelComments.Length} comment(s)");
            output.WriteLine("Boundary: review observation does not reply, resolve, approve, or request review.");
        }

        return 0;
    }

    private static async Task<int> HandleClassifyAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunOnly(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "feedback classify", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var request = ReadJson<FeedbackLoopRequest>(Path.Combine(runPath, "feedback-loop-request.json"));
        if (request is null)
            return Failure(output, error, parsed.Json, "feedback classify", "feedback-loop-request.json is missing; run 'irondev feedback request' first.");

        var report = FeedbackClassifier.Classify(
            request,
            ReadJson<CiObservationSnapshot>(Path.Combine(runPath, "ci-observation-snapshot.json")),
            ReadJson<ReviewFeedbackSnapshot>(Path.Combine(runPath, "review-feedback-snapshot.json")));
        await WriteJsonAsync(runPath, "feedback-classification-report.json", report, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "feedback-classification-report.md"), RenderClassification(report), cancellationToken).ConfigureAwait(false);
        await File.WriteAllLinesAsync(Path.Combine(runPath, "feedback-findings.jsonl"), report.Findings.Select(item => JsonSerializer.Serialize(item, JsonOptions)), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.FeedbackClassificationReportCreated, report.FeedbackClassificationReportId, "Feedback classification report was created.", ["feedback-classification-report.json"]);

        if (parsed.Json)
            WriteJson(output, "feedback classify", "succeeded", new { runPath, report }, []);
        else
        {
            output.WriteLine($"Feedback findings: {report.Findings.Length}");
            output.WriteLine("Boundary: classification labels feedback only and cannot fix or answer it.");
        }

        return 0;
    }

    private static async Task<int> HandlePlanAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunOnly(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "feedback plan", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var request = ReadJson<FeedbackLoopRequest>(Path.Combine(runPath, "feedback-loop-request.json"));
        var classification = ReadJson<FeedbackClassificationReport>(Path.Combine(runPath, "feedback-classification-report.json"));
        if (request is null || classification is null)
            return Failure(output, error, parsed.Json, "feedback plan", "feedback-loop-request.json and feedback-classification-report.json are required first.");

        var plan = FeedbackRemediationPlanner.Propose(request, classification, ReadKnownRisks(runPath));
        var testProfile = new SuggestedFeedbackTestProfile { RunId = request.RunId, SuggestedTests = plan.SuggestedTests };
        await WriteJsonAsync(runPath, "feedback-remediation-plan.json", plan, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "feedback-remediation-plan.md"), RenderPlan(plan), cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "suggested-feedback-test-profile.json", testProfile, cancellationToken).ConfigureAwait(false);
        await File.WriteAllLinesAsync(Path.Combine(runPath, "feedback-known-risks.md"), plan.KnownRisks, cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.FeedbackRemediationPlanCreated, plan.FeedbackRemediationPlanId, "Feedback remediation plan proposal was created.", ["feedback-remediation-plan.json"]);

        if (parsed.Json)
            WriteJson(output, "feedback plan", "succeeded", new { runPath, plan }, []);
        else
        {
            output.WriteLine($"Feedback remediation plan: {plan.FeedbackRemediationPlanId}");
            output.WriteLine("Boundary: plan is not a patch and cannot update the PR.");
        }

        return 0;
    }

    private static async Task<int> HandleReadinessAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunOnly(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "feedback readiness", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var request = ReadJson<FeedbackLoopRequest>(Path.Combine(runPath, "feedback-loop-request.json"));
        if (request is null)
            return Failure(output, error, parsed.Json, "feedback readiness", "feedback-loop-request.json is missing; run 'irondev feedback request' first.");

        var report = FeedbackReadinessReporter.Report(
            request,
            ReadJson<CiObservationSnapshot>(Path.Combine(runPath, "ci-observation-snapshot.json")),
            ReadJson<ReviewFeedbackSnapshot>(Path.Combine(runPath, "review-feedback-snapshot.json")),
            ReadJson<FeedbackClassificationReport>(Path.Combine(runPath, "feedback-classification-report.json")),
            ReadJson<FeedbackRemediationPlan>(Path.Combine(runPath, "feedback-remediation-plan.json")),
            ReadKnownRisks(runPath));
        await WriteJsonAsync(runPath, "feedback-readiness-report.json", report, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "feedback-readiness-report.md"), RenderReadiness(report), cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "feedback-status.json", new FeedbackLoopStatusReport { RunId = request.RunId, ArtifactNames = ReadArtifactNames(runPath) }, cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.FeedbackReadinessReportCreated, report.FeedbackReadinessReportId, "Feedback readiness report was created.", ["feedback-readiness-report.json"]);

        if (parsed.Json)
            WriteJson(output, "feedback readiness", "succeeded", new { runPath, report }, []);
        else
        {
            output.WriteLine($"Feedback readiness: {report.Outcome}");
            output.WriteLine("Boundary: readiness report does not mark ready, merge, release, deploy, or continue workflow.");
        }

        return 0;
    }

    private static int HandleStatus(string[] args, TextWriter output, TextWriter error)
    {
        var parsed = ParseRunOnly(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "feedback status", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var artifacts = StatusArtifacts.Select(name => new { name, exists = File.Exists(Path.Combine(runPath, name)) }).ToArray();
        var bypass = FeedbackLoopBypassEvaluator.Evaluate(RunId(runPath), ["feedback loop request", "CI observation snapshot", "review feedback snapshot", "feedback classification report", "remediation plan", "feedback readiness report", "test success", "build success", "review approval", "no known blocking feedback", "human-looking approval text", "AI review text", "memory plan text", "release readiness report"]);
        WriteJsonAsync(runPath, "feedback-loop-bypass-report.json", bypass, CancellationToken.None).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(runPath, "feedback-loop-bypass-report.md"), RenderBypass(bypass));
        RecordEvent(runPath, GovernanceKernelEventKind.FeedbackLoopBypassReportCreated, bypass.FeedbackLoopBypassReportId, "Feedback loop bypass report was created.", ["feedback-loop-bypass-report.json"]);

        if (parsed.Json)
            WriteJson(output, "feedback status", "succeeded", new { runPath, artifacts, bypass }, []);
        else
        {
            output.WriteLine($"Feedback artifacts: {runPath}");
            foreach (var artifact in artifacts)
                output.WriteLine($"- {artifact.name}: {(artifact.exists ? "present" : "missing")}");
            output.WriteLine("Boundary: status is evidence only and grants no PR or CI authority.");
        }

        return 0;
    }

    private static async Task<int> HandlePackageAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePackage(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "feedback package", parsed.Error);

        if (parsed.Mode == PackageMode.Status || parsed.Mode == PackageMode.Records)
        {
            var package = ReadJson<FeedbackRemediationPackage>(ResolvePackagePath(parsed.PackagePath!));
            if (package is null)
                return Failure(output, error, parsed.Json, "feedback package", "feedback remediation package is missing or invalid.");

            if (parsed.Mode == PackageMode.Records)
            {
                if (parsed.Json)
                    WriteJson(output, "feedback package records", "succeeded", new { package.RemediationCandidates, package.FeedbackItems }, []);
                else
                {
                    foreach (var candidate in package.RemediationCandidates)
                        output.WriteLine($"{candidate.Disposition}: {candidate.Rationale}");
                }
            }
            else if (parsed.Json)
            {
                WriteJson(output, "feedback package status", "succeeded", new
                {
                    package.FeedbackRemediationPackageId,
                    package.RunId,
                    package.PullRequestNumber,
                    package.CurrentHeadSha,
                    itemCount = package.FeedbackItems.Length,
                    candidateCount = package.RemediationCandidates.Length,
                    boundary = package.Boundary
                }, []);
            }
            else
            {
                output.WriteLine($"Feedback package: {package.FeedbackRemediationPackageId}");
                output.WriteLine($"Items: {package.FeedbackItems.Length}");
                output.WriteLine($"Candidates: {package.RemediationCandidates.Length}");
                output.WriteLine("Boundary: package is evidence only and cannot propose patches or update PRs.");
            }

            return 0;
        }

        var outPath = parsed.OutPath!;
        var input = parsed.Mode == PackageMode.FromReceipt
            ? BuildPackageInputFromValidationReceipt(parsed.FromReceiptPath!, outPath)
            : BuildPackageInputFromObservedFeedback(parsed, outPath);
        var artifacts = FeedbackRemediationPackager.Build(input);
        await WritePackageArtifactsAsync(outPath, artifacts, cancellationToken).ConfigureAwait(false);
        RecordEvent(ResolveOutputDirectory(outPath), GovernanceKernelEventKind.FeedbackRemediationPackageCreated, artifacts.Package.FeedbackRemediationPackageId, "Feedback remediation package was created.", ["feedback-remediation-package.json", "feedback-remediation-package-receipt.json"]);

        if (parsed.Json)
            WriteJson(output, "feedback package", "succeeded", new { outPath = ResolveOutputDirectory(outPath), artifacts.Package, artifacts.Receipt }, []);
        else
        {
            output.WriteLine($"Feedback remediation package: {artifacts.Package.FeedbackRemediationPackageId}");
            output.WriteLine($"Receipt: {artifacts.Receipt.FeedbackRemediationPackageReceiptId}");
            output.WriteLine("Boundary: AP package is evidence only and cannot propose patches, apply source changes, update PR branches, or continue workflow.");
        }

        return 0;
    }

    private static async Task<string?> ReadPullRequestHeadAsync(string repositoryFullName, int pullRequestNumber, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync("gh", ["pr", "view", pullRequestNumber.ToString(), "--repo", repositoryFullName, "--json", "headRefOid"], Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
            return null;
        try
        {
            using var document = JsonDocument.Parse(result.Stdout);
            return document.RootElement.TryGetProperty("headRefOid", out var head) ? head.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<CiCheckRunSummary[]> ReadCiChecksAsync(string repositoryFullName, int pullRequestNumber, string expectedHeadSha, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync("gh", ["pr", "checks", pullRequestNumber.ToString(), "--repo", repositoryFullName, "--json", "name,state,conclusion,detailsUrl,workflow"], Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
            return [];
        try
        {
            using var document = JsonDocument.Parse(result.Stdout);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];
            return document.RootElement.EnumerateArray().Select(item =>
                new CiCheckRunSummary
                {
                    Name = ReadString(item, "name") ?? "unknown check",
                    Status = ReadString(item, "state"),
                    Conclusion = ReadString(item, "conclusion"),
                    HeadSha = expectedHeadSha,
                    Url = ReadString(item, "detailsUrl")
                }).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static async Task<ReviewFeedbackSnapshot> ReadReviewFeedbackAsync(FeedbackLoopRequest request, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync("gh", ["pr", "view", request.PullRequestNumber.ToString(), "--repo", request.RepositoryFullName, "--json", "headRefOid,reviews,comments"], Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
            return ReviewFeedbackSnapshotBuilder.Build(request, null, [], [], []);
        try
        {
            using var document = JsonDocument.Parse(result.Stdout);
            var root = document.RootElement;
            var head = ReadString(root, "headRefOid");
            var reviews = ReadArray(root, "reviews").Select(item => new ReviewSubmissionSummary
            {
                Author = ReadNestedString(item, "author", "login") ?? "unknown",
                State = MapReviewState(ReadString(item, "state")),
                BodySummary = FeedbackText.Summary(ReadString(item, "body")),
                HeadSha = head,
                Url = ReadString(item, "url"),
                SubmittedAtUtc = ReadDate(item, "submittedAt")
            }).ToArray();
            var comments = ReadArray(root, "comments").Select(item => new ReviewCommentSummary
            {
                Author = ReadNestedString(item, "author", "login") ?? "unknown",
                BodySummary = FeedbackText.Summary(ReadString(item, "body")),
                HeadSha = head,
                Url = ReadString(item, "url"),
                CreatedAtUtc = ReadDate(item, "createdAt")
            }).ToArray();
            var inlineComments = await ReadInlineReviewCommentsAsync(request, head, cancellationToken).ConfigureAwait(false);
            return ReviewFeedbackSnapshotBuilder.Build(request, head, reviews, inlineComments, comments);
        }
        catch
        {
            return ReviewFeedbackSnapshotBuilder.Build(request, null, [], [], []);
        }
    }

    private static async Task<ReviewCommentSummary[]> ReadInlineReviewCommentsAsync(FeedbackLoopRequest request, string? observedHeadSha, CancellationToken cancellationToken)
    {
        var repositoryPath = request.RepositoryFullName.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(repositoryPath) || !repositoryPath.Contains('/'))
            return [];

        var result = await RunProcessAsync("gh", ["api", $"repos/{repositoryPath}/pulls/{request.PullRequestNumber}/comments", "--paginate"], Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
            return [];

        return IronDevCliFeedbackReviewParser.ParseInlineReviewComments(result.Stdout, observedHeadSha);
    }

    private static ReviewFeedbackState MapReviewState(string? state) =>
        state?.ToUpperInvariant() switch
        {
            "CHANGES_REQUESTED" => ReviewFeedbackState.RequestedChanges,
            "COMMENTED" => ReviewFeedbackState.Commented,
            "APPROVED" => ReviewFeedbackState.ApprovedButNonAuthoritative,
            "PENDING" => ReviewFeedbackState.Pending,
            "DISMISSED" => ReviewFeedbackState.Dismissed,
            _ => ReviewFeedbackState.Unknown
        };

    private static string RenderRequest(FeedbackLoopRequest request, PullRequestStatusReport? status) => $"""
        # Feedback Loop Request

        Request: `{request.FeedbackLoopRequestId}`
        PR: `{request.PullRequestNumber}`
        URL: {request.PullRequestUrl}
        Expected head SHA: `{request.ExpectedHeadSha}`
        Draft: `{status?.Draft.ToString() ?? "unknown"}`

        Boundary: AN1 creates a feedback-loop request. It does not inspect or mutate the PR.
        """;

    private static string RenderCi(CiObservationSnapshot snapshot) => $"""
        # CI Observation

        State: `{snapshot.OverallCiState}`
        Expected head SHA: `{snapshot.ExpectedHeadSha}`
        Observed head SHA: `{snapshot.ObservedHeadSha ?? "missing"}`
        Failures:
        {RenderBullets(snapshot.Failures.Select(item => $"{item.Source}: {item.Message}"))}

        Boundary: AN2 observes CI status for the controlled PR head. It does not rerun or update CI.
        """;

    private static string RenderReview(ReviewFeedbackSnapshot snapshot) => $"""
        # Review Feedback Observation

        Requested changes: `{snapshot.RequestedChanges.Length}`
        Inline comments: `{snapshot.InlineComments.Length}`
        Top-level comments: `{snapshot.TopLevelComments.Length}`
        Unresolved threads: `{snapshot.UnresolvedThreads.Length}`

        Boundary: AN3 observes review feedback. It does not reply, resolve, approve, or request review.
        """;

    private static string RenderClassification(FeedbackClassificationReport report) => $"""
        # Feedback Classification

        Findings:
        {RenderBullets(report.Findings.Select(item => $"{item.Category} / {item.Actionability}: {item.Message}"))}

        Boundary: AN4 classifies CI and review feedback. It does not fix or answer feedback.
        """;

    private static string RenderPlan(FeedbackRemediationPlan plan) => $"""
        # Feedback Remediation Plan

        Proposed patch scope:
        {RenderBullets(plan.ProposedPatchScope)}

        Human questions:
        {RenderBullets(plan.HumanQuestions)}

        Evidence refresh:
        {RenderBullets(plan.EvidenceRefreshRequests)}

        {plan.NonAuthorityBoundary}
        """;

    private static string RenderReadiness(FeedbackReadinessReport report) => $"""
        # Feedback Readiness

        Outcome: `{report.Outcome}`
        Suggested next command: `{report.SuggestedNextCommand}`
        Blockers:
        {RenderBullets(report.Blockers)}

        Boundary: AN6 reports feedback-loop readiness. It does not mark the PR ready or advance workflow.
        """;

    private static string RenderBypass(FeedbackLoopBypassReport report) => $"""
        # Feedback Loop Bypass Report

        {RenderBullets(report.EvidenceSubjects.Select(subject => $"{subject} cannot update PRs, reply, resolve, rerun CI, merge, release, deploy, or continue workflow."))}
        """;

    private static string RenderBullets(IEnumerable<string> values) =>
        string.Join(Environment.NewLine, values.DefaultIfEmpty("(none)").Select(item => $"- {item}"));

    private static string[] ReadKnownRisks(string runPath)
    {
        var path = Path.Combine(runPath, "feedback-known-risks.md");
        if (File.Exists(path))
            return File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        path = Path.Combine(runPath, "known-risks.md");
        return File.Exists(path) ? File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray() : [];
    }

    private static FeedbackRemediationPackageInput BuildPackageInputFromObservedFeedback(ParsedPackage parsed, string outPath)
    {
        var runPath = ResolveOutputDirectory(parsed.Run ?? outPath);
        var request = ReadJson<FeedbackLoopRequest>(Path.Combine(runPath, "feedback-loop-request.json"));
        var ci = ReadJson<CiObservationSnapshot>(Path.Combine(runPath, "ci-observation-snapshot.json"));
        var review = ReadJson<ReviewFeedbackSnapshot>(Path.Combine(runPath, "review-feedback-snapshot.json"));
        var items = new List<FeedbackItemInput>();
        if (ci is not null)
            items.AddRange(BuildItemsFromCi(ci));
        if (review is not null)
            items.AddRange(BuildItemsFromReview(review));

        return new FeedbackRemediationPackageInput
        {
            RunId = request?.RunId ?? RunId(runPath),
            RepositoryFullName = parsed.RepositoryFullName ?? request?.RepositoryFullName ?? "unknown/repository",
            PullRequestNumber = parsed.PullRequestNumber ?? request?.PullRequestNumber ?? 0,
            CurrentHeadSha = parsed.HeadSha ?? request?.ExpectedHeadSha ?? "unknown",
            PullRequestUrl = request?.PullRequestUrl,
            Items = items.ToArray(),
            EvidenceRefs = ReadArtifactNames(runPath)
        };
    }

    private static FeedbackRemediationPackageInput BuildPackageInputFromValidationReceipt(string receiptPath, string outPath)
    {
        var receipt = ReadJson<ValidationRunReceipt>(Path.GetFullPath(receiptPath)) ?? throw new InvalidOperationException("validation receipt is missing or invalid.");
        var items = new List<FeedbackItemInput>();
        foreach (var failure in receipt.FailureClassifications.DefaultIfEmpty(ValidationFailureKind.Passed).Where(kind => kind != ValidationFailureKind.Passed))
        {
            items.Add(new FeedbackItemInput
            {
                SourceKind = FeedbackSourceKind.LocalValidationReceipt,
                SourceId = $"{receipt.ValidationRunId}:{failure}",
                CommitSha = receipt.CommitSha,
                RawExcerpt = $"{failure} in validation receipt {receipt.ValidationRunId}. Verdict: {receipt.Verdict}.",
                CreatedAtUtc = receipt.FinishedUtc
            });
        }

        foreach (var dirty in receipt.DirtyChangedFiles)
        {
            items.Add(new FeedbackItemInput
            {
                SourceKind = FeedbackSourceKind.LocalValidationReceipt,
                SourceId = $"{receipt.ValidationRunId}:dirty:{dirty.Path}",
                CommitSha = receipt.CommitSha,
                FilePath = dirty.Path,
                RawExcerpt = $"Dirty generated artifact: {dirty.Path}. {dirty.Reason}",
                CreatedAtUtc = receipt.FinishedUtc
            });
        }

        return new FeedbackRemediationPackageInput
        {
            RunId = receipt.ValidationRunId,
            RepositoryFullName = "local/validation",
            PullRequestNumber = 0,
            CurrentHeadSha = receipt.CommitSha,
            Items = items.ToArray(),
            EvidenceRefs = [Path.GetFileName(receiptPath)],
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static IEnumerable<FeedbackItemInput> BuildItemsFromCi(CiObservationSnapshot ci)
    {
        foreach (var failure in ci.Failures)
        {
            yield return new FeedbackItemInput
            {
                SourceKind = FeedbackSourceKind.GitHubCheckRun,
                SourceId = failure.Source,
                SourceUrl = failure.Url,
                CommitSha = ci.ObservedHeadSha ?? ci.ExpectedHeadSha,
                RawExcerpt = FeedbackText.Summary($"{failure.Message} {failure.Excerpt}", maxLength: 500),
                CreatedAtUtc = ci.ObservedAtUtc
            };
        }
    }

    private static IEnumerable<FeedbackItemInput> BuildItemsFromReview(ReviewFeedbackSnapshot review)
    {
        foreach (var comment in review.InlineComments)
            yield return ToFeedbackItem(FeedbackSourceKind.GitHubReviewComment, comment, review.ExpectedHeadSha, threadId: null);
        foreach (var comment in review.TopLevelComments)
            yield return ToFeedbackItem(FeedbackSourceKind.GitHubIssueComment, comment, review.ExpectedHeadSha, threadId: null);
        foreach (var thread in review.UnresolvedThreads)
        {
            yield return new FeedbackItemInput
            {
                SourceKind = FeedbackSourceKind.GitHubReviewThread,
                SourceId = thread.ThreadId,
                SourceUrl = thread.Url,
                ThreadId = thread.ThreadId,
                CommitSha = thread.HeadSha ?? review.ExpectedHeadSha,
                RawExcerpt = thread.Summary,
                IsResolved = thread.IsResolved,
                CreatedAtUtc = review.ObservedAtUtc
            };
        }
    }

    private static FeedbackItemInput ToFeedbackItem(FeedbackSourceKind kind, ReviewCommentSummary comment, string expectedHeadSha, string? threadId) => new()
    {
        SourceKind = kind,
        SourceId = comment.Url ?? $"{kind}:{comment.Author}:{comment.Path}:{comment.Line}",
        SourceUrl = comment.Url,
        Author = comment.Author,
        CommitSha = comment.HeadSha ?? expectedHeadSha,
        FilePath = comment.Path,
        Line = comment.Line,
        ThreadId = threadId,
        RawExcerpt = comment.BodySummary,
        CreatedAtUtc = comment.CreatedAtUtc
    };

    private static async Task WritePackageArtifactsAsync(string outPath, FeedbackRemediationPackageArtifacts artifacts, CancellationToken cancellationToken)
    {
        var outDirectory = ResolveOutputDirectory(outPath);
        Directory.CreateDirectory(outDirectory);
        var packagePath = ResolvePackagePath(outPath, forWrite: true);
        await File.WriteAllTextAsync(packagePath, JsonSerializer.Serialize(artifacts.Package, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "feedback-remediation-package-receipt.json"), JsonSerializer.Serialize(artifacts.Receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "feedback-remediation-summary.md"), RenderPackageSummary(artifacts), cancellationToken).ConfigureAwait(false);
        await File.WriteAllLinesAsync(Path.Combine(outDirectory, "feedback-remediation-candidates.jsonl"), artifacts.Package.RemediationCandidates.Select(item => JsonSerializer.Serialize(item, JsonOptions)), cancellationToken).ConfigureAwait(false);
    }

    private static string RenderPackageSummary(FeedbackRemediationPackageArtifacts artifacts) => $"""
        # Feedback Remediation Package

        Verdict: `{artifacts.Receipt.Verdict}`
        Items: `{artifacts.Receipt.FeedbackItemCount}`
        Candidates: `{artifacts.Receipt.RemediationCandidateCount}`

        Candidates:
        {RenderBullets(artifacts.Package.RemediationCandidates.Select(item => $"{item.Disposition}: {item.Rationale}"))}

        Boundary: AP packages feedback into remediation evidence only. It does not propose patches, apply source changes, update PR branches, approve, mark ready, request reviewers, merge, release, deploy, or continue workflow.
        """;

    private static string ResolvePackagePath(string path, bool forWrite = false)
    {
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath) || !Path.HasExtension(fullPath) || forWrite && !string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(fullPath, "feedback-remediation-package.json");
        return fullPath;
    }

    private static string ResolveOutputDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Path.HasExtension(fullPath) && string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory()
            : fullPath;
    }

    private static string[] ReadArtifactNames(string runPath) =>
        Directory.Exists(runPath)
            ? Directory.EnumerateFiles(runPath).Select(Path.GetFileName).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];

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
            subjectKind: "CiAndReviewFeedbackLoop",
            subjectId: subjectId,
            summary: summary,
            evidenceRefs: evidenceRefs);

    private static ParsedRequest ParseRequest(string[] args)
    {
        string? run = null;
        string? repo = null;
        string? expectedHead = null;
        int? pr = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run": if (!TryRead(args, ref index, out run)) return ParsedRequest.Fail(json, "--run requires a value."); break;
                case "--repo": if (!TryRead(args, ref index, out repo)) return ParsedRequest.Fail(json, "--repo requires a value."); break;
                case "--pr":
                    if (!TryRead(args, ref index, out var prValue) || !int.TryParse(prValue, out var parsedPr)) return ParsedRequest.Fail(json, "--pr requires a number.");
                    pr = parsedPr;
                    break;
                case "--expected-head": if (!TryRead(args, ref index, out expectedHead)) return ParsedRequest.Fail(json, "--expected-head requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedRequest.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(run)) return ParsedRequest.Fail(json, "Missing required option: --run <run-id-or-path>.");
        if (string.IsNullOrWhiteSpace(repo)) return ParsedRequest.Fail(json, "Missing required option: --repo <owner/name>.");
        if (pr is null) return ParsedRequest.Fail(json, "Missing required option: --pr <number>.");
        if (string.IsNullOrWhiteSpace(expectedHead)) return ParsedRequest.Fail(json, "Missing required option: --expected-head <sha>.");
        return new ParsedRequest(run, repo, pr.Value, expectedHead, json, null);
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
                default: return ParsedRunOnly.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(run) ? ParsedRunOnly.Fail(json, "Missing required option: --run <run-id-or-path>.") : new ParsedRunOnly(run, json, null);
    }

    private static ParsedPackage ParsePackage(string[] args)
    {
        string? run = null;
        string? repo = null;
        string? head = null;
        string? outPath = null;
        string? fromReceipt = null;
        string? packagePath = null;
        int? pr = null;
        var status = false;
        var records = false;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run": if (!TryRead(args, ref index, out run)) return ParsedPackage.Fail(json, "--run requires a value."); break;
                case "--repo": if (!TryRead(args, ref index, out repo)) return ParsedPackage.Fail(json, "--repo requires a value."); break;
                case "--head": if (!TryRead(args, ref index, out head)) return ParsedPackage.Fail(json, "--head requires a value."); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedPackage.Fail(json, "--out requires a value."); break;
                case "--from-receipt": if (!TryRead(args, ref index, out fromReceipt)) return ParsedPackage.Fail(json, "--from-receipt requires a value."); break;
                case "--package": if (!TryRead(args, ref index, out packagePath)) return ParsedPackage.Fail(json, "--package requires a value."); break;
                case "--pr":
                    if (!TryRead(args, ref index, out var prValue) || !int.TryParse(prValue, out var parsedPr)) return ParsedPackage.Fail(json, "--pr requires a number.");
                    pr = parsedPr;
                    break;
                case "--status": status = true; break;
                case "--records": records = true; break;
                case "--json": json = true; break;
                default: return ParsedPackage.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (status || records)
        {
            if (string.IsNullOrWhiteSpace(packagePath)) return ParsedPackage.Fail(json, "Missing required option: --package <feedback-remediation-package.json>.");
            return new ParsedPackage(status ? PackageMode.Status : PackageMode.Records, run, repo, pr, head, outPath, fromReceipt, packagePath, json, null);
        }

        if (!string.IsNullOrWhiteSpace(fromReceipt))
        {
            if (!File.Exists(fromReceipt)) return ParsedPackage.Fail(json, $"validation receipt not found: {fromReceipt}");
            if (string.IsNullOrWhiteSpace(outPath)) return ParsedPackage.Fail(json, "Missing required option: --out <path>.");
            return new ParsedPackage(PackageMode.FromReceipt, run, repo, pr, head, outPath, fromReceipt, packagePath, json, null);
        }

        if (pr is null) return ParsedPackage.Fail(json, "Missing required option: --pr <number>.");
        if (string.IsNullOrWhiteSpace(head)) return ParsedPackage.Fail(json, "Missing required option: --head <sha>.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedPackage.Fail(json, "Missing required option: --out <path>.");
        return new ParsedPackage(PackageMode.FromObservedFeedback, run, repo, pr, head, outPath, fromReceipt, packagePath, json, null);
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
        error.WriteLine("  irondev feedback request --run <run-id-or-path> --repo <owner/name> --pr <number> --expected-head <sha> [--json]");
        error.WriteLine("  irondev feedback ci --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev feedback review --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev feedback classify --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev feedback plan --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev feedback readiness --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev feedback package --pr <number> --head <sha> --out <path> [--run <run-id-or-path>] [--repo <owner/name>] [--json]");
        error.WriteLine("  irondev feedback package --from-receipt <validation-receipt.json> --out <path> [--json]");
        error.WriteLine("  irondev feedback package --status --package <feedback-remediation-package.json> [--json]");
        error.WriteLine("  irondev feedback package --records --package <feedback-remediation-package.json> [--json]");
        error.WriteLine("  irondev feedback status --run <run-id-or-path> [--json]");
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
            boundary = FeedbackLoopBoundary.Evidence
        }, JsonOptions));
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? ReadNestedString(JsonElement element, string objectName, string propertyName) =>
        element.TryGetProperty(objectName, out var nested) && nested.ValueKind == JsonValueKind.Object ? ReadString(nested, propertyName) : null;

    private static DateTimeOffset? ReadDate(JsonElement element, string propertyName) =>
        DateTimeOffset.TryParse(ReadString(element, propertyName), out var parsed) ? parsed : null;

    private static IEnumerable<JsonElement> ReadArray(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    private sealed record ParsedRequest(string? Run, string? RepositoryFullName, int PullRequestNumber, string? ExpectedHeadSha, bool Json, string? Error)
    {
        public static ParsedRequest Fail(bool json, string error) => new(null, null, 0, null, json, error);
    }

    private sealed record ParsedRunOnly(string? Run, bool Json, string? Error)
    {
        public static ParsedRunOnly Fail(bool json, string error) => new(null, json, error);
    }

    private enum PackageMode
    {
        FromObservedFeedback = 0,
        FromReceipt,
        Status,
        Records
    }

    private sealed record ParsedPackage(
        PackageMode Mode,
        string? Run,
        string? RepositoryFullName,
        int? PullRequestNumber,
        string? HeadSha,
        string? OutPath,
        string? FromReceiptPath,
        string? PackagePath,
        bool Json,
        string? Error)
    {
        public static ParsedPackage Fail(bool json, string error) => new(PackageMode.FromObservedFeedback, null, null, null, null, null, null, null, json, error);
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}

public static class IronDevCliFeedbackReviewParser
{
    public static ReviewCommentSummary[] ParseInlineReviewComments(string stdout, string? observedHeadSha)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return [];

        try
        {
            using var document = JsonDocument.Parse(stdout);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            return FlattenJsonArray(document.RootElement)
                .Select(item => new ReviewCommentSummary
                {
                    Author = ReadNestedString(item, "user", "login") ?? "unknown",
                    BodySummary = FeedbackText.Summary(ReadString(item, "body")),
                    HeadSha = ReadString(item, "commit_id") ?? observedHeadSha,
                    Url = ReadString(item, "html_url") ?? ReadString(item, "url"),
                    Path = ReadString(item, "path"),
                    Line = ReadInt(item, "line") ?? ReadInt(item, "original_line"),
                    CreatedAtUtc = ReadDate(item, "created_at")
                })
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<JsonElement> FlattenJsonArray(JsonElement root)
    {
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Array)
            {
                foreach (var nested in item.EnumerateArray())
                    yield return nested;
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                yield return item;
            }
        }
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? ReadNestedString(JsonElement element, string objectName, string propertyName) =>
        element.TryGetProperty(objectName, out var nested) && nested.ValueKind == JsonValueKind.Object ? ReadString(nested, propertyName) : null;

    private static DateTimeOffset? ReadDate(JsonElement element, string propertyName) =>
        DateTimeOffset.TryParse(ReadString(element, propertyName), out var parsed) ? parsed : null;

    private static int? ReadInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed) ? parsed : null;
}
