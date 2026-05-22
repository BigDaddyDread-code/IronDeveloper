namespace IronDev.Core.Testing;

public sealed class TestRunRecord
{
    public Guid TestRunId { get; set; }
    public string ProjectName { get; set; } = "";
    public string TargetName { get; set; } = "";
    public TestTargetType TargetType { get; set; } = TestTargetType.DesktopApp;
    public TestRunStatus Status { get; set; } = TestRunStatus.Completed;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int MomentCount { get; set; }
    public string? Summary { get; set; }
    public string? RunFolderPath { get; set; }
    public string? ReportPath { get; set; }
}
