using IronDev.Core.RunReadiness;

namespace IronDev.IntegrationTests.Api;

/// <summary>
/// Test-only prerequisite truth for legacy API journeys whose subject starts after readiness.
/// The fixture must explicitly name its one expected project; every other project fails closed.
/// Dedicated readiness and zero-state tests do not register this service.
/// </summary>
internal sealed class ExpectedProjectRunReadinessService : IProjectRunReadinessService
{
    private int _expectedProjectId;

    public void ExpectProject(int projectId)
    {
        if (projectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(projectId));

        var previous = Interlocked.CompareExchange(ref _expectedProjectId, projectId, comparand: 0);
        if (previous != 0 && previous != projectId)
            throw new InvalidOperationException($"This fixture already expects project {previous}; it cannot also allow project {projectId}.");
    }

    public Task<ProjectRunReadiness> EvaluateAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var expected = Volatile.Read(ref _expectedProjectId);
        if (expected > 0 && projectId == expected)
        {
            return Task.FromResult(new ProjectRunReadiness
            {
                ProjectId = projectId,
                ProjectSetupReady = true,
                ExecutionReady = true,
                ReadyToRun = true,
                State = ProjectRunReadinessStates.ReadyToRun,
                NextAction = new ProjectRunReadinessNextAction
                {
                    Kind = "StartRun",
                    Label = "Ready to run",
                    NextSafeAction = "Continue the explicitly scoped legacy API journey."
                },
                Boundary = "Test-only prerequisite truth for one explicitly registered project. Production readiness is not replaced."
            });
        }

        return Task.FromResult(new ProjectRunReadiness
        {
            ProjectId = projectId,
            ProjectSetupReady = false,
            ExecutionReady = false,
            ReadyToRun = false,
            State = ProjectRunReadinessStates.SetupIncomplete,
            BlockedCount = 1,
            NextAction = new ProjectRunReadinessNextAction
            {
                Kind = "TestFixtureProjectMismatch",
                Label = "Register the expected test project",
                NextSafeAction = expected <= 0
                    ? "The legacy journey fixture did not register its expected project."
                    : $"The legacy journey fixture allows project {expected}, not project {projectId}.",
                TargetProductRoute = $"/projects/{projectId}/setup"
            },
            Boundary = "Fail-closed test-only readiness: unknown and different project IDs are never ready."
        });
    }
}
