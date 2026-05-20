using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IronDev.Agent.Services.Testing;

public sealed class ScreenshotCaptureService : IScreenshotCaptureService
{
    private const int Srccopy = 0x00CC0020;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    public Task CaptureDesktopAsync(string outputPath, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var width = GetSystemMetrics(SmCxScreen);
        var height = GetSystemMetrics(SmCyScreen);
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, width, height);
        var oldObject = SelectObject(memoryDc, bitmap);

        try
        {
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, 0, 0, Srccopy))
                throw new InvalidOperationException("Desktop screenshot capture failed.");

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            SavePng(source, outputPath);
        }
        finally
        {
            SelectObject(memoryDc, oldObject);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }

        return Task.CompletedTask;
    }

    public Task SaveAnnotatedCopyAsync(string sourcePath, string outputPath, Int32RectData? markedArea, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(sourcePath, UriKind.Absolute);
        image.EndInit();
        image.Freeze();

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));

            if (markedArea is { Width: > 0, Height: > 0 } area)
            {
                var pen = new Pen(Brushes.Red, 5);
                pen.Freeze();
                context.DrawRectangle(
                    null,
                    pen,
                    new Rect(area.X, area.Y, area.Width, area.Height));
            }
        }

        var render = new RenderTargetBitmap(
            image.PixelWidth,
            image.PixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        render.Render(visual);
        SavePng(render, outputPath);

        return Task.CompletedTask;
    }

    private static void SavePng(BitmapSource source, string outputPath)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, int rop);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);
}
