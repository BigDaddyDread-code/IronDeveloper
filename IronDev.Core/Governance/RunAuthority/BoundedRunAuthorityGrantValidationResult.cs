namespace IronDev.Core.Governance;

public sealed record BoundedRunAuthorityGrantValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyCollection<string> Issues { get; init; }
}
