namespace IronDev.Agent.ViewModels.Workspaces;

public interface IWorkspaceDirtyState
{
    bool HasDirtyEditState { get; }
    string DirtyEditMessage { get; }
}
