namespace IronDev.Core.Governance;

public interface IFrontendReadinessReadApi
{
    FrontendOperationStatusReadModel? GetOperationStatus(string operationId);
    FrontendOperationTimelineReadModel? GetOperationTimeline(string operationId);
    FrontendPatchPackageMetadataReadModel? GetPatchPackageMetadata(string packageId);
    FrontendValidationResultMetadataReadModel? GetValidationResultMetadata(string validationResultId);
    FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(string evidenceRef);
    FrontendReceiptMetadataReadModel? GetReceiptMetadata(string receiptRef);
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
