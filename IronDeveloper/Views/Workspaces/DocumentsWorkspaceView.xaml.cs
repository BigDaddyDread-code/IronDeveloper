using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using IronDev.Agent.ViewModels.Workspaces;

namespace IronDev.Agent.Views.Workspaces;

public partial class DocumentsWorkspaceView : UserControl
{
    private bool _webViewReady;

    public DocumentsWorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        _ = InitWebViewAsync();
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            await MarkdownViewer.EnsureCoreWebView2Async();
            _webViewReady = true;

            // If HTML was set before WebView2 was ready, render it now.
            if (DataContext is DocumentsWorkspaceViewModel vm && !string.IsNullOrEmpty(vm.RenderedHtml))
                MarkdownViewer.NavigateToString(vm.RenderedHtml);
        }
        catch
        {
            // WebView2 runtime not installed — viewer degrades gracefully (stays blank)
        }
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is DocumentsWorkspaceViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;

        if (e.NewValue is DocumentsWorkspaceViewModel newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DocumentsWorkspaceViewModel.RenderedHtml))
            return;

        if (!_webViewReady || sender is not DocumentsWorkspaceViewModel vm)
            return;

        Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrEmpty(vm.RenderedHtml))
                MarkdownViewer.NavigateToString(BuildEmptyDocumentHtml());
            else
                MarkdownViewer.NavigateToString(vm.RenderedHtml);
        });
    }

    private string BuildEmptyDocumentHtml()
    {
        var background = ToCssColor("IronDev.Brush.Surface.Panel");
        var foreground = ToCssColor("IronDev.Brush.Text.Secondary");

        return "<html><head><meta charset=\"utf-8\"><style>" +
               "html,body{margin:0;min-height:100%;}" +
               $"body{{background:{background};color:{foreground};font-family:'Segoe UI',sans-serif;}}" +
               "</style></head><body></body></html>";
    }

    private string ToCssColor(string resourceKey)
    {
        if (TryFindResource(resourceKey) is SolidColorBrush brush)
            return $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";

        return "transparent";
    }
}
