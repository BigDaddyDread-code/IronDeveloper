using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Core.Builder;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class CodexTicketReviewViewModel : ObservableObject
{
    private readonly Func<Task<CodebaseTicketGenerationResult>> _generateAsync;
    private readonly Func<IReadOnlyList<TicketReviewItemViewModel>, Task> _importAsync;

    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private int _contextQualityScore;
    [ObservableProperty] private int _fileCount;
    [ObservableProperty] private int _semanticSymbolCount;
    [ObservableProperty] private int _indexWarningCount;
    [ObservableProperty] private string _contextWarningText = string.Empty;

    public ObservableCollection<TicketReviewItemViewModel> Tickets { get; } = [];

    public IAsyncRelayCommand GenerateCodexTicketsCommand { get; }
    public IAsyncRelayCommand ImportSelectedTicketsCommand { get; }
    public IRelayCommand ClearCommand { get; }

    public bool HasTickets => Tickets.Count > 0;
    public int SelectedTicketCount => Tickets.Count(ticket => ticket.IsSelected);
    public bool HasSelectedTickets => SelectedTicketCount > 0;
    public bool HasContextWarnings => IndexWarningCount > 0 || !string.IsNullOrWhiteSpace(ContextWarningText);
    public string ContextBannerText =>
        $"Codex Context: {ContextQualityScore}/100 · Files: {FileCount} · Symbols: {SemanticSymbolCount} · Warnings: {IndexWarningCount}";

    public CodexTicketReviewViewModel(
        Func<Task<CodebaseTicketGenerationResult>> generateAsync,
        Func<IReadOnlyList<TicketReviewItemViewModel>, Task> importAsync)
    {
        _generateAsync = generateAsync;
        _importAsync = importAsync;
        GenerateCodexTicketsCommand = new AsyncRelayCommand(GenerateCodexTicketsAsync);
        ImportSelectedTicketsCommand = new AsyncRelayCommand(ImportSelectedTicketsAsync);
        ClearCommand = new RelayCommand(Clear);
        Tickets.CollectionChanged += OnTicketsChanged;
    }

    private async Task GenerateCodexTicketsAsync()
    {
        if (IsGenerating) return;

        IsGenerating = true;
        StatusText = "Generating Codex tickets...";

        try
        {
            Tickets.Clear();
            RefreshComputedState();

            CodebaseTicketGenerationResult result;
            try
            {
                result = await _generateAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Generation failed: {ex.Message}";
                return;
            }
            if (!result.Success)
            {
                StatusText = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "Generation failed."
                    : result.ErrorMessage;
                return;
            }

            ContextQualityScore = result.ContextQualityScore;
            FileCount = result.FileCount;
            SemanticSymbolCount = result.SemanticSymbolCount;
            IndexWarningCount = result.IndexWarningCount;
            ContextWarningText = string.Join(", ", result.MissingContextReasons.Concat(result.IndexWarnings).Take(6));

            foreach (var draft in result.Drafts.OrderBy(d => d.SuggestedBuildOrder <= 0 ? int.MaxValue : d.SuggestedBuildOrder))
            {
                Tickets.Add(TicketReviewItemViewModel.FromDraft(draft));
            }

            StatusText = $"Generated {Tickets.Count} Codex tickets.";
        }
        finally
        {
            IsGenerating = false;
            RefreshComputedState();
        }
    }

    private async Task ImportSelectedTicketsAsync()
    {
        var selected = Tickets.Where(ticket => ticket.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = "No tickets selected.";
            return;
        }

        IsImporting = true;
        StatusText = $"Importing {selected.Count} tickets...";

        try
        {
            try
            {
                await _importAsync(selected);
                StatusText = $"Imported {selected.Count} tickets.";
                Tickets.Clear();
            }
            catch (Exception ex)
            {
                StatusText = $"Import failed: {ex.Message}";
            }
        }
        finally
        {
            IsImporting = false;
            RefreshComputedState();
        }
    }

    private void Clear()
    {
        Tickets.Clear();
        StatusText = "Ready";
        RefreshComputedState();
    }

    private void OnTicketsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (TicketReviewItemViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnTicketPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (TicketReviewItemViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnTicketPropertyChanged;
            }
        }

        RefreshComputedState();
    }

    private void OnTicketPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TicketReviewItemViewModel.IsSelected))
        {
            RefreshComputedState();
        }
    }

    private void RefreshComputedState()
    {
        OnPropertyChanged(nameof(HasTickets));
        OnPropertyChanged(nameof(SelectedTicketCount));
        OnPropertyChanged(nameof(HasSelectedTickets));
        OnPropertyChanged(nameof(HasContextWarnings));
        OnPropertyChanged(nameof(ContextBannerText));
        ImportSelectedTicketsCommand.NotifyCanExecuteChanged();
    }
}
