using System;
using System.Collections.Generic;
using System.Linq;

namespace IronDev.Core.KnowledgeCompiler;

public enum SemanticIndexRebuildMode
{
    ProjectOnly = 0,
    DryRunProjectOnly = 1,
    FullCollectionResetBlocked = 2
}

public enum SemanticIndexRebuildStatus
{
    Planned = 0,
    Blocked = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Partial = 5
}

public enum SemanticIndexRebuildBlockReason
{
    MissingProjectId = 0,
    WeaviateDisabled = 1,
    WeaviateUnavailable = 2,
    UnsafeSharedCollectionReset = 3,
    UnsupportedGlobalRebuild = 4,
    UnsafeSourceContent = 5,
    SourceDocumentsUnavailable = 6,
    Cancelled = 7,
    Unknown = 8
}

public sealed record SemanticIndexRebuildRequest
{
    public int ProjectId { get; init; }
    public SemanticIndexRebuildMode Mode { get; init; } = SemanticIndexRebuildMode.ProjectOnly;
    public bool DryRun { get; init; }
    public bool AllowCollectionReset { get; init; }
    public string RequestedBy { get; init; } = string.Empty;
    public DateTime RequestedAtUtc { get; init; } = DateTime.UtcNow;
    public string Reason { get; init; } = string.Empty;
}

public sealed record SemanticIndexRebuildPlan
{
    public int ProjectId { get; init; }
    public string CollectionName { get; init; } = string.Empty;
    public SemanticIndexRebuildMode Mode { get; init; } = SemanticIndexRebuildMode.ProjectOnly;
    public bool IsDestructive { get; init; }
    public bool WillDeleteCollection { get; init; }
    public bool WillMutateSqlSourceRecords { get; init; }
    public bool WillMutateAuthorityRecords { get; init; }
    public int SourceDocumentCount { get; init; }
    public int EstimatedChunkCount { get; init; }
    public IReadOnlyList<SemanticIndexRebuildBlockReason> BlockReasons { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record SemanticIndexRebuildResult
{
    public SemanticIndexRebuildStatus Status { get; init; }
    public int ProjectId { get; init; }
    public string RunId { get; init; } = string.Empty;
    public string CollectionName { get; init; } = string.Empty;
    public int TotalDocuments { get; init; }
    public int ProcessedDocuments { get; init; }
    public int SkippedDocuments { get; init; }
    public SemanticIndexRebuildBlockReason? FailureReason { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public IReadOnlyList<SemanticIndexRebuildBlockReason> BlockReasons { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public DateTime StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public bool IsAuthorityGrant { get; init; }
    public bool GrantsApproval { get; init; }
    public bool GrantsPolicySatisfaction { get; init; }
    public bool GrantsSourceApplyAuthority { get; init; }
    public bool GrantsWorkflowContinuation { get; init; }
    public bool GrantsReleaseReadiness { get; init; }
    public bool GrantsDeploymentReadiness { get; init; }
}

public static class SemanticIndexRebuildGuard
{
    private static readonly string[] BoundaryWarnings =
    [
        "Weaviate rebuild restores recall. It does not restore authority.",
        "SQL remains source of truth.",
        "Weaviate remains a rebuildable derived index.",
        "A rebuilt vector index is still just an index.",
        "Vector recall is not authority.",
        "Vector indexing is not redaction.",
        "Rebuild must not index raw secrets, raw artifact bodies, or raw private payloads."
    ];

    public static SemanticIndexRebuildPlan BuildPlan(
        SemanticIndexRebuildRequest? request,
        string collectionName,
        bool weaviateEnabled,
        int sourceDocumentCount = 0,
        int estimatedChunkCount = 0)
    {
        var reasons = new List<SemanticIndexRebuildBlockReason>();
        var mode = request?.Mode ?? SemanticIndexRebuildMode.ProjectOnly;
        var projectId = request?.ProjectId ?? 0;

        if (request is null || projectId <= 0)
            reasons.Add(SemanticIndexRebuildBlockReason.MissingProjectId);

        if (!Enum.IsDefined(typeof(SemanticIndexRebuildMode), mode))
            reasons.Add(SemanticIndexRebuildBlockReason.Unknown);

        if (!weaviateEnabled)
            reasons.Add(SemanticIndexRebuildBlockReason.WeaviateDisabled);

        if (request?.AllowCollectionReset == true ||
            mode == SemanticIndexRebuildMode.FullCollectionResetBlocked)
        {
            reasons.Add(SemanticIndexRebuildBlockReason.UnsafeSharedCollectionReset);
        }

        return new SemanticIndexRebuildPlan
        {
            ProjectId = projectId,
            CollectionName = string.IsNullOrWhiteSpace(collectionName) ? "IronDevKnowledge" : collectionName,
            Mode = mode,
            IsDestructive = false,
            WillDeleteCollection = false,
            WillMutateSqlSourceRecords = false,
            WillMutateAuthorityRecords = false,
            SourceDocumentCount = Math.Max(0, sourceDocumentCount),
            EstimatedChunkCount = Math.Max(0, estimatedChunkCount),
            BlockReasons = reasons.Distinct().ToArray(),
            Warnings = BoundaryWarnings
        };
    }

    public static SemanticIndexRebuildResult Blocked(
        SemanticIndexRebuildPlan plan,
        DateTime startedAtUtc,
        SemanticIndexRebuildBlockReason? failureReason = null,
        string? errorMessage = null)
        => Result(
            SemanticIndexRebuildStatus.Blocked,
            plan,
            startedAtUtc,
            DateTime.UtcNow,
            runId: string.Empty,
            processedDocuments: 0,
            skippedDocuments: 0,
            failureReason ?? plan.BlockReasons.FirstOrDefault(),
            errorMessage);

    public static SemanticIndexRebuildResult Planned(SemanticIndexRebuildPlan plan, DateTime startedAtUtc)
        => Result(
            SemanticIndexRebuildStatus.Planned,
            plan,
            startedAtUtc,
            DateTime.UtcNow,
            runId: string.Empty,
            processedDocuments: 0,
            skippedDocuments: 0,
            failureReason: null,
            errorMessage: null);

    public static SemanticIndexRebuildResult Completed(
        SemanticIndexRebuildPlan plan,
        DateTime startedAtUtc,
        string runId,
        int processedDocuments,
        int skippedDocuments = 0)
        => Result(
            SemanticIndexRebuildStatus.Completed,
            plan,
            startedAtUtc,
            DateTime.UtcNow,
            runId,
            processedDocuments,
            skippedDocuments,
            failureReason: null,
            errorMessage: null);

    public static SemanticIndexRebuildResult Failed(
        SemanticIndexRebuildPlan plan,
        DateTime startedAtUtc,
        string runId,
        int processedDocuments,
        SemanticIndexRebuildBlockReason failureReason,
        string? errorMessage)
        => Result(
            SemanticIndexRebuildStatus.Failed,
            plan,
            startedAtUtc,
            DateTime.UtcNow,
            runId,
            processedDocuments,
            skippedDocuments: 0,
            failureReason,
            errorMessage);

    public static string SanitizeErrorMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        var lower = message.ToLowerInvariant();
        var unsafeMarkers = new[]
        {
            "authorization",
            "api key",
            "apikey",
            "bearer",
            "token",
            "password",
            "secret",
            "connection string"
        };

        return unsafeMarkers.Any(lower.Contains)
            ? "[redacted rebuild error]"
            : message;
    }

    private static SemanticIndexRebuildResult Result(
        SemanticIndexRebuildStatus status,
        SemanticIndexRebuildPlan plan,
        DateTime startedAtUtc,
        DateTime? completedAtUtc,
        string runId,
        int processedDocuments,
        int skippedDocuments,
        SemanticIndexRebuildBlockReason? failureReason,
        string? errorMessage)
        => new()
        {
            Status = status,
            ProjectId = plan.ProjectId,
            RunId = runId,
            CollectionName = plan.CollectionName,
            TotalDocuments = plan.SourceDocumentCount,
            ProcessedDocuments = Math.Max(0, processedDocuments),
            SkippedDocuments = Math.Max(0, skippedDocuments),
            FailureReason = failureReason,
            ErrorMessage = SanitizeErrorMessage(errorMessage),
            BlockReasons = plan.BlockReasons,
            Warnings = plan.Warnings,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            IsAuthorityGrant = false,
            GrantsApproval = false,
            GrantsPolicySatisfaction = false,
            GrantsSourceApplyAuthority = false,
            GrantsWorkflowContinuation = false,
            GrantsReleaseReadiness = false,
            GrantsDeploymentReadiness = false
        };
}
