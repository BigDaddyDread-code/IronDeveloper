using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services;

public sealed class DotNetRunnerService : IDotNetBuildService, IDotNetTestService
{
    public async Task<DotNetBuildResult> BuildAsync(string projectOrSolutionPath, CancellationToken cancellationToken = default)
    {
        var result = new DotNetBuildResult { StartedUtc = DateTime.UtcNow };
        
        var command = $"dotnet build \"{projectOrSolutionPath}\" --no-incremental -v quiet";
        var workingDir = Path.GetDirectoryName(Path.GetFullPath(projectOrSolutionPath)) ?? string.Empty;
        
        result.Command = command;
        result.WorkingDirectory = workingDir;

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectOrSolutionPath}\" --no-incremental -v quiet",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet build process.");
            
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            result.ExitCode = process.ExitCode;
            result.Succeeded = process.ExitCode == 0;
            result.StandardOutput = await stdoutTask;
            result.StandardError = await stderrTask;
        }
        catch (Exception ex)
        {
            result.Succeeded = false;
            result.StandardError = ex.Message;
        }
        finally
        {
            result.FinishedUtc = DateTime.UtcNow;
        }

        return result;
    }

    public async Task<DotNetTestResult> TestAsync(string projectOrSolutionPath, CancellationToken cancellationToken = default)
    {
        var result = new DotNetTestResult { StartedUtc = DateTime.UtcNow };
        
        var command = $"dotnet test \"{projectOrSolutionPath}\" --logger \"console;verbosity=minimal\"";
        var workingDir = Path.GetDirectoryName(Path.GetFullPath(projectOrSolutionPath)) ?? string.Empty;

        result.Command = command;
        result.WorkingDirectory = workingDir;

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"test \"{projectOrSolutionPath}\" --logger \"console;verbosity=minimal\"",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet test process.");
            
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            result.ExitCode = process.ExitCode;
            result.Succeeded = process.ExitCode == 0;
            result.StandardOutput = await stdoutTask;
            result.StandardError = await stderrTask;
        }
        catch (Exception ex)
        {
            result.Succeeded = false;
            result.StandardError = ex.Message;
        }
        finally
        {
            result.FinishedUtc = DateTime.UtcNow;
        }

        return result;
    }
}
