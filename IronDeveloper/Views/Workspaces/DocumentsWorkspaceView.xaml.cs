using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Controls;
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

            // If HTML was set before WebView2 was ready, render it now
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
                MarkdownViewer.NavigateToString("<html><body style='background:#0D1117'></body></html>");
            else
                MarkdownViewer.NavigateToString(vm.RenderedHtml);
        });
    }
}
