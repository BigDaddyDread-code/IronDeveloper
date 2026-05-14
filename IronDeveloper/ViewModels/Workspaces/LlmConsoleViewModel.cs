using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class LlmConsoleViewModel : ObservableObject, IDisposable
{
    private readonly ILlmTraceService _traceService;
    private bool _disposed;

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
        _traceService.TraceAdded += OnTraceAdded;
        Refresh();
    }

    // ── Live update ───────────────────────────────────────────────────────

    private void OnTraceAdded(object? sender, LlmTraceEntry entry)
    {
        // TraceAdded fires on the LLM call thread — dispatch to UI thread
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_disposed) return;

            // Apply the current filter to decide if this trace belongs in the list
            if (!MatchesFilter(entry)) return;

            // Insert at top (traces are newest-first)
            Traces.Insert(0, entry);

            // Cap displayed traces at 200
            while (Traces.Count > 200)
                Traces.RemoveAt(Traces.Count - 1);

            // Auto-select if nothing was selected
            if (SelectedTrace == null)
                SelectedTrace = entry;
        });
    }

    // ── Refresh ───────────────────────────────────────────────────────────

    [RelayCommand]
    public void Refresh()
    {
        var recent   = _traceService.GetRecentTraces(200);
        var filtered = recent.Where(MatchesFilter).ToList();

        // Preserve selection if the entry still exists in the new list
        var previousId = SelectedTrace?.Id;

        Traces.Clear();
        foreach (var t in filtered)
            Traces.Add(t);

        // Restore selection or default to newest
        if (previousId.HasValue)
            SelectedTrace = Traces.FirstOrDefault(t => t.Id == previousId.Value);

        if (SelectedTrace == null && Traces.Count > 0)
            SelectedTrace = Traces[0];
    }

    private bool MatchesFilter(LlmTraceEntry t)
    {
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var match = (t.FeatureName?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (t.RequestText?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (t.RawResponseText?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!match) return false;
        }

        if (FilterFailuresOnly && t.WasSuccessful) return false;
        if (FilterProjectId.HasValue && t.ProjectId != FilterProjectId.Value) return false;
        if (FilterTicketId.HasValue && t.TicketId != FilterTicketId.Value) return false;
        if (!string.IsNullOrWhiteSpace(FilterChatSessionId) &&
            t.ChatSessionId != FilterChatSessionId) return false;
        if (!string.IsNullOrWhiteSpace(FilterFeature) &&
            !(t.FeatureName?.Equals(FilterFeature, StringComparison.OrdinalIgnoreCase) ?? false)) return false;

        return true;
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void Clear()
    {
        _traceService.Clear();
        Traces.Clear();
        SelectedTrace = null;
    }

    [RelayCommand]
    private void CopyPrompt()
    {
        if (SelectedTrace != null && !string.IsNullOrEmpty(SelectedTrace.RequestText))
            Clipboard.SetText(SelectedTrace.RequestText);
    }

    [RelayCommand]
    private void CopyResponse()
    {
        if (SelectedTrace != null && !string.IsNullOrEmpty(SelectedTrace.RawResponseText))
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
        var text = _traceService.ExportAll();
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    // ── Filter change callbacks ───────────────────────────────────────────

    partial void OnFilterTextChanged(string value)          => Refresh();
    partial void OnFilterProjectIdChanged(int? value)       => Refresh();
    partial void OnFilterChatSessionIdChanged(string? value)=> Refresh();
    partial void OnFilterTicketIdChanged(long? value)       => Refresh();
    partial void OnFilterFeatureChanged(string? value)      => Refresh();
    partial void OnFilterFailuresOnlyChanged(bool value)    => Refresh();

    // ── Dispose ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _traceService.TraceAdded -= OnTraceAdded;
    }
}
