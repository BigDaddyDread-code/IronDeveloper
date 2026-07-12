using IronDev.Core.Board;
using IronDev.Core.Governance;
using IronDev.Core.WorkItems;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectGovernanceOverviewService : IProjectGovernanceOverviewService
{
    private readonly IProjectBoardReadService _board;
    private readonly IProjectWorkItemReadService _workItems;
    private readonly IConfiguration _configuration;

    public ProjectGovernanceOverviewService(
        IProjectBoardReadService board,
        IProjectWorkItemReadService workItems,
        IConfiguration configuration)
    {
        _board = board;
        _workItems = workItems;
        _configuration = configuration;
    }

    public async Task<ProjectGovernanceOverview?> GetAsync(
        int projectId,
        int currentUserId,
        CancellationToken cancellationToken = default)
    {
        var board = await _board.GetAsync(projectId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (board is null)
            return null;

        var reads = board.Items.Select(item => ReadWorkItemAsync(projectId, item.WorkItemId, currentUserId, cancellationToken));
        var results = await Task.WhenAll(reads).ConfigureAwait(false);
        var workItems = results.Where(result => result.Model is not null).Select(result => result.Model!).ToArray();
        var issues = results
            .Where(result => result.Issue is not null)
            .Select(result => result.Issue!)
            .ToArray();

        return ProjectGovernanceOverviewProjector.Build(
            board,
            workItems,
            ReadSoloApprovalExceptionAllowed(),
            issues,
            DateTimeOffset.UtcNow);
    }

    private async Task<WorkItemReadResult> ReadWorkItemAsync(
        int projectId,
        long workItemId,
        int currentUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var model = await _workItems.GetAsync(projectId, workItemId, currentUserId, cancellationToken).ConfigureAwait(false);
            return model is null
                ? new WorkItemReadResult(null, Issue(workItemId, "The Work Item evidence projection was not found."))
                : new WorkItemReadResult(model, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new WorkItemReadResult(null, Issue(workItemId, "The Work Item evidence projection could not be evaluated."));
        }
    }

    private bool ReadSoloApprovalExceptionAllowed() =>
        string.Equals(_configuration["SkeletonAuthority:AllowSoloApproval"], "true", StringComparison.OrdinalIgnoreCase);

    private static ProjectGovernanceSectionIssue Issue(long workItemId, string summary) => new()
    {
        Section = $"WorkItem:{workItemId}",
        Summary = summary,
        Retryable = true
    };

    private sealed record WorkItemReadResult(
        ProjectWorkItemReadModel? Model,
        ProjectGovernanceSectionIssue? Issue);
}
