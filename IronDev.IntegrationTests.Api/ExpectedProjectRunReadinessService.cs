using IronDev.Core.RunReadiness;

namespace IronDev.IntegrationTests.Api;

/// <summary>
/// Test-only prerequisite truth for legacy API journeys whose subject starts after readiness.
/// The fixture must explicitly name its one expected project; every other project fails closed.
/// Dedicated readiness and zero-state tests do not register this service.
/// </summary>
internal sealed class ExpectedProjectRunReadinessService : IProjectRunReadinessService
{
    private readonly ExpectedProjectApplyCapabilityService? _applyCapability;
    private int _expectedProjectId;

    public ExpectedProjectRunReadinessService(
        ExpectedProjectApplyCapabilityService? applyCapability = null)
    {
        _applyCapability = applyCapability;
    }

    public void ExpectProject(int projectId)
    {
        if (projectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(projectId));

        var previous = Interlocked.CompareExchange(ref _expectedProjectId, projectId, comparand: 0);
        if (previous != 0 && previous != projectId)
            throw new InvalidOperationException($"This fixture already expects project {previous}; it cannot also allow project {projectId}.");
    }

    public async Task<ProjectRunReadiness> EvaluateAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var expected = Volatile.Read(ref _expectedProjectId);
        if (expected > 0 && projectId == expected)
        {
            var completionCapability = _applyCapability is null
                ? null
                : await _applyCapability.EvaluateAsync(projectId, cancellationToken).ConfigureAwait(false);
            var completionReady = completionCapability?.IsReady ?? true;
            return new ProjectRunReadiness
            {
                ProjectId = projectId,
                ProjectSetupReady = true,
                ExecutionReady = true,
                CompletionCapabilityReady = completionReady,
                ReadyToRun = completionReady,
                State = completionReady
                    ? ProjectRunReadinessStates.ReadyToRun
                    : ProjectRunReadinessStates.ProjectWorkSessionRequired,
                CompletionCapability = completionCapability,
                NextAction = new ProjectRunReadinessNextAction
                {
                    Kind = completionReady ? "StartRun" : "TestFixtureCapabilityMismatch",
                    Label = completionReady ? "Ready to run" : "Register the expected apply capability",
                    NextSafeAction = completionReady
                        ? "Continue the explicitly scoped legacy API journey."
                        : "Register the same project and stable evidence hash in the REL-3 apply-capability fixture."
                },
                Boundary = "Test-only prerequisite truth for one explicitly registered project. Production readiness is not replaced."
            };
        }

        return new ProjectRunReadiness
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
        };
    }
}
