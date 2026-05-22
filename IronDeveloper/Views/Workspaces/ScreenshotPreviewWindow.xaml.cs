using System.Windows;

namespace IronDev.Agent.Views.Workspaces;

public partial class ScreenshotPreviewWindow : Window
{
    public ScreenshotPreviewWindow(string imagePath, string title)
    {
        InitializeComponent();
        DataContext = new ScreenshotPreviewViewModel(imagePath, title);
    }

    private sealed class ScreenshotPreviewViewModel
    {
        public ScreenshotPreviewViewModel(string imagePath, string title)
        {
            ImagePath = imagePath;
            Title = title;
        }

        public string ImagePath { get; }
        public string Title { get; }
    }
}
