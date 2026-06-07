using System.Security.Cryptography;
using System.Text.Json;

using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Workspaces;

public sealed class DisposableWorkspacePromotionApprovalService : IDisposableWorkspacePromotionApprovalService
{
    private static readonly JsonSerializerOptions ApprovalJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<DisposableWorkspacePromotionApprovalResult> CreateAsync(
        DisposableWorkspacePromotionApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        var workspacePath = NormalizePath(request.WorkspacePath);
        var errors = new List<string>();
        var warnings = new List<string>();
        var createdUtc = DateTimeOffset.UtcNow;
        var workspaceMetadataPath = Path.Combine(workspacePath, ".irondev", "workspace.json");
        var promotionPackagePath = Path.Combine(workspacePath, ".irondev", "runs", request.RunId, "promotion-package.json");
        var approvalEvidencePath = Path.Combine(workspacePath, ".irondev", "runs", request.RunId, "promotion-approval.json");

        if (!IsAllowedDecision(request.Decision))
            errors.Add("Promotion approval decision must be exactly 'approved' or 'rejected'.");
        if (string.IsNullOrWhiteSpace(request.ApprovedBy))
            errors.Add("Promotion approval requires --approved-by <name-or-id>.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            errors.Add("Promotion approval requires --reason <text>.");
        if (errors.Count > 0)
            return Blocked(request, workspacePath, promotionPackagePath, createdUtc, string.Empty, errors, warnings);

        if (!Directory.Exists(workspacePath))
        {
            errors.Add($"Workspace path does not exist: {workspacePath}");
            return Failed(request, workspacePath, promotionPackagePath, createdUtc, string.Empty, errors, warnings);
        }

        if (!File.Exists(workspaceMetadataPath))
        {
            errors.Add("Workspace preparation metadata was not found. Run 'irondev workspace prepare' before creating approval evidence.");
            return Blocked(request, workspacePath, promotionPackagePath, createdUtc, string.Empty, errors, warnings);
        }

        if (!File.Exists(promotionPackagePath))
        {
            errors.Add("Workspace promotion package was not found. Run 'irondev workspace promotion-package' before creating approval evidence.");
            return Blocked(request, workspacePath, promotionPackagePath, createdUtc, string.Empty, errors, warnings);
        }

        WorkspaceMetadata? workspaceMetadata;
        PromotionPackageMetadata? promotionPackage;
        try
        {
            workspaceMetadata = await ReadJsonAsync<WorkspaceMetadata>(workspaceMetadataPath, cancellationToken).ConfigureAwait(false);
            promotionPackage = await ReadJsonAsync<PromotionPackageMetadata>(promotionPackagePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            errors.Add($"Promotion approval evidence inputs could not be read: {ex.Message}");
            return Failed(request, workspacePath, promotionPackagePath, createdUtc, string.Empty, errors, warnings);
        }

        if (workspaceMetadata is null)
        {
            errors.Add("Workspace preparation metadata was empty.");
            return Blocked(request, workspacePath, promotionPackagePath, createdUtc, string.Empty, errors, warnings);
        }

        if (promotionPackage is null)
        {
            errors.Add("Workspace promotion package was empty.");
            return Blocked(request, workspacePath, promotionPackagePath, createdUtc, string.Empty, errors, warnings);
        }

        if (string.IsNullOrWhiteSpace(workspaceMetadata.RunId))
            errors.Add("Workspace metadata is missing runId.");
        else if (!string.Equals(workspaceMetadata.RunId, request.RunId, StringComparison.Ordinal))
            errors.Add($"Workspace runId mismatch. Metadata runId '{workspaceMetadata.RunId}' does not match requested runId '{request.RunId}'.");

        if (string.IsNullOrWhiteSpace(workspaceMetadata.SourceRepo))
            errors.Add("Workspace metadata is missing sourceRepo.");
        if (string.IsNullOrWhiteSpace(workspaceMetadata.WorkspacePath))
            errors.Add("Workspace metadata is missing workspacePath.");
        if (errors.Count > 0)
            return Blocked(request, workspacePath, promotionPackagePath, createdUtc, string.Empty, errors, warnings);

        var sourceRepo = NormalizePath(workspaceMetadata.SourceRepo!);
        var metadataWorkspacePath = NormalizePath(workspaceMetadata.WorkspacePath!);
        if (!PathsEqual(workspacePath, metadataWorkspacePath))
            errors.Add("Workspace path does not match the prepared workspace metadata.");
        if (!Directory.Exists(sourceRepo))
            errors.Add($"Source repository from workspace metadata does not exist: {sourceRepo}");
        if (PathContainsSegment(workspacePath, ".git") ||
            PathsEqual(workspacePath, sourceRepo) ||
            IsSameOrInside(sourceRepo, workspacePath) ||
            IsSameOrInside(workspacePath, sourceRepo))
            errors.Add("Workspace path and source repository must be isolated from each other.");
        if (errors.Count > 0)
            return Blocked(request, workspacePath, promotionPackagePath, createdUtc, string.Empty, errors, warnings);

        if (!string.Equals(promotionPackage.RunId, request.RunId, StringComparison.Ordinal))
            errors.Add("Promotion package runId does not match the requested runId.");
        if (string.IsNullOrWhiteSpace(promotionPackage.WorkspacePath) ||
            !PathsEqual(promotionPackage.WorkspacePath, workspacePath))
            errors.Add("Promotion package workspacePath does not match the prepared workspace.");
        if (promotionPackage.PromotionPackagePath is not null &&
            !PathsEqual(promotionPackage.PromotionPackagePath, promotionPackagePath))
            errors.Add("Promotion package path does not match the expected promotion package path.");
        if (promotionPackage.Approval is null)
            errors.Add("Promotion package approval section is missing.");
        else
        {
            if (!promotionPackage.Approval.RequiresHumanApproval)
                errors.Add("Promotion package is unsafe because it does not require human approval.");
            if (promotionPackage.Approval.CanApplyToSourceRepo)
                errors.Add("Promotion package is unsafe because it claims source repo apply capability.");
            if (promotionPackage.Approval.AutoPromotionAllowed)
                errors.Add("Promotion package is unsafe because it claims auto apply or auto promotion capability.");
        }
        if (errors.Count > 0)
            return Blocked(request, workspacePath, promotionPackagePath, createdUtc, string.Empty, errors, warnings);

        string promotionPackageSha256;
        try
        {
            promotionPackageSha256 = await ComputeSha256Async(promotionPackagePath, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(approvalEvidencePath)!);

            if (File.Exists(approvalEvidencePath))
            {
                errors.Add("Promotion approval evidence already exists for this run. Approval evidence is immutable.");
                return Blocked(request, workspacePath, promotionPackagePath, createdUtc, promotionPackageSha256, errors, warnings);
            }

            var allowsApply = false;
            var requiresSeparateApplyCommand = string.Equals(request.Decision, "approved", StringComparison.OrdinalIgnoreCase);
            var data = new DisposableWorkspacePromotionApprovalData
            {
                RunId = request.RunId,
                WorkspacePath = workspacePath,
                Decision = request.Decision.Trim().ToLowerInvariant(),
                ApprovedBy = request.ApprovedBy.Trim(),
                Reason = request.Reason.Trim(),
                CreatedUtc = createdUtc,
                PromotionPackagePath = promotionPackagePath,
                PromotionPackageSha256 = promotionPackageSha256,
                ApprovalEvidencePath = approvalEvidencePath,
                AllowsApply = allowsApply,
                RequiresSeparateApplyCommand = requiresSeparateApplyCommand,
                EvidencePaths = [promotionPackagePath, approvalEvidencePath],
                Errors = errors,
                Warnings = warnings
            };

            await File.WriteAllTextAsync(
                approvalEvidencePath,
                JsonSerializer.Serialize(
                    new ApprovalEvidenceMetadata
                    {
                        RunId = data.RunId,
                        WorkspacePath = data.WorkspacePath,
                        Decision = data.Decision,
                        ApprovedBy = data.ApprovedBy,
                        Reason = data.Reason,
                        CreatedUtc = data.CreatedUtc,
                        PromotionPackagePath = data.PromotionPackagePath,
                        PromotionPackageSha256 = data.PromotionPackageSha256,
                        AllowsApply = data.AllowsApply,
                        RequiresSeparateApplyCommand = data.RequiresSeparateApplyCommand
                    },
                    ApprovalJsonOptions),
                cancellationToken).ConfigureAwait(false);

            return new DisposableWorkspacePromotionApprovalResult
            {
                Status = "succeeded",
                Summary = "Workspace promotion approval evidence created.",
                ExitCode = 0,
                Data = data,
                Errors = errors,
                Warnings = warnings
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errors.Add($"Workspace promotion approval evidence could not be written: {ex.Message}");
            return Failed(request, workspacePath, promotionPackagePath, createdUtc, string.Empty, errors, warnings);
        }
    }

    private static bool IsAllowedDecision(string decision) =>
        string.Equals(decision, "approved", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(decision, "rejected", StringComparison.OrdinalIgnoreCase);

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, ApprovalJsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static DisposableWorkspacePromotionApprovalResult Blocked(
        DisposableWorkspacePromotionApprovalRequest request,
        string workspacePath,
        string promotionPackagePath,
        DateTimeOffset createdUtc,
        string promotionPackageSha256,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Status = "blocked",
            Summary = "Workspace promotion approval evidence was blocked.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, promotionPackagePath, createdUtc, promotionPackageSha256, approvalEvidencePath: null, errors, warnings),
            Errors = errors,
            Warnings = warnings
        };

    private static DisposableWorkspacePromotionApprovalResult Failed(
        DisposableWorkspacePromotionApprovalRequest request,
        string workspacePath,
        string promotionPackagePath,
        DateTimeOffset createdUtc,
        string promotionPackageSha256,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Status = "failed",
            Summary = "Workspace promotion approval evidence failed.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, promotionPackagePath, createdUtc, promotionPackageSha256, approvalEvidencePath: null, errors, warnings),
            Errors = errors,
            Warnings = warnings
        };

    private static DisposableWorkspacePromotionApprovalData BuildData(
        DisposableWorkspacePromotionApprovalRequest request,
        string workspacePath,
        string promotionPackagePath,
        DateTimeOffset createdUtc,
        string promotionPackageSha256,
        string? approvalEvidencePath,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings) =>
        new()
        {
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            Decision = request.Decision,
            ApprovedBy = request.ApprovedBy,
            Reason = request.Reason,
            CreatedUtc = createdUtc,
            PromotionPackagePath = promotionPackagePath,
            PromotionPackageSha256 = promotionPackageSha256,
            ApprovalEvidencePath = approvalEvidencePath,
            AllowsApply = false,
            RequiresSeparateApplyCommand = false,
            EvidencePaths = [],
            Errors = errors,
            Warnings = warnings
        };

    private static string NormalizePath(string path) => Path.GetFullPath(path.Trim());

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            TrimEndingDirectorySeparator(NormalizePath(left)),
            TrimEndingDirectorySeparator(NormalizePath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrInside(string parent, string candidate)
    {
        var normalizedParent = TrimEndingDirectorySeparator(NormalizePath(parent));
        var normalizedCandidate = TrimEndingDirectorySeparator(NormalizePath(candidate));

        return string.Equals(normalizedCandidate, normalizedParent, StringComparison.OrdinalIgnoreCase) ||
            normalizedCandidate.StartsWith(
                normalizedParent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            normalizedCandidate.StartsWith(
                normalizedParent + Path.AltDirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathContainsSegment(string path, string segment)
    {
        return NormalizePath(path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(part => string.Equals(part, segment, StringComparison.OrdinalIgnoreCase));
    }

    private static string TrimEndingDirectorySeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed record WorkspaceMetadata
    {
        public string? RunId { get; init; }
        public string? SourceRepo { get; init; }
        public string? WorkspacePath { get; init; }
    }

    private sealed record PromotionPackageMetadata
    {
        public string? RunId { get; init; }
        public string? WorkspacePath { get; init; }
        public string? PromotionPackagePath { get; init; }
        public PromotionApprovalMetadata? Approval { get; init; }
    }

    private sealed record PromotionApprovalMetadata
    {
        public bool RequiresHumanApproval { get; init; }
        public bool CanApplyToSourceRepo { get; init; }
        public bool AutoPromotionAllowed { get; init; }
    }

    private sealed record ApprovalEvidenceMetadata
    {
        public required string RunId { get; init; }
        public required string WorkspacePath { get; init; }
        public required string Decision { get; init; }
        public required string ApprovedBy { get; init; }
        public required string Reason { get; init; }
        public required DateTimeOffset CreatedUtc { get; init; }
        public required string PromotionPackagePath { get; init; }
        public required string PromotionPackageSha256 { get; init; }
        public required bool AllowsApply { get; init; }
        public required bool RequiresSeparateApplyCommand { get; init; }
    }
}
