using Dapper;
using System.Text;
using IronDev.Core.Sandbox;
using IronDev.Core.Workbench;
using IronDev.Data;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services.Sandbox;

public sealed class WorkbenchBuilderSandboxRunner : IWorkbenchBuilderSandboxRunner
{
    private readonly IDbConnectionFactory _connections;
    private readonly ISandboxRuntimePolicyCatalog _policies;
    private readonly ISandboxExecutionService _sandbox;
    private readonly ISandboxSourceSnapshotBuilder _snapshots;
    private readonly string _snapshotRoot;
    private readonly string _evidenceRoot;

    public WorkbenchBuilderSandboxRunner(
        IDbConnectionFactory connections,
        ISandboxRuntimePolicyCatalog policies,
        ISandboxExecutionService sandbox,
        ISandboxSourceSnapshotBuilder snapshots,
        IConfiguration configuration)
    {
        _connections = connections;
        _policies = policies;
        _sandbox = sandbox;
        _snapshots = snapshots;
        _snapshotRoot = configuration["WorkbenchProductionSandbox:SourceSnapshotRoot"]?.Trim() ?? string.Empty;
        _evidenceRoot = configuration["WorkbenchProductionSandbox:EvidenceRoot"]?.Trim() ?? string.Empty;
    }

    public async Task<SandboxExecutionResult> ValidateAsync(
        BuilderSandboxValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        var core = BuilderWorkPackageCoreCodec.NormalizeAndValidate(request.WorkPackageCore);
        using var connection = _connections.CreateConnection();
        connection.Open();
        var authority = await connection.QuerySingleOrDefaultAsync<AuthorityRow>(new CommandDefinition(
            """
            SELECT binding.CanonicalPath,
                   receipt.ManifestJson, receipt.ManifestSha256,
                   receipt.GitTreeId,
                   profile.ProfileDefinitionId, profile.ProfileDescriptorRevision,
                   profile.DescriptorSha256, profile.TemplateBundleSha256,
                   profile.ToolchainManifestId, profile.ExecutionImageReference,
                   profile.RestoreCommand, profile.BuildCommand, profile.TestCommand
            FROM dbo.RepositoryBindings binding
            INNER JOIN dbo.ProjectExecutionProfiles profile
                ON profile.TenantId=binding.TenantId AND profile.ProjectId=binding.ProjectId
               AND profile.RepositoryBindingId=binding.Id
            INNER JOIN dbo.RepositoryProvisioningReceipts receipt
                ON receipt.TenantId=binding.TenantId AND receipt.ProjectId=binding.ProjectId
               AND receipt.RepositoryBindingId=binding.Id
               AND receipt.RepositoryBindingRevision=binding.CurrentRevision
            WHERE binding.TenantId=@TenantId AND binding.ProjectId=@ProjectId
              AND binding.Id=@RepositoryBindingId
              AND binding.CurrentRevision=@RepositoryBindingRevision
              AND binding.BaselineCommit=@BaselineCommit
              AND profile.Id=@ProjectExecutionProfileId
              AND profile.CurrentRevision=@ProjectExecutionProfileRevision;
            """,
            new
            {
                core.TenantId,
                core.ProjectId,
                core.RepositoryBindingId,
                core.RepositoryBindingRevision,
                core.BaselineCommit,
                core.EffectiveProfile.ProjectExecutionProfileId,
                core.EffectiveProfile.ProjectExecutionProfileRevision
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false)
            ?? throw new BuilderExecutionConflictException(
                BuilderExecutionFailureCodes.RepositoryBaselineChanged,
                "The repository or execution profile changed before sandbox validation.");
        if (!string.Equals(authority.GitTreeId, core.RepositoryObservation.GitTreeId, StringComparison.Ordinal))
            throw new BuilderExecutionConflictException(
                BuilderExecutionFailureCodes.RepositoryBaselineChanged,
                "The qualified source tree changed before sandbox validation.");

        var policyResolution = _policies.Resolve(new SandboxExecutionProfileBinding
        {
            ProfileDefinitionId = authority.ProfileDefinitionId,
            ProfileDescriptorRevision = authority.ProfileDescriptorRevision,
            DescriptorSha256 = authority.DescriptorSha256,
            TemplateBundleSha256 = authority.TemplateBundleSha256,
            ToolchainManifestId = authority.ToolchainManifestId,
            ExecutionImageReference = authority.ExecutionImageReference,
            RestoreCommand = authority.RestoreCommand,
            BuildCommand = authority.BuildCommand,
            TestCommand = authority.TestCommand
        });
        if (!policyResolution.IsAvailable || policyResolution.Policy is null ||
            !string.Equals(policyResolution.Policy.PolicySha256, core.Sandbox.PolicySha256, StringComparison.Ordinal) ||
            !string.Equals(policyResolution.Policy.ContainerImageDigest, core.Sandbox.QualifiedImageDigest, StringComparison.Ordinal))
            throw new BuilderExecutionConflictException(
                BuilderExecutionFailureCodes.SandboxPolicyChanged,
                "The qualified sandbox policy or image changed before execution.");

        var snapshotRequest = new SandboxSourceSnapshotRequest(
            request.ExecutionId, authority.CanonicalPath, core.BaselineCommit,
            authority.GitTreeId, authority.ManifestJson, authority.ManifestSha256, _snapshotRoot);
        SandboxSourceSnapshot? snapshot = null;
        try
        {
            snapshot = await _snapshots.CreateOrRecoverAsync(snapshotRequest, cancellationToken).ConfigureAwait(false);
            foreach (var file in request.ProposedFiles)
                await WriteOwnedFileAsync(snapshot.SourcePath, file, cancellationToken).ConfigureAwait(false);
            var proposalFingerprint = SandboxCanonicalJson.Sha256(
                snapshot.WorktreeFingerprint + "\n" +
                string.Join("\n", request.ProposedFiles
                    .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                    .Select(file => $"{file.RelativePath}\0{file.ContentSha256}")));
            return await _sandbox.ExecuteAsync(new SandboxExecutionRequest
            {
                ExecutionId = request.ExecutionId,
                ProjectId = core.ProjectId,
                RepositoryBindingId = core.RepositoryBindingId,
                RepositoryBindingRevision = core.RepositoryBindingRevision,
                BaselineCommit = core.BaselineCommit,
                WorktreeFingerprint = proposalFingerprint,
                ProjectExecutionProfileId = core.EffectiveProfile.ProjectExecutionProfileId,
                ProjectExecutionProfileRevision = core.EffectiveProfile.ProjectExecutionProfileRevision,
                SourceSnapshotPath = snapshot.SourcePath,
                EvidenceOutputPath = Path.Combine(_evidenceRoot, request.ExecutionId.ToString("N")),
                Policy = policyResolution.Policy
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (snapshot is not null && !_snapshots.Cleanup(snapshot))
                throw new SandboxContractValidationException(
                    "The Builder source snapshot could not be safely removed.");
        }
    }

    private static async Task WriteOwnedFileAsync(
        string root,
        BuilderProposedFile file,
        CancellationToken cancellationToken)
    {
        var canonicalRoot = Path.GetFullPath(root).TrimEnd('\\', '/');
        var path = Path.GetFullPath(Path.Combine(
            canonicalRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new SandboxContractValidationException("A Builder proposal escaped its owned snapshot.");
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new SandboxContractValidationException("A Builder proposal targeted a reparse point.");
        await File.WriteAllTextAsync(path, file.Content, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record AuthorityRow(
        string CanonicalPath,
        string ManifestJson,
        string ManifestSha256,
        string GitTreeId,
        string ProfileDefinitionId,
        int ProfileDescriptorRevision,
        string DescriptorSha256,
        string TemplateBundleSha256,
        string ToolchainManifestId,
        string ExecutionImageReference,
        string RestoreCommand,
        string BuildCommand,
        string TestCommand);
}
