using IronDev.Core.Agents;
using IronDev.Core.Workbench;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace IronDev.Infrastructure.Services;

public sealed class ConfigurationWorkbenchAgentRunSubmissionAvailability
    : IWorkbenchAgentRunSubmissionAvailability
{
    private readonly bool _workerConfiguredAtStartup;
    private readonly ISkeletonAgentProfileService? _profiles;
    private readonly IAgentLlmResolver? _models;
    private readonly IConfiguration? _configuration;
    private readonly IHostEnvironment? _environment;

    public ConfigurationWorkbenchAgentRunSubmissionAvailability(
        bool workerConfiguredAtStartup,
        ISkeletonAgentProfileService? profiles = null,
        IAgentLlmResolver? models = null,
        IConfiguration? configuration = null,
        IHostEnvironment? environment = null)
    {
        _workerConfiguredAtStartup = workerConfiguredAtStartup;
        _profiles = profiles;
        _models = models;
        _configuration = configuration;
        _environment = environment;
    }

    public async Task<WorkbenchAgentRunSubmissionAvailability> CheckAsync(
        int tenantId,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (!_workerConfiguredAtStartup)
            return Unavailable(WorkbenchAgentRunFailureCategories.ServiceUnavailable);
        if (tenantId <= 0 || projectId <= 0 || _profiles is null || _models is null ||
            _configuration is null || _environment is null)
            return Unavailable(WorkbenchAgentRunFailureCategories.Configuration);

        try
        {
            var profile = (await _profiles.ListEffectiveAsync(
                    tenantId,
                    projectId,
                    cancellationToken)
                .ConfigureAwait(false)).SingleOrDefault(value => value.Role == SkeletonAgentRole.Analyst);
            if (profile is null || string.IsNullOrWhiteSpace(profile.EffectiveHash))
                return Unavailable(WorkbenchAgentRunFailureCategories.Configuration);

            // This is the exact explicit deterministic bypass used by the model gateway.
            // It still requires one valid effective Analyst profile, but no external
            // provider client or network reachability check is needed.
            if (_environment.IsEnvironment("LocalTest") &&
                _configuration.GetValue<bool>("WorkbenchBusinessAnalyst:LocalTestDeterministicEnabled"))
                return WorkbenchAgentRunSubmissionAvailability.Available;

            // Resolve only configuration and credentials. Provider construction makes
            // no external request; live reachability remains a bounded run-time result.
            var model = await _models.ResolveAsync(
                    profile,
                    tenantId,
                    projectId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (model.Role != SkeletonAgentRole.Analyst ||
                string.IsNullOrWhiteSpace(model.Provider) ||
                string.IsNullOrWhiteSpace(model.Model) ||
                model.TimeoutSeconds <= 0 ||
                model.Llm is not IWorkbenchBusinessAnalystRoleAwareLlmService)
                return Unavailable(WorkbenchAgentRunFailureCategories.Configuration);

            return WorkbenchAgentRunSubmissionAvailability.Available;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Unavailable(WorkbenchAgentRunFailureCategories.Configuration);
        }
    }

    private static WorkbenchAgentRunSubmissionAvailability Unavailable(string category) =>
        new(false, category);
}
