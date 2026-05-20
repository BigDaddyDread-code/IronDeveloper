using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using IronDev.Agent.ViewModels.Workspaces;

namespace IronDev.Agent.Views.Workspaces;

public partial class MarkMomentWindow : Window
{
    private Point? _dragStart;

    public MarkMomentWindow(MarkMomentViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private MarkMomentViewModel ViewModel => (MarkMomentViewModel)DataContext;

    private void ScreenshotHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(AnnotationCanvas);
        Canvas.SetLeft(SelectionRectangle, _dragStart.Value.X);
        Canvas.SetTop(SelectionRectangle, _dragStart.Value.Y);
        SelectionRectangle.Width = 0;
        SelectionRectangle.Height = 0;
        SelectionRectangle.Visibility = Visibility.Visible;
        ScreenshotHost.CaptureMouse();
    }

    private void ScreenshotHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(AnnotationCanvas);
        var x = Math.Min(current.X, _dragStart.Value.X);
        var y = Math.Min(current.Y, _dragStart.Value.Y);
        var width = Math.Abs(current.X - _dragStart.Value.X);
        var height = Math.Abs(current.Y - _dragStart.Value.Y);

        Canvas.SetLeft(SelectionRectangle, x);
        Canvas.SetTop(SelectionRectangle, y);
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
    }

    private void ScreenshotHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart == null)
            return;

        ScreenshotHost.ReleaseMouseCapture();
        _dragStart = null;
        UpdateMarkedAreaFromSelection();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        UpdateMarkedAreaFromSelection();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void UpdateMarkedAreaFromSelection()
    {
        if (SelectionRectangle.Visibility != Visibility.Visible ||
            SelectionRectangle.Width <= 1 ||
            SelectionRectangle.Height <= 1 ||
            ScreenshotImage.Source is not BitmapSource bitmap)
        {
            return;
        }

        var imageBounds = GetRenderedImageBounds(ScreenshotImage, bitmap);
        var rectX = Canvas.GetLeft(SelectionRectangle);
        var rectY = Canvas.GetTop(SelectionRectangle);
        var rectWidth = SelectionRectangle.Width;
        var rectHeight = SelectionRectangle.Height;

        var clippedX = Math.Max(rectX, imageBounds.X);
        var clippedY = Math.Max(rectY, imageBounds.Y);
        var clippedRight = Math.Min(rectX + rectWidth, imageBounds.X + imageBounds.Width);
        var clippedBottom = Math.Min(rectY + rectHeight, imageBounds.Y + imageBounds.Height);

        if (clippedRight <= clippedX || clippedBottom <= clippedY)
            return;

        var scaleX = bitmap.PixelWidth / imageBounds.Width;
        var scaleY = bitmap.PixelHeight / imageBounds.Height;

        ViewModel.MarkedAreaX = (int)Math.Round((clippedX - imageBounds.X) * scaleX);
        ViewModel.MarkedAreaY = (int)Math.Round((clippedY - imageBounds.Y) * scaleY);
        ViewModel.MarkedAreaWidth = (int)Math.Round((clippedRight - clippedX) * scaleX);
        ViewModel.MarkedAreaHeight = (int)Math.Round((clippedBottom - clippedY) * scaleY);
    }

    private static Rect GetRenderedImageBounds(Image image, BitmapSource bitmap)
    {
        var controlWidth = image.ActualWidth;
        var controlHeight = image.ActualHeight;
        var imageRatio = bitmap.PixelWidth / (double)bitmap.PixelHeight;
        var controlRatio = controlWidth / controlHeight;

        if (controlRatio > imageRatio)
        {
            var width = controlHeight * imageRatio;
            return new Rect((controlWidth - width) / 2, 0, width, controlHeight);
        }

        var height = controlWidth / imageRatio;
        return new Rect(0, (controlHeight - height) / 2, controlWidth, height);
    }
}
