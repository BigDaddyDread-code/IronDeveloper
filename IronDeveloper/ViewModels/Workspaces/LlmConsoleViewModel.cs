using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class LlmConsoleViewModel : ObservableObject
{
    private readonly ILlmTraceService _traceService;

    [ObservableProperty] private ObservableCollection<LlmTraceEntry> _traces = new();
    [ObservableProperty] private LlmTraceEntry? _selectedTrace;
    [ObservableProperty] private string _filterText = string.Empty;

    // ── Advanced Filters ──────────────────────────────────────────────────
    [ObservableProperty] private int? _filterProjectId;
    [ObservableProperty] private string? _filterChatSessionId;
    [ObservableProperty] private long? _filterTicketId;
    [ObservableProperty] private string? _filterFeature;
    [ObservableProperty] private bool _filterFailuresOnly;

    public LlmConsoleViewModel(ILlmTraceService traceService)
    {
        _traceService = traceService;
        Refresh();
    }

    [RelayCommand]
    public void Refresh()
    {
        var recent = _traceService.GetRecentTraces(200);
        
        var filtered = recent.Where(t => 
        {
            // Text search (Request, Response, Feature)
            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                var match = (t.FeatureName?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                            (t.RequestText?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                            (t.RawResponseText?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ?? false);
                if (!match) return false;
            }

            // Failure filter
            if (FilterFailuresOnly && t.WasSuccessful) return false;

            // Metadata filters
            if (FilterProjectId.HasValue && t.ProjectId != FilterProjectId.Value) return false;
            if (FilterTicketId.HasValue && t.TicketId != FilterTicketId.Value) return false;
            if (!string.IsNullOrWhiteSpace(FilterChatSessionId) && t.ChatSessionId != FilterChatSessionId) return false;
            if (!string.IsNullOrWhiteSpace(FilterFeature) && !t.FeatureName.Equals(FilterFeature, StringComparison.OrdinalIgnoreCase)) return false;

            return true;
        });

        Traces.Clear();
        foreach (var t in filtered)
        {
            Traces.Add(t);
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _traceService.Clear();
        Refresh();
    }

    [RelayCommand]
    private void CopyPrompt()
    {
        if (SelectedTrace != null)
            Clipboard.SetText(SelectedTrace.RequestText);
    }

    [RelayCommand]
    private void CopyResponse()
    {
        if (SelectedTrace != null)
            Clipboard.SetText(SelectedTrace.RawResponseText);
    }

    [RelayCommand]
    private void CopyFullTrace()
    {
        if (SelectedTrace != null)
            Clipboard.SetText(_traceService.ExportTrace(SelectedTrace));
    }

    [RelayCommand]
    private void ExportAll()
    {
        Clipboard.SetText(_traceService.ExportAll());
    }

    partial void OnFilterTextChanged(string value) => Refresh();
    partial void OnFilterProjectIdChanged(int? value) => Refresh();
    partial void OnFilterChatSessionIdChanged(string? value) => Refresh();
    partial void OnFilterTicketIdChanged(long? value) => Refresh();
    partial void OnFilterFeatureChanged(string? value) => Refresh();
    partial void OnFilterFailuresOnlyChanged(bool value) => Refresh();
}
