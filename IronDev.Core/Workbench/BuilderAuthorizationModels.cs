namespace IronDev.Core.Workbench;

public static class BuilderAuthorizationOperationKinds
{
    public const string CreateWorkPackage = "CreateBuilderWorkPackage";
    public const string GrantAuthorization = "GrantBuilderExecutionAuthorization";
    public const string RevokeAuthorization = "RevokeBuilderExecutionAuthorization";
}

public static class BuilderExecutionAuthorizationStates
{
    public const string Valid = "Valid";
    public const string Expired = "Expired";
    public const string Revoked = "Revoked";
    public const string Consumed = "Consumed";
    public const string ScopeStale = "ScopeStale";
}

public static class BuilderAuthorizationReasonCodes
{
    public const string Ready = "BuilderAuthorizationAvailable";
    public const string ProjectNotInDelivery = "ProjectNotInDelivery";
    public const string TechnicalReadinessNotCurrent = "TechnicalReadinessNotCurrent";
    public const string TicketRequired = "TicketRequired";
    public const string TicketRevisionChanged = "TicketRevisionChanged";
    public const string RepositoryRevisionChanged = "RepositoryRevisionChanged";
    public const string RepositoryBaselineChanged = "RepositoryBaselineChanged";
    public const string RepositoryBranchChanged = "RepositoryBranchChanged";
    public const string RepositoryFingerprintChanged = "RepositoryFingerprintChanged";
    public const string TechnicalEvidenceChanged = "TechnicalEvidenceChanged";
    public const string ActorAuthorizationLost = "ActorAuthorizationLost";
    public const string AuthorizationExpired = "AuthorizationExpired";
    public const string AuthorizationRevoked = "AuthorizationRevoked";
    public const string AuthorizationConsumed = "AuthorizationConsumed";
    public const string WorkPackageRequired = "WorkPackageRequired";
}

public sealed record BuilderExecutionAuthorizationSnapshot
{
    public required Guid Id { get; init; }
    public required int ProjectId { get; init; }
    public required int ActorUserId { get; init; }
    public required Guid BuilderWorkPackageCoreId { get; init; }
    public required string BuilderWorkPackageCoreHash { get; init; }
    public required DateTime GrantedAtUtc { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
    public bool SingleUse { get; init; } = true;
    public DateTime? ConsumedAtUtc { get; init; }
    public Guid? ConsumedByBuilderExecutionRunId { get; init; }
    public DateTime? RevokedAtUtc { get; init; }
    public required string State { get; init; }
    public required string ReasonCode { get; init; }
}

public sealed record BuilderWorkPackageResult(
    BuilderWorkPackageCore Core,
    string BuilderWorkPackageCoreHash,
    Guid ClientOperationId,
    bool IsReplay);

public sealed record BuilderAuthorizationResult(
    BuilderExecutionAuthorizationSnapshot Authorization,
    BuilderWorkPackage WorkPackage,
    Guid ClientOperationId,
    bool IsReplay);

public sealed record BuilderAuthorizationRevocationResult(
    BuilderExecutionAuthorizationSnapshot Authorization,
    Guid ClientOperationId,
    bool IsReplay);

public sealed record WorkbenchBuilderContext(
    int ProjectId,
    long? TicketId,
    string ProjectLifecyclePhase,
    string ExecutionReadiness,
    string? RepositoryBranchName,
    string? RepositoryBaselineCommit,
    BuilderWorkPackageResult? WorkPackage,
    BuilderExecutionAuthorizationSnapshot? Authorization,
    bool CanPrepareWorkPackage,
    bool CanGrantAuthorization,
    string ReasonCode);

public sealed record GetWorkbenchBuilderContextQuery(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long? TicketId);

public sealed record CreateBuilderWorkPackageCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    IReadOnlyList<long> TicketIds);

public sealed record GrantBuilderExecutionAuthorizationCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    Guid BuilderWorkPackageCoreId,
    string ExpectedCoreHash);

public sealed record RevokeBuilderExecutionAuthorizationCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    Guid BuilderExecutionAuthorizationId);

public sealed record BuilderRepositoryBranchObservation(
    string BranchName,
    string HeadCommit);

public interface IBuilderRepositoryBranchObserver
{
    Task<BuilderRepositoryBranchObservation> ObserveAsync(
        string canonicalRepositoryPath,
        CancellationToken cancellationToken = default);
}

public interface IWorkbenchBuilderAuthorizationService
{
    Task<WorkbenchBuilderContext> GetContextAsync(
        GetWorkbenchBuilderContextQuery query,
        CancellationToken cancellationToken = default);

    Task<BuilderWorkPackageResult> CreateWorkPackageAsync(
        CreateBuilderWorkPackageCommand command,
        CancellationToken cancellationToken = default);

    Task<BuilderAuthorizationResult> GrantAsync(
        GrantBuilderExecutionAuthorizationCommand command,
        CancellationToken cancellationToken = default);

    Task<BuilderAuthorizationRevocationResult> RevokeAsync(
        RevokeBuilderExecutionAuthorizationCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class BuilderAuthorizationValidationException(string message) : Exception(message)
{
    public const string ErrorCode = "builder_authorization_invalid";
}

public sealed class BuilderAuthorizationForbiddenException(string message) : Exception(message)
{
    public const string ErrorCode = "builder_authorization_forbidden";
}

public sealed class BuilderAuthorizationNotAllowedException(string reasonCode, string message)
    : Exception(message)
{
    public const string ErrorCode = "builder_authorization_not_allowed";
    public string ReasonCode { get; } = reasonCode;
}

public sealed class BuilderAuthorizationStaleScopeException(string reasonCode, string message)
    : Exception(message)
{
    public const string ErrorCode = "builder_authorization_scope_stale";
    public string ReasonCode { get; } = reasonCode;
}

public sealed class BuilderAuthorizationOperationMismatchException()
    : Exception("The client operation ID was already used with a different Builder authorization payload.")
{
    public const string ErrorCode = "operation_id_payload_mismatch";
}

public sealed class BuilderAuthorizationIntegrityException(string message) : Exception(message)
{
    public const string ErrorCode = "builder_authorization_integrity_failed";
}
