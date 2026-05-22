namespace IronDev.Core.Testing;

public sealed class TestRunReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TestRunId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public string Markdown { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Status { get; set; } = "Generated";
    public string? ReportPath { get; set; }
}
