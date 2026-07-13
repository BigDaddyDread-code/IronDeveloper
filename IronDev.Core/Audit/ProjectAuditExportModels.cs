using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Audit;

public static class ProjectAuditExportStatuses
{
    public const string Succeeded = "Succeeded";
    public const string NotFound = "NotFound";
    public const string Forbidden = "Forbidden";
    public const string ValidationError = "ValidationError";
}

public sealed record ProjectAuditExportFilters
{
    public long? WorkItemId { get; init; }
    public string? Actor { get; init; }
    public string? Event { get; init; }
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public int Take { get; init; } = 250;
}

public sealed record ProjectAuditExport
{
    public string SchemaVersion { get; init; } = "1";
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public required ProjectAuditExportFilters Filters { get; init; }
    public int ReturnedCount { get; init; }
    public int Take { get; init; }
    public bool Truncated { get; init; }
    public string ItemsSha256 { get; init; } = string.Empty;
    public IReadOnlyList<AuditLedgerItem> Items { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public AuditLedgerBoundary Boundary { get; init; } = new();
}

public sealed record ProjectAuditExportOutcome
{
    public string Status { get; init; } = ProjectAuditExportStatuses.Succeeded;
    public ProjectAuditExport? Export { get; init; }
    public IReadOnlyList<AuditLedgerIssue> Issues { get; init; } = [];
}

public interface IProjectAuditExportService
{
    Task<ProjectAuditExportOutcome> ExportAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        ProjectAuditExportFilters filters,
        CancellationToken cancellationToken = default);
}

public static class ProjectAuditExportProjector
{
    private static readonly string[] SecretMarkers = ["password", "api key", "bearer ", "private key", "secret="];

    public static ProjectAuditExport Build(
        int projectId,
        string projectName,
        ProjectAuditExportFilters filters,
        AuditLedgerResponse ledger,
        DateTimeOffset generatedUtc)
    {
        var take = Math.Clamp(filters.Take <= 0 ? 250 : filters.Take, 1, 250);
        var items = ledger.Items
            .Where(item => item.ProjectId == projectId)
            .Select(item => SanitizeItem(projectId, item))
            .ToArray();

        return new ProjectAuditExport
        {
            ProjectId = projectId,
            ProjectName = projectName,
            GeneratedUtc = generatedUtc,
            Filters = filters with { Take = take },
            ReturnedCount = items.Length,
            Take = take,
            Truncated = items.Length >= take,
            ItemsSha256 = HashItems(items),
            Items = items,
            Warnings =
            [
                "This export is bounded read-only traceability and may be truncated.",
                "Raw payload JSON, credentials, source content, prompts, completions, and private reasoning are not exported.",
                "The item hash is integrity metadata; it is not a signature, approval, or compliance claim."
            ],
            Boundary = ledger.Boundary
        };
    }

    public static string HashItems(IReadOnlyList<AuditLedgerItem> items)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var item in items)
        {
            Append(hash, item.LedgerId);
            Append(hash, item.TimeUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            Append(hash, item.ProjectId.ToString(CultureInfo.InvariantCulture));
            Append(hash, item.ProjectName);
            Append(hash, item.WorkItemId?.ToString(CultureInfo.InvariantCulture));
            Append(hash, item.WorkItemTitle);
            Append(hash, item.Source);
            Append(hash, item.ActorId);
            Append(hash, item.ActorDisplayName);
            Append(hash, item.Action);
            Append(hash, item.Outcome);
            Append(hash, item.Summary);
            Append(hash, item.CorrelationId);
            foreach (var link in item.EvidenceLinks)
            {
                Append(hash, link.Label);
                Append(hash, link.Href);
            }
            hash.AppendData([0x1e]);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static AuditLedgerItem SanitizeItem(int projectId, AuditLedgerItem item) => item with
    {
        Summary = ContainsSecretMarker(item.Summary) ? "[redacted audit summary]" : item.Summary,
        EvidenceLinks = item.EvidenceLinks.Where(link => AuditEvidenceLinkSafety.IsSafeForProject(projectId, link.Href)).ToArray()
    };

    private static bool ContainsSecretMarker(string value) =>
        SecretMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static void Append(IncrementalHash hash, string? value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        hash.AppendData([0x1f]);
    }
}

public static class AuditEvidenceLinkSafety
{
    public static bool IsSafeForProject(int projectId, string? href)
    {
        if (projectId <= 0 || string.IsNullOrWhiteSpace(href) ||
            !href.StartsWith('/') || href.StartsWith("//", StringComparison.Ordinal) ||
            href.Any(character => char.IsControl(character) || character == '\\') ||
            ContainsEncodedPathControl(href))
            return false;

        var pathEnd = href.IndexOfAny(['?', '#']);
        var path = pathEnd >= 0 ? href[..pathEnd] : href;
        string decodedPath;
        try
        {
            decodedPath = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            return false;
        }

        var segments = decodedPath.Split('/');
        if (segments.Length < 3 || segments[0].Length != 0 ||
            segments.Skip(1).Take(segments.Length - 2).Any(segment => segment.Length == 0 || segment is "." or ".."))
            return false;

        if (!segments[1].Equals("projects", StringComparison.OrdinalIgnoreCase))
            return segments[1].Equals("governance", StringComparison.OrdinalIgnoreCase) ||
                   segments[1].Equals("operations", StringComparison.OrdinalIgnoreCase) ||
                   segments[1].Equals("workflows", StringComparison.OrdinalIgnoreCase);

        if (segments.Length < 4 || !segments[2].All(char.IsAsciiDigit))
            return false;

        return int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out var linkedProjectId) &&
               linkedProjectId == projectId;
    }

    private static bool ContainsEncodedPathControl(string href) =>
        new[] { "%2e", "%2f", "%5c", "%25" }
            .Any(token => href.Contains(token, StringComparison.OrdinalIgnoreCase));
}
