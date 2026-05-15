using System;
using System.Windows;
using System.Windows.Threading;
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
        // Surface fatal crashes as a readable MessageBox instead of silent ExecutionEngineException
        DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(
                $"Unhandled error:\n\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\n" +
                $"{e.Exception.StackTrace?.Split('\n').FirstOrDefault()}",
                "IronDev — Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true; // keep app alive for diagnostics
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            MessageBox.Show(
                $"Fatal unhandled exception:\n\n{ex?.GetType().Name}: {ex?.Message}",
                "IronDev — Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

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
                services.AddTransient<global::IronDev.Infrastructure.Services.IDeepCodeLookupService, global::IronDev.Infrastructure.Services.DeepCodeLookupService>();
                services.AddTransient<global::IronDev.Services.IChatFeedbackService, global::IronDev.Services.ChatFeedbackService>();
                services.AddSingleton<global::IronDev.Services.ILookupService, global::IronDev.Services.LookupService>();
                services.AddSingleton<global::IronDev.Core.Interfaces.ILlmTraceService, global::IronDev.Infrastructure.Services.LlmTraceService>();
                services.AddTransient<global::IronDev.Agent.Services.Interfaces.ILocalIndexingService, global::IronDev.Agent.Services.LocalIndexingService>();
                services.AddTransient<global::IronDev.AI.IPromptContextBuilder, global::IronDev.AI.PromptContextBuilder>();
                services.AddTransient<
                    global::IronDev.Core.Interfaces.IContextAgentService,
                    global::IronDev.Infrastructure.Services.ContextAgentService>();
                services.AddTransient<
                    global::IronDev.Core.Interfaces.IContextConflictService,
                    global::IronDev.Infrastructure.Services.ContextConflictService>();
                services.AddTransient<
                    global::IronDev.Core.Interfaces.IContextAgentRouteJudge,
                    global::IronDev.Infrastructure.Services.ContextAgentRouteJudgeService>();

                var aiOptions = context.Configuration.GetSection("Ai").Get<global::IronDev.Core.Models.LlmOptions>() 
                                ?? new global::IronDev.Core.Models.LlmOptions();

                // Environment variable fallback for API key if not in config
                if (string.IsNullOrWhiteSpace(aiOptions.ApiKey))
                {
                    aiOptions.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                }

                services.AddTransient<global::IronDev.Core.ILLMService>(sp =>
                {
                    try
                    {
                        var provider = aiOptions.Provider?.ToLowerInvariant() ?? "openai";
                        return provider switch
                        {
                            "openai"      => new global::IronDev.Infrastructure.Services.OpenAiLlmService(aiOptions),
                            "localopenai" => new global::IronDev.Infrastructure.Services.LocalOpenAiCompatibleLlmService(aiOptions),
                            "ollama"      => new global::IronDev.Infrastructure.Services.OllamaLlmService(aiOptions),
                            "custom"      => new global::IronDev.Infrastructure.Services.LocalOpenAiCompatibleLlmService(aiOptions),
                            _ => throw new InvalidOperationException($"Unsupported AI provider: {aiOptions.Provider}. Check appsettings.json.")
                        };
                    }
                    catch (Exception ex)
                    {
                        // API key missing or provider misconfigured — return a stub so the
                        // app starts normally. Run Grounding Test will surface the error message.
                        return new NullLlmService($"LLM not configured: {ex.Message}\nCheck Ai:ApiKey / Ai:Provider in appsettings.json.");
                    }
                });

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
                services.AddSingleton<PromptPlaygroundViewModel>(sp =>
                    new PromptPlaygroundViewModel(
                        sp.GetRequiredService<global::IronDev.AI.IPromptContextBuilder>(),
                        sp.GetRequiredService<global::IronDev.Services.IProjectService>(),
                        sp.GetRequiredService<global::IronDev.Core.ILLMService>(),
                        sp.GetRequiredService<global::IronDev.Services.ICodeIndexService>(),
                        sp.GetRequiredService<global::IronDev.Core.Auth.ICurrentTenantContext>(),
                        sp.GetRequiredService<global::IronDev.Core.Interfaces.ILlmTraceService>()));

                services.AddSingleton<LlmConsoleViewModel>(sp =>
                    new LlmConsoleViewModel(
                        sp.GetRequiredService<global::IronDev.Core.Interfaces.ILlmTraceService>()));
                services.AddSingleton<SettingsWorkspaceViewModel>(sp => new SettingsWorkspaceViewModel(
                    sp.GetRequiredService<global::IronDev.Core.Interfaces.ILlmTraceService>())
                {
                    PromptPlaygroundFactory = () => sp.GetRequiredService<PromptPlaygroundViewModel>(),
                    LlmConsoleFactory       = () => sp.GetRequiredService<LlmConsoleViewModel>()
                });


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
                services.AddTransient<
                    global::IronDev.Core.Interfaces.IDraftTicketService,
                    global::IronDev.Infrastructure.Builder.DraftTicketService>();
                services.AddTransient<
                    global::IronDev.Core.Interfaces.IBuilderProposalService,
                    global::IronDev.Infrastructure.Builder.BuilderProposalService>();
                services.AddTransient<
                    global::IronDev.Core.Interfaces.ICodebaseTicketGeneratorService,
                    global::IronDev.Infrastructure.Services.CodebaseTicketGeneratorService>();

                services.AddTransient<
                    global::IronDev.Core.Interfaces.IDotNetBuildService,
                    global::IronDev.Infrastructure.Services.DotNetRunnerService>();
                services.AddTransient<
                    global::IronDev.Core.Interfaces.IDotNetTestService,
                    global::IronDev.Infrastructure.Services.DotNetRunnerService>();

                services.AddTransient<BuilderWorkspaceViewModel>();
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

    /// <summary>
    /// Stub LLM service returned when the AI provider is not configured.
    /// Prevents the DI container from throwing during Singleton construction
    /// and surfaces a clear error message when the user tries to run a test.
    /// </summary>
    private sealed class NullLlmService : global::IronDev.Core.ILLMService
    {
        private readonly string _reason;
        public NullLlmService(string reason) => _reason = reason;
        public System.Threading.Tasks.Task<string> GetResponseAsync(
            string prompt,
            System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult($"[AI not available] {_reason}");
    }
}
