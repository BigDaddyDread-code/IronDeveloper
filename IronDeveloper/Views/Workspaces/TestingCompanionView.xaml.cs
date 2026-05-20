using System.Windows.Controls;
using System.Windows.Input;
using IronDev.Agent.ViewModels.Workspaces;

namespace IronDev.Agent.Views.Workspaces;

public partial class TestingCompanionView : UserControl
{
    public TestingCompanionView()
    {
        InitializeComponent();
    }

    private void SelectedScreenshot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not TestingCompanionViewModel { SelectedMoment: { } selected })
            return;

        var path = !string.IsNullOrWhiteSpace(selected.AnnotatedScreenshotPath)
            ? selected.AnnotatedScreenshotPath
            : selected.OriginalScreenshotPath;

        if (string.IsNullOrWhiteSpace(path))
            return;

        var window = new ScreenshotPreviewWindow(path, selected.Title)
        {
            Owner = System.Windows.Window.GetWindow(this)
        };
        window.Show();
    }
}
