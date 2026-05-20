namespace IronDev.Core.Testing;

public sealed class StartTestRunRequest
{
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public string? ProjectPath { get; set; }
    public TestTarget Target { get; set; } = new();
}

public sealed class BrokenMomentCaptureDraft
{
    public Guid TestRunId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public string ScreenshotPath { get; set; } = "";
    public string? ActiveWorkspace { get; set; }
    public string? ActiveProjectName { get; set; }
    public string? RelevantLogsText { get; set; }
    public string? RelevantTraceText { get; set; }
}

public sealed class SaveMarkedMomentRequest
{
    public BrokenMomentCaptureDraft Draft { get; set; } = new();
    public TestMomentType MomentType { get; set; } = TestMomentType.Bug;
    public string? UserTextNote { get; set; }
    public string? ExpectedBehavior { get; set; }
    public string? ActualBehavior { get; set; }
    public string? Severity { get; set; }
    public string? SuspectedArea { get; set; }
    public int? MarkedAreaX { get; set; }
    public int? MarkedAreaY { get; set; }
    public int? MarkedAreaWidth { get; set; }
    public int? MarkedAreaHeight { get; set; }
}
