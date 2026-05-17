using IronDev.Agent.ViewModels.Workspaces;
using IronDev.Core.Builder;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CodexTicketReviewTests
{
    [TestMethod]
    public void FromDraft_MapsGroundingFields()
    {
        var draft = new CodebaseTicketDraft
        {
            Title = "Ground generated tickets",
            Category = "Dogfood",
            Problem = "Generated tickets need proof.",
            ProposedChange = "Show confidence and warnings before import.",
            RiskLevel = "Low",
            ConfidenceScore = 92,
            SuggestedBuildOrder = 2,
            AffectedFiles = ["IronDeveloper/ViewModels/Workspaces/TicketsWorkspaceViewModel.cs"],
            AffectedSymbols = ["TicketsWorkspaceViewModel.GenerateCodebaseTicketsAsync"],
            GroundingWarnings = ["Ticket does not reference any tests."],
            AcceptanceCriteria = "- Review shows confidence\n- Review shows warnings",
            TestSuggestions = ["Add mapping tests"]
        };

        var item = TicketReviewItemViewModel.FromDraft(draft);

        Assert.IsTrue(item.IsSelected);
        Assert.AreEqual("Ground generated tickets", item.Title);
        Assert.AreEqual("92/100", item.ConfidenceText);
        Assert.AreEqual(1, item.FilesCount);
        Assert.AreEqual(1, item.SymbolsCount);
        Assert.AreEqual(1, item.WarningsCount);
        CollectionAssert.Contains(item.AffectedFiles.ToList(), "IronDeveloper/ViewModels/Workspaces/TicketsWorkspaceViewModel.cs");
        CollectionAssert.Contains(item.AffectedSymbols.ToList(), "TicketsWorkspaceViewModel.GenerateCodebaseTicketsAsync");
        CollectionAssert.Contains(item.AcceptanceCriteria.ToList(), "Review shows confidence");
    }

    [TestMethod]
    public void BuildCodexTechnicalNotes_IncludesGroundingMetadata()
    {
        var item = TicketReviewItemViewModel.FromDraft(new CodebaseTicketDraft
        {
            Title = "Ground generated tickets",
            Category = "Dogfood",
            ProposedChange = "Add a review grid.",
            ConfidenceScore = 84,
            SuggestedBuildOrder = 1,
            AffectedFiles = ["IronDeveloper/Views/Workspaces/TicketsWorkspaceView.xaml"],
            AffectedSymbols = ["CodexTicketReviewViewModel"],
            GroundingWarnings = ["No context warnings."]
        });

        var notes = item.BuildCodexTechnicalNotes(76, ["Semantic index completed with warnings."]);

        StringAssert.Contains(notes, "## Source");
        StringAssert.Contains(notes, "Codex");
        StringAssert.Contains(notes, "## Context Quality");
        StringAssert.Contains(notes, "76/100");
        StringAssert.Contains(notes, "IronDeveloper/Views/Workspaces/TicketsWorkspaceView.xaml");
        StringAssert.Contains(notes, "CodexTicketReviewViewModel");
        StringAssert.Contains(notes, "No context warnings.");
    }

    [TestMethod]
    public async Task ImportSelectedTicketsCommand_ImportsOnlySelectedItems()
    {
        var result = new CodebaseTicketGenerationResult
        {
            Success = true,
            ContextQualityScore = 82,
            FileCount = 12,
            SemanticSymbolCount = 40,
            IndexWarningCount = 1,
            MissingContextReasons = ["No decisions loaded."],
            IndexWarnings = ["XAML structural index only."],
            Drafts =
            [
                new CodebaseTicketDraft
                {
                    Title = "First ticket",
                    AffectedFiles = ["IronDeveloper/ViewModels/Workspaces/TicketsWorkspaceViewModel.cs"],
                    AffectedSymbols = ["TicketsWorkspaceViewModel"],
                    ConfidenceScore = 90,
                    SuggestedBuildOrder = 1
                },
                new CodebaseTicketDraft
                {
                    Title = "Second ticket",
                    AffectedFiles = ["IronDeveloper/Views/Workspaces/TicketsWorkspaceView.xaml"],
                    AffectedSymbols = ["TicketsWorkspaceView"],
                    ConfidenceScore = 80,
                    SuggestedBuildOrder = 2
                }
            ]
        };

        var imported = new List<TicketReviewItemViewModel>();
        var vm = new CodexTicketReviewViewModel(
            () => Task.FromResult(result),
            selected =>
            {
                imported.AddRange(selected);
                return Task.CompletedTask;
            });

        await vm.GenerateCodexTicketsCommand.ExecuteAsync(null);
        vm.Tickets[1].IsSelected = false;

        await vm.ImportSelectedTicketsCommand.ExecuteAsync(null);

        Assert.AreEqual(1, imported.Count);
        Assert.AreEqual("First ticket", imported[0].Title);
        Assert.IsFalse(vm.HasTickets);
        StringAssert.Contains(vm.ContextBannerText, "Codex Context: 82/100");
    }
}
