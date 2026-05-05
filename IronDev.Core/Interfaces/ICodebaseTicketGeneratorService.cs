using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Builder;

namespace IronDev.Core.Interfaces;

/// <summary>
/// Service that analyzes the indexed codebase and generates a set of 
/// improvement or maintenance ticket drafts.
/// </summary>
public interface ICodebaseTicketGeneratorService
{
    /// <summary>
    /// Scans the indexed codebase for the given project and generates 
    /// a collection of ticket drafts for review.
    /// </summary>
    Task<CodebaseTicketGenerationResult> GenerateTicketsAsync(
        int projectId, 
        CancellationToken ct = default);
}
