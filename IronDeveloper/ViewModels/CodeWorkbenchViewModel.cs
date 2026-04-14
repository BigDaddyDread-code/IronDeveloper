using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Data.Models;
using IronDev.Services;
using IronDev.AI;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.Agent.ViewModels;

public partial class CodeWorkbenchViewModel : ObservableObject
{
    private readonly ITicketService _ticketService;
    private readonly IWorkbenchGeneratorService _generatorService;
    private readonly ICodeIndexService _codeIndexService;
    private readonly IPromptContextBuilder _promptContextBuilder;

    [ObservableProperty]
    private long _ticketId;

    [ObservableProperty]
    private ProjectTicket? _loadedTicket;

    [ObservableProperty]
    private ImplementationPlanResult? _implementationPlan;

    [ObservableProperty]
    private string _generatedCode = string.Empty;

    [ObservableProperty]
    private string _generatedTestCode = string.Empty;

    [ObservableProperty]
    private string _outputLog = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _selectedTargetFilePath = string.Empty;
    
    // For integration testing binding validation
    public bool LoadTriggered { get; private set; }

    public CodeWorkbenchViewModel(
        ITicketService ticketService,
        IWorkbenchGeneratorService generatorService,
        ICodeIndexService codeIndexService,
        IPromptContextBuilder promptContextBuilder)
    {
        _ticketService = ticketService;
        _generatorService = generatorService;
        _codeIndexService = codeIndexService;
        _promptContextBuilder = promptContextBuilder;
    }

    private void Log(string message)
    {
        OutputLog += $"[{DateTime.Now:T}] {message}\n";
    }

    [RelayCommand]
    public async Task LoadTicketAsync()
    {
        if (TicketId <= 0) return;
        
        IsBusy = true;
        Log($"Loading full ticket ID: {TicketId}...");
        
        try
        {
            LoadedTicket = await _ticketService.GetTicketByIdAsync((int)TicketId); // DB uses BIGINT but ID passes around as int/long, assuming cast
            
            if (LoadedTicket != null)
                Log($"Loaded Ticket: {LoadedTicket.Title}");
            else
                Log("Error: Ticket not found.");
                
            LoadTriggered = true;
        }
        catch (Exception ex)
        {
            Log($"Error loading ticket: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task GeneratePlanAsync()
    {
        if (LoadedTicket == null) return;
        
        IsBusy = true;
        Log("Generating structured Implementation Plan...");
        
        try
        {
            var rawContext = await _promptContextBuilder.BuildAsync(LoadedTicket.ProjectId, LoadedTicket.SessionId, "What components are needed?");
            ImplementationPlan = await _generatorService.GeneratePlanAsync(LoadedTicket.ProjectId, LoadedTicket, rawContext);
            SelectedTargetFilePath = ImplementationPlan.TargetFilePath;
            
            Log("Plan Generated Successfully.");
        }
        catch (Exception ex)
        {
            Log($"Error generating plan: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task GenerateCodeDraftAsync()
    {
        if (LoadedTicket == null || ImplementationPlan == null) return;
        
        IsBusy = true;
        Log($"Drafting code for: {SelectedTargetFilePath}...");
        
        try
        {
            var rawContext = await _promptContextBuilder.BuildAsync(LoadedTicket.ProjectId, LoadedTicket.SessionId, $"Code drafting for {SelectedTargetFilePath}");
            var draft = await _generatorService.GenerateCodeDraftAsync(LoadedTicket, ImplementationPlan, rawContext);
            
            GeneratedCode = draft.Code;
            Log("Code Draft Generated.");
        }
        catch (Exception ex)
        {
            Log($"Error generating code: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task GenerateTestDraftAsync()
    {
        if (LoadedTicket == null || ImplementationPlan == null || string.IsNullOrWhiteSpace(GeneratedCode)) return;
        
        IsBusy = true;
        Log("Drafting test suite...");
        
        try
        {
            var draft = new CodeDraftResult { Code = GeneratedCode, FilePath = SelectedTargetFilePath };
            var testDraft = await _generatorService.GenerateTestDraftAsync(LoadedTicket, ImplementationPlan, draft, "");
            
            GeneratedTestCode = testDraft.Code;
            Log("Test Draft Generated.");
        }
        catch (Exception ex)
        {
            Log($"Error generating tests: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void ClearLog()
    {
        OutputLog = string.Empty;
    }
}
