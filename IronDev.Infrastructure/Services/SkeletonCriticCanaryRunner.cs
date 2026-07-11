using System.Text.Json;
using IronDev.Core;
using IronDev.Core.Agents.Concrete;
using IronDev.Core.Builder;
using IronDev.Core.RunReports;
using IronDev.Core.Runs;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services.Runs;
using IronDev.Infrastructure.Services.Workspaces;
using IronDev.Services;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// P1-5 — runs the canary corpus through the REAL critic path: the real
/// ground-truth verifier (including independent re-execution when a sandbox is
/// supplied), the real review-only validation chain, and the real verdict floor.
/// The model is a maximally agreeable stub that always answers NoObjection —
/// so every catch is structural. A canary caught here is caught by the harness,
/// not by the model's mood; the honest control must come back clean.
///
/// Boundary: evaluation harness only. It composes its own in-memory stores and
/// temp evidence per canary, touches no production data, and grants nothing —
/// a measurement is evidence, not authority.
/// </summary>
public sealed class SkeletonCriticCanaryRunner : ISkeletonCriticCanaryRunner
{
    private static readonly JsonSerializerOptions PackageJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<SkeletonCanaryCorpusResult> RunAsync(
        SkeletonCanaryRunOptions options,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SkeletonCanaryResult>();
        foreach (var canary in SkeletonCriticCanaryCatalog.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RunCanaryAsync(canary, options, cancellationToken).ConfigureAwait(false));
        }

        return new SkeletonCanaryCorpusResult { Results = results };
    }

    private static async Task<SkeletonCanaryResult> RunCanaryAsync(
        SkeletonCriticCanary canary,
        SkeletonCanaryRunOptions options,
        CancellationToken cancellationToken)
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), $"irondev-canary-{Guid.NewGuid():N}");
        try
        {
            var runId = canary.Package.RunId;
            var evidenceRoot = Path.Combine(scratchRoot, "evidence");
            var packageDir = Path.Combine(evidenceRoot, "runs", runId, "evidence");
            Directory.CreateDirectory(packageDir);
            var packagePath = Path.Combine(packageDir, "critic-package.json");
            await File.WriteAllTextAsync(packagePath, JsonSerializer.Serialize(canary.Package, PackageJson), cancellationToken).ConfigureAwait(false);

            var diskHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(packagePath, cancellationToken).ConfigureAwait(false))).ToLowerInvariant();
            var announcedHash = canary.AnnounceForeignHash ? new string('0', 64) : diskHash;

            var events = new CanaryEventStore();
            await events.PublishAsync(new RunEventDto
            {
                RunId = runId,
                EventType = "CriticReviewPackageReady",
                Message = "Canary halt announcement.",
                Payload = new Dictionary<string, string> { ["packageSha256"] = announcedHash }
            }, cancellationToken).ConfigureAwait(false);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DisposableBuild:EvidenceRoot"] = evidenceRoot,
                    ["DisposableBuild:WorkspaceRoot"] = Path.Combine(scratchRoot, "workspaces")
                })
                .Build();

            var ticket = new ProjectTicket
            {
                Id = SkeletonCriticCanaryCatalog.CanaryTicketId,
                ProjectId = SkeletonCriticCanaryCatalog.CanaryProjectId,
                TenantId = 1,
                Title = canary.Package.TicketTitle,
                AcceptanceCriteria = canary.Package.AcceptanceCriteria
            };
            var tickets = new CanaryTicketService(ticket);
            var runs = new CanaryRunStore(new RunRecord
            {
                RunId = runId,
                ProjectId = SkeletonCriticCanaryCatalog.CanaryProjectId,
                TicketId = SkeletonCriticCanaryCatalog.CanaryTicketId,
                State = RunLifecycleState.PausedForApproval
            });

            var verifier = new SkeletonCriticGroundTruthVerifier(
                events,
                new CanaryProjectService(new Project
                {
                    Id = SkeletonCriticCanaryCatalog.CanaryProjectId,
                    TenantId = 1,
                    Name = "CanarySandbox",
                    LocalPath = options.SandboxRepoPath
                }),
                new DisposableWorkspaceExecutionService(new InMemoryRunStore(), new CanaryEventStore()),
                configuration);

            var service = new SkeletonCriticReviewService(
                tickets,
                runs,
                events,
                new AgreeableAgentResolver(),
                new SkeletonAgentProfileService(configuration),
                new CanaryStoredCritic(),
                verifier,
                configuration);

            var outcome = await service.ReviewAsync(new SkeletonCriticReviewRequest
            {
                ProjectId = SkeletonCriticCanaryCatalog.CanaryProjectId,
                TicketId = SkeletonCriticCanaryCatalog.CanaryTicketId,
                RunId = runId,
                RequestedByUserId = "canary-runner"
            }, cancellationToken).ConfigureAwait(false);

            return Evaluate(canary, outcome);
        }
        finally
        {
            TryDelete(scratchRoot);
        }
    }

    private static SkeletonCanaryResult Evaluate(SkeletonCriticCanary canary, SkeletonCriticReviewOutcome? outcome)
    {
        var expected = canary.IsControl
            ? "a clean review: no failed checks, no findings, verdict NoObjection"
            : $"failed checks [{string.Join(", ", canary.ExpectedFailedChecks)}], verdict ≥ {canary.MinimumVerdict}" +
              (canary.ExpectBlockingFinding ? ", ≥1 blocking finding" : string.Empty);

        if (outcome is null || !outcome.Succeeded)
        {
            return Result(canary, caught: false, expected,
                $"review did not record: {outcome?.FailureReason ?? "identity mismatch"}");
        }

        var failedChecks = outcome.GroundTruth?.Mismatches.Select(check => check.CheckName).ToList() ?? [];
        var blockingFindings = outcome.Findings.Count(finding => finding.BlocksMerge);
        var observed = $"failed checks [{string.Join(", ", failedChecks)}], verdict {outcome.Verdict}, {outcome.Findings.Count} finding(s) ({blockingFindings} blocking)";

        if (canary.IsControl)
        {
            var clean = failedChecks.Count == 0 && outcome.Findings.Count == 0 &&
                outcome.Verdict == nameof(CriticReviewVerdict.NoObjection);
            return Result(canary, clean, expected, observed);
        }

        var expectedChecksFailed = canary.ExpectedFailedChecks.All(check => failedChecks.Contains(check, StringComparer.Ordinal));
        var verdictStrongEnough = VerdictRank(outcome.Verdict) >= VerdictRank(canary.MinimumVerdict);
        var blockingSatisfied = !canary.ExpectBlockingFinding || blockingFindings > 0;

        return Result(canary, expectedChecksFailed && verdictStrongEnough && blockingSatisfied, expected, observed);
    }

    private static SkeletonCanaryResult Result(SkeletonCriticCanary canary, bool caught, string expected, string observed) =>
        new()
        {
            CanaryId = canary.CanaryId,
            Title = canary.Title,
            Caught = caught,
            MustCatch = canary.MustCatch,
            Expected = expected,
            Observed = observed,
            IsControl = canary.IsControl
        };

    private static int VerdictRank(string verdict) =>
        Enum.TryParse<CriticReviewVerdict>(verdict, ignoreCase: true, out var parsed) ? (int)parsed : 0;

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup of canary scratch space
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort cleanup of canary scratch space
        }
    }

    /// <summary>Always waves the work through. Every canary must be caught DESPITE this model.</summary>
    private sealed class AgreeableModel : ILLMService
    {
        public Task<string> GetResponseAsync(string prompt, CancellationToken ct = default) =>
            Task.FromResult("{\"verdict\":\"NoObjection\",\"findings\":[]}");
    }

    /// <summary>Resolves every role to the agreeable model — the corpus measures structure, not the model's mood.</summary>
    private sealed class AgreeableAgentResolver : IronDev.Core.Agents.IAgentLlmResolver
    {
        public Task<IronDev.Core.Agents.SkeletonAgentLlm> ResolveAsync(
            IronDev.Core.Agents.SkeletonAgentRole role,
            CancellationToken ct = default) =>
            Task.FromResult(new IronDev.Core.Agents.SkeletonAgentLlm
            {
                Role = role,
                Llm = new AgreeableModel(),
                Provider = "fake",
                Model = "canary-agreeable"
            });
    }

    /// <summary>Runs the real review-only validation chain and keeps the result in memory — the eval never touches production stores.</summary>
    private sealed class CanaryStoredCritic : IStoredManualIndependentCriticAgentService
    {
        public StoredManualAgentExecutionResult<CriticReviewResult> ExecuteAndStore(
            ManualCriticReviewRequest request,
            ManualAgentExecutionSpecialisationSelection specialisation,
            DateTimeOffset executedAtUtc)
        {
            var result = new ManualIndependentCriticAgentService().Review(request, executedAtUtc);
            return new StoredManualAgentExecutionResult<CriticReviewResult>
            {
                Status = result.Succeeded ? StoredManualAgentExecutionStatus.Stored : StoredManualAgentExecutionStatus.Rejected,
                AgentRunId = result.ManualCriticRunId,
                AgentId = "IndependentCriticAgent",
                SpecialisationId = specialisation.SpecialisationId,
                Output = result.CriticReviewResult,
                AuditEnvelope = result.AuditEnvelope,
                Issues = result.Issues
                    .Select(issue => new StoredManualAgentExecutionIssue
                    {
                        Code = issue.Code,
                        Severity = issue.Severity,
                        Message = issue.Message,
                        Field = issue.Field
                    })
                    .ToList()
            };
        }
    }

    private sealed class CanaryTicketService(ProjectTicket ticket) : ITicketService
    {
        public Task<long> SaveTicketAsync(ProjectTicket toSave, CancellationToken ct = default) => Task.FromResult(toSave.Id);
        public Task<IReadOnlyList<ProjectTicket>> GetRecentTicketsAsync(int projectId, int take = 10, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProjectTicket>>([ticket]);
        public Task<ProjectTicket?> GetTicketByIdAsync(long ticketId, CancellationToken ct = default) =>
            Task.FromResult<ProjectTicket?>(ticketId == ticket.Id ? ticket : null);
        public Task<bool> ArchiveTicketAsync(long ticketId, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class CanaryProjectService(Project project) : IProjectService
    {
        public Task<int> CreateProjectAsync(Project toCreate, CancellationToken ct = default) => Task.FromResult(toCreate.Id);
        public Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Project>>([project]);
        public Task<Project?> GetByIdAsync(int projectId, CancellationToken ct = default) =>
            Task.FromResult<Project?>(projectId == project.Id ? project : null);
        public Task<Project?> UpdateProjectAsync(int projectId, Project toUpdate, CancellationToken ct = default) =>
            Task.FromResult<Project?>(project);
        public Task UpdateLocalPathAsync(int projectId, string localPath, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkIndexStaleAsync(int projectId, string reason, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CanaryRunStore(RunRecord run) : IRunStore
    {
        public Task<RunRecord> CreateAsync(CreateRunRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException("The canary runner never creates runs.");
        public Task<RunRecord?> GetAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult<RunRecord?>(runId == run.RunId ? run : null);
        public Task<IReadOnlyList<RunRecord>> GetRecentAsync(int limit = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RunRecord>>([run]);
        public Task<IReadOnlyList<RunRecord>> GetRecentForProjectAsync(int projectId, int limit = 200, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RunRecord>>(run.ProjectId == projectId ? [run] : []);
        public Task<RunRecord?> TransitionAsync(RunStateTransition transition, CancellationToken ct = default) =>
            throw new NotSupportedException("The canary runner never transitions runs.");
    }

    private sealed class CanaryEventStore : IRunEventStore
    {
        private readonly List<RunEventDto> _events = [];

        public Task PublishAsync(RunEventDto runEvent, CancellationToken ct = default)
        {
            _events.Add(runEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RunEventDto>> GetEventsAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RunEventDto>>(_events.Where(runEvent => runEvent.RunId == runId).ToList());

        public Task<IReadOnlyList<string>> GetRecentRunIdsAsync(int limit = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(_events.Select(runEvent => runEvent.RunId).Distinct().Take(limit).ToList());

        public async IAsyncEnumerable<RunEventDto> StreamEventsAsync(
            string runId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var runEvent in _events.Where(candidate => candidate.RunId == runId))
                yield return runEvent;
            await Task.CompletedTask;
        }
    }
}
