namespace IronDev.Agent.Services.Testing;

public interface IScreenshotCaptureService
{
    Task CaptureDesktopAsync(string outputPath, CancellationToken ct = default);
    Task SaveAnnotatedCopyAsync(string sourcePath, string outputPath, Int32RectData? markedArea, CancellationToken ct = default);
}

public readonly record struct Int32RectData(int X, int Y, int Width, int Height);
