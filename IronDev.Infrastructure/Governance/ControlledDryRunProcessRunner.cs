using System.Diagnostics;
using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class ControlledDryRunProcessRunner : IControlledDryRunProcessRunner
{
    public async Task<ControlledDryRunProcessResult> RunAsync(
        ControlledDryRunProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.Executable,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var waitTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(request.TimeoutSeconds), cancellationToken);
        var exited = await Task.WhenAny(waitTask, timeoutTask).ConfigureAwait(false) == waitTask;

        if (!exited)
        {
            process.Kill(entireProcessTree: true);
        }
        else
        {
            await waitTask.ConfigureAwait(false);
        }

        return new ControlledDryRunProcessResult(
            request.CommandId,
            exited ? process.ExitCode : -1,
            !exited,
            await outputTask.ConfigureAwait(false),
            await errorTask.ConfigureAwait(false));
    }
}
