namespace IronDev.Core.Governance;

public sealed record ControlledDryRunProcessRequest(
    string CommandId,
    string WorkingDirectory,
    string Executable,
    IReadOnlyList<string> Arguments,
    int TimeoutSeconds);

public sealed record ControlledDryRunProcessResult(
    string CommandId,
    int ExitCode,
    bool TimedOut,
    string StandardOutput,
    string StandardError);

public interface IControlledDryRunProcessRunner
{
    Task<ControlledDryRunProcessResult> RunAsync(
        ControlledDryRunProcessRequest request,
        CancellationToken cancellationToken = default);
}
