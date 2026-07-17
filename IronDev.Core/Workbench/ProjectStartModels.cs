namespace IronDev.Core.Workbench;

public static class ProjectLifecyclePhases
{
    public const string Shaping = "Shaping";
    public const string Delivery = "Delivery";
    public const string Archived = "Archived";
}

public static class ProjectExecutionReadinessStates
{
    public const string NotConfigured = "NotConfigured";
    public const string ValidationRequired = "ValidationRequired";
    public const string Ready = "Ready";
}

public static class ProjectStartOperationKinds
{
    public const string StartProject = "StartProject";
    public const string OpenWorkbenchProject = "OpenWorkbenchProject";
}

public sealed record StartProjectCommand(
    int TenantId,
    int ActorUserId,
    Guid ClientOperationId,
    string Name);

public sealed record StartProjectResult(
    int ProjectId,
    int TenantId,
    string Name,
    string ProjectLifecyclePhase,
    string ExecutionReadiness,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    DateTime CreatedAtUtc,
    bool IsReplay)
{
    public object? RepositoryBinding => null;
}

public interface IProjectStartService
{
    Task<StartProjectResult> StartAsync(
        StartProjectCommand command,
        CancellationToken cancellationToken = default);
}

public enum ProjectStartFailurePoint
{
    ProjectCreated,
    OwnerMembershipCreated,
    UnderstandingCreated,
    ReadinessCreated,
    WorkbenchSessionCreated,
    WriteLeaseCreated,
    OutboxEventsCreated
}

public interface IProjectStartFailureInjector
{
    void ThrowIfRequested(ProjectStartFailurePoint point);
}

public sealed class NoOpProjectStartFailureInjector : IProjectStartFailureInjector
{
    public void ThrowIfRequested(ProjectStartFailurePoint point)
    {
    }
}

public sealed class ProjectStartValidationException(string message) : Exception(message);

public sealed class ProjectStartOperationMismatchException : Exception
{
    public const string ErrorCode = "operation_id_payload_mismatch";

    public ProjectStartOperationMismatchException()
        : base("The client operation ID was already used with a different payload.")
    {
    }
}

public sealed record WorkbenchProjectEntryContext(
    int ProjectId,
    int TenantId,
    string Name,
    string ProjectLifecyclePhase,
    string ExecutionReadiness,
    long WorkbenchSessionId,
    long LeaseEpoch,
    bool WasResumed,
    bool WasTakenOver,
    Guid ClientOperationId)
{
    public object? RepositoryBinding => null;
}

public sealed record OpenWorkbenchProjectCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    Guid ClientOperationId,
    bool TakeOver);

public interface IWorkbenchProjectEntryService
{
    Task<WorkbenchProjectEntryContext> OpenAsync(
        OpenWorkbenchProjectCommand command,
        CancellationToken cancellationToken = default);

    Task<bool> HasCurrentWriteLeaseAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        CancellationToken cancellationToken = default);
}

public sealed class WorkbenchProjectNotAccessibleException : Exception
{
}

public sealed class WorkbenchLeaseTakeoverRequiredException : Exception
{
    public WorkbenchLeaseTakeoverRequiredException()
        : base("Another Workbench session currently holds the writable lease. Confirm takeover to continue.")
    {
    }
}

public sealed class WorkbenchLeaseFenceException : Exception
{
    public const string ErrorCode = "workbench_lease_fence_rejected";

    public WorkbenchLeaseFenceException()
        : base("The Workbench session or lease epoch is no longer current. Reopen the project before retrying.")
    {
    }
}
