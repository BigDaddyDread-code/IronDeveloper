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
}
