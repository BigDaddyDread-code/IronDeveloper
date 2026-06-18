using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace IronDev.Core.SourceApply;

public sealed record SourceSnapshotCapture(SourceSnapshot Snapshot, string DiffText);

public static class SourceSnapshotBuilder
{
    public static async Task<SourceSnapshotCapture> CaptureAsync(string runId, string sourceRepoPath, CancellationToken cancellationToken)
    {
        var head = await GitAsync(sourceRepoPath, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
        var branch = await GitAsync(sourceRepoPath, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken).ConfigureAwait(false);
        var status = await GitAsync(sourceRepoPath, ["status", "--porcelain=v1"], cancellationToken).ConfigureAwait(false);
        var diff = await GitAsync(sourceRepoPath, ["diff", "--binary"], cancellationToken).ConfigureAwait(false);
        var changed = await GitAsync(sourceRepoPath, ["diff", "--name-only"], cancellationToken).ConfigureAwait(false);

        var diffText = diff.ExitCode == 0 ? diff.Stdout : string.Empty;
        var snapshot = new SourceSnapshot
        {
            SourceSnapshotId = $"source_snapshot_{Guid.NewGuid():N}",
            RunId = runId,
            SourceRepoPath = sourceRepoPath,
            HeadCommit = head.ExitCode == 0 ? head.Stdout.Trim() : string.Empty,
            Branch = branch.ExitCode == 0 ? branch.Stdout.Trim() : string.Empty,
            StatusPorcelain = status.ExitCode == 0 ? status.Stdout : string.Empty,
            DiffSha256 = SourceApplyHash.TextSha256(diffText),
            ChangedFiles = changed.ExitCode == 0
                ? changed.Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray()
                : [],
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Boundary = SourceApplyBoundary.None
        };

        return new SourceSnapshotCapture(snapshot, diffText);
    }

    private static async Task<ProcessResult> GitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        var output = new StringBuilder();
        var error = new StringBuilder();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, item) => { if (item.Data is not null) output.AppendLine(item.Data); };
        process.ErrorDataReceived += (_, item) => { if (item.Data is not null) error.AppendLine(item.Data); };

        try
        {
            if (!process.Start())
                return new ProcessResult(-1, string.Empty, "could not start git");
        }
        catch (Win32Exception ex)
        {
            return new ProcessResult(-1, string.Empty, ex.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
