using System.Diagnostics;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IronDev.Infrastructure.Tracing;

public sealed class TracingTicketBuildOrchestratorDecorator : ITicketBuildOrchestrator
{
    private readonly ITicketBuildOrchestrator _inner;
    private readonly ILogger<TracingTicketBuildOrchestratorDecorator> _logger;

    public TracingTicketBuildOrchestratorDecorator(
        IronDev.Infrastructure.Builder.TicketBuildOrchestrator inner,
        ILogger<TracingTicketBuildOrchestratorDecorator> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task<TicketBuildPreview> CreateBuildPreviewAsync(
        int projectId,
        long ticketId,
        CancellationToken cancellationToken = default)
    {
        return await TraceAsync(
            "TicketBuild.CreateBuildPreview",
            projectId,
            ticketId,
            async () => await _inner.CreateBuildPreviewAsync(projectId, ticketId, cancellationToken));
    }

    public async Task<TicketBuildResult> ApplyAndBuildAsync(
        TicketBuildApproval approval,
        CancellationToken cancellationToken = default)
    {
        return await TraceAsync(
            "TicketBuild.ApplyAndBuild",
            null,
            approval.TicketId,
            async () => await _inner.ApplyAndBuildAsync(approval, cancellationToken));
    }

    private async Task<T> TraceAsync<T>(
        string operation,
        int? projectId,
        long ticketId,
        Func<Task<T>> action)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await action();
            _logger.LogInformation(
                "{Operation} succeeded in {DurationMs}ms projectId={ProjectId} ticketId={TicketId}",
                operation,
                sw.ElapsedMilliseconds,
                projectId,
                ticketId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "{Operation} failed after {DurationMs}ms projectId={ProjectId} ticketId={TicketId}",
                operation,
                sw.ElapsedMilliseconds,
                projectId,
                ticketId);
            throw;
        }
    }
}
