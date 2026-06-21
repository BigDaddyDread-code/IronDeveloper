namespace IronDev.Core.Governance;

public static class OperationEligibilityPatchHashRules
{
    private static readonly IReadOnlyCollection<string> ForbiddenPatchHashValues =
    [
        "*",
        "all",
        "any",
        "latest",
        "current",
        "approved",
        "validation-passed",
        "unknown"
    ];

    public static bool IsSafePatchHash(string? patchHash)
    {
        if (string.IsNullOrWhiteSpace(patchHash))
            return false;

        var trimmed = patchHash.Trim();
        if (!string.Equals(trimmed, patchHash, StringComparison.Ordinal))
            return false;
        if (ForbiddenPatchHashValues.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            return false;
        if (trimmed.Any(char.IsWhiteSpace))
            return false;
        if (trimmed.Any(char.IsControl))
            return false;

        return true;
    }
}
