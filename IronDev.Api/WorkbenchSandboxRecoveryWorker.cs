using IronDev.Infrastructure.Services.Sandbox;

namespace IronDev.Api;

/// <summary>
/// Runs a bounded pass at startup and repeats it so transient runtime/configuration
/// failures never strand exact owned resources. The recovery service uses the same
/// project SQL application lock as foreground qualification before touching anything.
/// </summary>
public sealed class WorkbenchSandboxRecoveryWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<WorkbenchSandboxRecoveryWorker> logger) : BackgroundService
{
    private readonly int _batchSize = Math.Clamp(
        configuration.GetValue("WorkbenchProductionSandbox:RecoveryBatchSize", 32),
        1,
        256);
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(Math.Clamp(
        configuration.GetValue("WorkbenchProductionSandbox:RecoveryIntervalSeconds", 60),
        15,
        3_600));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var recovery = scope.ServiceProvider.GetRequiredService<IWorkbenchSandboxRecoveryService>();
                var summary = await recovery.RecoverStaleAttemptsAsync(_batchSize, stoppingToken)
                    .ConfigureAwait(false);
                if (summary.AttemptsRecovered > 0 || summary.AttemptsMaterialized > 0 ||
                    summary.RecoveryFailures > 0)
                {
                    logger.LogInformation(
                        "Workbench sandbox recovery read {Candidates}; recovered {Recovered}, materialized {Materialized}, skipped active {Active}, retryable failures {Failures}.",
                        summary.CandidatesRead,
                        summary.AttemptsRecovered,
                        summary.AttemptsMaterialized,
                        summary.ActiveAttemptsSkipped,
                        summary.RecoveryFailures);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The bounded Workbench sandbox recovery pass will be retried.");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
