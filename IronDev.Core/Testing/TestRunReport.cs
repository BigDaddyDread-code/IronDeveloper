namespace IronDev.Core.Testing;

public sealed class TestRunReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TestRunId { get; set; }

    /// <summary>UTC timestamp. Legacy name retained for compatibility.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string Markdown { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Status { get; set; } = "Generated";
    public string? ReportPath { get; set; }
}
