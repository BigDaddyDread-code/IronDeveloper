using System.Security.Cryptography;
using System.Text;
using IronDev.Core.RunReadiness;

namespace IronDev.IntegrationTests.Api;

/// <summary>
/// Test-only prerequisite capability for the REL-3 API journey. It permits one
/// explicitly registered project and keeps one immutable evidence hash for the
/// run-start snapshot and final apply recheck. It never writes qualification
/// records or replaces the production evaluator.
/// </summary>
internal sealed class ExpectedProjectApplyCapabilityService : IProjectApplyCapabilityService
{
    private readonly object _sync = new();
    private int _expectedProjectId;
    private string _readinessEvidenceHash = string.Empty;
    private bool _bindingInvalid;

    public void ExpectProject(int projectId, string readinessEvidenceHash)
    {
        lock (_sync)
        {
            if (_bindingInvalid)
                throw new InvalidOperationException("This apply-capability fixture is already fail-closed after a binding change.");

            if (projectId <= 0 || string.IsNullOrWhiteSpace(readinessEvidenceHash))
            {
                _bindingInvalid = true;
                throw new InvalidOperationException("An explicit project and readiness-evidence hash are required; missing binding truth fails closed.");
            }

            var evidenceHash = readinessEvidenceHash.Trim();

            if (_expectedProjectId == 0)
            {
                _expectedProjectId = projectId;
                _readinessEvidenceHash = evidenceHash;
                return;
            }

            if (_expectedProjectId == projectId &&
                _readinessEvidenceHash.Equals(evidenceHash, StringComparison.Ordinal))
                return;

            _bindingInvalid = true;
            throw new InvalidOperationException(
                $"This fixture already binds project {_expectedProjectId} to one evidence hash; project or hash changes fail closed.");
        }
    }

    public Task<ProjectApplyCapability> EvaluateAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_bindingInvalid && _expectedProjectId > 0 && projectId == _expectedProjectId)
                return Task.FromResult(Ready(projectId, _readinessEvidenceHash));

            var reason = _bindingInvalid
                ? "The test-only project or evidence-hash binding changed and is permanently fail-closed."
                : _expectedProjectId == 0
                    ? "The REL-3 journey did not register its one expected apply-capability project."
                    : $"The REL-3 apply-capability fixture allows project {_expectedProjectId}, not project {projectId}.";
            return Task.FromResult(Refused(projectId, reason));
        }
    }

    public Task<ProjectApplyCapability> QualifyDisposableProjectAsync(
        int projectId,
        int qualifyingActorUserId,
        CancellationToken cancellationToken = default) =>
        EvaluateAsync(projectId, cancellationToken);

    internal static string CreateReadinessEvidenceHash(int projectId, string evidenceContract)
    {
        if (projectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(projectId));
        if (string.IsNullOrWhiteSpace(evidenceContract))
            throw new ArgumentException("An explicit evidence contract is required.", nameof(evidenceContract));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{projectId}\n{evidenceContract.Trim()}"))).ToLowerInvariant();
    }

    private static ProjectApplyCapability Ready(int projectId, string evidenceHash) => new()
    {
        ProjectId = projectId,
        IsReady = true,
        State = "Ready",
        ReasonCode = ProjectApplyCapabilityReasonCodes.Ready,
        Reason = "The explicitly registered REL-3 project has test-only prerequisite apply capability.",
        SessionMode = ProjectRunPurposes.ProjectFeatureWork,
        LauncherSessionId = "rel3-single-project-fixture",
        RepositoryCommit = "test-fixture",
        QualificationId = $"rel3-project-{projectId}",
        QualificationFingerprint = evidenceHash,
        ReadinessEvidenceHash = evidenceHash
    };

    private static ProjectApplyCapability Refused(int projectId, string reason) => new()
    {
        ProjectId = projectId,
        IsReady = false,
        State = "Disabled",
        ReasonCode = ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationBindingMismatch,
        Reason = reason
    };

}
