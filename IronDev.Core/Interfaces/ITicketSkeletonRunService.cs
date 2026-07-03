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
/// the workspace service). A skeleton run ends when evidence is packaged: it cannot
/// request, consume, or simulate approval, and it never mutates the source repository.
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
}
