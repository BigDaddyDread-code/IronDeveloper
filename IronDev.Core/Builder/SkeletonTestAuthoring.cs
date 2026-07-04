namespace IronDev.Core.Builder;

/// <summary>
/// The Tester's input contract for authoring test files in a skeleton run.
///
/// Boundary — Tester independence is enforced BY CONTRACT, the same way the critic's
/// memory-blindness is: this request carries only the ticket's requirement surface
/// (title, acceptance criteria, problem statement). There is no field through which
/// the builder's proposal, diff, or proposed file contents could reach the Tester,
/// so authored tests check what was ASKED FOR, never what was built. Tests written
/// from the code silently ratify the code — including its mistakes.
/// </summary>
public sealed record SkeletonTestAuthoringRequest
{
    public required long TicketId { get; init; }
    public required int ProjectId { get; init; }
    public required string TicketTitle { get; init; }
    public string AcceptanceCriteria { get; init; } = string.Empty;
    public string Problem { get; init; } = string.Empty;
}

/// <summary>One authored test file, mapped to the criterion it covers.</summary>
public sealed record SkeletonAuthoredTest
{
    public required string RelativePath { get; init; }
    public required string Content { get; init; }
    public string CoversCriterion { get; init; } = string.Empty;
}

public sealed record SkeletonTestAuthoringResult
{
    public required bool Succeeded { get; init; }
    public IReadOnlyList<SkeletonAuthoredTest> Tests { get; init; } = [];
    public string FailureReason { get; init; } = string.Empty;

    /// <summary>AG-2: which model authored these tests — provenance the run stamps into its events.</summary>
    public string ModelProvider { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
}

/// <summary>
/// Authors test files from acceptance criteria. Advisory generation only: authored
/// tests are workspace material for the build/test step and critic review — authoring
/// grants nothing, and a test passing is evidence, not approval.
/// </summary>
public interface ISkeletonTestAuthoringService
{
    Task<SkeletonTestAuthoringResult> AuthorTestsAsync(SkeletonTestAuthoringRequest request, CancellationToken cancellationToken = default);
}
