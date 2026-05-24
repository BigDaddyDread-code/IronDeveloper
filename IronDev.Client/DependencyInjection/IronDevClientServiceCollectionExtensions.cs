using IronDev.Client.Auth;
using IronDev.Client.Chat;
using IronDev.Client.CodeIndex;
using IronDev.Client.Documents;
using IronDev.Client.Http;
using IronDev.Client.Memory;
using IronDev.Client.Profiles;
using IronDev.Client.Projects;
using IronDev.Client.Prompting;
using IronDev.Client.RunReports;
using IronDev.Client.Settings;
using IronDev.Client.Tickets;
using IronDev.Client.Traces;
using IronDev.Core.Interfaces;
using IronDev.Core.RunReports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.Client.DependencyInjection;

public static class IronDevClientServiceCollectionExtensions
{
    public static IServiceCollection AddIronDevClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection("Api").Get<IronDevApiOptions>() ?? new IronDevApiOptions();
        services.AddSingleton(options);
        services.AddSingleton<IIronDevSession, IronDevSession>();
        services.AddTransient<AuthTokenHandler>();

        services.AddIronDevHttpClient<IAuthApiClient, AuthApiClient>(options);
        services.AddIronDevHttpClient<IProjectsApiClient, ProjectsApiClient>(options);
        services.AddIronDevHttpClient<ITicketsApiClient, TicketsApiClient>(options);
        services.AddIronDevHttpClient<IMemoryApiClient, MemoryApiClient>(options);
        services.AddIronDevHttpClient<IChatApiClient, ChatApiClient>(options);
        services.AddIronDevHttpClient<ICodeIndexApiClient, CodeIndexApiClient>(options);
        services.AddIronDevHttpClient<IDocumentsApiClient, DocumentsApiClient>(options);
        services.AddIronDevHttpClient<IProjectProfilesApiClient, ProjectProfilesApiClient>(options);
        services.AddIronDevHttpClient<IRunReportsApiClient, RunReportsApiClient>(options);

        services.AddSingleton<ITraceApiClient, InMemoryTraceClient>();
        services.AddSingleton<ISettingsApiClient, NoopSettingsApiClient>();
        services.AddSingleton<IPromptContextBuilder, ClientPromptContextBuilder>();

        services.AddTransient<IProjectDocumentService>(sp => sp.GetRequiredService<IDocumentsApiClient>());
        services.AddTransient<IProjectProfileService>(sp => sp.GetRequiredService<IProjectProfilesApiClient>());
        services.AddTransient<IProjectProfileDetectionService>(sp => sp.GetRequiredService<IProjectProfilesApiClient>());
        services.AddTransient<IDraftTicketService>(sp => sp.GetRequiredService<ITicketsApiClient>());
        services.AddTransient<ITicketBuildOrchestrator>(sp => sp.GetRequiredService<ITicketsApiClient>());
        services.AddTransient<IBuilderReadinessService>(sp => sp.GetRequiredService<ITicketsApiClient>());
        services.AddTransient<IBuilderProposalService>(sp => sp.GetRequiredService<ITicketsApiClient>());
        services.AddTransient<IRunReportService>(sp => sp.GetRequiredService<IRunReportsApiClient>());
        services.AddTransient<IRunEvidenceService>(sp => sp.GetRequiredService<IRunReportsApiClient>());
        services.AddSingleton<ILlmTraceService>(sp => sp.GetRequiredService<ITraceApiClient>());

        return services;
    }

    private sealed class NoopSettingsApiClient : ISettingsApiClient
    {
    }

    private static IServiceCollection AddIronDevHttpClient<TInterface, TImplementation>(
        this IServiceCollection services,
        IronDevApiOptions options)
        where TInterface : class
        where TImplementation : class, TInterface
    {
        services.AddHttpClient<TInterface, TImplementation>(client =>
        {
            var baseAddress = options.BaseAddress.EndsWith("/", StringComparison.Ordinal)
                ? options.BaseAddress
                : options.BaseAddress + "/";
            client.BaseAddress = new Uri(baseAddress);
        }).AddHttpMessageHandler<AuthTokenHandler>();

        return services;
    }
}
