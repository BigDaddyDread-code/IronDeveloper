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

    Task<TicketBuildRunDto?> ContinueAsAsync(
        int projectId,
        long ticketId,
        string runId,
        string requestedByUserId,
        CancellationToken cancellationToken = default) =>
        ContinueAsync(projectId, ticketId, runId, cancellationToken);

    /// <summary>
    /// REVISE-1: the human at the gate directs a bounded revision instead of
    /// approving — cited undispositioned findings plus a written instruction
    /// produce a new proposal, a fresh attempt-scoped build/test, and a NEW
    /// critic package at the SAME human gate. Off unless SkeletonRevision:MaxAttempts
    /// is explicitly configured. A revision grants nothing: the revised package
    /// needs its own critic review, dispositions, and hash-bound approval, and a
    /// failed revision leaves the previous gate package canonical and untouched.
    /// </summary>
    Task<TicketBuildRunDto?> ReviseAsync(int projectId, long ticketId, string runId, SkeletonRunRevisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies an approved, continued run through the governed workspace apply spine —
    /// copy-only, evidence-chained, sandbox repositories only (off by default via
    /// SkeletonApply:Enabled). The approval is re-verified live at this step; the
    /// spine's workspace evidence chain is the receipt. Commit, push, and release
    /// remain separate governed steps this service does not have.
    /// </summary>
    Task<TicketBuildRunDto?> ApplyAsync(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken cancellationToken = default);

    Task<TicketBuildRunDto?> ApplyAsAsync(
        int projectId,
        long ticketId,
        string runId,
        string requestedByUserId,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(projectId, ticketId, runId, cancellationToken);

    /// <summary>
    /// Records an explicit human recovery decision for the latest apply attempt. Resume
    /// and retry create a fresh attempt only when durable stage evidence proves source
    /// mutation was not observed. Uncertain mutation permits manual review or abandon only.
    /// </summary>
    Task<TicketBuildRunDto?> RecoverApplyAsync(
        int projectId,
        long ticketId,
        string runId,
        SkeletonApplyRecoveryRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<TicketBuildRunDto?>(null);

    /// <summary>
    /// Reconstructs the whole governed loop for a run from durable evidence: events,
    /// critic package (hash recomputed from disk), the approval requirement and the
    /// continuation that consumed it, and the apply spine's receipts. Read-only and
    /// verifying: unverifiable links are named as gaps, never patched over. A report
    /// grants nothing and cannot alter the run.
    /// </summary>
    Task<SkeletonRunReport?> GetRunReportAsync(int projectId, long ticketId, string runId, CancellationToken cancellationToken = default);
}
