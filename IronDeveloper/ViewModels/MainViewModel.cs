using CommunityToolkit.Mvvm.ComponentModel;

namespace IronDev.Agent.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ProjectPanelViewModel ProjectPanel { get; }
    public ChatViewModel Chat { get; }
    public OutputPanelViewModel OutputPanel { get; }

    public MainViewModel(
        ProjectPanelViewModel projectPanel,
        ChatViewModel chat,
        OutputPanelViewModel outputPanel)
    {
        ProjectPanel = projectPanel;
        Chat = chat;
        OutputPanel = outputPanel;

        // Initialize state
        _ = ProjectPanel.LoadMemoryAsync();
        _ = Chat.LoadChatAsync();
    }
}
