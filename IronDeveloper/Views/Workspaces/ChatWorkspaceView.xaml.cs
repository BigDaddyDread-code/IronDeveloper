using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using IronDev.Agent.ViewModels.Workspaces;

namespace IronDev.Agent.Views.Workspaces;

public partial class ChatWorkspaceView : UserControl
{
    public ChatWorkspaceView()
    {
        InitializeComponent();
        
        DataContextChanged += (s, e) =>
        {
            if (e.NewValue is ChatWorkspaceViewModel vm)
            {
                vm.Messages.CollectionChanged += (s2, e2) =>
                {
                    if (e2.Action == NotifyCollectionChangedAction.Add)
                    {
                        Dispatcher.BeginInvoke(() => MessagesScroller.ScrollToEnd());
                    }
                };
            }
        };
    }


}
