using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Data.Models;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class DecisionsWorkspaceViewModel : ObservableObject
{
    private readonly global::IronDev.Services.IProjectMemoryService _memoryService;
    private int _activeProjectId;

    [ObservableProperty] private ObservableCollection<ProjectContextDocument> _documents = [];
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DiscussSelectedDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditSelectedDocumentCommand))]
    private ProjectContextDocument? _selectedDocument;
    [ObservableProperty] private bool _hasDetail;
    [ObservableProperty] private bool _isEditingDocument;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string _saveStatus = string.Empty;

    [ObservableProperty] private string _projectSummary = string.Empty;
    [ObservableProperty] private string _summarySaveStatus = string.Empty;

    [ObservableProperty] private long _editId;
    [ObservableProperty] private string _editDocumentType = "ProjectFact";
    [ObservableProperty] private string _editAuthorityLevel = "ObservedFact";
    [ObservableProperty] private string _editStatus = "Active";
    [ObservableProperty] private string _editTitle = string.Empty;
    [ObservableProperty] private string _editSummary = string.Empty;
    [ObservableProperty] private string _editContent = string.Empty;
    [ObservableProperty] private string _editTags = string.Empty;
    [ObservableProperty] private string _editAppliesToArea = string.Empty;
    [ObservableProperty] private string _editAppliesToCapability = string.Empty;
    [ObservableProperty] private string _editSource = string.Empty;

    [ObservableProperty] private string _filterDocumentType = "All";
    [ObservableProperty] private string _filterStatus = "Active";

    public string SelectedDocumentBody
    {
        get
        {
            if (!HasDetail)
                return string.Empty;

            if (string.IsNullOrWhiteSpace(EditSummary))
                return EditContent;

            if (string.IsNullOrWhiteSpace(EditContent))
                return EditSummary;

            return $"{EditSummary.Trim()}\n\n{EditContent.Trim()}";
        }
    }

    public string SelectedDocumentMeta
    {
        get
        {
            if (!HasDetail)
                return string.Empty;

            var updated = SelectedDocument?.UpdatedDate ?? SelectedDocument?.CreatedDate;
            var updatedText = updated is null || updated.Value == default
                ? string.Empty
                : $"Updated {updated.Value.ToLocalTime():MMM d, h:mm tt}";

            var source = string.IsNullOrWhiteSpace(EditSource) ? string.Empty : $"Source {EditSource.Trim()}";
            return string.Join("  |  ", new[] { EditDocumentType, EditAuthorityLevel, EditStatus, source, updatedText }
                .Where(static value => !string.IsNullOrWhiteSpace(value)));
        }
    }

    public string ProjectSummaryPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ProjectSummary))
                return "No project summary saved yet.";

            var normalized = ProjectSummary.Replace("\r\n", " ").Replace('\n', ' ').Trim();
            return normalized.Length <= 180 ? normalized : normalized[..177] + "...";
        }
    }

    public ObservableCollection<string> DocumentTypeOptions { get; } =
    [
        "ArchitectureDecision",
        "ProjectStandard",
        "ProjectFact",
        "TechnologyNote",
        "DiscussionNote",
        "Constraint",
        "OpenQuestion",
        "Recommendation",
        "ImplementationNote",
        "ProductBlueprint",
        "CapabilityNote",
        "MilestoneNote"
    ];

    public ObservableCollection<string> DocumentTypeFilterOptions { get; } =
    [
        "All",
        "ArchitectureDecision",
        "ProjectStandard",
        "ProjectFact",
        "TechnologyNote",
        "DiscussionNote",
        "Constraint",
        "OpenQuestion",
        "Recommendation",
        "ImplementationNote",
        "ProductBlueprint",
        "CapabilityNote",
        "MilestoneNote"
    ];

    public ObservableCollection<string> AuthorityLevelOptions { get; } =
    [
        "Binding",
        "StrongGuidance",
        "ObservedFact",
        "ContextOnly",
        "Pending",
        "Rejected",
        "Superseded"
    ];

    public ObservableCollection<string> StatusOptions { get; } =
    [
        "Active",
        "Pending",
        "Accepted",
        "Rejected",
        "Superseded",
        "Archived"
    ];

    public ObservableCollection<string> StatusFilterOptions { get; } =
    [
        "Active",
        "Pending",
        "Accepted",
        "All",
        "Archived"
    ];

    public Action<string>? OnDiscussDocumentInChat { get; set; }

    public DecisionsWorkspaceViewModel(
        global::IronDev.Services.IProjectMemoryService memoryService,
        global::IronDev.Services.ILookupService lookupService)
    {
        _memoryService = memoryService;
    }

    internal async Task LoadAsync(Project project)
    {
        _activeProjectId = project.Id;
        await LoadSummaryAsync();
        await RefreshListAsync();
    }

    private async Task LoadSummaryAsync()
    {
        var summary = await _memoryService.GetLatestSummaryAsync(_activeProjectId);
        ProjectSummary = summary?.Summary ?? string.Empty;
        SummarySaveStatus = string.Empty;
    }

    [RelayCommand]
    private async Task SaveSummaryAsync()
    {
        if (_activeProjectId <= 0) return;

        SummarySaveStatus = "Saving...";
        await _memoryService.SaveSummaryAsync(new ProjectSummary
        {
            ProjectId = _activeProjectId,
            Summary = ProjectSummary.Trim(),
            UpdatedDate = DateTime.UtcNow
        });

        SummarySaveStatus = "Saved";
    }

    [RelayCommand]
    private Task RefreshListAsync()
    {
        var preferredDocumentId = SelectedDocument?.Id > 0 ? SelectedDocument.Id : (long?)null;
        return RefreshDocumentsAsync(preferredDocumentId);
    }

    private async Task RefreshDocumentsAsync(
        long? preferredDocumentId = null,
        bool includePreferredIfFilteredOut = false)
    {
        var documentType = FilterDocumentType == "All" ? null : FilterDocumentType;
        var status = FilterStatus == "All" ? null : FilterStatus;
        var documents = await _memoryService.GetContextDocumentsAsync(
            _activeProjectId,
            documentType: documentType,
            status: status,
            take: 200);

        var documentList = documents.ToList();
        if (preferredDocumentId is > 0 &&
            includePreferredIfFilteredOut &&
            documentList.All(d => d.Id != preferredDocumentId.Value))
        {
            var preferredDocument = await _memoryService.GetContextDocumentByIdAsync(preferredDocumentId.Value);
            if (preferredDocument != null && preferredDocument.ProjectId == _activeProjectId)
                documentList.Insert(0, preferredDocument);
        }

        Documents.Clear();
        foreach (var document in documentList)
            Documents.Add(document);

        var selectedDocument = preferredDocumentId is > 0
            ? Documents.FirstOrDefault(d => d.Id == preferredDocumentId.Value)
            : null;

        if (selectedDocument != null)
        {
            SelectedDocument = selectedDocument;
        }
        else if (!IsEditingDocument)
        {
            SelectedDocument = null;
        }
    }

    partial void OnSelectedDocumentChanged(ProjectContextDocument? value)
    {
        if (value == null)
        {
            ClearEditor();
            return;
        }

        LoadDocumentIntoEditor(value);
    }

    partial void OnHasDetailChanged(bool value)
        => NotifyDocumentPreviewChanged();

    partial void OnProjectSummaryChanged(string value)
        => OnPropertyChanged(nameof(ProjectSummaryPreview));

    partial void OnEditDocumentTypeChanged(string value)
        => OnPropertyChanged(nameof(SelectedDocumentMeta));

    partial void OnEditAuthorityLevelChanged(string value)
        => OnPropertyChanged(nameof(SelectedDocumentMeta));

    partial void OnEditStatusChanged(string value)
        => OnPropertyChanged(nameof(SelectedDocumentMeta));

    partial void OnEditSummaryChanged(string value)
        => OnPropertyChanged(nameof(SelectedDocumentBody));

    partial void OnEditContentChanged(string value)
        => OnPropertyChanged(nameof(SelectedDocumentBody));

    partial void OnEditSourceChanged(string value)
        => OnPropertyChanged(nameof(SelectedDocumentMeta));

    partial void OnFilterDocumentTypeChanged(string value)
        => _ = RefreshListAsync();

    partial void OnFilterStatusChanged(string value)
        => _ = RefreshListAsync();

    [RelayCommand]
    private void NewDocument()
    {
        SelectedDocument = null;
        HasDetail = true;
        IsEditingDocument = true;
        EditId = 0;
        EditDocumentType = "ProjectFact";
        EditAuthorityLevel = "ObservedFact";
        EditStatus = "Active";
        EditTitle = string.Empty;
        EditSummary = string.Empty;
        EditContent = string.Empty;
        EditTags = string.Empty;
        EditAppliesToArea = string.Empty;
        EditAppliesToCapability = string.Empty;
        EditSource = "Manual";
        SaveStatus = string.Empty;
    }

    [RelayCommand]
    private async Task SaveDocumentAsync()
    {
        if (string.IsNullOrWhiteSpace(EditTitle))
        {
            SaveStatus = "Title is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(EditContent))
        {
            SaveStatus = "Content is required.";
            return;
        }

        IsSaving = true;
        SaveStatus = "Saving...";

        try
        {
            var document = new ProjectContextDocument
            {
                Id = EditId,
                ProjectId = _activeProjectId,
                DocumentType = EditDocumentType,
                AuthorityLevel = EditAuthorityLevel,
                Status = EditStatus,
                Title = EditTitle.Trim(),
                Summary = EmptyToNull(EditSummary),
                Content = EditContent.Trim(),
                Tags = EmptyToNull(EditTags),
                AppliesToArea = EmptyToNull(EditAppliesToArea),
                AppliesToCapability = EmptyToNull(EditAppliesToCapability),
                Source = EmptyToNull(EditSource)
            };

            var savedId = await _memoryService.SaveContextDocumentAsync(document);
            EditId = savedId;
            await RefreshDocumentsAsync(savedId, includePreferredIfFilteredOut: true);
            IsEditingDocument = false;
            SaveStatus = "Saved";
        }
        catch (Exception ex)
        {
            SaveStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task ArchiveDocumentAsync()
    {
        if (EditId <= 0) return;

        IsSaving = true;
        try
        {
            await _memoryService.ArchiveContextDocumentAsync(EditId);
            await RefreshDocumentsAsync();
            SelectedDocument = null;
            SaveStatus = "Archived";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        if (SelectedDocument != null)
        {
            LoadDocumentIntoEditor(SelectedDocument);
            IsEditingDocument = false;
        }
        else
            ClearEditor();
    }

    private bool CanDiscussSelectedDocument()
        => SelectedDocument != null && HasDetail;

    [RelayCommand(CanExecute = nameof(CanDiscussSelectedDocument))]
    private void DiscussSelectedDocument()
    {
        if (SelectedDocument == null || OnDiscussDocumentInChat == null)
            return;

        OnDiscussDocumentInChat(BuildChatPrompt(SelectedDocument));
    }

    private bool CanEditSelectedDocument()
        => SelectedDocument != null && HasDetail;

    [RelayCommand(CanExecute = nameof(CanEditSelectedDocument))]
    private void EditSelectedDocument()
    {
        if (SelectedDocument == null)
            return;

        IsEditingDocument = true;
    }

    public void PrefillFromChat(string title, string detail, string? linkedFilePaths, string? linkedSymbols)
    {
        NewDocument();
        EditDocumentType = "ArchitectureDecision";
        EditAuthorityLevel = "Binding";
        EditStatus = "Accepted";
        EditTitle = title;
        EditContent = detail;
        EditTags = linkedSymbols ?? string.Empty;
        EditAppliesToArea = linkedFilePaths ?? string.Empty;
        EditSource = "Chat";
    }

    public void PrefillDocumentFromChat(
        string title,
        string content,
        string? summary,
        string? linkedFilePaths,
        string? linkedSymbols)
    {
        NewDocument();
        EditDocumentType = "DiscussionNote";
        EditAuthorityLevel = "Pending";
        EditStatus = "Pending";
        EditTitle = string.IsNullOrWhiteSpace(title) ? "Chat-generated project document" : title.Trim();
        EditSummary = summary ?? string.Empty;
        EditContent = content.Trim();
        EditTags = linkedSymbols ?? string.Empty;
        EditAppliesToArea = linkedFilePaths ?? string.Empty;
        EditSource = "Chat";
    }

    private void LoadDocumentIntoEditor(ProjectContextDocument document)
    {
        HasDetail = true;
        EditId = document.Id;
        EditDocumentType = document.DocumentType;
        EditAuthorityLevel = document.AuthorityLevel;
        EditStatus = document.Status;
        EditTitle = document.Title;
        EditSummary = document.Summary ?? string.Empty;
        EditContent = document.Content;
        EditTags = document.Tags ?? string.Empty;
        EditAppliesToArea = document.AppliesToArea ?? string.Empty;
        EditAppliesToCapability = document.AppliesToCapability ?? string.Empty;
        EditSource = document.Source ?? string.Empty;
        SaveStatus = string.Empty;
        IsEditingDocument = false;
        NotifyDocumentPreviewChanged();
    }

    private void ClearEditor()
    {
        HasDetail = false;
        IsEditingDocument = false;
        EditId = 0;
        EditDocumentType = "ProjectFact";
        EditAuthorityLevel = "ObservedFact";
        EditStatus = "Active";
        EditTitle = string.Empty;
        EditSummary = string.Empty;
        EditContent = string.Empty;
        EditTags = string.Empty;
        EditAppliesToArea = string.Empty;
        EditAppliesToCapability = string.Empty;
        EditSource = string.Empty;
        SaveStatus = string.Empty;
        NotifyDocumentPreviewChanged();
    }

    private static string? EmptyToNull(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void NotifyDocumentPreviewChanged()
    {
        OnPropertyChanged(nameof(SelectedDocumentBody));
        OnPropertyChanged(nameof(SelectedDocumentMeta));
    }

    private static string BuildChatPrompt(ProjectContextDocument document)
    {
        var summary = string.IsNullOrWhiteSpace(document.Summary)
            ? string.Empty
            : $"Summary:\n{document.Summary.Trim()}\n\n";

        return
            "Discuss this project memory item.\n\n" +
            $"DocumentId: {document.Id}\n" +
            $"Type: {document.DocumentType}\n" +
            $"Status: {document.Status}\n" +
            $"Authority: {document.AuthorityLevel}\n" +
            $"Title: {document.Title}\n\n" +
            summary +
            "Content:\n" +
            document.Content.Trim() +
            "\n\nIf we update this memory item during the discussion, keep the DocumentId visible so it can be saved back to the original record.";
    }
}
