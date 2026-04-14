using System.Windows;
using IronDev.Agent.ViewModels;

namespace IronDev.Agent.Views;

public partial class CodeWorkbenchWindow : Window
{
    public CodeWorkbenchWindow(CodeWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
