using IronDev.Core.Auth;
using IronDev.Core.Governance;
using IronDev.Core.RunReports;
using System.Text.Json;

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

        var scope = ReadScope();
        return FirstVisible(source => source.GetOperationStatus(key, scope), status =>
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

    public FrontendReadinessReadState GetOperationStatusReadState(string operationId)
    {
        var key = Normalize(operationId);
        if (key is null)
            return FrontendReadinessReadState.NotFound("OperationStatusNotFound", operationId);

        return ReadStateFromSources(
            source => source.GetOperationStatus(key, ReadScope()),
            status =>
            {
                var snapshot = new FrontendReadinessReadSnapshot
                {
                    OperationStatuses = new Dictionary<string, GovernedOperationStatus>(StringComparer.OrdinalIgnoreCase)
                    {
                        [key] = status,
                        [status.OperationId] = status
                    }
                };

                return FrontendReadinessReadStateClassifier.OperationStatus(
                    new FrontendReadinessReadApi(snapshot).GetOperationStatus(key),
                    key);
            },
            FrontendReadinessReadState.NotFound("OperationStatusNotFound", key));
    }

    public FrontendOperationTimelineReadModel? GetOperationTimeline(string operationId)
    {
        var key = Normalize(operationId);
        if (key is null)
            return null;

        var scope = ReadScope();
        return FirstVisible(source => source.GetOperationTimeline(key, scope), timeline =>
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

    public FrontendReadinessReadState GetOperationTimelineReadState(string operationId)
    {
        var key = Normalize(operationId);
        if (key is null)
            return FrontendReadinessReadState.NotFound("OperationTimelineNotFound", operationId);

        return ReadStateFromSources(
            source => source.GetOperationTimeline(key, ReadScope()),
            timeline => FrontendReadinessReadStateClassifier.OperationTimeline(
                new FrontendReadinessReadApi(new FrontendReadinessReadSnapshot
                {
                    Timelines = new Dictionary<string, FrontendOperationTimelineReadModel>(StringComparer.OrdinalIgnoreCase)
                    {
                        [key] = timeline,
                        [timeline.OperationId] = timeline
                    }
                }).GetOperationTimeline(key),
                key),
            FrontendReadinessReadState.NotFound("OperationTimelineNotFound", key));
    }

    public FrontendPatchPackageMetadataReadModel? GetPatchPackageMetadata(string packageId)
    {
        var key = Normalize(packageId);
        if (key is null)
            return null;

        var scope = ReadScope();
        return FirstVisible(source => source.GetPatchPackageMetadata(key, scope), metadata =>
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

    public FrontendReadinessReadState GetPatchPackageMetadataReadState(string packageId)
    {
        var key = Normalize(packageId);
        if (key is null)
            return FrontendReadinessReadState.NotFound("PatchPackageMetadataNotFound", packageId);

        return ReadStateFromSources(
            source => source.GetPatchPackageMetadata(key, ReadScope()),
            metadata => FrontendReadinessReadStateClassifier.PatchPackageMetadata(
                new FrontendReadinessReadApi(new FrontendReadinessReadSnapshot
                {
                    PatchPackages = new Dictionary<string, FrontendPatchPackageMetadataReadModel>(StringComparer.OrdinalIgnoreCase)
                    {
                        [key] = metadata,
                        [metadata.PackageId] = metadata
                    }
                }).GetPatchPackageMetadata(key),
                key),
            FrontendReadinessReadState.NotFound("PatchPackageMetadataNotFound", key));
    }

    public FrontendPatchPackageArtifactsReadModel? GetPatchPackageArtifacts(string packageId)
    {
        var key = Normalize(packageId);
        if (key is null)
            return null;

        var scope = ReadScope();
        return FirstVisible(source => source.GetPatchPackageArtifacts(key, scope), artifacts =>
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

    public FrontendReadinessReadState GetPatchPackageArtifactsReadState(string packageId)
    {
        var key = Normalize(packageId);
        if (key is null)
            return FrontendReadinessReadState.NotFound("PatchPackageArtifactsNotFound", packageId);

        return ReadStateFromSources(
            source => source.GetPatchPackageArtifacts(key, ReadScope()),
            artifacts => FrontendReadinessReadStateClassifier.PatchPackageArtifacts(
                new FrontendReadinessReadApi(new FrontendReadinessReadSnapshot
                {
                    PatchPackageArtifacts = new Dictionary<string, FrontendPatchPackageArtifactsReadModel>(StringComparer.OrdinalIgnoreCase)
                    {
                        [key] = artifacts,
                        [artifacts.PackageId] = artifacts
                    }
                }).GetPatchPackageArtifacts(key),
                key),
            FrontendReadinessReadState.NotFound("PatchPackageArtifactsNotFound", key));
    }

    public FrontendValidationResultMetadataReadModel? GetValidationResultMetadata(string validationResultId)
    {
        var key = Normalize(validationResultId);
        if (key is null)
            return null;

        var scope = ReadScope();
        return FirstVisible(source => source.GetValidationResultMetadata(key, scope), metadata =>
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

    public FrontendReadinessReadState GetValidationResultMetadataReadState(string validationResultId)
    {
        var key = Normalize(validationResultId);
        if (key is null)
            return FrontendReadinessReadState.NotFound("ValidationResultMetadataNotFound", validationResultId);

        return ReadStateFromSources(
            source => source.GetValidationResultMetadata(key, ReadScope()),
            metadata => FrontendReadinessReadStateClassifier.ValidationResultMetadata(
                new FrontendReadinessReadApi(new FrontendReadinessReadSnapshot
                {
                    ValidationResults = new Dictionary<string, FrontendValidationResultMetadataReadModel>(StringComparer.OrdinalIgnoreCase)
                    {
                        [key] = metadata,
                        [metadata.ValidationResultId] = metadata
                    }
                }).GetValidationResultMetadata(key),
                key),
            FrontendReadinessReadState.NotFound("ValidationResultMetadataNotFound", key));
    }

    public FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(string evidenceRef)
    {
        var key = Normalize(evidenceRef);
        if (key is null)
            return null;

        var scope = ReadScope();
        return FirstVisible(source => source.GetEvidenceMetadata(key, scope), metadata =>
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

    public FrontendReadinessReadState GetEvidenceMetadataReadState(string evidenceRef)
    {
        var key = Normalize(evidenceRef);
        if (key is null)
            return FrontendReadinessReadState.NotFound("EvidenceMetadataNotFound", evidenceRef);

        return ReadStateFromSources(
            source => source.GetEvidenceMetadata(key, ReadScope()),
            metadata => FrontendReadinessReadStateClassifier.EvidenceMetadata(
                new FrontendReadinessReadApi(new FrontendReadinessReadSnapshot
                {
                    Evidence = new Dictionary<string, FrontendEvidenceMetadataReadModel>(StringComparer.OrdinalIgnoreCase)
                    {
                        [key] = metadata,
                        [metadata.EvidenceRef] = metadata
                    }
                }).GetEvidenceMetadata(key),
                key),
            FrontendReadinessReadState.NotFound("EvidenceMetadataNotFound", key));
    }

    public FrontendReceiptMetadataReadModel? GetReceiptMetadata(string receiptRef)
    {
        var key = Normalize(receiptRef);
        if (key is null)
            return null;

        var scope = ReadScope();
        return FirstVisible(source => source.GetReceiptMetadata(key, scope), metadata =>
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

    public FrontendReadinessReadState GetReceiptMetadataReadState(string receiptRef)
    {
        var key = Normalize(receiptRef);
        if (key is null)
            return FrontendReadinessReadState.NotFound("ReceiptMetadataNotFound", receiptRef);

        return ReadStateFromSources(
            source => source.GetReceiptMetadata(key, ReadScope()),
            metadata => FrontendReadinessReadStateClassifier.ReceiptMetadata(
                new FrontendReadinessReadApi(new FrontendReadinessReadSnapshot
                {
                    Receipts = new Dictionary<string, FrontendReceiptMetadataReadModel>(StringComparer.OrdinalIgnoreCase)
                    {
                        [key] = metadata,
                        [metadata.ReceiptRef] = metadata
                    }
                }).GetReceiptMetadata(key),
                key),
            FrontendReadinessReadState.NotFound("ReceiptMetadataNotFound", key));
    }

    private TResult? FirstVisible<TSourceValue, TResult>(
        Func<IFrontendReadinessBackendTruthSource, TSourceValue?> read,
        Func<TSourceValue, TResult?> sanitize)
        where TSourceValue : class
        where TResult : class
    {
        var scope = ReadScope();
        foreach (var source in _sources)
        {
            if (!source.IsVisibleTo(scope))
                return null;

            var value = read(source);
            if (value is null)
                continue;

            var sanitized = sanitize(value);
            if (sanitized is not null)
                return sanitized;
        }

        return null;
    }

    private FrontendReadinessReadState ReadStateFromSources<TSourceValue>(
        Func<IFrontendReadinessBackendTruthSource, TSourceValue?> read,
        Func<TSourceValue, FrontendReadinessReadState> classify,
        FrontendReadinessReadState notFoundState)
        where TSourceValue : class
    {
        var scope = ReadScope();
        foreach (var source in _sources)
        {
            if (!source.IsVisibleTo(scope))
                return FrontendReadinessReadState.NotVisible();

            TSourceValue? value;
            try
            {
                value = read(source);
            }
            catch (Exception)
            {
                return FrontendReadinessReadState.Unavailable();
            }

            if (value is null)
                continue;

            return classify(value);
        }

        return notFoundState;
    }

    private FrontendReadinessReadScope ReadScope() =>
        _tenantContext is null
            ? FrontendReadinessReadScope.Unscoped
            : new FrontendReadinessReadScope(_tenantContext.TenantId);

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

    public override GovernedOperationStatus? GetOperationStatus(string operationId, FrontendReadinessReadScope scope)
    {
        var detail = LoadRun(operationId, scope);
        if (detail is null)
            return null;

        var events = LoadEvents(detail.RunId);
        var observedAt = TryObservedAt(detail, events);
        if (observedAt is null)
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
            ObservedAtUtc = observedAt.Value,
            ExpiresAtUtc = null
        };
    }

    public override FrontendOperationTimelineReadModel? GetOperationTimeline(string operationId, FrontendReadinessReadScope scope)
    {
        var detail = LoadRun(operationId, scope);
        if (detail is null)
            return null;

        var events = LoadEvents(detail.RunId);
        var observedAt = TryObservedAt(detail, events);
        if (observedAt is null)
            return null;

        var entries = new List<FrontendTimelineEntry>();
        entries.AddRange(detail.Stages.Select((stage, index) => new FrontendTimelineEntry
        {
            EntryId = $"run-stage:{detail.RunId}:{index}",
            EventKind = string.IsNullOrWhiteSpace(stage.StageName) ? "RunStage" : stage.StageName,
            Summary = string.IsNullOrWhiteSpace(stage.Summary) ? $"{stage.AgentName} {stage.Status}".Trim() : stage.Summary,
            EvidenceRefs = EvidenceRefs(detail).ToArray(),
            ReceiptRefs = [],
            ObservedAtUtc = observedAt.Value
        }));
        entries.AddRange(detail.Attempts.Select((attempt, index) => new FrontendTimelineEntry
        {
            EntryId = $"run-attempt:{detail.RunId}:{index}",
            EventKind = string.IsNullOrWhiteSpace(attempt.Type) ? "RunAttempt" : attempt.Type,
            Summary = string.IsNullOrWhiteSpace(attempt.Summary) ? attempt.Status : attempt.Summary,
            EvidenceRefs = EvidenceRefs(detail).ToArray(),
            ReceiptRefs = [],
            ObservedAtUtc = observedAt.Value
        }));

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
                ObservedAtUtc = observedAt.Value
            });
        }

        return new FrontendOperationTimelineReadModel
        {
            OperationId = detail.RunId,
            Entries = entries,
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
    }

    public override FrontendValidationResultMetadataReadModel? GetValidationResultMetadata(string validationResultId, FrontendReadinessReadScope scope)
    {
        var runId = StripPrefix(validationResultId, "run-validation:");
        var detail = LoadRun(runId, scope);
        if (detail is null)
            return null;

        var events = LoadEvents(detail.RunId);
        if (TryObservedAt(detail, events) is null)
            return null;

        var freshnessProven = HasFreshnessEvidence(detail);
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
            WhatWasSkipped = freshnessProven ? [] : ["FreshnessUnknown"],
            IsStale = !freshnessProven,
            EvidenceRefs = EvidenceRefs(detail).ToArray(),
            ReceiptRefs = [],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
    }

    public override FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(string evidenceRef, FrontendReadinessReadScope scope)
    {
        if (!TryParseIndexedRef(evidenceRef, "run-evidence:", out var runId, out var index))
            return null;

        var detail = LoadRun(runId, scope);
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

    public override FrontendReceiptMetadataReadModel? GetReceiptMetadata(string receiptRef, FrontendReadinessReadScope scope)
    {
        var runId = StripPrefix(receiptRef, "run-report:");
        var detail = LoadRun(runId, scope);
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

    private RunReportDetail? LoadRun(string runId, FrontendReadinessReadScope scope)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return null;

        var detail = _runReports.GetRunAsync(runId).ConfigureAwait(false).GetAwaiter().GetResult();
        if (detail is null)
            return null;

        return CanReadRun(detail, scope) ? detail : null;
    }

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

    private static DateTimeOffset? TryObservedAt(RunReportDetail detail, IReadOnlyList<RunEventDto> events)
    {
        if (!string.IsNullOrWhiteSpace(detail.ReportPath) && File.Exists(detail.ReportPath))
            return new DateTimeOffset(File.GetLastWriteTimeUtc(detail.ReportPath), TimeSpan.Zero);

        if (events.Count > 0)
            return events.Max(runEvent => runEvent.TimestampUtc);

        return null;
    }

    private static bool CanReadRun(RunReportDetail detail, FrontendReadinessReadScope scope)
    {
        var tenantId = ReadTenantId(detail);
        if (string.IsNullOrWhiteSpace(tenantId) || !scope.HasTenant)
            return false;

        return string.Equals(tenantId, scope.TenantId.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadTenantId(RunReportDetail detail)
    {
        if (string.IsNullOrWhiteSpace(detail.ReportPath) || !File.Exists(detail.ReportPath))
            return null;

        try
        {
            using var stream = File.OpenRead(detail.ReportPath);
            using var document = JsonDocument.Parse(stream);
            return ReadString(document.RootElement, "TenantId", "tenantId", "Tenant", "tenant");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasFreshnessEvidence(RunReportDetail detail) =>
        detail.Evidence.Any(evidence =>
            Contains(evidence.Type, "freshness") ||
            Contains(evidence.Summary, "freshness") ||
            Contains(evidence.Path, "freshness"));

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

    private static string? ReadString(JsonElement root, params string[] names)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var direct))
                return direct.ValueKind switch
                {
                    JsonValueKind.String => direct.GetString(),
                    JsonValueKind.Number => direct.GetRawText(),
                    _ => null
                };

            foreach (var property in root.EnumerateObject())
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                return property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    _ => null
                };
            }
        }

        return null;
    }
}
