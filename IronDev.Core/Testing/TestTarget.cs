namespace IronDev.Core.Testing;

public sealed class TestTarget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public TestTargetType TargetType { get; set; } = TestTargetType.DesktopApp;

    public string? LaunchCommand { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? Url { get; set; }
    public string? ProcessName { get; set; }
    public int? ProcessId { get; set; }
    public string? LogPath { get; set; }

    public string? ProjectId { get; set; }
    public string? MetadataJson { get; set; }
}
