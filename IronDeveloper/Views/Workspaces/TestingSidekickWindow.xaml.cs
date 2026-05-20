using System.Windows;
using IronDev.Agent.ViewModels.Workspaces;

namespace IronDev.Agent.Views.Workspaces;

public partial class TestingSidekickWindow : Window
{
    private readonly Func<Task> _markMoment;
    private TestingCompanionViewModel ViewModel => (TestingCompanionViewModel)DataContext;

    public TestingSidekickWindow(TestingCompanionViewModel viewModel, Func<Task> markMoment)
    {
        InitializeComponent();
        DataContext = viewModel;
        _markMoment = markMoment;
    }

    public void PositionNear(Window owner)
    {
        Left = owner.Left + owner.ActualWidth - Width - 34;
        Top = owner.Top + 78;
    }

    private async void Mark_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        await _markMoment();
        Show();
        Activate();
    }

    private async void CopyLast_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.CopyLastPromptAsync();
    }

    private async void CopyBundle_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.CopyBundlePromptAsync();
    }

    private async void Finish_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.FinishSessionAsync();
    }

    private void OpenReport_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenLatestReport();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}
