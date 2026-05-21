using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Core.Interfaces;
using IronDev.Data.Models;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class DocumentsWorkspaceViewModel : ObservableObject, IWorkspaceDirtyState
{
    private readonly IProjectDocumentService _documentService;
    private readonly IMarkdownRenderService _markdownRenderer;

    private int _activeProjectId;

    // ── List state ────────────────────────────────────────────────────
    [ObservableProperty] private string _filterType = string.Empty; // empty = all
    [ObservableProperty] private ProjectDocumentItemViewModel? _selectedDocument;
    [ObservableProperty] private bool _isLoadingDocuments;

    // ── Version state ─────────────────────────────────────────────────
    [ObservableProperty] private ProjectDocumentVersionItemViewModel? _selectedVersion;
    [ObservableProperty] private bool _isLoadingVersions;

    // ── Editor state ──────────────────────────────────────────────────
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isCreatingDocument;
    [ObservableProperty] private string _editorTitle = string.Empty;
    [ObservableProperty] private string _editorDocumentType = "DiscussionSummary";
    [ObservableProperty] private string _editorMarkdown = string.Empty;
    [ObservableProperty] private string _editorChangeSummary = string.Empty;
    [ObservableProperty] private bool _isSavingVersion;

    // ── Status ────────────────────────────────────────────────────────
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _hasError;

    // ── Rendered HTML (bound to WebView2 via code-behind) ─────────────
    [ObservableProperty] private string _renderedHtml = string.Empty;

    public ObservableCollection<ProjectDocumentItemViewModel> Documents { get; } = [];
    public ObservableCollection<ProjectDocumentVersionItemViewModel> VersionHistory { get; } = [];

    public bool HasDocuments => Documents.Count > 0;
    public bool HasSelectedDocument => SelectedDocument != null;
    public bool HasSelectedVersion => SelectedVersion != null;
    public bool IsViewMode => !IsEditing;
    public bool HasStatusText => !string.IsNullOrEmpty(StatusText);
    public bool HasDirtyEditState => IsEditing && CanSaveVersion;
    public string DirtyEditMessage => "This document has unsaved edit text. Leave Documents and discard those changes?";
    public bool CanSaveVersion => IsEditing
        && (!IsCreatingDocument || !string.IsNullOrWhiteSpace(EditorTitle))
        && !string.IsNullOrWhiteSpace(EditorMarkdown)
        && !string.IsNullOrWhiteSpace(EditorChangeSummary)
        && !IsSavingVersion;

    // Filter chip labels
    public static string[] DocumentTypes { get; } =
        ["All", "Architecture", "BuildPlan", "DecisionLog", "DiscussionSummary"];

    public DocumentsWorkspaceViewModel(
        IProjectDocumentService documentService,
        IMarkdownRenderService markdownRenderer)
    {
        _documentService = documentService;
        _markdownRenderer = markdownRenderer;
    }

    // ------------------------------------------------------------------
    // Load
    // ------------------------------------------------------------------

    public async Task LoadAsync(Project project)
    {
        _activeProjectId = project.Id;
        FilterType = string.Empty;
        await LoadDocumentsAsync();
    }

    // ------------------------------------------------------------------
    // Commands — list
    // ------------------------------------------------------------------

    [RelayCommand]
    private async Task SetFilterTypeAsync(string type)
    {
        FilterType = type == "All" ? string.Empty : type;
        await LoadDocumentsAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
        => await LoadDocumentsAsync();

    [RelayCommand]
    private void NewDocument()
    {
        SelectedDocument = null;
        SelectedVersion = null;
        VersionHistory.Clear();
        RenderedHtml = string.Empty;
        EditorTitle = string.Empty;
        EditorDocumentType = "DiscussionSummary";
        EditorMarkdown = "# New discussion document\n\n";
        EditorChangeSummary = "Initial version";
        IsCreatingDocument = true;
        IsEditing = true;
        StatusText = "Creating a new document.";
        RefreshComputedState();
        SaveNewVersionCommand.NotifyCanExecuteChanged();
    }

    // ------------------------------------------------------------------
    // Commands — editor
    // ------------------------------------------------------------------

    [RelayCommand]
    private void StartEditing()
    {
        if (SelectedVersion == null) return;

        IsCreatingDocument = false;
        EditorTitle = SelectedDocument?.Title ?? string.Empty;
        EditorDocumentType = SelectedDocument?.DocumentType ?? "DiscussionSummary";
        EditorMarkdown = SelectedVersion.ContentMarkdown;
        EditorChangeSummary = string.Empty;
        IsEditing = true;
        RefreshComputedState();
    }

    [RelayCommand]
    private void CancelEditing()
    {
        IsEditing = false;
        IsCreatingDocument = false;
        EditorTitle = string.Empty;
        EditorMarkdown = string.Empty;
        EditorChangeSummary = string.Empty;
        RefreshComputedState();
    }

    [RelayCommand(CanExecute = nameof(CanSaveVersion))]
    private async Task SaveNewVersionAsync()
    {
        if (string.IsNullOrWhiteSpace(EditorMarkdown)
            || string.IsNullOrWhiteSpace(EditorChangeSummary))
            return;

        IsSavingVersion = true;
        HasError = false;

        try
        {
            if (IsCreatingDocument)
            {
                if (string.IsNullOrWhiteSpace(EditorTitle))
                {
                    StatusText = "Title is required.";
                    HasError = true;
                    return;
                }

                var document = await _documentService.CreateDocumentAsync(new CreateProjectDocumentRequest
                {
                    ProjectId = _activeProjectId,
                    Title = EditorTitle.Trim(),
                    DocumentType = string.IsNullOrWhiteSpace(EditorDocumentType) ? "DiscussionSummary" : EditorDocumentType,
                    ContentMarkdown = EditorMarkdown,
                    ChangeSummary = EditorChangeSummary,
                    CreatedBy = "IronDev"
                });

                IsEditing = false;
                IsCreatingDocument = false;
                EditorMarkdown = string.Empty;
                EditorChangeSummary = string.Empty;
                await LoadDocumentsAsync();
                SelectedDocument = Documents.FirstOrDefault(d => d.Id == document.Id);
                StatusText = $"Created \"{document.Title}\".";
                return;
            }

            if (SelectedDocument == null)
                return;

            var newVersion = await _documentService.AddVersionAsync(new AddProjectDocumentVersionRequest
            {
                DocumentId      = SelectedDocument.Id,
                ContentMarkdown = EditorMarkdown,
                ChangeSummary   = EditorChangeSummary,
                Status          = "Draft"
            });

            IsEditing = false;
            EditorMarkdown = string.Empty;
            EditorChangeSummary = string.Empty;

            // Reload the version history and select the new version
            await LoadVersionHistoryAsync(SelectedDocument.Id);

            SelectedVersion = VersionHistory.FirstOrDefault(v => v.Id == newVersion.Id)
                           ?? VersionHistory.FirstOrDefault();

            StatusText = $"Saved {newVersion.VersionLabel} — {EditorChangeSummary}";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("identical"))
        {
            StatusText = "No changes detected — content is identical to the current version.";
            HasError = true;
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsSavingVersion = false;
            RefreshComputedState();
        }
    }

    [RelayCommand]
    private async Task ArchiveDocumentAsync()
    {
        if (SelectedDocument == null) return;
        await _documentService.ArchiveDocumentAsync(SelectedDocument.Id);
        StatusText = $"\"{SelectedDocument.Title}\" archived.";
        await LoadDocumentsAsync();
    }

    [RelayCommand]
    private void CopyDocument()
    {
        if (SelectedDocument == null || SelectedVersion == null)
            return;

        var sb = new StringBuilder();
        sb.AppendLine($"# {SelectedDocument.Title}");
        sb.AppendLine();
        sb.AppendLine($"- Type: {SelectedDocument.DocumentType}");
        sb.AppendLine($"- Status: {SelectedDocument.Status}");
        sb.AppendLine($"- Version: {SelectedVersion.VersionLabel}");
        sb.AppendLine();
        sb.AppendLine(SelectedVersion.ContentMarkdown.Trim());
        Clipboard.SetText(sb.ToString().TrimEnd());
        StatusText = "Document copied.";
    }

    // ------------------------------------------------------------------
    // Property change handlers
    // ------------------------------------------------------------------

    partial void OnSelectedDocumentChanged(ProjectDocumentItemViewModel? value)
    {
        IsEditing = false;
        IsCreatingDocument = false;
        VersionHistory.Clear();
        SelectedVersion = null;
        RenderedHtml = string.Empty;
        StatusText = string.Empty;
        HasError = false;

        if (value != null)
            _ = LoadVersionHistoryAndSelectCurrentAsync(value.Id, value.CurrentVersionId);

        RefreshComputedState();
    }

    partial void OnSelectedVersionChanged(ProjectDocumentVersionItemViewModel? value)
    {
        if (value != null)
            RenderSelectedVersion(value);
        else
            RenderedHtml = string.Empty;

        RefreshComputedState();
    }

    partial void OnIsEditingChanged(bool value)
        => RefreshComputedState();

    partial void OnEditorMarkdownChanged(string value)
    {
        RefreshComputedState();
        SaveNewVersionCommand.NotifyCanExecuteChanged();
    }

    partial void OnEditorTitleChanged(string value)
    {
        RefreshComputedState();
        SaveNewVersionCommand.NotifyCanExecuteChanged();
    }

    partial void OnEditorChangeSummaryChanged(string value)
    {
        RefreshComputedState();
        SaveNewVersionCommand.NotifyCanExecuteChanged();
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private async Task LoadDocumentsAsync()
    {
        IsLoadingDocuments = true;
        HasError = false;

        try
        {
            var docs = await _documentService.GetDocumentsAsync(new GetProjectDocumentsRequest
            {
                ProjectId    = _activeProjectId,
                DocumentType = string.IsNullOrWhiteSpace(FilterType) ? null : FilterType,
                Status       = "Active"
            });

            var previousId = SelectedDocument?.Id;
            Documents.Clear();

            foreach (var doc in docs)
                Documents.Add(new ProjectDocumentItemViewModel(doc));

            SelectedDocument = previousId.HasValue
                ? Documents.FirstOrDefault(d => d.Id == previousId.Value)
                : Documents.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load documents: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsLoadingDocuments = false;
            RefreshComputedState();
        }
    }

    private async Task LoadVersionHistoryAndSelectCurrentAsync(long documentId, long? currentVersionId)
    {
        await LoadVersionHistoryAsync(documentId);

        SelectedVersion = currentVersionId.HasValue
            ? VersionHistory.FirstOrDefault(v => v.Id == currentVersionId.Value)
            : VersionHistory.FirstOrDefault();
    }

    private async Task LoadVersionHistoryAsync(long documentId)
    {
        IsLoadingVersions = true;

        try
        {
            var versions = await _documentService.GetVersionHistoryAsync(documentId);
            var currentVersionId = SelectedDocument?.CurrentVersionId;

            VersionHistory.Clear();
            foreach (var v in versions)
                VersionHistory.Add(new ProjectDocumentVersionItemViewModel(v, v.Id == currentVersionId));
        }
        finally
        {
            IsLoadingVersions = false;
        }
    }

    private void RenderSelectedVersion(ProjectDocumentVersionItemViewModel version)
    {
        RenderedHtml = _markdownRenderer.ToStyledHtmlDocument(version.ContentMarkdown);
    }

    private void RefreshComputedState()
    {
        OnPropertyChanged(nameof(HasDocuments));
        OnPropertyChanged(nameof(HasSelectedDocument));
        OnPropertyChanged(nameof(HasSelectedVersion));
        OnPropertyChanged(nameof(IsViewMode));
        OnPropertyChanged(nameof(HasStatusText));
        OnPropertyChanged(nameof(CanSaveVersion));
        OnPropertyChanged(nameof(HasDirtyEditState));
    }
}

// =====================================================================
// Item ViewModels
// =====================================================================

public sealed class ProjectDocumentItemViewModel
{
    private readonly ProjectDocument _doc;

    public ProjectDocumentItemViewModel(ProjectDocument doc) => _doc = doc;

    public long Id => _doc.Id;
    public string Title => _doc.Title;
    public string DocumentType => _doc.DocumentType;
    public string Status => _doc.Status;
    public long? CurrentVersionId => _doc.CurrentVersionId;

    public string TypeLabel => _doc.DocumentType switch
    {
        "Architecture"      => "Architecture",
        "BuildPlan"         => "Build Plan",
        "DecisionLog"       => "Decision Log",
        "DiscussionSummary" => "Discussion",
        _                   => _doc.DocumentType
    };

    public string LastUpdatedLabel
    {
        get
        {
            var dt = (_doc.UpdatedAtUtc ?? _doc.CreatedAtUtc).ToLocalTime();
            return (DateTime.Today - dt.Date).Days == 0
                ? dt.ToString("h:mm tt")
                : dt.ToString("MMM d");
        }
    }
}

public sealed class ProjectDocumentVersionItemViewModel
{
    private readonly ProjectDocumentVersion _version;

    public ProjectDocumentVersionItemViewModel(ProjectDocumentVersion version, bool isCurrent)
    {
        _version = version;
        IsCurrent = isCurrent;
    }

    public long Id => _version.Id;
    public string VersionLabel => _version.VersionLabel;
    public string Status => _version.Status;
    public string? ChangeSummary => _version.ChangeSummary;
    public string ContentMarkdown => _version.ContentMarkdown;
    public bool IsCurrent { get; }

    public string DateLabel
    {
        get
        {
            var dt = _version.CreatedAtUtc.ToLocalTime();
            return (DateTime.Today - dt.Date).Days == 0
                ? dt.ToString("h:mm tt")
                : dt.ToString("MMM d");
        }
    }

    public string DisplayLabel => IsCurrent
        ? $"{VersionLabel}  ·  {Status}  ·  Current"
        : $"{VersionLabel}  ·  {Status}";
}
