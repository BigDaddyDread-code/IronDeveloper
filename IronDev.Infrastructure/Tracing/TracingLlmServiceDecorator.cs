using System.Diagnostics;
using IronDev.Core;
using Microsoft.Extensions.Logging;

namespace IronDev.Infrastructure.Tracing;

public sealed class TracingLlmServiceDecorator : ILLMService
{
    private readonly ILLMService _inner;
    private readonly ILogger<TracingLlmServiceDecorator> _logger;

    public TracingLlmServiceDecorator(
        ILLMService inner,
        ILogger<TracingLlmServiceDecorator> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task<string> GetResponseAsync(string prompt, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var response = await _inner.GetResponseAsync(prompt, ct);

            _logger.LogInformation(
                "LLM service call succeeded in {DurationMs}ms promptLength={PromptLength} responseLength={ResponseLength}",
                sw.ElapsedMilliseconds,
                prompt.Length,
                response.Length);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "LLM service call failed after {DurationMs}ms promptLength={PromptLength}",
                sw.ElapsedMilliseconds,
                prompt.Length);
            throw;
        }
    }
}
