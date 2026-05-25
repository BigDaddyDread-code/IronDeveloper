namespace IronDev.Core.Testing;

public sealed class TestMoment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TestRunId { get; set; }

    /// <summary>UTC timestamp. Legacy name retained for compatibility.</summary>
    public DateTimeOffset MarkedAt { get; set; } = DateTimeOffset.UtcNow;

    public TestMomentType MomentType { get; set; } = TestMomentType.Bug;

    public string? UserTextNote { get; set; }
    public string? ExpectedBehavior { get; set; }
    public string? ActualBehavior { get; set; }
    public string? Severity { get; set; }
    public string? SuspectedArea { get; set; }

    public string? AudioFilePath { get; set; }
    public string? AudioTranscript { get; set; }

    public string? ScreenshotPath { get; set; }
    public string? AnnotatedScreenshotPath { get; set; }

    public int? MarkedAreaX { get; set; }
    public int? MarkedAreaY { get; set; }
    public int? MarkedAreaWidth { get; set; }
    public int? MarkedAreaHeight { get; set; }

    public string? ActiveWorkspace { get; set; }
    public string? ActiveProjectName { get; set; }
    public string? ActiveTicketId { get; set; }
    public string? ActiveTicketTitle { get; set; }
    public string? ActiveDocumentId { get; set; }

    public string? RelevantLogsText { get; set; }
    public string? RelevantTraceText { get; set; }

    public string? AnalysisMarkdown { get; set; }
    public string? MetadataJson { get; set; }
}
