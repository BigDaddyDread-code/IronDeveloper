using IronDev.Core.Workspaces;

namespace IronDev.IntegrationTests;

internal sealed class AgentSkillExecutionTestValidationService : IDisposableWorkspaceValidationService
{
    public int CallCount { get; private set; }

    public Task<DisposableWorkspaceValidationResult> ValidateAsync(
        DisposableWorkspaceValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(new DisposableWorkspaceValidationResult
        {
            Status = "blocked",
            Summary = "Test validation service should not be called by this test.",
            ExitCode = 1,
            Data = new DisposableWorkspaceValidationData
            {
                RunId = request.RunId,
                WorkspacePath = request.WorkspacePath,
                ProfileId = request.ProfileId,
                Status = "blocked",
                Succeeded = false,
                Steps = [],
                EvidencePaths = [],
                ValidationMetadataPath = null,
                Errors = ["Test validation service should not be called by this test."],
                Warnings = []
            },
            Errors = ["Test validation service should not be called by this test."],
            Warnings = []
        });
    }
}