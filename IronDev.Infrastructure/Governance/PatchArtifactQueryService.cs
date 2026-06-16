using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class PatchArtifactQueryService : IPatchArtifactQueryService
{
    private const string RedactedUnsafeText = "[redacted: sensitive patch artifact text]";

    private static readonly string[] PrivateMaterialMarkers =
    [
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "private reasoning",
        "hidden reasoning",
        "scratchpad",
        "system prompt",
        "developer prompt",
        "password",
        "api_key",
        "apikey",
        "secret",
        "private key",
        "bearer"
    ];

    private readonly IPatchArtifactStore _store;

    public PatchArtifactQueryService(IPatchArtifactStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<PatchArtifactReadModel?> GetAsync(Guid projectId, Guid patchArtifactId, CancellationToken cancellationToken = default)
    {
        var artifact = await _store.GetAsync(projectId, patchArtifactId, cancellationToken);
        return artifact is null ? null : ToReadModel(artifact);
    }

    public async Task<IReadOnlyList<PatchArtifactReadModel>> ListByDryRunReceiptHashAsync(Guid projectId, string dryRunReceiptHash, CancellationToken cancellationToken = default)
    {
        var artifacts = await _store.ListByDryRunReceiptHashAsync(projectId, dryRunReceiptHash, cancellationToken);
        return artifacts.Select(ToReadModel).ToArray();
    }

    public async Task<IReadOnlyList<PatchArtifactReadModel>> ListByDryRunAuditHashAsync(Guid projectId, string dryRunAuditHash, CancellationToken cancellationToken = default)
    {
        var artifacts = await _store.ListByDryRunAuditHashAsync(projectId, dryRunAuditHash, cancellationToken);
        return artifacts.Select(ToReadModel).ToArray();
    }

    public async Task<IReadOnlyList<PatchArtifactReadModel>> ListByControlledDryRunRequestAsync(Guid projectId, Guid controlledDryRunRequestId, CancellationToken cancellationToken = default)
    {
        var artifacts = await _store.ListByControlledDryRunRequestAsync(projectId, controlledDryRunRequestId, cancellationToken);
        return artifacts.Select(ToReadModel).ToArray();
    }

    public async Task<IReadOnlyList<PatchArtifactReadModel>> ListBySubjectAsync(Guid projectId, string subjectKind, string subjectId, CancellationToken cancellationToken = default)
    {
        var artifacts = await _store.ListBySubjectAsync(projectId, subjectKind, subjectId, cancellationToken);
        return artifacts.Select(ToReadModel).ToArray();
    }

    public async Task<IReadOnlyList<PatchArtifactReadModel>> ListByPatchHashAsync(Guid projectId, string patchHash, CancellationToken cancellationToken = default)
    {
        var artifacts = await _store.ListByPatchHashAsync(projectId, patchHash, cancellationToken);
        return artifacts.Select(ToReadModel).ToArray();
    }

    public async Task<IReadOnlyList<PatchArtifactReadModel>> ListBySourceBaselineHashAsync(Guid projectId, string sourceBaselineHash, CancellationToken cancellationToken = default)
    {
        var artifacts = await _store.ListBySourceBaselineHashAsync(projectId, sourceBaselineHash, cancellationToken);
        return artifacts.Select(ToReadModel).ToArray();
    }

    private static PatchArtifactReadModel ToReadModel(PatchArtifact artifact) => new()
    {
        PatchArtifactId = artifact.PatchArtifactId,
        ProjectId = artifact.ProjectId,
        PatchArtifactKind = SafeText(artifact.PatchArtifactKind),
        ControlledDryRunRequestId = artifact.ControlledDryRunRequestId,
        DryRunExecutionAuditId = artifact.DryRunExecutionAuditId,
        DryRunAuditHash = SafeText(artifact.DryRunAuditHash),
        DryRunReceiptHash = SafeText(artifact.DryRunReceiptHash),
        PolicySatisfactionId = artifact.PolicySatisfactionId,
        PolicySatisfactionHash = SafeText(artifact.PolicySatisfactionHash),
        SubjectKind = SafeText(artifact.SubjectKind),
        SubjectId = SafeText(artifact.SubjectId),
        SubjectHash = SafeText(artifact.SubjectHash),
        SourceSnapshotReference = SafeText(artifact.SourceSnapshotReference),
        SourceBaselineHash = SafeText(artifact.SourceBaselineHash),
        WorkspaceBoundaryHash = SafeText(artifact.WorkspaceBoundaryHash),
        ValidationPlanId = SafeText(artifact.ValidationPlanId),
        ValidationPlanHash = SafeText(artifact.ValidationPlanHash),
        PatchHash = SafeText(artifact.PatchHash),
        ChangeSetHash = SafeText(artifact.ChangeSetHash),
        FileChanges = artifact.FileChanges.Select(ToReadModel).ToArray(),
        CreatedAtUtc = artifact.CreatedAtUtc,
        ExpiresAtUtc = artifact.ExpiresAtUtc,
        EvidenceReferences = artifact.EvidenceReferences.Select(SafeText).ToArray(),
        BoundaryMaxims = artifact.BoundaryMaxims.Select(SafeText).ToArray(),
        Boundary = SafeText(artifact.Boundary),
        AuthorityBoundary = PatchArtifactReadBoundaryText.AuthorityBoundary,
        Warnings = PatchArtifactReadBoundaryText.Warnings
    };

    private static PatchArtifactFileChangeReadModel ToReadModel(PatchArtifactFileChange change) => new()
    {
        Path = SafeText(change.Path),
        PreviousPath = SafeNullableText(change.PreviousPath),
        ChangeKind = SafeText(change.ChangeKind),
        BeforeContentHash = SafeNullableText(change.BeforeContentHash),
        AfterContentHash = SafeNullableText(change.AfterContentHash),
        DiffHash = SafeText(change.DiffHash),
        NormalizedDiff = SafeText(change.NormalizedDiff),
        IsBinary = change.IsBinary
    };

    private static string SafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return PrivateMaterialMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? RedactedUnsafeText
            : value.Trim();
    }

    private static string? SafeNullableText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : SafeText(value);
}
