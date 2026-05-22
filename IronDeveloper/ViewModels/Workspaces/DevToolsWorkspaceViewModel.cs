using CommunityToolkit.Mvvm.ComponentModel;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class DevToolsWorkspaceViewModel : ObservableObject
{
    private readonly Func<LlmConsoleViewModel> _llmConsoleFactory;
    private readonly Func<TestingCompanionViewModel> _testingCompanionFactory;
    private readonly Func<PromptPlaygroundViewModel> _promptPlaygroundFactory;

    private LlmConsoleViewModel? _llmConsole;
    private TestingCompanionViewModel? _testingCompanion;
    private PromptPlaygroundViewModel? _promptPlayground;

    public DevToolsWorkspaceViewModel(
        Func<LlmConsoleViewModel> llmConsoleFactory,
        Func<TestingCompanionViewModel> testingCompanionFactory,
        Func<PromptPlaygroundViewModel> promptPlaygroundFactory)
    {
        _llmConsoleFactory = llmConsoleFactory;
        _testingCompanionFactory = testingCompanionFactory;
        _promptPlaygroundFactory = promptPlaygroundFactory;
    }

    public LlmConsoleViewModel LlmConsole => _llmConsole ??= _llmConsoleFactory();
    public TestingCompanionViewModel TestingCompanion => _testingCompanion ??= _testingCompanionFactory();
    public PromptPlaygroundViewModel PromptPlayground => _promptPlayground ??= _promptPlaygroundFactory();
}
