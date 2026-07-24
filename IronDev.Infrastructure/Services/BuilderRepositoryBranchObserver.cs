using IronDev.Core.Workbench;

namespace IronDev.Infrastructure.Services;

public sealed class BuilderRepositoryBranchObserver(
    IRepositoryProvisioningGitRunner git) : IBuilderRepositoryBranchObserver
{
    public async Task<BuilderRepositoryBranchObservation> ObserveAsync(
        string canonicalRepositoryPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(canonicalRepositoryPath) ||
            !Path.IsPathFullyQualified(canonicalRepositoryPath) ||
            !Directory.Exists(canonicalRepositoryPath))
            throw new BuilderAuthorizationNotAllowedException(
                BuilderAuthorizationReasonCodes.RepositoryRevisionChanged,
                "The server-owned repository path is unavailable.");

        var branch = await RunAsync(
            canonicalRepositoryPath,
            ["symbolic-ref", "--quiet", "--short", "HEAD"],
            cancellationToken).ConfigureAwait(false);
        var head = await RunAsync(
            canonicalRepositoryPath,
            ["rev-parse", "--verify", "HEAD"],
            cancellationToken).ConfigureAwait(false);
        var branchName = OneLine(branch.StandardOutput);
        var headCommit = OneLine(head.StandardOutput).ToLowerInvariant();
        if (branchName.Length is 0 or > BuilderWorkPackageCoreContract.MaximumBranchNameLength ||
            headCommit.Length is not 40 and not 64 ||
            headCommit.Any(static value => value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new BuilderAuthorizationNotAllowedException(
                BuilderAuthorizationReasonCodes.RepositoryBranchChanged,
                "The repository must be attached to one valid named branch.");

        return new BuilderRepositoryBranchObservation(branchName, headCommit);
    }

    private async Task<RepositoryProvisioningGitResult> RunAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        RepositoryProvisioningGitResult result;
        try
        {
            result = await git.RunAsync(
                repositoryPath,
                arguments,
                DateTime.UnixEpoch,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            throw Unavailable();
        }

        if (result.ExitCode != 0)
            throw Unavailable();
        return result;
    }

    private static string OneLine(string value)
    {
        var lines = (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 1 ? lines[0] : string.Empty;
    }

    private static BuilderAuthorizationNotAllowedException Unavailable() => new(
        BuilderAuthorizationReasonCodes.RepositoryBranchChanged,
        "The current repository branch could not be observed through the controlled Git runner.");
}
