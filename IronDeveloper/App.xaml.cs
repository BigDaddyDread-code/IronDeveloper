using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using IronDev.Agent.ViewModels.Shell;
using IronDev.Agent.ViewModels.Workflow;
using IronDev.Agent.ViewModels.Workspaces;
using IronDev.Agent.Views;
using IronDev.Agent.Services;
using IronDev.Agent.Services.Interfaces;
using IronDev.Agent.Services.Mock;

namespace IronDev.Agent;

public partial class App : Application
{
    private readonly IHost _host;

    public IServiceProvider Services => _host.Services;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // ── Data & Infrastructure ─────────────────────────────────────
                services.AddSingleton<global::IronDev.Data.IDbConnectionFactory, global::IronDev.Data.SqlConnectionFactory>();
                
                // Tenancy Bridge
                services.AddSingleton<AgentTenantContext>();
                services.AddSingleton<global::IronDev.Core.Auth.ICurrentTenantContext>(sp => sp.GetRequiredService<AgentTenantContext>());
                
                // Real Services from Infrastructure
                services.AddTransient<global::IronDev.Services.IUserService, global::IronDev.Services.UserService>();
                services.AddTransient<global::IronDev.Services.IProjectService, global::IronDev.Services.ProjectService>();
                services.AddTransient<global::IronDev.Services.ITicketService, global::IronDev.Services.TicketService>();
                services.AddTransient<global::IronDev.Services.IChatHistoryService, global::IronDev.Services.ChatHistoryService>();
                services.AddTransient<global::IronDev.Services.IProjectMemoryService, global::IronDev.Services.ProjectMemoryService>();
                services.AddTransient<global::IronDev.Services.ICodeIndexService, global::IronDev.Services.SqlCodeIndexService>();
                services.AddSingleton<global::IronDev.Services.ILookupService, global::IronDev.Services.LookupService>();
                services.AddTransient<global::IronDev.Agent.Services.Interfaces.ILocalIndexingService, global::IronDev.Agent.Services.LocalIndexingService>();
                services.AddTransient<global::IronDev.AI.IPromptContextBuilder, global::IronDev.AI.PromptContextBuilder>();
                var openAiKey = context.Configuration["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                var openAiModel = context.Configuration["OPENAI_MODEL"] ?? Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o";
                services.AddTransient<global::IronDev.Core.ILLMService>(sp => 
                    new global::IronDev.Infrastructure.Services.OpenAiLlmService(openAiKey, openAiModel));

                // ── Workflow ViewModels ───────────────────────────────────────
                services.AddTransient<LoginViewModel>();
                services.AddTransient<ProjectHubViewModel>();
                services.AddTransient<CreateProjectViewModel>();
                services.AddTransient<ProjectOverviewViewModel>();

                // ── Workspace ViewModels ──────────────────────────────────────
                services.AddTransient<ChatWorkspaceViewModel>();
                services.AddTransient<TicketsWorkspaceViewModel>();
                services.AddTransient<DecisionsWorkspaceViewModel>();
                services.AddTransient<ImplementationPlansWorkspaceViewModel>();
                services.AddTransient<SettingsWorkspaceViewModel>();

                // Shell
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<MainWindow>();

                // Mocks for pending features
                services.AddSingleton<global::IronDev.Agent.Services.Interfaces.IProjectShellService, global::IronDev.Agent.Services.Mock.MockProjectShellService>();

                // ── Build Ticket MVP — Phase 2 + 3 + 4A ────────────────────────
                services.AddTransient<
                    global::IronDev.Core.Interfaces.IBuilderContextService,
                    global::IronDev.Infrastructure.Builder.BuilderContextService>();
                services.AddTransient<
                    global::IronDev.Core.Interfaces.ICodeChangeProposalService,
                    global::IronDev.Infrastructure.Builder.CodeChangeProposalService>();
                services.AddTransient<
                    global::IronDev.Core.Interfaces.ICodePatchService,
                    global::IronDev.Infrastructure.Builder.CodePatchService>();
                services.AddTransient<
                    global::IronDev.Core.Interfaces.ITicketBuildOrchestrator,
                    global::IronDev.Infrastructure.Builder.TicketBuildOrchestrator>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        using (_host)
            await _host.StopAsync(TimeSpan.FromSeconds(5));

        base.OnExit(e);
    }
}
