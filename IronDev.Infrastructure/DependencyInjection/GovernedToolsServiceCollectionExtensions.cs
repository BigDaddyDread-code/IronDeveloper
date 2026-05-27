using IronDev.Core.Tools;
using IronDev.Infrastructure.Tools;
using IronDev.Infrastructure.Tools.CodeStandards;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.Infrastructure.DependencyInjection;

public static class GovernedToolsServiceCollectionExtensions
{
    public static IServiceCollection AddGovernedTools(this IServiceCollection services)
    {
        services.AddSingleton<GovernedToolPolicyEvaluator>();
        services.AddSingleton<IGovernedToolThoughtLedger, InMemoryGovernedToolThoughtLedger>();
        services.AddSingleton<CodeStandardsAnalysisTool>();
        services.AddSingleton<IGovernedTool<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>>(sp =>
            sp.GetRequiredService<CodeStandardsAnalysisTool>());
        services.AddSingleton<IGovernedToolRegistration>(sp =>
            sp.GetRequiredService<CodeStandardsAnalysisTool>());
        services.AddSingleton<IGovernedToolRegistry, GovernedToolRegistry>();

        return services;
    }
}
