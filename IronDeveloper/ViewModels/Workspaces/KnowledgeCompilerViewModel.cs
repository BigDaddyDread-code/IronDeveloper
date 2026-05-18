using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDeveloperControls.Primitives;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class KnowledgeCompilerViewModel : ObservableObject
{
    private readonly IDiscussionSeedService _seedService;
    private readonly IDiscussionResolverService _resolverService;
    private readonly IKnowledgeArtefactApplyService _applyService;
    private readonly global::IronDev.Services.IProjectMemoryService _memoryService;
    private readonly global::IronDev.Services.ITicketService _ticketService;

    private int _activeProjectId;
    private string _activeProjectName = string.Empty;

    [ObservableProperty] private string _projectSummaryText = string.Empty;
    [ObservableProperty] private DiscussionDocumentItemViewModel? _selectedDiscussion;
    [ObservableProperty] private string _discussionNotes = string.Empty;
    [ObservableProperty] private string _resolutionSummary = string.Empty;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _bannerTitle = "Knowledge Compiler";
    [ObservableProperty] private string _bannerMessage = "Generate guided discussions, resolve one, then review proposed decisions, docs, risks, and tickets.";
    [ObservableProperty] private BadgeStatus _bannerStatus = BadgeStatus.Info;
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private bool _isSavingDiscussions;
    [ObservableProperty] private bool _isResolving;
    [ObservableProperty] private bool _isApplying;
    [ObservableProperty] private int _resolutionConfidenceScore;

    public ObservableCollection<DiscussionDocumentItemViewModel> DiscussionDocuments { get; } = [];
    public ObservableCollection<ArtefactProposalItemViewModel> Proposals { get; } = [];
    public ObservableCollection<string> OpenQuestions { get; } = [];
    public ObservableCollection<string> BuildOrder { get; } = [];

    public bool HasDiscussions => DiscussionDocuments.Count > 0;
    public bool HasSelectedDiscussion => SelectedDiscussion != null;
    public bool HasProposals => Proposals.Count > 0;
    public bool HasSelectedProposals => Proposals.Any(p => p.IsSelected);
    public bool HasResolutionSummary => !string.IsNullOrWhiteSpace(ResolutionSummary);
    public bool HasOpenQuestions => OpenQuestions.Count > 0;
    public bool HasBuildOrder => BuildOrder.Count > 0;
    public string CompilerStatusText =>
        $"Discussions: {DiscussionDocuments.Count} | Proposals: {Proposals.Count} | Selected: {Proposals.Count(p => p.IsSelected)}";

    public KnowledgeCompilerViewModel(
        IDiscussionSeedService seedService,
        IDiscussionResolverService resolverService,
        IKnowledgeArtefactApplyService applyService,
        global::IronDev.Services.IProjectMemoryService memoryService,
        global::IronDev.Services.ITicketService ticketService)
    {
        _seedService = seedService;
        _resolverService = resolverService;
        _applyService = applyService;
        _memoryService = memoryService;
        _ticketService = ticketService;

        DiscussionDocuments.CollectionChanged += OnCollectionChanged;
        Proposals.CollectionChanged += OnCollectionChanged;
    }

    public async Task LoadAsync(Project project)
    {
        _activeProjectId = project.Id;
        _activeProjectName = project.Name;

        var summary = await _memoryService.GetLatestSummaryAsync(project.Id);
        ProjectSummaryText = summary?.Summary ?? project.Description ?? string.Empty;
        await LoadDiscussionDocumentsAsync();
        StatusText = "Ready";
        SetBanner("Knowledge Compiler", "Start from a project summary, then compile the useful parts into reviewable artefacts.", BadgeStatus.Info);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDiscussionDocumentsAsync();
    }

    [RelayCommand]
    private async Task GenerateDiscussionsAsync()
    {
        if (_activeProjectId <= 0 || IsGenerating)
            return;

        if (string.IsNullOrWhiteSpace(ProjectSummaryText))
        {
            SetBanner("Project summary needed", "Add a short project summary before generating discovery discussions.", BadgeStatus.Warning);
            return;
        }

        IsGenerating = true;
        StatusText = "Generating discussion documents...";
        ClearResolution();

        try
        {
            var result = await _seedService.GenerateDiscussionDocumentsAsync(new DiscussionSeedRequest
            {
                ProjectId = _activeProjectId,
                ProjectName = _activeProjectName,
                ProjectSummary = ProjectSummaryText,
                ExistingDiscussionTitles = DiscussionDocuments.Select(d => d.Title).ToList()
            });

            if (!result.Success)
            {
                SetBanner("Discussion generation failed", result.ErrorMessage, BadgeStatus.Danger);
                StatusText = result.ErrorMessage;
                return;
            }

            foreach (var discussion in result.Discussions)
            {
                DiscussionDocuments.Add(DiscussionDocumentItemViewModel.FromGenerated(discussion));
            }

            SelectedDiscussion = DiscussionDocuments.FirstOrDefault(d => !d.IsPersisted) ?? DiscussionDocuments.FirstOrDefault();
            SetBanner("Discussion drafts ready", $"Generated {result.Discussions.Count} discussion drafts. Save the useful ones before resolving.", BadgeStatus.Ready);
            StatusText = $"Generated {result.Discussions.Count} discussion drafts.";
        }
        finally
        {
            IsGenerating = false;
            RefreshComputedState();
        }
    }

    [RelayCommand]
    private async Task SaveSelectedDiscussionsAsync()
    {
        var drafts = DiscussionDocuments
            .Where(d => d.IsSelected && !d.IsPersisted)
            .ToList();

        if (drafts.Count == 0)
        {
            SetBanner("Nothing to save", "Select one or more unsaved discussion drafts first.", BadgeStatus.Info);
            return;
        }

        IsSavingDiscussions = true;
        StatusText = $"Saving {drafts.Count} discussion documents...";

        try
        {
            foreach (var draft in drafts)
            {
                var document = draft.ToContextDocument(_activeProjectId);
                var id = await _memoryService.SaveContextDocumentAsync(document);
                draft.MarkPersisted(id);
            }

            SetBanner("Discussions saved", $"Saved {drafts.Count} discussion documents. Select one and resolve it when ready.", BadgeStatus.Ready);
            StatusText = $"Saved {drafts.Count} discussion documents.";
        }
        finally
        {
            IsSavingDiscussions = false;
            RefreshComputedState();
        }
    }

    [RelayCommand]
    private async Task ResolveSelectedDiscussionAsync()
    {
        if (SelectedDiscussion == null)
        {
            SetBanner("Select a discussion", "Choose a saved discussion document before resolving.", BadgeStatus.Warning);
            return;
        }

        if (!SelectedDiscussion.IsPersisted)
        {
            SetBanner("Save before resolving", "Save this discussion first so any generated artefacts can link back to it.", BadgeStatus.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(DiscussionNotes))
        {
            SetBanner("Discussion notes needed", "Add the current discussion outcome or notes before resolving.", BadgeStatus.Warning);
            return;
        }

        IsResolving = true;
        StatusText = "Resolving discussion...";
        ClearResolution();

        try
        {
            var decisions = await _memoryService.GetRecentDecisionsAsync(_activeProjectId, 30);
            var tickets = await _ticketService.GetRecentTicketsAsync(_activeProjectId, 50);

            var result = await _resolverService.ResolveDiscussionAsync(new DiscussionResolverRequest
            {
                ProjectId = _activeProjectId,
                ProjectName = _activeProjectName,
                SourceDiscussionDocumentId = SelectedDiscussion.Id,
                DiscussionTitle = SelectedDiscussion.Title,
                DiscussionPrompt = SelectedDiscussion.FullPromptText,
                DiscussionNotes = DiscussionNotes,
                ProjectSummary = ProjectSummaryText,
                ExistingDecisionTitles = decisions.Select(d => d.Title).ToList(),
                ExistingTicketTitles = tickets.Select(t => t.Title).ToList()
            });

            if (!result.Success)
            {
                SetBanner("Resolution failed", result.ErrorMessage, BadgeStatus.Danger);
                StatusText = result.ErrorMessage;
                return;
            }

            ResolutionSummary = result.ResolutionSummary;
            ResolutionConfidenceScore = result.ConfidenceScore;

            foreach (var question in result.OpenQuestions)
                OpenQuestions.Add(question);
            foreach (var step in result.BuildOrder)
                BuildOrder.Add(step);
            foreach (var proposal in result.Proposals)
                Proposals.Add(new ArtefactProposalItemViewModel(proposal));

            SetBanner(
                "Resolution ready",
                $"Review {result.Proposals.Count} proposed artefacts. Nothing is saved until you apply selected proposals.",
                BadgeStatus.ReviewRequired);
            StatusText = $"Resolved discussion into {result.Proposals.Count} proposals.";
        }
        finally
        {
            IsResolving = false;
            RefreshComputedState();
        }
    }

    [RelayCommand]
    private async Task ApplySelectedProposalsAsync()
    {
        var selected = Proposals
            .Where(p => p.IsSelected)
            .ToList();

        if (selected.Count == 0)
        {
            SetBanner("No proposals selected", "Select at least one proposal to apply.", BadgeStatus.Info);
            return;
        }

        IsApplying = true;
        StatusText = $"Applying {selected.Count} proposals...";

        try
        {
            var result = await _applyService.ApplyAsync(new ArtefactApplyRequest
            {
                ProjectId = _activeProjectId,
                Proposals = selected.Select(p => p.Proposal).ToList()
            });

            foreach (var item in selected)
            {
                var applied = result.Results.FirstOrDefault(r => r.ProposalId == item.Proposal.Id);
                if (applied != null)
                    item.ApplyResult(applied);
            }

            SetBanner("Artefacts applied", $"Applied {result.AppliedCount} of {selected.Count} selected proposals.", BadgeStatus.Ready);
            StatusText = $"Applied {result.AppliedCount} proposals.";
            await LoadDiscussionDocumentsAsync(keepSelection: true);
        }
        finally
        {
            IsApplying = false;
            RefreshComputedState();
        }
    }

    [RelayCommand]
    private void ClearResolution()
    {
        ResolutionSummary = string.Empty;
        ResolutionConfidenceScore = 0;
        Proposals.Clear();
        OpenQuestions.Clear();
        BuildOrder.Clear();
        RefreshComputedState();
    }

    partial void OnSelectedDiscussionChanged(DiscussionDocumentItemViewModel? value)
    {
        DiscussionNotes = string.Empty;
        if (value != null && string.IsNullOrWhiteSpace(ProjectSummaryText))
            ProjectSummaryText = value.Summary;

        ClearResolution();
        RefreshComputedState();
    }

    private async Task LoadDiscussionDocumentsAsync(bool keepSelection = false)
    {
        var selectedId = keepSelection ? SelectedDiscussion?.Id : null;
        var documents = await _memoryService.GetContextDocumentsAsync(
            _activeProjectId,
            documentType: "DiscussionDocument",
            status: "Active",
            take: 100);

        DiscussionDocuments.Clear();
        foreach (var document in documents.OrderByDescending(d => d.UpdatedDate ?? d.CreatedDate))
        {
            DiscussionDocuments.Add(DiscussionDocumentItemViewModel.FromDocument(document));
        }

        SelectedDiscussion = selectedId.HasValue
            ? DiscussionDocuments.FirstOrDefault(d => d.Id == selectedId.Value)
            : DiscussionDocuments.FirstOrDefault();

        RefreshComputedState();
    }

    private void SetBanner(string title, string message, BadgeStatus status)
    {
        BannerTitle = title;
        BannerMessage = message;
        BannerStatus = status;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (object item in e.OldItems)
            {
                if (item is INotifyPropertyChanged notify)
                    notify.PropertyChanged -= OnChildPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (object item in e.NewItems)
            {
                if (item is INotifyPropertyChanged notify)
                    notify.PropertyChanged += OnChildPropertyChanged;
            }
        }

        RefreshComputedState();
    }

    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiscussionDocumentItemViewModel.IsSelected) ||
            e.PropertyName == nameof(ArtefactProposalItemViewModel.IsSelected))
        {
            RefreshComputedState();
        }
    }

    private void RefreshComputedState()
    {
        OnPropertyChanged(nameof(HasDiscussions));
        OnPropertyChanged(nameof(HasSelectedDiscussion));
        OnPropertyChanged(nameof(HasProposals));
        OnPropertyChanged(nameof(HasSelectedProposals));
        OnPropertyChanged(nameof(HasResolutionSummary));
        OnPropertyChanged(nameof(HasOpenQuestions));
        OnPropertyChanged(nameof(HasBuildOrder));
        OnPropertyChanged(nameof(CompilerStatusText));
    }
}

public sealed partial class DiscussionDocumentItemViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private long _id;

    public string Title { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public IReadOnlyList<string> Prompts { get; init; } = [];
    public IReadOnlyList<string> PossibleOutputs { get; init; } = [];
    public string SuggestedArea { get; init; } = string.Empty;
    public int SuggestedOrder { get; init; }
    public string Summary { get; init; } = string.Empty;
    public bool IsPersisted => Id > 0;
    public string PersistenceText => IsPersisted ? "Saved" : "Draft";
    public string PromptText => ToLines(Prompts);
    public string PossibleOutputsText => ToLines(PossibleOutputs);
    public string PreviewText => string.IsNullOrWhiteSpace(Summary) ? Purpose : Summary;
    public string FullPromptText => BuildFullPromptText();

    public static DiscussionDocumentItemViewModel FromGenerated(GeneratedDiscussionDocument document)
    {
        return new DiscussionDocumentItemViewModel
        {
            Title = document.Title,
            Purpose = document.Purpose,
            Prompts = document.Prompts.ToList(),
            PossibleOutputs = document.PossibleOutputs.ToList(),
            SuggestedArea = document.SuggestedArea,
            SuggestedOrder = document.SuggestedOrder,
            Summary = document.Purpose
        };
    }

    public static DiscussionDocumentItemViewModel FromDocument(ProjectContextDocument document)
    {
        return new DiscussionDocumentItemViewModel
        {
            Id = document.Id,
            Title = document.Title,
            Purpose = document.Summary ?? ExtractSection(document.Content, "Purpose"),
            Prompts = ExtractListSection(document.Content, "Prompts"),
            PossibleOutputs = ExtractListSection(document.Content, "Possible Outputs"),
            SuggestedArea = document.AppliesToArea ?? string.Empty,
            Summary = document.Summary ?? string.Empty
        };
    }

    public void MarkPersisted(long id)
    {
        Id = id;
        OnPropertyChanged(nameof(IsPersisted));
        OnPropertyChanged(nameof(PersistenceText));
    }

    public ProjectContextDocument ToContextDocument(int projectId)
    {
        return new ProjectContextDocument
        {
            ProjectId = projectId,
            DocumentType = "DiscussionDocument",
            AuthorityLevel = "DiscussionPrompt",
            Status = "Active",
            Title = Title,
            Content = BuildFullPromptText(),
            Summary = string.IsNullOrWhiteSpace(Purpose) ? Title : Purpose,
            Tags = "knowledge-compiler,discussion",
            AppliesToArea = SuggestedArea,
            Source = "ProjectKnowledgeCompiler"
        };
    }

    private string BuildFullPromptText()
    {
        var sb = new StringBuilder();
        AppendSection(sb, "Purpose", Purpose);
        AppendList(sb, "Prompts", Prompts);
        AppendList(sb, "Possible Outputs", PossibleOutputs);
        return sb.ToString().TrimEnd();
    }

    private static string ToLines(IReadOnlyList<string> values)
        => values.Count == 0 ? "None" : string.Join("\n", values);

    private static void AppendSection(StringBuilder sb, string title, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (sb.Length > 0) sb.AppendLine();
        sb.AppendLine(title);
        sb.AppendLine(value.Trim());
    }

    private static void AppendList(StringBuilder sb, string title, IReadOnlyList<string> values)
    {
        if (values.Count == 0) return;
        if (sb.Length > 0) sb.AppendLine();
        sb.AppendLine(title);
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
            sb.AppendLine($"- {value.Trim()}");
    }

    private static string ExtractSection(string content, string title)
    {
        var header = title.Trim();
        var start = content.IndexOf(header, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return string.Empty;
        start += header.Length;
        while (start < content.Length && char.IsWhiteSpace(content[start])) start++;
        var end = content.IndexOf("\n\n", start, StringComparison.Ordinal);
        return (end < 0 ? content[start..] : content[start..end]).Trim();
    }

    private static IReadOnlyList<string> ExtractListSection(string content, string title)
    {
        var section = ExtractSection(content, title);
        return section
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimStart('-', '*').Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }
}

public sealed partial class ArtefactProposalItemViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private bool _isApplied;
    [ObservableProperty] private string _applyStatusText = "Proposed";
    [ObservableProperty] private bool _applyFailed;

    public ArtefactProposal Proposal { get; }

    public ArtefactProposalItemViewModel(ArtefactProposal proposal)
    {
        Proposal = proposal;
    }

    public string KindText => Proposal.Kind.ToString();
    public string Title => Proposal.Title;
    public string Summary => Proposal.Summary;
    public string Detail => Proposal.Detail;
    public string Rationale => Proposal.Rationale;
    public string Category => Proposal.Category;
    public string Priority => Proposal.Priority;
    public string RiskLevel => Proposal.RiskLevel;
    public int SuggestedBuildOrder => Proposal.SuggestedBuildOrder;
    public string ConfidenceText => Proposal.ConfidenceScore > 0 ? $"{Proposal.ConfidenceScore}/100" : "Unscored";
    public string AcceptanceCriteriaText => ToLines(Proposal.AcceptanceCriteria);
    public string TestSuggestionsText => ToLines(Proposal.TestSuggestions);
    public string AffectedFilesText => ToLines(Proposal.AffectedFiles);
    public string AffectedSymbolsText => ToLines(Proposal.AffectedSymbols);
    public string GroundingWarningsText => ToLines(Proposal.GroundingWarnings);
    public bool HasGroundingWarnings => Proposal.GroundingWarnings.Count > 0;
    public bool HasBuildOrder => SuggestedBuildOrder > 0;

    public void ApplyResult(AppliedArtefactResult result)
    {
        IsApplied = result.Success;
        ApplyFailed = !result.Success;
        ApplyStatusText = result.Success
            ? $"{result.ArtifactType} #{result.ArtifactId}"
            : result.Message;
        IsSelected = !result.Success;
    }

    private static string ToLines(IReadOnlyList<string> values)
        => values.Count == 0 ? "None" : string.Join("\n", values);
}
