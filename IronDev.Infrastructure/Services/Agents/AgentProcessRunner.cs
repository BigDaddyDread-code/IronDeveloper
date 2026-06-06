using System;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Agents;

namespace IronDev.Infrastructure.Services.Agents;

public sealed record AgentProcessRunResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut,
    string Command);

public interface IAgentProcessRunner
{
    Task<AgentProcessRunResult> RunAsync(
        string fileName,
        string[] arguments,
        string workingDirectory,
        CancellationToken ct = default);
}

public sealed class AgentProcessRunner : IAgentProcessRunner
{
    private readonly IGovernedAgentProcessExecutor _executor;

    public AgentProcessRunner(IGovernedAgentProcessExecutor? executor = null)
    {
        _executor = executor ?? new GovernedAgentProcessExecutor();
    }

    public async Task<AgentProcessRunResult> RunAsync(
        string fileName,
        string[] arguments,
        string workingDirectory,
        CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync(new GovernedAgentProcessRequest
        {
            ToolCallId = Guid.NewGuid().ToString("N"),
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory
        }, ct);

        return new AgentProcessRunResult(result.ExitCode, result.Stdout, result.Stderr, result.TimedOut, result.Command);
    }
}
