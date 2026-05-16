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
    [ObservableProperty] private ProjectContextDocument? _selectedDocument;
    [ObservableProperty] private bool _hasDetail;
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
    private async Task RefreshListAsync()
    {
        Documents.Clear();
        ClearEditor();

        var documentType = FilterDocumentType == "All" ? null : FilterDocumentType;
        var status = FilterStatus == "All" ? null : FilterStatus;
        var documents = await _memoryService.GetContextDocumentsAsync(
            _activeProjectId,
            documentType: documentType,
            status: status,
            take: 200);

        foreach (var document in documents)
            Documents.Add(document);
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

    partial void OnFilterDocumentTypeChanged(string value)
        => _ = RefreshListAsync();

    partial void OnFilterStatusChanged(string value)
        => _ = RefreshListAsync();

    [RelayCommand]
    private void NewDocument()
    {
        SelectedDocument = null;
        HasDetail = true;
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
            SaveStatus = "Saved";
            await RefreshListAsync();
            SelectedDocument = Documents.FirstOrDefault(d => d.Id == savedId);
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
            SaveStatus = "Archived";
            await RefreshListAsync();
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
            LoadDocumentIntoEditor(SelectedDocument);
        else
            ClearEditor();
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
    }

    private void ClearEditor()
    {
        HasDetail = false;
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
    }

    private static string? EmptyToNull(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
