namespace IronDev.Core.Governance;

public sealed class L4CapabilityMatrix : IL4CapabilityMatrix
{
    private static readonly string[] CommonBoundaryMaxims =
    [
        "Capability matrix is not capability execution.",
        "Capability definition is not authority.",
        "Required approval is not accepted approval.",
        "Required policy is not policy satisfaction.",
        "Required dry-run is not dry-run execution.",
        "Required patch artifact is not a patch artifact.",
        "Required source apply is not source apply.",
        "Required rollback is not rollback.",
        "Required workflow continuation is not workflow continuation.",
        "Required release gate is not release readiness.",
        "Evidence requirement is not evidence.",
        "Matrix row is not permission.",
        "Capability stage is not execution state.",
        "Backend authority must be backend-owned.",
        "UI cannot own L4 authority.",
        "L4 is governed execution, not autonomous theatre."
    ];

    private static readonly IReadOnlyList<L4CapabilityMatrixEntry> Entries =
    [
        Entry(
            L4CapabilityCodes.AcceptedApprovalRecord,
            "Accepted approval record",
            "accepted approval record",
            1,
            requiredAuthorityRecords:
            [
                "human approval evidence",
                "approval scope",
                "approver identity",
                "approval target",
                "approval expiry/staleness rule"
            ],
            requiredEvidenceRecords:
            [
                "approval evidence reference",
                "approval actor provenance",
                "approval scope reference"
            ],
            forbiddenEffects:
            [
                "create approval",
                "accept approval",
                "approve workflow",
                "satisfy policy",
                "continue workflow",
                "apply source",
                "release software"
            ]),
        Entry(
            L4CapabilityCodes.PolicySatisfactionRecord,
            "Policy satisfaction record",
            "policy satisfaction record",
            2,
            requiredAuthorityRecords:
            [
                "accepted approval record",
                "policy rule set",
                "capability requirement",
                "scope match",
                "freshness check"
            ],
            requiredEvidenceRecords:
            [
                "policy evaluation evidence",
                "accepted approval reference",
                "scope match evidence"
            ],
            forbiddenEffects:
            [
                "satisfy policy",
                "override policy",
                "continue workflow",
                "apply source",
                "release software"
            ]),
        Entry(
            L4CapabilityCodes.ControlledDryRun,
            "Controlled dry-run",
            "controlled dry-run",
            3,
            requiredAuthorityRecords:
            [
                "policy satisfaction record",
                "dry-run plan",
                "operation target",
                "execution cage/scope"
            ],
            requiredEvidenceRecords:
            [
                "dry-run result",
                "stdout/stderr summary",
                "exit code",
                "changed-file prediction",
                "failure summary",
                "hash/reference"
            ],
            forbiddenEffects:
            [
                "run dry-run",
                "apply patch",
                "mutate source",
                "continue workflow",
                "release software"
            ]),
        Entry(
            L4CapabilityCodes.PatchArtifact,
            "Patch artifact",
            "patch artifact",
            4,
            requiredAuthorityRecords:
            [
                "policy satisfaction record",
                "dry-run proof",
                "patch proposal evidence"
            ],
            requiredEvidenceRecords:
            [
                "patch content hash",
                "target files",
                "diff summary",
                "validation references",
                "rollback reference requirement"
            ],
            forbiddenEffects:
            [
                "create patch artifact",
                "apply patch",
                "mutate source",
                "continue workflow",
                "release software"
            ]),
        Entry(
            L4CapabilityCodes.ControlledSourceApply,
            "Controlled source apply",
            "controlled source apply",
            5,
            requiredAuthorityRecords:
            [
                "accepted approval record",
                "policy satisfaction record",
                "controlled dry-run result",
                "patch artifact",
                "rollback plan",
                "source apply approval requirement"
            ],
            requiredEvidenceRecords:
            [
                "pre-apply source state",
                "patch hash",
                "target branch/worktree",
                "apply result",
                "post-apply validation reference",
                "rollback record"
            ],
            forbiddenEffects:
            [
                "apply source",
                "write files",
                "commit changes",
                "push branch",
                "continue workflow",
                "release software"
            ]),
        Entry(
            L4CapabilityCodes.RollbackRecord,
            "Rollback record",
            "rollback",
            6,
            requiredAuthorityRecords:
            [
                "source apply record",
                "pre-apply state",
                "rollback strategy"
            ],
            requiredEvidenceRecords:
            [
                "rollback command or patch",
                "rollback target",
                "rollback verification",
                "rollback status"
            ],
            forbiddenEffects:
            [
                "execute rollback",
                "mutate source",
                "continue workflow",
                "release software"
            ]),
        Entry(
            L4CapabilityCodes.WorkflowContinuation,
            "Workflow continuation",
            "workflow continuation",
            7,
            requiredAuthorityRecords:
            [
                "policy satisfaction record",
                "source apply record or explicit no-apply proof",
                "validation proof",
                "workflow transition decision"
            ],
            requiredEvidenceRecords:
            [
                "current workflow state",
                "transition target",
                "transition reason",
                "causation/correlation",
                "guard result"
            ],
            forbiddenEffects:
            [
                "continue workflow",
                "transition workflow",
                "retry workflow",
                "repair workflow",
                "release software"
            ]),
        Entry(
            L4CapabilityCodes.ReleaseReadinessGate,
            "Release readiness gate",
            "release readiness gate",
            8,
            requiredAuthorityRecords:
            [
                "workflow completion evidence",
                "validation proof",
                "dogfood evidence",
                "policy satisfaction",
                "approval state",
                "release gate decision"
            ],
            requiredEvidenceRecords:
            [
                "validation summary",
                "dogfood receipt",
                "known limitations",
                "open risks",
                "release decision id"
            ],
            forbiddenEffects:
            [
                "approve release",
                "mark release ready",
                "ship software",
                "tag release",
                "deploy"
            ],
            additionalBoundaryMaxims:
            [
                "Dogfood pass is not release readiness.",
                "Health check is not release readiness.",
                "Validation summary is not release readiness.",
                "UI review is not release readiness."
            ])
    ];

    private static readonly IReadOnlyDictionary<string, L4CapabilityMatrixEntry> EntriesByCode = Entries
        .ToDictionary(entry => entry.CapabilityCode, StringComparer.Ordinal);

    public IReadOnlyList<L4CapabilityMatrixEntry> List() => Entries;

    public L4CapabilityMatrixEntry Get(string capabilityCode)
    {
        if (string.IsNullOrWhiteSpace(capabilityCode))
        {
            throw new ArgumentException("Capability code is required.", nameof(capabilityCode));
        }

        if (!EntriesByCode.TryGetValue(capabilityCode, out var entry))
        {
            throw new KeyNotFoundException($"Unknown L4 capability code '{capabilityCode}'.");
        }

        return entry;
    }

    private static L4CapabilityMatrixEntry Entry(
        string capabilityCode,
        string capabilityName,
        string stage,
        int order,
        IReadOnlyList<string> requiredAuthorityRecords,
        IReadOnlyList<string> requiredEvidenceRecords,
        IReadOnlyList<string> forbiddenEffects,
        IReadOnlyList<string>? additionalBoundaryMaxims = null) =>
        new(
            CapabilityCode: capabilityCode,
            CapabilityName: capabilityName,
            Stage: stage,
            Order: order,
            Implemented: false,
            AuthorityRequired: true,
            EvidenceRequired: true,
            RequiredAuthorityRecords: requiredAuthorityRecords,
            RequiredEvidenceRecords: requiredEvidenceRecords,
            AllowedEffects: ["definition only"],
            ForbiddenEffects: forbiddenEffects,
            BoundaryMaxims: additionalBoundaryMaxims is null
                ? CommonBoundaryMaxims
                : CommonBoundaryMaxims.Concat(additionalBoundaryMaxims).ToArray());
}
