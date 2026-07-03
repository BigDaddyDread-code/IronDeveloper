using IronDev.Core.Builder;
using IronDev.Core.Workflow;

namespace IronDev.Core.Interfaces;

/// <summary>
/// Phase 0 walking-skeleton orchestrator: chains services that already exist
/// (readiness → proposal → disposable workspace → apply-in-workspace → build/test →
/// evidence) into one governed run.
///
/// Boundary: no new authority — composition only. Every gate stays enforced at the
/// step that owns it (readiness inside proposal generation, path containment inside
/// the workspace service). A successful run halts for approval after the critic
/// package is prepared; continuation consumes a live accepted approval recorded
/// through its own governed surface and verified against the run's exact requirement.
/// This service can never create, grant, or simulate approval, and it never mutates
/// the source repository. Halt is not approval; continuation is not apply permission.
/// The orchestrator coordinates work; it does not bless work.
/// </summary>
public interface ITicketSkeletonRunService
{
    /// <summary>Starts a skeleton run. Returns null when the ticket does not belong to the project.</summary>
    Task<TicketBuildRunDto?> StartAsync(int projectId, long ticketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the critic review package a completed skeleton run prepared. Read-only:
    /// the package is review material for the independent critic; serving it grants,
    /// requests, and simulates nothing.
    /// </summary>
    Task<SkeletonCriticPackage?> GetCriticPackageAsync(int projectId, long ticketId, string runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests continuation of a run halted for approval. The only unblock is a live
    /// AcceptedApprovalRecord that matches the run's approval requirement exactly
    /// (target kind, run id, critic-package hash, capability code) and has not
    /// expired — evaluated through the approval satisfaction and workflow halt
    /// evaluators. Continuation records that approval evidence was verified; it is
    /// not apply permission, and this service can never create an approval.
    /// </summary>
    Task<TicketBuildRunDto?> ContinueAsync(int projectId, long ticketId, string runId, CancellationToken cancellationToken = default);
}
