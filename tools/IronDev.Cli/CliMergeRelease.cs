using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

internal static class IronDevCliMergeRelease
{
    private const string DefaultRunsFolderName = "irondev-patch-runs";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "merge",
        "auto-merge",
        "enable-auto-merge",
        "release",
        "deploy",
        "tag",
        "publish",
        "continue",
        "continue-workflow",
        "push",
        "commit",
        "rerun-ci",
        "ready",
        "request-reviewers"
    ];

    private static readonly string[] StatusArtifacts =
    [
        "merge-release-separation-request.json",
        "merge-readiness-evidence-package.json",
        "release-readiness-evidence-package.json",
        "merge-release-boundary-map.json",
        "merge-separation-readiness-record.json",
        "release-separation-readiness-record.json",
        "merge-release-separation-report.json",
        "merge-release-bypass-report.json"
    ];

    public static bool IsMergeReleaseCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "merge-release", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "merge-release requires a subcommand: request, merge-evidence, release-evidence, boundary-map, records, or status.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"merge-release {args[1]} is intentionally unsupported; Block AO separates evidence only.");

        return subcommand switch
        {
            "request" => await HandleRequestAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "merge-evidence" => await HandleMergeEvidenceAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "release-evidence" => await HandleReleaseEvidenceAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "boundary-map" => await HandleBoundaryMapAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "records" => await HandleRecordsAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "status" => await HandleStatusAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            _ => Usage(error, $"unsupported merge-release subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandleRequestAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRequest(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "merge-release request", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        Directory.CreateDirectory(runPath);
        var prReceipt = ReadJson<PullRequestCreationReceipt>(Path.Combine(runPath, "pull-request-created-receipt.json"));
        var prRequest = ReadJson<PullRequestCreationRequest>(Path.Combine(runPath, "pull-request-creation-request.json"));
        var feedbackReadiness = ReadJson<FeedbackReadinessReport>(Path.Combine(runPath, "feedback-readiness-report.json"));
        if (prReceipt is null)
            return Failure(output, error, parsed.Json, "merge-release request", "pull-request-created-receipt.json is required first.");
        if (!string.Equals(prReceipt.RepositoryFullName, parsed.RepositoryFullName, StringComparison.OrdinalIgnoreCase))
            return Failure(output, error, parsed.Json, "merge-release request", "repository does not match the pull request creation receipt.");
        if (prReceipt.PullRequestNumber != parsed.PullRequestNumber)
            return Failure(output, error, parsed.Json, "merge-release request", "pull request number does not match the pull request creation receipt.");
        if (!string.Equals(prReceipt.ExpectedHeadSha, parsed.ExpectedHeadSha, StringComparison.OrdinalIgnoreCase))
            return Failure(output, error, parsed.Json, "merge-release request", "expected head SHA does not match the pull request creation receipt.");

        var evidenceRefs = new List<string> { "pull-request-created-receipt.json" };
        var feedbackReadinessId = feedbackReadiness?.FeedbackReadinessReportId ?? "missing-feedback-readiness-report";
        if (feedbackReadiness is not null)
            evidenceRefs.Add("feedback-readiness-report.json");
        else
            evidenceRefs.Add("MissingFeedbackReadinessReport");

        var request = MergeReleaseSeparationRequestWriter.Create(new MergeReleaseSeparationRequestInput
        {
            RunId = RunId(runPath),
            ProjectId = prRequest?.ProjectId ?? "unknown-project",
            RepositoryFullName = prReceipt.RepositoryFullName,
            PullRequestNumber = prReceipt.PullRequestNumber,
            PullRequestUrl = prReceipt.PullRequestUrl,
            BaseBranch = prReceipt.BaseBranch,
            HeadBranch = prReceipt.HeadBranch,
            ExpectedHeadSha = prReceipt.ExpectedHeadSha,
            PullRequestCreationReceiptId = prReceipt.PullRequestCreationReceiptId,
            FeedbackReadinessReportId = feedbackReadinessId,
            RequestedBy = Environment.UserName,
            Reason = "Separate merge readiness from release readiness for a controlled PR.",
            EvidenceRefs = evidenceRefs.ToArray()
        });

        await WriteJsonAsync(runPath, "merge-release-separation-request.json", request, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "merge-release-separation-request.md"), RenderRequest(request), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.MergeReleaseSeparationRequestCreated, request.MergeReleaseSeparationRequestId, "Merge/release separation request was created.", ["merge-release-separation-request.json"]);

        if (parsed.Json)
            WriteJson(output, "merge-release request", "succeeded", new { runPath, request }, []);
        else
        {
            output.WriteLine($"Merge/release separation request: {request.MergeReleaseSeparationRequestId}");
            output.WriteLine($"Run path: {runPath}");
            output.WriteLine("Boundary: request evidence does not merge, release, deploy, tag, publish, or continue workflow.");
        }

        return 0;
    }

    private static async Task<int> HandleMergeEvidenceAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunOnly(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "merge-release merge-evidence", parsed.Error);
        var runPath = ResolveRunPath(parsed.Run!);
        var request = ReadJson<MergeReleaseSeparationRequest>(Path.Combine(runPath, "merge-release-separation-request.json"));
        if (request is null)
            return Failure(output, error, parsed.Json, "merge-release merge-evidence", "merge-release-separation-request.json is missing; run 'irondev merge-release request' first.");

        var package = MergeReadinessEvidencePackager.Build(CreateMergeInput(runPath, request));
        await WriteJsonAsync(runPath, "merge-readiness-evidence-package.json", package, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "merge-readiness-evidence-report.md"), RenderMergeEvidence(package), cancellationToken).ConfigureAwait(false);
        await File.WriteAllLinesAsync(Path.Combine(runPath, "merge-readiness-blockers.jsonl"), package.MergeBlockers.Select(item => JsonSerializer.Serialize(new { blocker = item }, JsonOptions)), cancellationToken).ConfigureAwait(false);
        await File.WriteAllLinesAsync(Path.Combine(runPath, "merge-evidence-gaps.jsonl"), package.MergeEvidenceGaps.Select(item => JsonSerializer.Serialize(new { gap = item }, JsonOptions)), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.MergeReadinessEvidencePackageCreated, package.MergeReadinessEvidencePackageId, "Merge readiness evidence package was created.", ["merge-readiness-evidence-package.json"]);

        if (parsed.Json)
            WriteJson(output, "merge-release merge-evidence", "succeeded", new { runPath, package }, []);
        else
            output.WriteLine($"Merge readiness evidence: {package.Outcome}");
        return package.Outcome == MergeReadinessOutcome.ReadyForMergeDecision ? 0 : 1;
    }

    private static async Task<int> HandleReleaseEvidenceAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunOnly(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "merge-release release-evidence", parsed.Error);
        var runPath = ResolveRunPath(parsed.Run!);
        var request = ReadJson<MergeReleaseSeparationRequest>(Path.Combine(runPath, "merge-release-separation-request.json"));
        if (request is null)
            return Failure(output, error, parsed.Json, "merge-release release-evidence", "merge-release-separation-request.json is missing; run 'irondev merge-release request' first.");

        var package = ReleaseReadinessEvidencePackager.Build(CreateReleaseInput(runPath, request));
        await WriteJsonAsync(runPath, "release-readiness-evidence-package.json", package, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "release-readiness-evidence-report.md"), RenderReleaseEvidence(package), cancellationToken).ConfigureAwait(false);
        await File.WriteAllLinesAsync(Path.Combine(runPath, "release-readiness-blockers.jsonl"), package.ReleaseBlockers.Select(item => JsonSerializer.Serialize(new { blocker = item }, JsonOptions)), cancellationToken).ConfigureAwait(false);
        await File.WriteAllLinesAsync(Path.Combine(runPath, "release-evidence-gaps.jsonl"), package.ReleaseEvidenceGaps.Select(item => JsonSerializer.Serialize(new { gap = item }, JsonOptions)), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.ReleaseReadinessEvidencePackageCreated, package.ReleaseReadinessEvidencePackageId, "Release readiness evidence package was created.", ["release-readiness-evidence-package.json"]);

        if (parsed.Json)
            WriteJson(output, "merge-release release-evidence", "succeeded", new { runPath, package }, []);
        else
            output.WriteLine($"Release readiness evidence: {package.Outcome}");
        return package.Outcome == ReleaseReadinessEvidenceOutcome.ReadyForReleaseDecision ? 0 : 1;
    }

    private static async Task<int> HandleBoundaryMapAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunOnly(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "merge-release boundary-map", parsed.Error);
        var runPath = ResolveRunPath(parsed.Run!);
        var request = ReadJson<MergeReleaseSeparationRequest>(Path.Combine(runPath, "merge-release-separation-request.json"));
        if (request is null)
            return Failure(output, error, parsed.Json, "merge-release boundary-map", "merge-release-separation-request.json is missing; run 'irondev merge-release request' first.");

        var map = MergeReleaseBoundaryMapper.Build(request.RunId, Directory.EnumerateFiles(runPath).Select(Path.GetFileName).OfType<string>());
        await WriteJsonAsync(runPath, "merge-release-boundary-map.json", map, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "merge-release-boundary-map.md"), RenderBoundaryMap(map), cancellationToken).ConfigureAwait(false);
        await File.WriteAllLinesAsync(Path.Combine(runPath, "merge-release-boundary-violations.jsonl"), map.ForbiddenCrossUseFindings.Select(item => JsonSerializer.Serialize(new { violation = item }, JsonOptions)), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.MergeReleaseBoundaryMapCreated, map.MergeReleaseBoundaryMapId, "Merge/release boundary map was created.", ["merge-release-boundary-map.json"]);

        if (parsed.Json)
            WriteJson(output, "merge-release boundary-map", "succeeded", new { runPath, map }, []);
        else
            output.WriteLine($"Merge/release boundary map: {map.MergeReleaseBoundaryMapId}");
        return 0;
    }

    private static async Task<int> HandleRecordsAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRecords(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "merge-release records", parsed.Error);
        var runPath = ResolveRunPath(parsed.Run!);
        var request = ReadJson<MergeReleaseSeparationRequest>(Path.Combine(runPath, "merge-release-separation-request.json"));
        var merge = ReadJson<MergeReadinessEvidencePackage>(Path.Combine(runPath, "merge-readiness-evidence-package.json"));
        var release = ReadJson<ReleaseReadinessEvidencePackage>(Path.Combine(runPath, "release-readiness-evidence-package.json"));
        var map = ReadJson<MergeReleaseBoundaryMap>(Path.Combine(runPath, "merge-release-boundary-map.json"));
        if (request is null || merge is null || release is null || map is null)
            return Failure(output, error, parsed.Json, "merge-release records", "request, merge evidence, release evidence, and boundary map artifacts are required first.");

        var records = MergeReleaseSeparationRecordBuilder.Build(request, merge, release, map, parsed.ReviewedBy ?? Environment.UserName);
        await WriteJsonAsync(runPath, "merge-separation-readiness-record.json", records.MergeRecord, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "release-separation-readiness-record.json", records.ReleaseRecord, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "merge-release-separation-report.json", records.CombinedReport, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "merge-release-separation-report.md"), RenderSeparationReport(records.CombinedReport), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.MergeReleaseSeparationRecordsCreated, records.CombinedReport.MergeReleaseSeparationReportId, "Merge/release separation readiness records were created.", ["merge-release-separation-report.json"]);

        if (parsed.Json)
            WriteJson(output, "merge-release records", "succeeded", new { runPath, records }, []);
        else
            output.WriteLine($"Merge/release separation report: {records.CombinedReport.MergeReleaseSeparationReportId}");
        return 0;
    }

    private static async Task<int> HandleStatusAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunOnly(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "merge-release status", parsed.Error);
        var runPath = ResolveRunPath(parsed.Run!);
        Directory.CreateDirectory(runPath);
        var artifacts = StatusArtifacts.Select(name => new { name, exists = File.Exists(Path.Combine(runPath, name)) }).ToArray();
        var bypass = MergeReleaseBypassEvaluator.Evaluate(RunId(runPath), ["CI pass", "review approval", "no known blocking feedback", "feedback readiness report", "merge readiness evidence package", "release readiness evidence package", "merge-release boundary map", "merge separation readiness record", "release separation readiness record", "release-readiness report", "human-looking approval text", "AI review text", "memory plan text", "test success", "build success"]);
        await WriteJsonAsync(runPath, "merge-release-bypass-report.json", bypass, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "merge-release-bypass-report.md"), RenderBypass(bypass), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.MergeReleaseBypassReportCreated, bypass.MergeReleaseBypassReportId, "Merge/release bypass report was created.", ["merge-release-bypass-report.json"]);

        if (parsed.Json)
            WriteJson(output, "merge-release status", "succeeded", new { runPath, artifacts, bypass }, []);
        else
        {
            output.WriteLine($"Merge/release artifacts: {runPath}");
            foreach (var artifact in artifacts)
                output.WriteLine($"- {artifact.name}: {(artifact.exists ? "present" : "missing")}");
            output.WriteLine("Boundary: status is evidence only and grants no merge or release authority.");
        }

        return 0;
    }

    private static MergeReadinessEvidenceInput CreateMergeInput(string runPath, MergeReleaseSeparationRequest request)
    {
        var prReceipt = ReadJson<PullRequestCreationReceipt>(Path.Combine(runPath, "pull-request-created-receipt.json"));
        var prStatus = ReadJson<PullRequestStatusReport>(Path.Combine(runPath, "pull-request-status.json"));
        var review = ReadJson<CommitReadinessReview>(Path.Combine(runPath, "commit-readiness-review.json"));
        var ci = ReadJson<CiObservationSnapshot>(Path.Combine(runPath, "ci-observation-snapshot.json"));
        var reviewSnapshot = ReadJson<ReviewFeedbackSnapshot>(Path.Combine(runPath, "review-feedback-snapshot.json"));
        var feedback = ReadJson<FeedbackReadinessReport>(Path.Combine(runPath, "feedback-readiness-report.json"));
        var artifacts = ExistingArtifactNames(runPath);
        return new MergeReadinessEvidenceInput
        {
            Request = request,
            PullRequestReceiptExists = prReceipt is not null,
            PullRequestStatusExists = prStatus is not null,
            ObservedHeadSha = prStatus is not null ? prReceipt?.ObservedHeadSha : prReceipt?.ObservedHeadSha,
            PullRequestDraft = prStatus?.Draft ?? prReceipt?.Draft ?? false,
            CommitReadinessReviewExists = review is not null,
            CommitReadinessDecision = review?.Decision,
            CiObservationExists = ci is not null,
            CiState = ci?.OverallCiState,
            ReviewFeedbackSnapshotExists = reviewSnapshot is not null,
            RequestedChangeCount = reviewSnapshot?.RequestedChanges.Length ?? 0,
            FeedbackReadinessReportExists = feedback is not null,
            FeedbackReadinessOutcome = feedback?.Outcome,
            ArtifactConsistencyReportExists = File.Exists(Path.Combine(runPath, "artifact-consistency-report.json")),
            ArtifactConsistencyBlockers = CountArtifactConsistencyBlockers(Path.Combine(runPath, "artifact-consistency-report.json")),
            UnsafeMaterialReportExists = File.Exists(Path.Combine(runPath, "unsafe-material-report.json")),
            UnsafeMaterialFindings = CountUnsafeFindings(Path.Combine(runPath, "unsafe-material-report.json")),
            EvidenceRefs = artifacts
        };
    }

    private static ReleaseReadinessEvidenceInput CreateReleaseInput(string runPath, MergeReleaseSeparationRequest request)
    {
        var prStatus = ReadJson<PullRequestStatusReport>(Path.Combine(runPath, "pull-request-status.json"));
        var releaseReportPath = Path.Combine(runPath, "release-readiness-report.json");
        var releaseOutcome = ReadJsonText(releaseReportPath, "outcome") ?? ReadJsonText(releaseReportPath, "status");
        var dogfood = ReadJson<ProductDogfoodRun>(Path.Combine(runPath, "dogfood-run.json"));
        var artifacts = ExistingArtifactNames(runPath);
        return new ReleaseReadinessEvidenceInput
        {
            Request = request,
            PullRequestStatusExists = prStatus is not null,
            PullRequestMerged = prStatus?.Merged ?? false,
            ReleaseCandidateRef = prStatus?.Merged == true ? prStatus.PullRequestUrl : null,
            ProductHardeningEvidenceExists = dogfood is not null || File.Exists(Path.Combine(runPath, "product-hardening-bypass-report.json")),
            ProductHardeningPassed = dogfood?.Outcome == ProductHardeningAuditOutcome.Pass,
            ReleaseReadinessReportExists = File.Exists(releaseReportPath),
            ReleaseReadinessReportOutcome = releaseOutcome,
            ReleaseReadinessDecisionRecordExists = File.Exists(Path.Combine(runPath, "release-readiness-decision-record.json")),
            ArtifactConsistencyReportExists = File.Exists(Path.Combine(runPath, "artifact-consistency-report.json")),
            ArtifactConsistencyBlockers = CountArtifactConsistencyBlockers(Path.Combine(runPath, "artifact-consistency-report.json")),
            UnsafeMaterialReportExists = File.Exists(Path.Combine(runPath, "unsafe-material-report.json")),
            UnsafeMaterialFindings = CountUnsafeFindings(Path.Combine(runPath, "unsafe-material-report.json")),
            KnownRisksDocumented = File.Exists(Path.Combine(runPath, "known-risks.md")) || File.Exists(Path.Combine(runPath, "dogfood-known-risks.md")) || File.Exists(Path.Combine(runPath, "feedback-known-risks.md")),
            RecoveryEvidenceExists = File.Exists(Path.Combine(runPath, "resume-report.json")) || File.Exists(Path.Combine(runPath, "failure-summary.json")) || ExistingArtifactNames(runPath).Any(name => name.Contains("rollback", StringComparison.OrdinalIgnoreCase) || name.Contains("recovery", StringComparison.OrdinalIgnoreCase)),
            EvidenceRefs = artifacts
        };
    }

    private static string RenderRequest(MergeReleaseSeparationRequest request) => $"""
        # Merge/Release Separation Request

        Request: `{request.MergeReleaseSeparationRequestId}`
        PR: `{request.PullRequestNumber}`
        Expected head SHA: `{request.ExpectedHeadSha}`
        Pull request receipt: `{request.PullRequestCreationReceiptId}`
        Feedback readiness: `{request.FeedbackReadinessReportId}`

        Boundary: AO1 creates a merge/release separation request. It does not evaluate, merge, or release.
        """;

    private static string RenderMergeEvidence(MergeReadinessEvidencePackage package) => $"""
        # Merge Readiness Evidence

        Outcome: `{package.Outcome}`
        Blockers:
        {RenderBullets(package.MergeBlockers)}
        Gaps:
        {RenderBullets(package.MergeEvidenceGaps)}

        Boundary: AO2 packages merge-readiness evidence. It does not approve or perform a merge.
        """;

    private static string RenderReleaseEvidence(ReleaseReadinessEvidencePackage package) => $"""
        # Release Readiness Evidence

        Outcome: `{package.Outcome}`
        Blockers:
        {RenderBullets(package.ReleaseBlockers)}
        Gaps:
        {RenderBullets(package.ReleaseEvidenceGaps)}

        Boundary: AO3 packages release-readiness evidence separately from merge evidence. It does not release or deploy.
        """;

    private static string RenderBoundaryMap(MergeReleaseBoundaryMap map) => $"""
        # Merge/Release Boundary Map

        Merge evidence:
        {RenderBullets(map.MergeEvidenceRefs)}
        Release evidence:
        {RenderBullets(map.ReleaseEvidenceRefs)}
        Shared evidence:
        {RenderBullets(map.SharedEvidenceRefs)}
        Forbidden cross-use:
        {RenderBullets(map.ForbiddenCrossUseFindings)}

        Boundary: AO4 maps merge and release evidence families. It does not authorize either.
        """;

    private static string RenderSeparationReport(MergeReleaseSeparationReport report) => $"""
        # Merge/Release Separation Report

        Merge outcome: `{report.MergeOutcome}`
        Release outcome: `{report.ReleaseOutcome}`

        {string.Join(Environment.NewLine, report.BoundaryStatements)}

        Boundary: AO5 records merge/release separation readiness. It does not approve merge or release.
        """;

    private static string RenderBypass(MergeReleaseBypassReport report) => $"""
        # Merge/Release Bypass Report

        Evidence subjects:
        {RenderBullets(report.EvidenceSubjects)}

        None of these can merge, release, deploy, tag, publish, satisfy policy, or continue workflow.
        """;

    private static string RenderBullets(IEnumerable<string> values)
    {
        var items = values.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        return items.Length == 0 ? "- none" : string.Join(Environment.NewLine, items.Select(item => $"- {item}"));
    }

    private static int CountArtifactConsistencyBlockers(string path)
    {
        if (!File.Exists(path))
            return 0;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var count = 0;
            if (root.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array)
                count += issues.EnumerateArray().Count(item => string.Equals(ReadString(item, "severity"), "Blocking", StringComparison.OrdinalIgnoreCase));
            if (root.TryGetProperty("blockingIssues", out var blockers) && blockers.ValueKind == JsonValueKind.Array)
                count += blockers.GetArrayLength();
            var outcome = ReadString(root, "outcome") ?? ReadString(root, "status");
            if (!string.IsNullOrWhiteSpace(outcome) && (outcome.Contains("NotReady", StringComparison.OrdinalIgnoreCase) || outcome.Contains("Blocked", StringComparison.OrdinalIgnoreCase)))
                count++;
            return count;
        }
        catch
        {
            return 1;
        }
    }

    private static int CountUnsafeFindings(string path)
    {
        if (!File.Exists(path))
            return 0;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var count = root.TryGetProperty("findings", out var findings) && findings.ValueKind == JsonValueKind.Array ? findings.GetArrayLength() : 0;
            var outcome = ReadString(root, "outcome") ?? ReadString(root, "status");
            if (!string.IsNullOrWhiteSpace(outcome) && (outcome.Contains("NotReady", StringComparison.OrdinalIgnoreCase) || outcome.Contains("Blocked", StringComparison.OrdinalIgnoreCase)))
                count++;
            return count;
        }
        catch
        {
            return 1;
        }
    }

    private static string? ReadJsonText(string path, string property)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return ReadString(doc.RootElement, property);
        }
        catch
        {
            return null;
        }
    }

    private static string[] ExistingArtifactNames(string runPath) =>
        Directory.Exists(runPath)
            ? Directory.EnumerateFiles(runPath).Select(Path.GetFileName).OfType<string>().OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];

    private static T? ReadJson<T>(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : default;

    private static async Task WriteJsonAsync<T>(string runPath, string artifactName, T value, CancellationToken cancellationToken) =>
        await File.WriteAllTextAsync(Path.Combine(runPath, artifactName), JsonSerializer.Serialize(value, JsonOptions), cancellationToken).ConfigureAwait(false);

    private static void RecordEvent(string runPath, GovernanceKernelEventKind kind, string subjectId, string summary, string[] evidenceRefs) =>
        new FileBackedGovernanceEventStore(runPath).Append(RunId(runPath), subjectId, kind, "MergeReleaseSeparation", subjectId, summary, evidenceRefs);

    private static ParsedRequest ParseRequest(string[] args)
    {
        string? run = null;
        string? repo = null;
        int? pr = null;
        string? expectedHead = null;
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

    private static ParsedRecords ParseRecords(string[] args)
    {
        string? run = null;
        string? reviewedBy = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run": if (!TryRead(args, ref index, out run)) return ParsedRecords.Fail(json, "--run requires a value."); break;
                case "--reviewed-by": if (!TryRead(args, ref index, out reviewedBy)) return ParsedRecords.Fail(json, "--reviewed-by requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedRecords.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(run) ? ParsedRecords.Fail(json, "Missing required option: --run <run-id-or-path>.") : new ParsedRecords(run, reviewedBy, json, null);
    }

    private static bool TryRead(string[] args, ref int index, out string value)
    {
        value = string.Empty;
        if (index + 1 >= args.Length)
            return false;
        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string ResolveRunPath(string run)
    {
        var candidate = Path.GetFullPath(run.Trim());
        if (Path.IsPathRooted(run) || Directory.Exists(candidate) || File.Exists(Path.Combine(candidate, "run.json")))
            return candidate;
        return Path.Combine(Path.GetTempPath(), DefaultRunsFolderName, run.Trim());
    }

    private static string RunId(string runPath) => Path.GetFileName(Path.GetFullPath(runPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Usage(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine("Usage:");
        error.WriteLine("  irondev merge-release request --run <run-id-or-path> --repo <owner/name> --pr <number> --expected-head <sha> [--json]");
        error.WriteLine("  irondev merge-release merge-evidence --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev merge-release release-evidence --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev merge-release boundary-map --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev merge-release records --run <run-id-or-path> [--reviewed-by <name>] [--json]");
        error.WriteLine("  irondev merge-release status --run <run-id-or-path> [--json]");
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
            boundary = MergeReleaseSeparationBoundary.Evidence
        }, JsonOptions));
    }

    private sealed record ParsedRequest(string? Run, string? RepositoryFullName, int PullRequestNumber, string? ExpectedHeadSha, bool Json, string? Error)
    {
        public static ParsedRequest Fail(bool json, string error) => new(null, null, 0, null, json, error);
    }

    private sealed record ParsedRunOnly(string? Run, bool Json, string? Error)
    {
        public static ParsedRunOnly Fail(bool json, string error) => new(null, json, error);
    }

    private sealed record ParsedRecords(string? Run, string? ReviewedBy, bool Json, string? Error)
    {
        public static ParsedRecords Fail(bool json, string error) => new(null, null, json, error);
    }
}
