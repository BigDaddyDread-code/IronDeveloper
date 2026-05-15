using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

/// <summary>
/// Evaluates a proposed ticket-creation request against existing project
/// tickets, decisions, and rules and returns a structured conflict assessment.
///
/// No database schema changes are required: the service reads data that is
/// already available via ITicketService and IProjectMemoryService.
/// </summary>
public interface IContextConflictService
{
    /// <summary>
    /// Assess whether the proposed work in <paramref name="context"/> conflicts
    /// with, duplicates, or needs coordination with existing project artefacts.
    /// </summary>
    Task<TicketConflictAssessment> AssessAsync(
        ConflictAssessmentContext context,
        CancellationToken ct = default);
}
