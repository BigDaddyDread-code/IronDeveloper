using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using IronDev.Agent.ViewModels.Shell;
using IronDev.Agent.ViewModels.Workflow;
using IronDev.Agent.ViewModels.Workspaces;
using IronDev.Agent.Views;
using IronDev.Agent.Services;
using IronDev.Client.DependencyInjection;
using IronDev.Client.Traces;
using IronDev.Core.Interfaces;
using IronDev.Agent.Services.Interfaces;
using IronDev.Agent.Services.Mock;
using IronDev.Agent.Services.Testing;

namespace IronDev.Agent;

public partial class App : Application
{
    private readonly IHost _host;
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IronDev",
        "logs");

    public IServiceProvider Services => _host.Services;

    public App()
    {
        ConfigureFileLogging();
        ConfigureWpfTracing();

        // Surface fatal crashes as a readable MessageBox instead of silent ExecutionEngineException
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Fatal(e.Exception, "Unhandled dispatcher exception");
            MessageBox.Show(
                $"Unhandled error:\n\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\n" +
                $"{e.Exception.StackTrace?.Split('\n').FirstOrDefault()}",
                "IronDev — Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true; // keep app alive for diagnostics
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            if (ex != null)
                Log.Fatal(ex, "Fatal AppDomain exception");
            else
                Log.Fatal("Fatal AppDomain exception with non-Exception payload: {Payload}", e.ExceptionObject);

            MessageBox.Show(
                $"Fatal unhandled exception:\n\n{ex?.GetType().Name}: {ex?.Message}",
                "IronDev — Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                services.AddIronDevClient(context.Configuration);

                // Local desktop-only services. These do not own product persistence.
                services.AddSingleton<global::IronDev.Agent.Services.IAppSettingsService, global::IronDev.Agent.Services.AppSettingsService>();
                services.AddSingleton<IScreenshotCaptureService, ScreenshotCaptureService>();
                services.AddSingleton<ITestingCompanionAgent, TestingCompanionAgent>();
                services.AddTransient<global::IronDev.Agent.Services.Interfaces.ILocalIndexingService, global::IronDev.Agent.Services.LocalIndexingService>();
                services.AddSingleton<global::IronDev.Agent.Services.Interfaces.IProjectShellService, global::IronDev.Agent.Services.Mock.MockProjectShellService>();
                services.AddSingleton<global::IronDev.Core.Interfaces.IMarkdownRenderService, ClientMarkdownRenderService>();

                // Workflow ViewModels
                services.AddTransient<LoginViewModel>();
                services.AddTransient<ProjectHubViewModel>();
                services.AddTransient<CreateProjectViewModel>();
                services.AddTransient<ProjectOverviewViewModel>();

                // Workspace ViewModels
                services.AddTransient<ChatWorkspaceViewModel>();
                services.AddTransient<KnowledgeCompilerViewModel>();
                services.AddTransient<DocumentsWorkspaceViewModel>();
                services.AddTransient<TicketsWorkspaceViewModel>();
                services.AddSingleton<TestingCompanionViewModel>();
                services.AddTransient<DecisionsWorkspaceViewModel>();
                services.AddTransient<ImplementationPlansWorkspaceViewModel>();
                services.AddSingleton<LlmConsoleViewModel>();
                services.AddSingleton<SettingsWorkspaceViewModel>();
                services.AddTransient<BuilderWorkspaceViewModel>();
                services.AddTransient<ProjectProfileViewModel>();
                services.AddSingleton<PromptPlaygroundViewModel>();
                services.AddSingleton(sp => new DevToolsWorkspaceViewModel(
                    () => sp.GetRequiredService<LlmConsoleViewModel>(),
                    () => sp.GetRequiredService<TestingCompanionViewModel>(),
                    () => sp.GetRequiredService<PromptPlaygroundViewModel>()));
                services.AddSingleton<RunReportsViewModel>();

                // Shell
                services.AddSingleton<AgentTenantContext>();
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();
        Log.Information("IronDev started. Logs: {LogDirectory}", LogDirectory);

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        using (_host)
            await _host.StopAsync(TimeSpan.FromSeconds(5));

        Log.Information("IronDev stopped");
        Log.CloseAndFlush();

        base.OnExit(e);
    }

    private static void ConfigureFileLogging()
    {
        Directory.CreateDirectory(LogDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(LogDirectory, "irondev-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private static void ConfigureWpfTracing()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);

            const string listenerName = "IronDevWpfBindingTrace";
            var hasListener = false;

            foreach (TraceListener listener in Trace.Listeners)
            {
                if (string.Equals(listener.Name, listenerName, StringComparison.Ordinal))
                {
                    hasListener = true;
                    break;
                }
            }

            if (!hasListener)
            {
                Trace.Listeners.Add(new TextWriterTraceListener(
                    Path.Combine(LogDirectory, "wpf-binding-trace.log"),
                    listenerName));
                Trace.AutoFlush = true;
            }

            PresentationTraceSources.DataBindingSource.Switch.Level =
                SourceLevels.Warning | SourceLevels.Error | SourceLevels.Critical;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to configure WPF binding trace listener");
        }
    }

    private sealed class ClientMarkdownRenderService : IMarkdownRenderService
    {
        public string ToHtml(string markdown) => System.Net.WebUtility.HtmlEncode(markdown ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace("\n", "<br/>");

        public string ToStyledHtmlDocument(string markdown)
        {
            var encoded = ToHtml(markdown);

            return """
                <!doctype html>
                <html>
                <head>
                    <meta charset="utf-8" />
                    <style>
                        body {{ background:#0D1117; color:#EAEBED; font-family:'Segoe UI', sans-serif; line-height:1.55; padding:20px; }}
                        code, pre {{ font-family:Consolas, monospace; }}
                    </style>
                </head>
                <body>
                """
                + encoded +
                """
                </body>
                </html>
                """;
        }
    }
}
