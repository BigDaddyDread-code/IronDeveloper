using System.Collections.Generic;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using IronDev.Core.Builder;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class TicketReviewItemViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;

    public string Title { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Background { get; init; } = string.Empty;
    public string Problem { get; init; } = string.Empty;
    public string ProposedChange { get; init; } = string.Empty;
    public string WhyNow { get; init; } = string.Empty;
    public string Priority { get; init; } = "Medium";
    public string TicketType { get; init; } = "Task";
    public string RiskLevel { get; init; } = "Medium";
    public int ConfidenceScore { get; init; }
    public int SuggestedBuildOrder { get; init; }
    public IReadOnlyList<string> AffectedFiles { get; init; } = [];
    public IReadOnlyList<string> AffectedSymbols { get; init; } = [];
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];
    public IReadOnlyList<string> TestSuggestions { get; init; } = [];
    public IReadOnlyList<string> GroundingWarnings { get; init; } = [];
    public string UnitTests { get; init; } = string.Empty;
    public string IntegrationTests { get; init; } = string.Empty;
    public string ManualTests { get; init; } = string.Empty;
    public string RegressionTests { get; init; } = string.Empty;
    public string BuildValidation { get; init; } = string.Empty;

    public string ConfidenceText => ConfidenceScore > 0 ? $"{ConfidenceScore}/100" : "Unscored";
    public int FilesCount => AffectedFiles.Count;
    public int SymbolsCount => AffectedSymbols.Count;
    public int WarningsCount => GroundingWarnings.Count;
    public bool HasGroundingWarnings => GroundingWarnings.Count > 0;
    public string FilesCountText => FilesCount.ToString();
    public string SymbolsCountText => SymbolsCount.ToString();
    public string WarningsCountText => WarningsCount == 0 ? "None" : WarningsCount.ToString();
    public string AffectedFilesText => ToLines(AffectedFiles);
    public string AffectedSymbolsText => ToLines(AffectedSymbols);
    public string DependenciesText => ToLines(Dependencies);
    public string AcceptanceCriteriaText => ToLines(AcceptanceCriteria);
    public string TestSuggestionsText => ToLines(TestSuggestions);
    public string GroundingWarningsText => ToLines(GroundingWarnings);

    public static TicketReviewItemViewModel FromDraft(CodebaseTicketDraft draft)
    {
        return new TicketReviewItemViewModel
        {
            Title = draft.Title,
            Category = draft.Category,
            Summary = draft.Summary,
            Background = draft.Background,
            Problem = draft.Problem,
            ProposedChange = draft.ProposedChange,
            WhyNow = draft.WhyNow,
            Priority = draft.Priority,
            TicketType = draft.TicketType,
            RiskLevel = string.IsNullOrWhiteSpace(draft.RiskLevel) ? "Medium" : draft.RiskLevel,
            ConfidenceScore = draft.ConfidenceScore,
            SuggestedBuildOrder = draft.SuggestedBuildOrder,
            AffectedFiles = draft.AffectedFiles?.ToList() ?? [],
            AffectedSymbols = draft.AffectedSymbols?.ToList() ?? [],
            Dependencies = draft.Dependencies?.ToList() ?? [],
            AcceptanceCriteria = SplitLines(draft.AcceptanceCriteria),
            TestSuggestions = draft.TestSuggestions?.ToList() ?? [],
            GroundingWarnings = draft.GroundingWarnings?.ToList() ?? [],
            UnitTests = draft.UnitTests,
            IntegrationTests = draft.IntegrationTests,
            ManualTests = draft.ManualTests,
            RegressionTests = draft.RegressionTests,
            BuildValidation = draft.BuildValidation
        };
    }

    public string BuildCodexTechnicalNotes(
        int contextQualityScore,
        IReadOnlyList<string> missingContextReasons)
    {
        var sb = new StringBuilder();
        AppendSection(sb, "## Source", "Codex");
        AppendSection(sb, "## Category", Category);
        AppendSection(sb, "## Proposed Change", ProposedChange);
        AppendSection(sb, "## Why Now", WhyNow);
        AppendSection(sb, "## Suggested Build Order", SuggestedBuildOrder > 0 ? SuggestedBuildOrder.ToString() : string.Empty);
        AppendSection(sb, "## Risk Level", RiskLevel);
        AppendSection(sb, "## Confidence", ConfidenceScore > 0 ? $"{ConfidenceScore}/100" : string.Empty);
        AppendSection(sb, "## Context Quality", $"{contextQualityScore}/100");
        AppendList(sb, "## Context Warnings", missingContextReasons);
        AppendList(sb, "## Dependencies", Dependencies);
        AppendList(sb, "## Affected Files", AffectedFiles);
        AppendList(sb, "## Affected Symbols", AffectedSymbols);
        AppendList(sb, "## Grounding Warnings", GroundingWarnings);
        AppendList(sb, "## Test Suggestions", TestSuggestions);
        AppendSection(sb, "## Unit Tests", UnitTests);
        AppendSection(sb, "## Integration Tests", IntegrationTests);
        AppendSection(sb, "## UI / Manual Tests", ManualTests);
        AppendSection(sb, "## Regression Tests", RegressionTests);
        AppendSection(sb, "## Build Validation", BuildValidation);
        return sb.ToString().TrimEnd();
    }

    private static IReadOnlyList<string> SplitLines(string value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim().TrimStart('-', '*').Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

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
        {
            sb.AppendLine($"- {value.Trim()}");
        }
    }
}
