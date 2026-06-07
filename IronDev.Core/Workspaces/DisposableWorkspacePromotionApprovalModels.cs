namespace IronDev.Core.Workspaces;

public sealed record DisposableWorkspacePromotionApprovalRequest
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public required string Decision { get; init; }
    public required string ApprovedBy { get; init; }
    public required string Reason { get; init; }
}

public sealed record DisposableWorkspacePromotionApprovalData
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }

    public required string Decision { get; init; }
    public required string ApprovedBy { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }

    public required string PromotionPackagePath { get; init; }
    public required string PromotionPackageSha256 { get; init; }
    public string? ApprovalEvidencePath { get; init; }

    public required bool AllowsApply { get; init; }
    public required bool RequiresSeparateApplyCommand { get; init; }

    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspacePromotionApprovalResult
{
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public required int ExitCode { get; init; }
    public required DisposableWorkspacePromotionApprovalData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspacePromotionApprovalEnvelope
{
    public required string Status { get; init; }
    public required string Command { get; init; }
    public string? TraceId { get; init; }
    public required string Summary { get; init; }
    public required DisposableWorkspacePromotionApprovalData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface IDisposableWorkspacePromotionApprovalService
{
    Task<DisposableWorkspacePromotionApprovalResult> CreateAsync(
        DisposableWorkspacePromotionApprovalRequest request,
        CancellationToken cancellationToken = default);
}
