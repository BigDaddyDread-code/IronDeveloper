using System;
using System.Windows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using IronDev.AI;
using IronDev.Core.Auth;
using IronDev.Data;
using IronDev.Infrastructure.Auth;
using IronDev.Services;
using IronDev.Agent.Services;
using IronDev.Core;
using IronDev.Agent.ViewModels;
using IronDev.Agent.Views;

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
                services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
                // Scoped tenant context — DevelopmentTenantContext always returns TenantId=1 for local dev.
                // Will be replaced by session/JWT context in Sprint 2 without changing any service code.
                services.AddScoped<ICurrentTenantContext, DevelopmentTenantContext>();
                services.AddScoped<IProjectService, ProjectService>();
                services.AddScoped<IChatHistoryService, ChatHistoryService>();
                services.AddScoped<IProjectMemoryService, ProjectMemoryService>();
                services.AddScoped<ITicketService, TicketService>();
                services.AddScoped<ICodeIndexService, SqlCodeIndexService>();
                services.AddScoped<IWorkbenchGeneratorService, WorkbenchGeneratorService>();
                services.AddScoped<IPromptContextBuilder, PromptContextBuilder>();

                var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("OPENAI_API_KEY is not set.");

                services.AddSingleton<ILLMService>(sp => new OpenAiLlmService(apiKey));

                // ── Original ViewModels ───────────────────────────────────────
                services.AddTransient<ProjectPanelViewModel>();
                services.AddTransient<ChatViewModel>();
                services.AddTransient<OutputPanelViewModel>();
                services.AddTransient<CodeWorkbenchViewModel>();
                services.AddTransient<MainViewModel>();

                // ── Views ─────────────────────────────────────────────────────
                services.AddTransient<MainWindow>();
                services.AddTransient<CodeWorkbenchWindow>();

                // ── Workspace ViewModels (from builder branch) ────────────────
                services.AddTransient<IronDev.Agent.ViewModels.Workspaces.TicketsWorkspaceViewModel>();

                // ── Build Ticket MVP — Phase 2 + 3 ───────────────────────────
                services.AddTransient<
                    global::IronDev.Core.Interfaces.IBuilderContextService,
                    global::IronDev.Infrastructure.Builder.BuilderContextService>();
                services.AddTransient<
                    global::IronDev.Core.Interfaces.ICodeChangeProposalService,
                    global::IronDev.Infrastructure.Builder.CodeChangeProposalService>();
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
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
        }

        base.OnExit(e);
    }
}
