using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static class BoundedRunAuthorityGrantFileScope
{
    public static bool IsSafeRelativeGlob(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = Normalize(value);
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.StartsWith("//", StringComparison.Ordinal) ||
            normalized.StartsWith("~/", StringComparison.Ordinal) ||
            normalized.Equals("~", StringComparison.Ordinal) ||
            normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(normalized, "^[A-Za-z]:/"))
        {
            return false;
        }

        return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(segment => !string.Equals(segment, "..", StringComparison.Ordinal));
    }

    public static bool IsAllowed(
        string candidatePath,
        IReadOnlyCollection<string> allowedGlobs,
        IReadOnlyCollection<string> forbiddenGlobs)
    {
        if (!IsSafeRelativeGlob(candidatePath))
            return false;

        var normalized = Normalize(candidatePath);
        if (forbiddenGlobs.Any(pattern => Matches(pattern, normalized)))
            return false;

        return allowedGlobs.Any(pattern => Matches(pattern, normalized));
    }

    public static bool IsForbidden(string candidatePath, IReadOnlyCollection<string> forbiddenGlobs)
    {
        if (!IsSafeRelativeGlob(candidatePath))
            return true;

        var normalized = Normalize(candidatePath);
        return forbiddenGlobs.Any(pattern => Matches(pattern, normalized));
    }

    private static bool Matches(string pattern, string normalizedPath)
    {
        if (!IsSafeRelativeGlob(pattern))
            return false;

        var normalizedPattern = Normalize(pattern);
        var expression = "^" + Regex.Escape(normalizedPattern)
            .Replace("\\*\\*", ".*", StringComparison.Ordinal)
            .Replace("\\*", "[^/]*", StringComparison.Ordinal)
            .Replace("\\?", "[^/]", StringComparison.Ordinal) + "$";

        return Regex.IsMatch(normalizedPath, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Normalize(string value) =>
        value.Trim().Replace('\\', '/');
}
