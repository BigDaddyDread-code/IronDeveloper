using IronDev.Core.Workbench;

namespace IronDev.Api;

public sealed class WorkbenchAgentRunOutboxWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(5);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WorkbenchAgentRunOutboxWorker> _logger;
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public WorkbenchAgentRunOutboxWorker(
        IServiceScopeFactory scopes,
        ILogger<WorkbenchAgentRunOutboxWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var outbox = scope.ServiceProvider.GetRequiredService<IWorkbenchAgentRunOutbox>();
                var processor = scope.ServiceProvider.GetRequiredService<IWorkbenchAgentRunProcessor>();
                var items = await outbox.ReadPendingAsync(10, stoppingToken);
                if (items.Count == 0)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                    continue;
                }

                foreach (var item in items)
                    await processor.ProcessAsync(item, _workerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Workbench agent-run worker iteration failed.");
                await Task.Delay(FailureDelay, stoppingToken);
            }
        }
    }
}
