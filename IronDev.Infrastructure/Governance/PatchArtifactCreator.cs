using System.Diagnostics.CodeAnalysis;
using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class PatchArtifactCreator : IPatchArtifactCreator
{
    private readonly IControlledDryRunReceiptStore _receiptStore;
    private readonly IPatchArtifactStore _patchArtifactStore;
    private readonly TimeProvider _timeProvider;
    private readonly Func<Guid> _idProvider;

    public PatchArtifactCreator(
        IControlledDryRunReceiptStore receiptStore,
        IPatchArtifactStore patchArtifactStore,
        TimeProvider? timeProvider = null,
        Func<Guid>? idProvider = null)
    {
        _receiptStore = receiptStore ?? throw new ArgumentNullException(nameof(receiptStore));
        _patchArtifactStore = patchArtifactStore ?? throw new ArgumentNullException(nameof(patchArtifactStore));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _idProvider = idProvider ?? Guid.NewGuid;
    }


    private async Task<PatchArtifact> BuildValidatedArtifactAsync(
        PatchArtifactCreationRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfIssues(PatchArtifactCreationValidation.ValidateRequest(request));

        var audit = await _receiptStore.GetAsync(
            request.ProjectId,
            request.DryRunExecutionAuditId,
            cancellationToken).ConfigureAwait(false);

        if (audit is null)
        {
            Throw("DRY_RUN_RECEIPT_NOT_FOUND", nameof(request.DryRunExecutionAuditId), "Dry-run receipt was not found.");
        }

        var auditValidation = ControlledDryRunExecutionAuditValidation.Validate(audit);
        if (!auditValidation.IsValid)
        {
            Throw("DRY_RUN_AUDIT_INVALID", "audit", $"Dry-run audit is invalid: {string.Join(", ", auditValidation.Issues.Select(issue => issue.Code))}.");
        }

        if (audit.ProjectId != request.ProjectId)
        {
            Throw("PROJECT_ID_MISMATCH", nameof(request.ProjectId), "Dry-run audit project ID does not match the request project ID.");
        }

        if (!Same(audit.AuditHash, request.DryRunAuditHash))
        {
            Throw("DRY_RUN_AUDIT_HASH_MISMATCH", nameof(request.DryRunAuditHash), "Dry-run audit hash does not match the loaded audit.");
        }

        if (!audit.DryRunCompleted)
        {
            Throw("DRY_RUN_NOT_COMPLETED", nameof(audit.DryRunCompleted), "Dry-run audit must be completed before patch artifact creation.");
        }

        if (!audit.DryRunSucceeded)
        {
            Throw("DRY_RUN_NOT_SUCCESSFUL", nameof(audit.DryRunSucceeded), "Dry-run audit must be successful before patch artifact creation.");
        }

        var fileChanges = request.FileChanges.Select(NormalizeFileChange).ToArray();
        var evidenceReferences = DistinctNonBlank(
            request.EvidenceReferences,
            [
                $"controlled-dry-run-receipt:{audit.DryRunExecutionAuditId:D}",
                $"controlled-dry-run-audit:{audit.AuditHash.Trim()}",
                $"patch-artifact-created-from-dry-run:{audit.DryRunExecutionAuditId:D}"
            ]);
        var boundaryMaxims = DistinctNonBlank(
            request.BoundaryMaxims,
            PatchArtifactCreationBoundaryText.CreationBoundaryMaxims);

        var changeSetHash = PatchArtifactHashing.ComputeChangeSetHash(fileChanges);
        var artifactWithoutPatchHash = new PatchArtifact
        {
            PatchArtifactId = _idProvider(),
            ProjectId = request.ProjectId,
            PatchArtifactKind = request.PatchArtifactKind.Trim(),
            ControlledDryRunRequestId = audit.ControlledDryRunRequestId,
            DryRunExecutionAuditId = audit.DryRunExecutionAuditId,
            DryRunAuditHash = audit.AuditHash.Trim(),
            DryRunReceiptHash = request.DryRunReceiptHash.Trim(),
            PolicySatisfactionId = audit.PolicySatisfactionId,
            PolicySatisfactionHash = audit.PolicySatisfactionHash.Trim(),
            SubjectKind = audit.SubjectKind.Trim(),
            SubjectId = audit.SubjectId.Trim(),
            SubjectHash = audit.SubjectHash.Trim(),
            SourceSnapshotReference = audit.SourceSnapshotReference.Trim(),
            SourceBaselineHash = request.SourceBaselineHash.Trim(),
            WorkspaceBoundaryHash = audit.WorkspaceBoundaryHash.Trim(),
            ValidationPlanId = audit.ValidationPlanId.Trim(),
            ValidationPlanHash = audit.ValidationPlanHash.Trim(),
            PatchHash = "sha256:pending",
            ChangeSetHash = changeSetHash,
            FileChanges = fileChanges,
            CreatedAtUtc = _timeProvider.GetUtcNow(),
            ExpiresAtUtc = null,
            EvidenceReferences = evidenceReferences,
            BoundaryMaxims = boundaryMaxims,
            Boundary = PatchArtifactBoundaryText.Boundary
        };

        var patchHash = PatchArtifactHashing.ComputePatchHash(artifactWithoutPatchHash, changeSetHash);
        var artifact = artifactWithoutPatchHash with { PatchHash = patchHash };

        var artifactValidation = PatchArtifactValidation.Validate(artifact);
        if (!artifactValidation.IsValid)
        {
            Throw("PATCH_ARTIFACT_INVALID", nameof(PatchArtifact), $"Created patch artifact is invalid: {string.Join(", ", artifactValidation.Issues.Select(issue => issue.Code))}.");
        }

        var baseHashValidation = PatchBaseHashValidation.Validate(ToBaseHashContext(artifact, audit, request));
        if (!baseHashValidation.IsValid)
        {
            Throw("PATCH_BASE_HASH_VALIDATION_FAILED", nameof(PatchBaseHashValidation), $"Patch base/hash validation failed: {string.Join(", ", baseHashValidation.Issues.Select(issue => issue.Code))}.");
        }

        return artifact;
    }

    public async Task<PatchArtifactCreationResult> CreateAsync(
        PatchArtifactCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        var artifact = await BuildValidatedArtifactAsync(request, cancellationToken).ConfigureAwait(false);
        return ToResult(artifact, stored: false);
    }

    public async Task<PatchArtifactCreationResult> CreateAndStoreAsync(
        PatchArtifactCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        var artifact = await BuildValidatedArtifactAsync(request, cancellationToken).ConfigureAwait(false);
        await _patchArtifactStore.SaveAsync(artifact, cancellationToken).ConfigureAwait(false);
        return ToResult(artifact, stored: true);
    }
    private static PatchBaseHashValidationContext ToBaseHashContext(
        PatchArtifact artifact,
        ControlledDryRunExecutionAudit audit,
        PatchArtifactCreationRequest request) =>
        new()
        {
            PatchArtifact = artifact,
            ProjectId = request.ProjectId,
            ControlledDryRunRequestId = audit.ControlledDryRunRequestId,
            DryRunExecutionAuditId = audit.DryRunExecutionAuditId,
            DryRunAuditHash = audit.AuditHash.Trim(),
            DryRunReceiptHash = request.DryRunReceiptHash.Trim(),
            PolicySatisfactionId = audit.PolicySatisfactionId,
            PolicySatisfactionHash = audit.PolicySatisfactionHash.Trim(),
            SubjectKind = audit.SubjectKind.Trim(),
            SubjectId = audit.SubjectId.Trim(),
            SubjectHash = audit.SubjectHash.Trim(),
            SourceSnapshotReference = audit.SourceSnapshotReference.Trim(),
            SourceBaselineHash = request.SourceBaselineHash.Trim(),
            WorkspaceBoundaryHash = audit.WorkspaceBoundaryHash.Trim(),
            ValidationPlanId = audit.ValidationPlanId.Trim(),
            ValidationPlanHash = audit.ValidationPlanHash.Trim(),
            EvidenceReferences = artifact.EvidenceReferences,
            BoundaryMaxims = artifact.BoundaryMaxims
        };

    private static PatchArtifactCreationResult ToResult(PatchArtifact artifact, bool stored) =>
        new()
        {
            PatchArtifactId = artifact.PatchArtifactId,
            ProjectId = artifact.ProjectId,
            DryRunExecutionAuditId = artifact.DryRunExecutionAuditId,
            DryRunAuditHash = artifact.DryRunAuditHash,
            DryRunReceiptHash = artifact.DryRunReceiptHash,
            PatchHash = artifact.PatchHash,
            ChangeSetHash = artifact.ChangeSetHash,
            Stored = stored,
            PatchArtifact = artifact,
            Boundary = PatchArtifactCreationBoundaryText.Boundary,
            Warnings = PatchArtifactCreationBoundaryText.Warnings
        };

    private static PatchArtifactFileChange NormalizeFileChange(PatchArtifactFileChange change) =>
        new()
        {
            Path = change.Path.Trim(),
            PreviousPath = string.IsNullOrWhiteSpace(change.PreviousPath) ? null : change.PreviousPath.Trim(),
            ChangeKind = change.ChangeKind.Trim(),
            BeforeContentHash = string.IsNullOrWhiteSpace(change.BeforeContentHash) ? null : change.BeforeContentHash.Trim(),
            AfterContentHash = string.IsNullOrWhiteSpace(change.AfterContentHash) ? null : change.AfterContentHash.Trim(),
            DiffHash = change.DiffHash.Trim(),
            NormalizedDiff = change.NormalizedDiff.Trim(),
            IsBinary = change.IsBinary
        };

    private static IReadOnlyList<string> DistinctNonBlank(params IEnumerable<string>[] groups) =>
        groups
            .SelectMany(group => group)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);

    [DoesNotReturn]
    private static void Throw(string code, string field, string message) =>
        throw new PatchArtifactCreationException([new PatchArtifactCreationIssue(code, field, message)]);

    private static void ThrowIfIssues(IReadOnlyList<PatchArtifactCreationIssue> issues)
    {
        if (issues.Count > 0)
        {
            throw new PatchArtifactCreationException(issues);
        }
    }
}


