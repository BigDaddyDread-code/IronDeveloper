using IronDev.Core.Auth;
using IronDev.Core.Governance;
using IronDev.Core.RunReports;

namespace IronDev.Infrastructure.Governance;

public sealed class BackendFrontendReadinessReadApi : IFrontendReadinessReadApi
{
    private readonly IReadOnlyList<IFrontendReadinessBackendTruthSource> _sources;
    private readonly ICurrentTenantContext? _tenantContext;

    public BackendFrontendReadinessReadApi(
        IEnumerable<IFrontendReadinessBackendTruthSource> sources,
        ICurrentTenantContext? tenantContext = null)
    {
        _sources = sources?.ToArray() ?? throw new ArgumentNullException(nameof(sources));
        _tenantContext = tenantContext;
    }

    public FrontendOperationStatusReadModel? GetOperationStatus(string operationId)
    {
        var key = Normalize(operationId);
        if (key is null)
            return null;

        return FirstVisible(source => source.GetOperationStatus(key), status =>
        {
            var snapshot = new FrontendReadinessReadSnapshot
            {
                OperationStatuses = new Dictionary<string, GovernedOperationStatus>(StringComparer.OrdinalIgnoreCase)
                {
                    [key] = status,
                    [status.OperationId] = status
                }
            };

            return new FrontendReadinessReadApi(snapshot).GetOperationStatus(key);
        });
    }

    public FrontendOperationTimelineReadModel? GetOperationTimeline(string operationId)
    {
        var key = Normalize(operationId);
        if (key is null)
            return null;

        return FirstVisible(source => source.GetOperationTimeline(key), timeline =>
        {
            var snapshot = new FrontendReadinessReadSnapshot
            {
                Timelines = new Dictionary<string, FrontendOperationTimelineReadModel>(StringComparer.OrdinalIgnoreCase)
                {
                    [key] = timeline,
                    [timeline.OperationId] = timeline
                }
            };

            return new FrontendReadinessReadApi(snapshot).GetOperationTimeline(key);
        });
    }

    public FrontendPatchPackageMetadataReadModel? GetPatchPackageMetadata(string packageId)
    {
        var key = Normalize(packageId);
        if (key is null)
            return null;

        return FirstVisible(source => source.GetPatchPackageMetadata(key), metadata =>
        {
            var snapshot = new FrontendReadinessReadSnapshot
            {
                PatchPackages = new Dictionary<string, FrontendPatchPackageMetadataReadModel>(StringComparer.OrdinalIgnoreCase)
                {
                    [key] = metadata,
                    [metadata.PackageId] = metadata
                }
            };

            return new FrontendReadinessReadApi(snapshot).GetPatchPackageMetadata(key);
        });
    }

    public FrontendPatchPackageArtifactsReadModel? GetPatchPackageArtifacts(string packageId)
    {
        var key = Normalize(packageId);
        if (key is null)
            return null;

        return FirstVisible(source => source.GetPatchPackageArtifacts(key), artifacts =>
        {
            var snapshot = new FrontendReadinessReadSnapshot
            {
                PatchPackageArtifacts = new Dictionary<string, FrontendPatchPackageArtifactsReadModel>(StringComparer.OrdinalIgnoreCase)
                {
                    [key] = artifacts,
                    [artifacts.PackageId] = artifacts
                }
            };

            return new FrontendReadinessReadApi(snapshot).GetPatchPackageArtifacts(key);
        });
    }

    public FrontendValidationResultMetadataReadModel? GetValidationResultMetadata(string validationResultId)
    {
        var key = Normalize(validationResultId);
        if (key is null)
            return null;

        return FirstVisible(source => source.GetValidationResultMetadata(key), metadata =>
        {
            var snapshot = new FrontendReadinessReadSnapshot
            {
                ValidationResults = new Dictionary<string, FrontendValidationResultMetadataReadModel>(StringComparer.OrdinalIgnoreCase)
                {
                    [key] = metadata,
                    [metadata.ValidationResultId] = metadata
                }
            };

            return new FrontendReadinessReadApi(snapshot).GetValidationResultMetadata(key);
        });
    }

    public FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(string evidenceRef)
    {
        var key = Normalize(evidenceRef);
        if (key is null)
            return null;

        return FirstVisible(source => source.GetEvidenceMetadata(key), metadata =>
        {
            var snapshot = new FrontendReadinessReadSnapshot
            {
                Evidence = new Dictionary<string, FrontendEvidenceMetadataReadModel>(StringComparer.OrdinalIgnoreCase)
                {
                    [key] = metadata,
                    [metadata.EvidenceRef] = metadata
                }
            };

            return new FrontendReadinessReadApi(snapshot).GetEvidenceMetadata(key);
        });
    }

    public FrontendReceiptMetadataReadModel? GetReceiptMetadata(string receiptRef)
    {
        var key = Normalize(receiptRef);
        if (key is null)
            return null;

        return FirstVisible(source => source.GetReceiptMetadata(key), metadata =>
        {
            var snapshot = new FrontendReadinessReadSnapshot
            {
                Receipts = new Dictionary<string, FrontendReceiptMetadataReadModel>(StringComparer.OrdinalIgnoreCase)
                {
                    [key] = metadata,
                    [metadata.ReceiptRef] = metadata
                }
            };

            return new FrontendReadinessReadApi(snapshot).GetReceiptMetadata(key);
        });
    }

    private TResult? FirstVisible<TSourceValue, TResult>(
        Func<IFrontendReadinessBackendTruthSource, TSourceValue?> read,
        Func<TSourceValue, TResult?> sanitize)
        where TSourceValue : class
        where TResult : class
    {
        foreach (var source in _sources.Where(IsVisible))
        {
            var value = read(source);
            if (value is null)
                continue;

            var sanitized = sanitize(value);
            if (sanitized is not null)
                return sanitized;
        }

        return null;
    }

    private bool IsVisible(IFrontendReadinessBackendTruthSource source)
    {
        if (source.TenantId is null)
            return true;

        return _tenantContext is not null &&
            _tenantContext.TenantId > 0 &&
            _tenantContext.TenantId == source.TenantId.Value;
    }

    private static string? Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class RunReportFrontendReadinessBackendTruthSource : FrontendReadinessBackendTruthSource
{
    private static readonly string[] ReadOnlyForbiddenActions =
    [
        "do not treat run report status as approval",
        "do not treat validation output as policy satisfaction",
        "do not execute source apply, rollback, commit, push, PR, merge, release, deployment, memory promotion, or workflow continuation from frontend readiness"
    ];

    private readonly IRunReportService _runReports;
    private readonly IRunEventStore _runEvents;

    public RunReportFrontendReadinessBackendTruthSource(
        IRunReportService runReports,
        IRunEventStore runEvents)
    {
        _runReports = runReports ?? throw new ArgumentNullException(nameof(runReports));
        _runEvents = runEvents ?? throw new ArgumentNullException(nameof(runEvents));
    }

    public override string SourceName => "run-reports";

    public override GovernedOperationStatus? GetOperationStatus(string operationId)
    {
        var detail = LoadRun(operationId);
        if (detail is null)
            return null;

        var state = MapState(detail.Status);
        var evidenceRefs = EvidenceRefs(detail).ToArray();
        var receiptRefs = state == GovernedOperationState.Completed ? [ReceiptRef(detail.RunId)] : Array.Empty<string>();

        return new GovernedOperationStatus
        {
            OperationId = detail.RunId,
            OperationKind = "RunReportStatus",
            Subject = Subject(detail),
            State = state,
            BlockedReasons = state == GovernedOperationState.Blocked ? ["Run report is present but does not prove completion."] : [],
            MissingEvidence = state == GovernedOperationState.Blocked ? ["completed-run-report"] : [],
            NextSafeActions = state switch
            {
                GovernedOperationState.Completed => ["inspect run report evidence"],
                GovernedOperationState.Failed => ["review run report failure evidence"],
                GovernedOperationState.Running => ["observe run status"],
                _ => ["collect backend run report evidence"]
            },
            ForbiddenActions = ReadOnlyForbiddenActions,
            EvidenceRefs = evidenceRefs,
            ReceiptRefs = receiptRefs,
            ObservedAtUtc = ObservedAt(detail),
            ExpiresAtUtc = null
        };
    }

    public override FrontendOperationTimelineReadModel? GetOperationTimeline(string operationId)
    {
        var detail = LoadRun(operationId);
        if (detail is null)
            return null;

        var entries = new List<FrontendTimelineEntry>();
        entries.AddRange(detail.Stages.Select((stage, index) => new FrontendTimelineEntry
        {
            EntryId = $"run-stage:{detail.RunId}:{index}",
            EventKind = string.IsNullOrWhiteSpace(stage.StageName) ? "RunStage" : stage.StageName,
            Summary = string.IsNullOrWhiteSpace(stage.Summary) ? $"{stage.AgentName} {stage.Status}".Trim() : stage.Summary,
            EvidenceRefs = EvidenceRefs(detail).ToArray(),
            ReceiptRefs = [],
            ObservedAtUtc = ObservedAt(detail)
        }));
        entries.AddRange(detail.Attempts.Select((attempt, index) => new FrontendTimelineEntry
        {
            EntryId = $"run-attempt:{detail.RunId}:{index}",
            EventKind = string.IsNullOrWhiteSpace(attempt.Type) ? "RunAttempt" : attempt.Type,
            Summary = string.IsNullOrWhiteSpace(attempt.Summary) ? attempt.Status : attempt.Summary,
            EvidenceRefs = EvidenceRefs(detail).ToArray(),
            ReceiptRefs = [],
            ObservedAtUtc = ObservedAt(detail)
        }));

        var events = LoadEvents(detail.RunId);
        entries.AddRange(events.Select(runEvent => new FrontendTimelineEntry
        {
            EntryId = $"run-event:{runEvent.EventId:D}",
            EventKind = runEvent.EventType,
            Summary = runEvent.Message,
            EvidenceRefs = runEvent.Payload.Select(pair => $"run-event-payload:{detail.RunId}:{pair.Key}").ToArray(),
            ReceiptRefs = [],
            ObservedAtUtc = runEvent.TimestampUtc
        }));

        if (entries.Count == 0)
        {
            entries.Add(new FrontendTimelineEntry
            {
                EntryId = $"run-report:{detail.RunId}",
                EventKind = "RunReportObserved",
                Summary = string.IsNullOrWhiteSpace(detail.Summary) ? detail.Status : detail.Summary,
                EvidenceRefs = EvidenceRefs(detail).ToArray(),
                ReceiptRefs = [],
                ObservedAtUtc = ObservedAt(detail)
            });
        }

        return new FrontendOperationTimelineReadModel
        {
            OperationId = detail.RunId,
            Entries = entries,
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
    }

    public override FrontendValidationResultMetadataReadModel? GetValidationResultMetadata(string validationResultId)
    {
        var runId = StripPrefix(validationResultId, "run-validation:");
        var detail = LoadRun(runId);
        if (detail is null)
            return null;

        return new FrontendValidationResultMetadataReadModel
        {
            ValidationResultId = $"run-validation:{detail.RunId}",
            Repository = string.Empty,
            Branch = string.Empty,
            RunId = detail.RunId,
            PatchHash = string.Empty,
            Outcome = detail.Status,
            WhatRan = detail.Attempts.Select(attempt => attempt.Type).Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            WhatPassed = detail.Attempts.Where(attempt => IsPassing(attempt.Status)).Select(attempt => attempt.Type).Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            WhatFailed = detail.Attempts.Where(attempt => IsFailing(attempt.Status)).Select(attempt => attempt.Type).Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            WhatWasSkipped = [],
            IsStale = false,
            EvidenceRefs = EvidenceRefs(detail).ToArray(),
            ReceiptRefs = [],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
    }

    public override FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(string evidenceRef)
    {
        if (!TryParseIndexedRef(evidenceRef, "run-evidence:", out var runId, out var index))
            return null;

        var detail = LoadRun(runId);
        if (detail is null || index < 0 || index >= detail.Evidence.Count)
            return null;

        var evidence = detail.Evidence[index];
        return new FrontendEvidenceMetadataReadModel
        {
            EvidenceRef = EvidenceRef(detail.RunId, index),
            EvidenceKind = evidence.Type,
            Summary = string.IsNullOrWhiteSpace(evidence.Summary) ? "Run evidence reference." : evidence.Summary,
            ReferenceOnly = true,
            ContainsRawPayload = false,
            Warnings = ["Run evidence metadata is reference-only.", "Run evidence does not approve or continue workflow."],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
    }

    public override FrontendReceiptMetadataReadModel? GetReceiptMetadata(string receiptRef)
    {
        var runId = StripPrefix(receiptRef, "run-report:");
        var detail = LoadRun(runId);
        if (detail is null)
            return null;

        return new FrontendReceiptMetadataReadModel
        {
            ReceiptRef = ReceiptRef(detail.RunId),
            ReceiptKind = "RunReport",
            Summary = string.IsNullOrWhiteSpace(detail.Summary) ? $"Run report status: {detail.Status}" : detail.Summary,
            ReferenceOnly = true,
            GrantsAuthority = false,
            ContinuesWorkflow = false,
            Warnings = ["Run report receipt metadata is reference-only.", "Run report receipt does not grant authority."],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
    }

    private RunReportDetail? LoadRun(string runId) =>
        string.IsNullOrWhiteSpace(runId)
            ? null
            : _runReports.GetRunAsync(runId).ConfigureAwait(false).GetAwaiter().GetResult();

    private IReadOnlyList<RunEventDto> LoadEvents(string runId) =>
        string.IsNullOrWhiteSpace(runId)
            ? []
            : _runEvents.GetEventsAsync(runId).ConfigureAwait(false).GetAwaiter().GetResult();

    private static GovernedOperationState MapState(string status)
    {
        if (Contains(status, "complete") || Contains(status, "passed") || Contains(status, "succeeded"))
            return GovernedOperationState.Completed;
        if (Contains(status, "failed") || Contains(status, "error") || Contains(status, "invalid"))
            return GovernedOperationState.Failed;
        if (Contains(status, "running"))
            return GovernedOperationState.Running;
        if (Contains(status, "expired"))
            return GovernedOperationState.Expired;

        return GovernedOperationState.Blocked;
    }

    private static DateTimeOffset ObservedAt(RunReportDetail detail)
    {
        if (!string.IsNullOrWhiteSpace(detail.ReportPath) && File.Exists(detail.ReportPath))
            return new DateTimeOffset(File.GetLastWriteTimeUtc(detail.ReportPath), TimeSpan.Zero);

        return DateTimeOffset.UtcNow;
    }

    private static string Subject(RunReportDetail detail) =>
        $"run:{detail.RunId} project:{detail.Project}";

    private static IEnumerable<string> EvidenceRefs(RunReportDetail detail) =>
        detail.Evidence.Select((_, index) => EvidenceRef(detail.RunId, index));

    private static string EvidenceRef(string runId, int index) =>
        $"run-evidence:{runId}:{index}";

    private static string ReceiptRef(string runId) =>
        $"run-report:{runId}";

    private static bool TryParseIndexedRef(string value, string prefix, out string runId, out int index)
    {
        runId = string.Empty;
        index = -1;

        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = value[prefix.Length..];
        var separator = rest.LastIndexOf(':');
        if (separator <= 0 || separator == rest.Length - 1)
            return false;

        runId = rest[..separator];
        return int.TryParse(rest[(separator + 1)..], out index);
    }

    private static string StripPrefix(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value[prefix.Length..]
            : value;

    private static bool Contains(string value, string marker) =>
        value.Contains(marker, StringComparison.OrdinalIgnoreCase);

    private static bool IsPassing(string value) =>
        Contains(value, "pass") || Contains(value, "success") || Contains(value, "complete");

    private static bool IsFailing(string value) =>
        Contains(value, "fail") || Contains(value, "error");

    private static bool NotBlank(string value) =>
        !string.IsNullOrWhiteSpace(value);
}
