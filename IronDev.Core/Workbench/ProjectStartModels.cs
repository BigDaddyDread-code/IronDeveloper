namespace IronDev.Core.Workbench;

public static class ProjectLifecyclePhases
{
    public const string Shaping = "Shaping";
}

public static class ProjectExecutionReadinessStates
{
    public const string NotConfigured = "NotConfigured";
}

public static class ProjectStartOperationKinds
{
    public const string StartProject = "StartProject";
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
    Guid WorkbenchSessionId,
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
        : base("The client operation ID was already used with a different project-start payload.")
    {
    }
}
