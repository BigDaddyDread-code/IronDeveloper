namespace IronDev.Core.Governance;

public interface IFrontendReadinessReadApi
{
    FrontendOperationStatusReadModel? GetOperationStatus(string operationId);
    FrontendReadinessReadState GetOperationStatusReadState(string operationId);
    FrontendOperationTimelineReadModel? GetOperationTimeline(string operationId);
    FrontendReadinessReadState GetOperationTimelineReadState(string operationId);
    FrontendPatchPackageMetadataReadModel? GetPatchPackageMetadata(string packageId);
    FrontendReadinessReadState GetPatchPackageMetadataReadState(string packageId);
    FrontendPatchPackageArtifactsReadModel? GetPatchPackageArtifacts(string packageId);
    FrontendReadinessReadState GetPatchPackageArtifactsReadState(string packageId);
    FrontendValidationResultMetadataReadModel? GetValidationResultMetadata(string validationResultId);
    FrontendReadinessReadState GetValidationResultMetadataReadState(string validationResultId);
    FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(string evidenceRef);
    FrontendReadinessReadState GetEvidenceMetadataReadState(string evidenceRef);
    FrontendReceiptMetadataReadModel? GetReceiptMetadata(string receiptRef);
    FrontendReadinessReadState GetReceiptMetadataReadState(string receiptRef);
}

public interface IFrontendReadinessBackendTruthSource
{
    string SourceName { get; }
    int? TenantId { get; }

    bool IsVisibleTo(FrontendReadinessReadScope scope);

    FrontendReadinessBackendReadResult<GovernedOperationStatus> ReadOperationStatus(string operationId, FrontendReadinessReadScope scope);
    FrontendReadinessBackendReadResult<FrontendOperationTimelineReadModel> ReadOperationTimeline(string operationId, FrontendReadinessReadScope scope);
    FrontendReadinessBackendReadResult<FrontendPatchPackageMetadataReadModel> ReadPatchPackageMetadata(string packageId, FrontendReadinessReadScope scope);
    FrontendReadinessBackendReadResult<FrontendPatchPackageArtifactsReadModel> ReadPatchPackageArtifacts(string packageId, FrontendReadinessReadScope scope);
    FrontendReadinessBackendReadResult<FrontendValidationResultMetadataReadModel> ReadValidationResultMetadata(string validationResultId, FrontendReadinessReadScope scope);
    FrontendReadinessBackendReadResult<FrontendEvidenceMetadataReadModel> ReadEvidenceMetadata(string evidenceRef, FrontendReadinessReadScope scope);
    FrontendReadinessBackendReadResult<FrontendReceiptMetadataReadModel> ReadReceiptMetadata(string receiptRef, FrontendReadinessReadScope scope);

    GovernedOperationStatus? GetOperationStatus(string operationId, FrontendReadinessReadScope scope);
    FrontendOperationTimelineReadModel? GetOperationTimeline(string operationId, FrontendReadinessReadScope scope);
    FrontendPatchPackageMetadataReadModel? GetPatchPackageMetadata(string packageId, FrontendReadinessReadScope scope);
    FrontendPatchPackageArtifactsReadModel? GetPatchPackageArtifacts(string packageId, FrontendReadinessReadScope scope);
    FrontendValidationResultMetadataReadModel? GetValidationResultMetadata(string validationResultId, FrontendReadinessReadScope scope);
    FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(string evidenceRef, FrontendReadinessReadScope scope);
    FrontendReceiptMetadataReadModel? GetReceiptMetadata(string receiptRef, FrontendReadinessReadScope scope);

    GovernedOperationStatus? GetOperationStatus(string operationId);
    FrontendOperationTimelineReadModel? GetOperationTimeline(string operationId);
    FrontendPatchPackageMetadataReadModel? GetPatchPackageMetadata(string packageId);
    FrontendPatchPackageArtifactsReadModel? GetPatchPackageArtifacts(string packageId);
    FrontendValidationResultMetadataReadModel? GetValidationResultMetadata(string validationResultId);
    FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(string evidenceRef);
    FrontendReceiptMetadataReadModel? GetReceiptMetadata(string receiptRef);
}

public sealed record FrontendReadinessBackendReadResult<TData>
    where TData : class
{
    public TData? Data { get; init; }
    public required FrontendReadinessReadState ReadState { get; init; }

    public static FrontendReadinessBackendReadResult<TData> WithData(
        TData data,
        FrontendReadinessReadState state) =>
        new()
        {
            Data = data,
            ReadState = state
        };

    public static FrontendReadinessBackendReadResult<TData> WithoutData(FrontendReadinessReadState state) =>
        new()
        {
            Data = null,
            ReadState = state
        };
}

public sealed record FrontendReadinessReadScope(int TenantId)
{
    public bool HasTenant => TenantId > 0;

    public static FrontendReadinessReadScope Unscoped { get; } = new(0);
}

public abstract class FrontendReadinessBackendTruthSource : IFrontendReadinessBackendTruthSource
{
    public abstract string SourceName { get; }
    public virtual int? TenantId => null;

    public virtual bool IsVisibleTo(FrontendReadinessReadScope scope) =>
        TenantId is null || (scope.HasTenant && scope.TenantId == TenantId.Value);

    public virtual FrontendReadinessBackendReadResult<GovernedOperationStatus> ReadOperationStatus(
        string operationId,
        FrontendReadinessReadScope scope) =>
        ReadDefault(
            operationId,
            scope,
            GetOperationStatus,
            "OperationStatusNotFound");

    public virtual FrontendReadinessBackendReadResult<FrontendOperationTimelineReadModel> ReadOperationTimeline(
        string operationId,
        FrontendReadinessReadScope scope) =>
        ReadDefault(
            operationId,
            scope,
            GetOperationTimeline,
            "OperationTimelineNotFound");

    public virtual FrontendReadinessBackendReadResult<FrontendPatchPackageMetadataReadModel> ReadPatchPackageMetadata(
        string packageId,
        FrontendReadinessReadScope scope) =>
        ReadDefault(
            packageId,
            scope,
            GetPatchPackageMetadata,
            "PatchPackageMetadataNotFound");

    public virtual FrontendReadinessBackendReadResult<FrontendPatchPackageArtifactsReadModel> ReadPatchPackageArtifacts(
        string packageId,
        FrontendReadinessReadScope scope) =>
        ReadDefault(
            packageId,
            scope,
            GetPatchPackageArtifacts,
            "PatchPackageArtifactsNotFound");

    public virtual FrontendReadinessBackendReadResult<FrontendValidationResultMetadataReadModel> ReadValidationResultMetadata(
        string validationResultId,
        FrontendReadinessReadScope scope) =>
        ReadDefault(
            validationResultId,
            scope,
            GetValidationResultMetadata,
            "ValidationResultMetadataNotFound");

    public virtual FrontendReadinessBackendReadResult<FrontendEvidenceMetadataReadModel> ReadEvidenceMetadata(
        string evidenceRef,
        FrontendReadinessReadScope scope) =>
        ReadDefault(
            evidenceRef,
            scope,
            GetEvidenceMetadata,
            "EvidenceMetadataNotFound");

    public virtual FrontendReadinessBackendReadResult<FrontendReceiptMetadataReadModel> ReadReceiptMetadata(
        string receiptRef,
        FrontendReadinessReadScope scope) =>
        ReadDefault(
            receiptRef,
            scope,
            GetReceiptMetadata,
            "ReceiptMetadataNotFound");

    public virtual GovernedOperationStatus? GetOperationStatus(string operationId, FrontendReadinessReadScope scope) =>
        IsVisibleTo(scope) ? GetOperationStatus(operationId) : null;

    public virtual FrontendOperationTimelineReadModel? GetOperationTimeline(string operationId, FrontendReadinessReadScope scope) =>
        IsVisibleTo(scope) ? GetOperationTimeline(operationId) : null;

    public virtual FrontendPatchPackageMetadataReadModel? GetPatchPackageMetadata(string packageId, FrontendReadinessReadScope scope) =>
        IsVisibleTo(scope) ? GetPatchPackageMetadata(packageId) : null;

    public virtual FrontendPatchPackageArtifactsReadModel? GetPatchPackageArtifacts(string packageId, FrontendReadinessReadScope scope) =>
        IsVisibleTo(scope) ? GetPatchPackageArtifacts(packageId) : null;

    public virtual FrontendValidationResultMetadataReadModel? GetValidationResultMetadata(string validationResultId, FrontendReadinessReadScope scope) =>
        IsVisibleTo(scope) ? GetValidationResultMetadata(validationResultId) : null;

    public virtual FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(string evidenceRef, FrontendReadinessReadScope scope) =>
        IsVisibleTo(scope) ? GetEvidenceMetadata(evidenceRef) : null;

    public virtual FrontendReceiptMetadataReadModel? GetReceiptMetadata(string receiptRef, FrontendReadinessReadScope scope) =>
        IsVisibleTo(scope) ? GetReceiptMetadata(receiptRef) : null;

    public virtual GovernedOperationStatus? GetOperationStatus(string operationId) => null;
    public virtual FrontendOperationTimelineReadModel? GetOperationTimeline(string operationId) => null;
    public virtual FrontendPatchPackageMetadataReadModel? GetPatchPackageMetadata(string packageId) => null;
    public virtual FrontendPatchPackageArtifactsReadModel? GetPatchPackageArtifacts(string packageId) => null;
    public virtual FrontendValidationResultMetadataReadModel? GetValidationResultMetadata(string validationResultId) => null;
    public virtual FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(string evidenceRef) => null;
    public virtual FrontendReceiptMetadataReadModel? GetReceiptMetadata(string receiptRef) => null;

    protected static FrontendReadinessBackendReadResult<TData> FromRepositoryResult<TData>(
        bool found,
        TData? data,
        IReadOnlyCollection<string> issues,
        Func<TData, FrontendReadinessReadState> classify,
        string notFoundReason)
        where TData : class
    {
        if (found && data is not null)
            return FrontendReadinessBackendReadResult<TData>.WithData(data, classify(data));

        if (IsVisibilityIssue(issues))
            return FrontendReadinessBackendReadResult<TData>.WithoutData(FrontendReadinessReadState.NotVisible("RecordNotVisible"));

        if (IsInvalidIssue(issues))
            return FrontendReadinessBackendReadResult<TData>.WithoutData(FrontendReadinessReadState.Invalid(issues.First()));

        return FrontendReadinessBackendReadResult<TData>.WithoutData(FrontendReadinessReadState.NotFound(notFoundReason));
    }

    private FrontendReadinessBackendReadResult<TData> ReadDefault<TData>(
        string id,
        FrontendReadinessReadScope scope,
        Func<string, FrontendReadinessReadScope, TData?> read,
        string notFoundReason)
        where TData : class
    {
        if (!IsVisibleTo(scope))
            return FrontendReadinessBackendReadResult<TData>.WithoutData(FrontendReadinessReadState.NotVisible());

        var data = read(id, scope);
        return data is null
            ? FrontendReadinessBackendReadResult<TData>.WithoutData(FrontendReadinessReadState.NotFound(notFoundReason, id))
            : FrontendReadinessBackendReadResult<TData>.WithData(data, FrontendReadinessReadState.Available("BackendTruthAvailable"));
    }

    private static bool IsVisibilityIssue(IEnumerable<string> issues) =>
        issues.Any(issue =>
            issue.Contains("Tenant", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("NotVisible", StringComparison.OrdinalIgnoreCase));

    private static bool IsInvalidIssue(IEnumerable<string> issues) =>
        issues.Any(issue =>
            issue.Contains("Invalid", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("Required", StringComparison.OrdinalIgnoreCase));
}

public sealed record FrontendReadBoundary
{
    public bool ReadOnly { get; init; } = true;
    public bool StatusOnly { get; init; } = true;

    public bool CanCreateApproval { get; init; }
    public bool CanAcceptApproval { get; init; }
    public bool CanSatisfyPolicy { get; init; }
    public bool CanExecute { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanRollback { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanCreatePullRequest { get; init; }
    public bool CanMarkReadyForReview { get; init; }
    public bool CanMerge { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanContinueWorkflow { get; init; }

    public static FrontendReadBoundary ReadOnlyStatus { get; } = new();
}

public enum FrontendReadinessReadStateKind
{
    Available,
    NotFound,
    Empty,
    Redacted,
    Unavailable,
    Invalid,
    Stale,
    NotVisible,
    Unknown
}

public sealed record FrontendReadinessReadState
{
    public required FrontendReadinessReadStateKind Kind { get; init; }
    public required bool HasData { get; init; }
    public required bool IsFinal { get; init; }
    public required bool IsFallback { get; init; }
    public required bool IsRedacted { get; init; }
    public required bool IsStale { get; init; }
    public required bool IsAuthorityGrant { get; init; }
    public required bool AllowsMutation { get; init; }
    public required IReadOnlyCollection<string> Reasons { get; init; }
    public required IReadOnlyCollection<string> MissingRefs { get; init; }
    public required IReadOnlyCollection<string> Warnings { get; init; }
    public required IReadOnlyCollection<string> NextSafeActions { get; init; }
    public required FrontendReadBoundary Boundary { get; init; }

    public static FrontendReadinessReadState Available(params string[] reasons) =>
        Create(
            FrontendReadinessReadStateKind.Available,
            hasData: true,
            isFinal: true,
            reasons: reasons.Length == 0 ? ["DataAvailable"] : reasons,
            nextSafeActions: ["inspect returned frontend-readiness data"]);

    public static FrontendReadinessReadState NotFound(string reason, params string[] missingRefs) =>
        Create(
            FrontendReadinessReadStateKind.NotFound,
            hasData: false,
            isFinal: true,
            reasons: [reason],
            missingRefs: missingRefs,
            nextSafeActions: ["inspect backend read source"]);

    public static FrontendReadinessReadState Empty(string reason) =>
        Create(
            FrontendReadinessReadStateKind.Empty,
            hasData: true,
            isFinal: true,
            reasons: [reason],
            nextSafeActions: ["inspect backend read source"]);

    public static FrontendReadinessReadState Redacted(string reason) =>
        Create(
            FrontendReadinessReadStateKind.Redacted,
            hasData: true,
            isFinal: true,
            isRedacted: true,
            reasons: [reason],
            warnings: ["Unsafe or private material was redacted."],
            nextSafeActions: ["inspect safe metadata producer"]);

    public static FrontendReadinessReadState Unavailable(string reason = "BackendTruthUnavailable") =>
        Create(
            FrontendReadinessReadStateKind.Unavailable,
            hasData: false,
            isFinal: false,
            reasons: [reason],
            warnings: ["Canonical backend truth was unavailable and no fallback was used."],
            nextSafeActions: ["restore backend truth source"]);

    public static FrontendReadinessReadState Invalid(string reason) =>
        Create(
            FrontendReadinessReadStateKind.Invalid,
            hasData: true,
            isFinal: true,
            reasons: [reason],
            warnings: ["Invalid stored metadata cannot be treated as frontend readiness data."],
            nextSafeActions: ["inspect backend record producer"]);

    public static FrontendReadinessReadState Stale(string reason) =>
        Create(
            FrontendReadinessReadStateKind.Stale,
            hasData: true,
            isFinal: true,
            isStale: true,
            reasons: [reason],
            warnings: ["Stale validation evidence is not approval or mutation authority."],
            nextSafeActions: ["refresh validation evidence"]);

    public static FrontendReadinessReadState NotVisible(string reason = "NotFoundOrNotVisible") =>
        Create(
            FrontendReadinessReadStateKind.NotVisible,
            hasData: false,
            isFinal: true,
            reasons: [reason],
            warnings: ["Requested data was not visible in the current read scope."],
            nextSafeActions: ["inspect tenant or visibility scope"]);

    public static FrontendReadinessReadState Unknown(string reason = "UnknownReadState") =>
        Create(
            FrontendReadinessReadStateKind.Unknown,
            hasData: false,
            isFinal: false,
            reasons: [reason],
            warnings: ["Unknown read state is not success."],
            nextSafeActions: ["inspect backend read source"]);

    private static FrontendReadinessReadState Create(
        FrontendReadinessReadStateKind kind,
        bool hasData,
        bool isFinal,
        IReadOnlyCollection<string> reasons,
        IReadOnlyCollection<string>? missingRefs = null,
        IReadOnlyCollection<string>? warnings = null,
        IReadOnlyCollection<string>? nextSafeActions = null,
        bool isRedacted = false,
        bool isStale = false) =>
        new()
        {
            Kind = kind,
            HasData = hasData,
            IsFinal = isFinal,
            IsFallback = false,
            IsRedacted = isRedacted,
            IsStale = isStale,
            IsAuthorityGrant = false,
            AllowsMutation = false,
            Reasons = Clean(reasons),
            MissingRefs = Clean(missingRefs ?? []),
            Warnings = Clean((warnings ?? []).Concat(NoAuthorityWarnings)),
            NextSafeActions = Clean(nextSafeActions ?? []),
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static readonly string[] NoAuthorityWarnings =
    [
        "Read state is not approval.",
        "Read state is not policy satisfaction.",
        "Read state is not source apply authority.",
        "Read state does not allow mutation or workflow continuation."
    ];

    private static IReadOnlyList<string> Clean(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public static class FrontendReadinessReadStateClassifier
{
    public static FrontendReadinessReadState OperationStatus(FrontendOperationStatusReadModel? model, string requestedRef) =>
        model is null
            ? FrontendReadinessReadState.NotFound("OperationStatusNotFound", requestedRef)
            : model.BlockedReasons.Contains("StoredOperationStatusInvalid", StringComparer.OrdinalIgnoreCase)
                ? FrontendReadinessReadState.Invalid("StoredOperationStatusInvalid")
                : FrontendReadinessReadState.Available("OperationStatusAvailable");

    public static FrontendReadinessReadState EvidenceMetadata(FrontendEvidenceMetadataReadModel? model, string requestedRef) =>
        model is null
            ? FrontendReadinessReadState.NotFound("EvidenceMetadataNotFound", requestedRef)
            : IsRedactedEvidence(model)
                ? FrontendReadinessReadState.Redacted("EvidenceMetadataUnsafe")
                : FrontendReadinessReadState.Available("EvidenceMetadataAvailable");

    public static FrontendReadinessReadState ReceiptMetadata(FrontendReceiptMetadataReadModel? model, string requestedRef) =>
        model is null
            ? FrontendReadinessReadState.NotFound("ReceiptMetadataNotFound", requestedRef)
            : IsRedactedReceipt(model)
                ? FrontendReadinessReadState.Redacted("ReceiptMetadataUnsafe")
                : FrontendReadinessReadState.Available("ReceiptMetadataAvailable");

    public static FrontendReadinessReadState OperationTimeline(FrontendOperationTimelineReadModel? model, string requestedRef) =>
        model is null
            ? FrontendReadinessReadState.NotFound("OperationTimelineNotFound", requestedRef)
            : model.Entries.Count == 0
                ? FrontendReadinessReadState.Empty("NoVisibleTimelineEntries")
                : model.Entries.Any(IsRedactedTimelineEntry)
                    ? FrontendReadinessReadState.Redacted("TimelineEventRedacted")
                    : FrontendReadinessReadState.Available("OperationTimelineAvailable");

    public static FrontendReadinessReadState PatchPackageMetadata(FrontendPatchPackageMetadataReadModel? model, string requestedRef) =>
        model is null
            ? FrontendReadinessReadState.NotFound("PatchPackageMetadataNotFound", requestedRef)
            : IsRedactedPatchPackage(model)
                ? FrontendReadinessReadState.Redacted("PatchPackageMetadataUnsafe")
                : FrontendReadinessReadState.Available("PatchPackageMetadataAvailable");

    public static FrontendReadinessReadState PatchPackageArtifacts(FrontendPatchPackageArtifactsReadModel? model, string requestedRef) =>
        model is null
            ? FrontendReadinessReadState.NotFound("PatchPackageArtifactsNotFound", requestedRef)
            : model.ValidationIsStale
                ? FrontendReadinessReadState.Stale("PatchPackageValidationStale")
                : FrontendReadinessReadState.Available("PatchPackageArtifactsAvailable");

    public static FrontendReadinessReadState ValidationResultMetadata(FrontendValidationResultMetadataReadModel? model, string requestedRef) =>
        model is null
            ? FrontendReadinessReadState.NotFound("ValidationResultMetadataNotFound", requestedRef)
            : IsRedactedValidation(model)
                ? FrontendReadinessReadState.Redacted("ValidationResultMetadataUnsafe")
                : model.IsStale
                    ? FrontendReadinessReadState.Stale("ValidationResultStale")
                    : FrontendReadinessReadState.Available("ValidationResultMetadataAvailable");

    private static bool IsRedactedEvidence(FrontendEvidenceMetadataReadModel model) =>
        string.Equals(model.EvidenceKind, "RedactedEvidenceMetadata", StringComparison.OrdinalIgnoreCase) ||
        model.Summary.Contains("[redacted: evidence metadata unavailable]", StringComparison.OrdinalIgnoreCase);

    private static bool IsRedactedReceipt(FrontendReceiptMetadataReadModel model) =>
        string.Equals(model.ReceiptKind, "RedactedReceiptMetadata", StringComparison.OrdinalIgnoreCase) ||
        model.Summary.Contains("[redacted: receipt metadata unavailable]", StringComparison.OrdinalIgnoreCase);

    private static bool IsRedactedTimelineEntry(FrontendTimelineEntry entry) =>
        string.Equals(entry.EventKind, "RedactedTimelineEvent", StringComparison.OrdinalIgnoreCase) ||
        entry.Summary.Contains("[redacted: timeline event unavailable]", StringComparison.OrdinalIgnoreCase);

    private static bool IsRedactedPatchPackage(FrontendPatchPackageMetadataReadModel model) =>
        string.Equals(model.Repository, "[redacted]", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(model.PatchHash, "[redacted]", StringComparison.OrdinalIgnoreCase);

    private static bool IsRedactedValidation(FrontendValidationResultMetadataReadModel model) =>
        string.Equals(model.Outcome, "UnsafeValidationMetadata", StringComparison.OrdinalIgnoreCase) ||
        model.WhatWasSkipped.Contains("ValidationMetadataUnsafe", StringComparer.OrdinalIgnoreCase);
}

public sealed record FrontendOperationStatusReadModel
{
    public required string OperationId { get; init; }
    public required string OperationKind { get; init; }
    public required string Subject { get; init; }
    public required string State { get; init; }

    public required IReadOnlyCollection<string> BlockedReasons { get; init; }
    public required IReadOnlyCollection<string> MissingEvidence { get; init; }
    public required IReadOnlyCollection<string> NextSafeActions { get; init; }
    public required IReadOnlyCollection<string> ForbiddenActions { get; init; }

    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }

    public required IReadOnlyCollection<string> AuthorityWarnings { get; init; }

    public required FrontendReadBoundary Boundary { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

public sealed record FrontendOperationTimelineReadModel
{
    public required string OperationId { get; init; }
    public required IReadOnlyCollection<FrontendTimelineEntry> Entries { get; init; }
    public required FrontendReadBoundary Boundary { get; init; }
}

public sealed record FrontendTimelineEntry
{
    public required string EntryId { get; init; }
    public required string EventKind { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record FrontendPatchPackageMetadataReadModel
{
    public required string PackageId { get; init; }
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required IReadOnlyCollection<string> ProposedFilePaths { get; init; }
    public required IReadOnlyCollection<string> ArtifactRefs { get; init; }
    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }

    public required string ReviewSummaryRef { get; init; }
    public required string KnownRisksRef { get; init; }

    public required FrontendReadBoundary Boundary { get; init; }
}

public sealed record FrontendPatchPackageArtifactsReadModel
{
    public required string PackageId { get; init; }
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string PatchDiffText { get; init; }
    public required string ReviewSummaryText { get; init; }
    public required string KnownRisksText { get; init; }
    public required string ValidationSummaryText { get; init; }

    public required string ValidationOutcome { get; init; }
    public required IReadOnlyCollection<string> WhatRan { get; init; }
    public required IReadOnlyCollection<string> WhatPassed { get; init; }
    public required IReadOnlyCollection<string> WhatFailed { get; init; }
    public required IReadOnlyCollection<string> WhatWasSkipped { get; init; }
    public required bool ValidationIsStale { get; init; }

    public required IReadOnlyCollection<string> ProposedFilePaths { get; init; }
    public required IReadOnlyCollection<string> ArtifactRefs { get; init; }
    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }

    public required IReadOnlyCollection<string> AuthorityWarnings { get; init; }
    public required FrontendReadBoundary Boundary { get; init; }
}

public sealed record FrontendValidationResultMetadataReadModel
{
    public required string ValidationResultId { get; init; }
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string Outcome { get; init; }
    public required IReadOnlyCollection<string> WhatRan { get; init; }
    public required IReadOnlyCollection<string> WhatPassed { get; init; }
    public required IReadOnlyCollection<string> WhatFailed { get; init; }
    public required IReadOnlyCollection<string> WhatWasSkipped { get; init; }
    public required bool IsStale { get; init; }

    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }

    public required FrontendReadBoundary Boundary { get; init; }
}

public sealed record FrontendEvidenceMetadataReadModel
{
    public required string EvidenceRef { get; init; }
    public required string EvidenceKind { get; init; }
    public required string Summary { get; init; }
    public bool ReferenceOnly { get; init; } = true;
    public bool ContainsRawPayload { get; init; }
    public required IReadOnlyCollection<string> Warnings { get; init; }
    public required FrontendReadBoundary Boundary { get; init; }
}

public sealed record FrontendReceiptMetadataReadModel
{
    public required string ReceiptRef { get; init; }
    public required string ReceiptKind { get; init; }
    public required string Summary { get; init; }
    public bool ReferenceOnly { get; init; } = true;
    public bool GrantsAuthority { get; init; }
    public bool ContinuesWorkflow { get; init; }
    public required IReadOnlyCollection<string> Warnings { get; init; }
    public required FrontendReadBoundary Boundary { get; init; }
}

public sealed record FrontendReadinessReadSnapshot
{
    public IReadOnlyDictionary<string, GovernedOperationStatus> OperationStatuses { get; init; } =
        new Dictionary<string, GovernedOperationStatus>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, FrontendOperationTimelineReadModel> Timelines { get; init; } =
        new Dictionary<string, FrontendOperationTimelineReadModel>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, FrontendPatchPackageMetadataReadModel> PatchPackages { get; init; } =
        new Dictionary<string, FrontendPatchPackageMetadataReadModel>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, FrontendPatchPackageArtifactsReadModel> PatchPackageArtifacts { get; init; } =
        new Dictionary<string, FrontendPatchPackageArtifactsReadModel>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, FrontendValidationResultMetadataReadModel> ValidationResults { get; init; } =
        new Dictionary<string, FrontendValidationResultMetadataReadModel>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, FrontendEvidenceMetadataReadModel> Evidence { get; init; } =
        new Dictionary<string, FrontendEvidenceMetadataReadModel>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, FrontendReceiptMetadataReadModel> Receipts { get; init; } =
        new Dictionary<string, FrontendReceiptMetadataReadModel>(StringComparer.OrdinalIgnoreCase);

    public static FrontendReadinessReadSnapshot Empty { get; } = new();
}

public sealed class FrontendReadinessReadApi : IFrontendReadinessReadApi
{
    private static readonly string[] PrivateMaterialMarkers =
    [
        "hiddenReasoning",
        "hidden reasoning",
        "chainOfThought",
        "chain of thought",
        "chain-of-thought",
        "private reasoning",
        "scratchpad",
        "rawPrompt",
        "raw prompt",
        "rawCompletion",
        "raw completion",
        "rawToolOutput",
        "raw tool output",
        "password",
        "api_key",
        "secret",
        "private key",
        "bearer "
    ];

    private static readonly string[] RequiredForbiddenActions =
    [
        "do not treat patch package as source apply authority",
        "do not treat validation as approval",
        "do not treat freshness as authority",
        "do not treat draft PR as ready-for-review authority",
        "do not treat PR URL as release candidate ref",
        "do not continue workflow from status, receipt, memory, or UI text"
    ];

    private static readonly string[] BoundaryWarnings =
    [
        "Read API output is backend truth for display only.",
        "Status output is not authority.",
        "Evidence refs are metadata only and are not approval.",
        "Receipt refs are metadata only and are not workflow continuation.",
        "Next safe actions are guidance only."
    ];

    private readonly FrontendReadinessReadSnapshot _snapshot;

    public FrontendReadinessReadApi(FrontendReadinessReadSnapshot snapshot) =>
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public static FrontendReadinessReadApi Empty { get; } = new(FrontendReadinessReadSnapshot.Empty);

    public FrontendOperationStatusReadModel? GetOperationStatus(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId) ||
            !_snapshot.OperationStatuses.TryGetValue(operationId.Trim(), out var status))
        {
            return null;
        }

        var message = GovernedStatusUserMessageFormatter.Format(status);
        return new FrontendOperationStatusReadModel
        {
            OperationId = CleanText(status.OperationId),
            OperationKind = CleanText(status.OperationKind),
            Subject = CleanText(status.Subject),
            State = status.State.ToString(),
            BlockedReasons = Clean(status.BlockedReasons),
            MissingEvidence = Clean(status.MissingEvidence),
            NextSafeActions = LabelGuidance(Clean(status.NextSafeActions)),
            ForbiddenActions = EnsureForbiddenActions(Clean(status.ForbiddenActions.Concat(message.PlainForbiddenActions))),
            EvidenceRefs = CleanRefs(status.EvidenceRefs),
            ReceiptRefs = CleanRefs(status.ReceiptRefs),
            AuthorityWarnings = Clean(BoundaryWarnings.Concat(message.AuthorityWarnings)),
            Boundary = FrontendReadBoundary.ReadOnlyStatus,
            ObservedAtUtc = status.ObservedAtUtc,
            ExpiresAtUtc = status.ExpiresAtUtc
        };
    }

    public FrontendReadinessReadState GetOperationStatusReadState(string operationId) =>
        FrontendReadinessReadStateClassifier.OperationStatus(GetOperationStatus(operationId), operationId);

    public FrontendOperationTimelineReadModel? GetOperationTimeline(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId) ||
            !_snapshot.Timelines.TryGetValue(operationId.Trim(), out var timeline))
        {
            return null;
        }

        return timeline with
        {
            OperationId = CleanText(timeline.OperationId),
            Entries = timeline.Entries.Select(entry => entry with
            {
                EntryId = CleanText(entry.EntryId),
                EventKind = CleanText(entry.EventKind),
                Summary = CleanText(entry.Summary),
                EvidenceRefs = CleanRefs(entry.EvidenceRefs),
                ReceiptRefs = CleanRefs(entry.ReceiptRefs)
            }).ToArray(),
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
    }

    public FrontendReadinessReadState GetOperationTimelineReadState(string operationId) =>
        FrontendReadinessReadStateClassifier.OperationTimeline(GetOperationTimeline(operationId), operationId);

    public FrontendPatchPackageMetadataReadModel? GetPatchPackageMetadata(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId) ||
            !_snapshot.PatchPackages.TryGetValue(packageId.Trim(), out var metadata))
        {
            return null;
        }

        return metadata with
        {
            PackageId = CleanText(metadata.PackageId),
            Repository = CleanText(metadata.Repository),
            Branch = CleanText(metadata.Branch),
            RunId = CleanText(metadata.RunId),
            PatchHash = CleanText(metadata.PatchHash),
            ProposedFilePaths = Clean(metadata.ProposedFilePaths),
            ArtifactRefs = CleanRefs(metadata.ArtifactRefs),
            EvidenceRefs = CleanRefs(metadata.EvidenceRefs),
            ReceiptRefs = CleanRefs(metadata.ReceiptRefs),
            ReviewSummaryRef = CleanText(metadata.ReviewSummaryRef),
            KnownRisksRef = CleanText(metadata.KnownRisksRef),
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
    }

    public FrontendReadinessReadState GetPatchPackageMetadataReadState(string packageId) =>
        FrontendReadinessReadStateClassifier.PatchPackageMetadata(GetPatchPackageMetadata(packageId), packageId);

    public FrontendPatchPackageArtifactsReadModel? GetPatchPackageArtifacts(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId) ||
            !_snapshot.PatchPackageArtifacts.TryGetValue(packageId.Trim(), out var artifacts))
        {
            return null;
        }

        return artifacts with
        {
            PackageId = CleanText(artifacts.PackageId),
            Repository = CleanText(artifacts.Repository),
            Branch = CleanText(artifacts.Branch),
            RunId = CleanText(artifacts.RunId),
            PatchHash = CleanText(artifacts.PatchHash),
            PatchDiffText = CleanText(artifacts.PatchDiffText),
            ReviewSummaryText = CleanText(artifacts.ReviewSummaryText),
            KnownRisksText = CleanText(artifacts.KnownRisksText),
            ValidationSummaryText = CleanText(artifacts.ValidationSummaryText),
            ValidationOutcome = CleanText(artifacts.ValidationOutcome),
            WhatRan = Clean(artifacts.WhatRan),
            WhatPassed = Clean(artifacts.WhatPassed),
            WhatFailed = Clean(artifacts.WhatFailed),
            WhatWasSkipped = Clean(artifacts.WhatWasSkipped),
            ProposedFilePaths = Clean(artifacts.ProposedFilePaths),
            ArtifactRefs = CleanRefs(artifacts.ArtifactRefs),
            EvidenceRefs = CleanRefs(artifacts.EvidenceRefs),
            ReceiptRefs = CleanRefs(artifacts.ReceiptRefs),
            AuthorityWarnings = Clean(BoundaryWarnings.Concat(artifacts.AuthorityWarnings)),
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
    }

    public FrontendReadinessReadState GetPatchPackageArtifactsReadState(string packageId) =>
        FrontendReadinessReadStateClassifier.PatchPackageArtifacts(GetPatchPackageArtifacts(packageId), packageId);

    public FrontendValidationResultMetadataReadModel? GetValidationResultMetadata(string validationResultId)
    {
        if (string.IsNullOrWhiteSpace(validationResultId) ||
            !_snapshot.ValidationResults.TryGetValue(validationResultId.Trim(), out var metadata))
        {
            return null;
        }

        return metadata with
        {
            ValidationResultId = CleanText(metadata.ValidationResultId),
            Repository = CleanText(metadata.Repository),
            Branch = CleanText(metadata.Branch),
            RunId = CleanText(metadata.RunId),
            PatchHash = CleanText(metadata.PatchHash),
            Outcome = CleanText(metadata.Outcome),
            WhatRan = Clean(metadata.WhatRan),
            WhatPassed = Clean(metadata.WhatPassed),
            WhatFailed = Clean(metadata.WhatFailed),
            WhatWasSkipped = Clean(metadata.WhatWasSkipped),
            EvidenceRefs = CleanRefs(metadata.EvidenceRefs),
            ReceiptRefs = CleanRefs(metadata.ReceiptRefs),
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
    }

    public FrontendReadinessReadState GetValidationResultMetadataReadState(string validationResultId) =>
        FrontendReadinessReadStateClassifier.ValidationResultMetadata(GetValidationResultMetadata(validationResultId), validationResultId);

    public FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(string evidenceRef)
    {
        if (string.IsNullOrWhiteSpace(evidenceRef) ||
            !_snapshot.Evidence.TryGetValue(evidenceRef.Trim(), out var metadata))
        {
            return null;
        }

        return metadata with
        {
            EvidenceRef = CleanRef(metadata.EvidenceRef),
            EvidenceKind = CleanText(metadata.EvidenceKind),
            Summary = CleanText(metadata.Summary),
            ReferenceOnly = true,
            ContainsRawPayload = false,
            Warnings = Clean(BoundaryWarnings.Concat(metadata.Warnings).Append("Evidence ref is not approval.")),
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
    }

    public FrontendReadinessReadState GetEvidenceMetadataReadState(string evidenceRef) =>
        FrontendReadinessReadStateClassifier.EvidenceMetadata(GetEvidenceMetadata(evidenceRef), evidenceRef);

    public FrontendReceiptMetadataReadModel? GetReceiptMetadata(string receiptRef)
    {
        if (string.IsNullOrWhiteSpace(receiptRef) ||
            !_snapshot.Receipts.TryGetValue(receiptRef.Trim(), out var metadata))
        {
            return null;
        }

        return metadata with
        {
            ReceiptRef = CleanRef(metadata.ReceiptRef),
            ReceiptKind = CleanText(metadata.ReceiptKind),
            Summary = CleanText(metadata.Summary),
            ReferenceOnly = true,
            GrantsAuthority = false,
            ContinuesWorkflow = false,
            Warnings = Clean(BoundaryWarnings.Concat(metadata.Warnings).Append("Receipt ref is not authority.")),
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
    }

    public FrontendReadinessReadState GetReceiptMetadataReadState(string receiptRef) =>
        FrontendReadinessReadStateClassifier.ReceiptMetadata(GetReceiptMetadata(receiptRef), receiptRef);

    private static IReadOnlyList<string> EnsureForbiddenActions(IReadOnlyCollection<string> values) =>
        Clean(values.Concat(RequiredForbiddenActions));

    private static IReadOnlyList<string> LabelGuidance(IReadOnlyCollection<string> values) =>
        Clean(values.Select(value =>
            value.EndsWith("(guidance only)", StringComparison.OrdinalIgnoreCase)
                ? value
                : $"{value} (guidance only)"));

    private static IReadOnlyList<string> CleanRefs(IEnumerable<string?> values) =>
        Clean(values.Select(CleanRef));

    private static string CleanRef(string? value) =>
        CleanText(value);

    private static string CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        return PrivateMaterialMarkers.Any(marker => trimmed.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? "[redacted: private material]"
            : trimmed;
    }

    private static IReadOnlyList<string> Clean(IEnumerable<string?> values) =>
        values
            .Select(CleanText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
