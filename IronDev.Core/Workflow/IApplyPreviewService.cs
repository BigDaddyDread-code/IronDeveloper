namespace IronDev.Core.Workflow;

public interface IApplyPreviewService
{
    Task<ApplyPreviewResponse> GetPreviewAsync(ApplyPreviewRequest request, CancellationToken cancellationToken = default);
}
