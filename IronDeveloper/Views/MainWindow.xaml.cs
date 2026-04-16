using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using IronDev.Agent.Models;
using IronDev.Agent.ViewModels;

namespace IronDev.Agent.Views;

public partial class MainWindow : Window, IRecipient<OpenWorkbenchMessage>
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        
        WeakReferenceMessenger.Default.Register<OpenWorkbenchMessage>(this);
    }

    public void Receive(OpenWorkbenchMessage message)
    {
        try
        {
            var app = (App)Application.Current;
            var workbenchWindow = app.Services.GetRequiredService<CodeWorkbenchWindow>();
            
            if (workbenchWindow.DataContext is CodeWorkbenchViewModel vm)
            {
                vm.TicketId = message.TicketId;
                _ = vm.LoadTicketCommand.ExecuteAsync(null);
                
                workbenchWindow.Owner = this;
                workbenchWindow.Show();
            }
            // else: VM not set, which shouldn't happen with our DI setup
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new StatusMessage($"Error opening workbench: {ex.Message}"));
        }
    }

    private void Ticket_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is TicketItem ticket)
        {
            var vm = (MainViewModel)DataContext;
            vm.ProjectPanel.SelectTicketCommand.Execute(ticket);
        }
    }
}
