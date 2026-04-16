using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using IronDev.Agent.Models;

namespace IronDev.Agent.ViewModels;

public partial class MainViewModel : ObservableObject, IRecipient<TicketSelectedMessage>
{
    public ProjectPanelViewModel ProjectPanel { get; }
    public ChatViewModel Chat { get; }
    public OutputPanelViewModel OutputPanel { get; }

    [ObservableProperty]
    private long _selectedTicketId;

    public MainViewModel(
        ProjectPanelViewModel projectPanel,
        ChatViewModel chat,
        OutputPanelViewModel outputPanel)
    {
        ProjectPanel = projectPanel;
        Chat = chat;
        OutputPanel = outputPanel;

        WeakReferenceMessenger.Default.Register<TicketSelectedMessage>(this);

        // Initialize state
        _ = ProjectPanel.LoadMemoryAsync();
        _ = Chat.LoadChatAsync();
    }

    public void Receive(TicketSelectedMessage message)
    {
        SelectedTicketId = message.Ticket.Id;
    }
}
