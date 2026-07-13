using System.Text.RegularExpressions;

namespace IronDev.Core.Security;

public static class SensitiveDataRedactor
{
    public const string RedactedValue = "[REDACTED]";
    public const string LocalPathValue = "[LOCAL_PATH]";

    private static readonly Regex AssignmentPattern = new(
        """(?ix)\b(?<key>password|pwd|secret|token|api[_-]?key|client[_-]?secret|access[_-]?token|refresh[_-]?token|credential|authorization)\s*[:=]\s*(?:"[^"]*"|'[^']*'|[^\s,;]+)""",
        RegexOptions.Compiled);

    private static readonly Regex BearerPattern = new(
        @"(?i)\bBearer\s+[A-Za-z0-9._~+\-/]+=*",
        RegexOptions.Compiled);

    private static readonly Regex ProviderKeyPattern = new(
        @"\b(?:sk-[A-Za-z0-9_-]{8,}|ghp_[A-Za-z0-9]{8,}|github_pat_[A-Za-z0-9_]{8,})\b",
        RegexOptions.Compiled);

    private static readonly Regex JwtPattern = new(
        @"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b",
        RegexOptions.Compiled);

    private static readonly Regex PrivateKeyPattern = new(
        @"-----BEGIN [^-\r\n]*PRIVATE KEY-----[\s\S]*?-----END [^-\r\n]*PRIVATE KEY-----",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WindowsPathPattern = new(
        """(?i)(?:file:///)?\b[A-Z]:[\\/][^\s`"'<>|]+""",
        RegexOptions.Compiled);

    private static readonly Regex UnixLocalPathPattern = new(
        """(?<![A-Za-z0-9_])/(?:home|Users|tmp|private/var|var/folders)/[^\s`"'<>]+""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var redacted = PrivateKeyPattern.Replace(value, RedactedValue);
        redacted = BearerPattern.Replace(redacted, $"Bearer {RedactedValue}");
        redacted = ProviderKeyPattern.Replace(redacted, RedactedValue);
        redacted = JwtPattern.Replace(redacted, RedactedValue);
        redacted = AssignmentPattern.Replace(redacted, match => $"{match.Groups["key"].Value}={RedactedValue}");
        redacted = WindowsPathPattern.Replace(redacted, LocalPathValue);
        return UnixLocalPathPattern.Replace(redacted, LocalPathValue);
    }
}
