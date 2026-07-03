namespace IronDev.Core.Builder;

/// <summary>
/// Thrown by proposal generation when the build-readiness gate blocks the ticket.
/// Typed so callers can distinguish "the ticket is not ready" (the user resolves
/// blocking issues) from proposal/model/service failures (an operator investigates) —
/// without sniffing exception message text.
/// </summary>
public sealed class BuildReadinessBlockedException : InvalidOperationException
{
    public BuildReadinessBlockedException(string message, IReadOnlyList<string> blockingIssues)
        : base(message)
    {
        BlockingIssues = blockingIssues;
    }

    public IReadOnlyList<string> BlockingIssues { get; }
}
