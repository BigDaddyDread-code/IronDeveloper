using System.Diagnostics;
using IronDev.Core.Agents;
using IronDev.Core.Workbench;

namespace IronDev.Infrastructure.Services;

/// <summary>Builder-only provider gateway. It resolves no task context and grants no tools.</summary>
public sealed class WorkbenchBuilderModelGateway(IAgentLlmResolver models)
    : IWorkbenchBuilderModelGateway
{
    public async Task<BuilderProviderResponse> InvokeAsync(
        BuilderPreparedExecutionInput input,
        int attemptNumber,
        string? repairEvidence,
        CancellationToken cancellationToken = default)
    {
        if (attemptNumber is < 1 or > BuilderExecutionContract.MaximumAttempts)
            throw new InvalidOperationException("Builder attempt is outside the code-owned repair bound.");
        var profile = input.WorkPackageCore.EffectiveProfile;
        var model = await models.ResolveAsync(
            SkeletonAgentRole.Builder, input.TenantId, input.ProjectId, cancellationToken).ConfigureAwait(false);
        if (model.Role != SkeletonAgentRole.Builder ||
            !string.Equals(model.Provider, profile.ProviderId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(model.Model, profile.ModelId, StringComparison.Ordinal))
            throw new BuilderExecutionConflictException(
                BuilderExecutionFailureCodes.BuilderProfileChanged,
                "The effective Builder provider/profile no longer matches the frozen run.");

        var safeRequestId = $"builder-{input.BuilderAgentRunId:N}-{attemptNumber}";
        var envelope = new BuilderProviderEnvelope(
            1, safeRequestId, input.SystemPrompt, input.RoleContextJson,
            input.ToolManifestJson, BuilderRoleContract.OutputSchemaVersion,
            attemptNumber, repairEvidence);
        if (model.Llm is not IWorkbenchBuilderRoleAwareLlmService roleAware)
            throw new BuilderExecutionConflictException(
                BuilderExecutionFailureCodes.BuilderProfileChanged,
                "The frozen Builder provider cannot preserve the Builder role hierarchy.");
        var started = Stopwatch.GetTimestamp();
        var response = await roleAware.GetBuilderResponseAsync(envelope, cancellationToken)
            .ConfigureAwait(false);
        return response with
        {
            DurationMilliseconds = response.DurationMilliseconds == 0
                ? Stopwatch.GetElapsedTime(started).Ticks / TimeSpan.TicksPerMillisecond
                : response.DurationMilliseconds
        };
    }
}
