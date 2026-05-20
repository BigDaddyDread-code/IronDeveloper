namespace IronDev.Core.Testing;

public sealed class TestRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = "";

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? EndedAt { get; set; }

    public TestRunStatus Status { get; set; } = TestRunStatus.Running;

    public Guid? TargetId { get; set; }
    public string TargetName { get; set; } = "";
    public TestTargetType TargetType { get; set; } = TestTargetType.DesktopApp;
    public string? TargetExecutablePath { get; set; }
    public string? TargetProcessName { get; set; }
    public int? TargetProcessId { get; set; }

    public string? GitBranch { get; set; }
    public string? GitCommit { get; set; }

    public string? LogFilePath { get; set; }
    public string? RunFolderPath { get; set; }
    public string? ScreenshotFolderPath { get; set; }
    public string? AudioFolderPath { get; set; }
    public string? ReportFolderPath { get; set; }
    public DateTimeOffset? TraceCollectionStartedAt { get; set; }
    public DateTimeOffset? TraceCollectionEndedAt { get; set; }
    public string? SessionLogPath { get; set; }
    public string? SessionTracePath { get; set; }

    public string? Summary { get; set; }
}
