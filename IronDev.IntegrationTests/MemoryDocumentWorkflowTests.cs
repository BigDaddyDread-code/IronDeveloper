using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Agent.ViewModels.Workspaces;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class MemoryDocumentWorkflowTests
{
    [TestMethod]
    public void PrefillDocumentFromChat_CreatesPendingDiscussionDocumentDraft()
    {
        var vm = CreateViewModel();

        vm.PrefillDocumentFromChat(
            "Architecture follow-up",
            "We should review persistence options before creating tickets.",
            "Review persistence options.",
            "IronDev.Core/Services/TicketService.cs",
            "TicketService");

        Assert.IsTrue(vm.HasDetail);
        Assert.IsTrue(vm.IsEditingDocument);
        Assert.AreEqual("DiscussionNote", vm.EditDocumentType);
        Assert.AreEqual("Pending", vm.EditAuthorityLevel);
        Assert.AreEqual("Pending", vm.EditStatus);
        Assert.AreEqual("Architecture follow-up", vm.EditTitle);
        Assert.AreEqual("Review persistence options.", vm.EditSummary);
        Assert.AreEqual("Chat", vm.EditSource);
        Assert.AreEqual("IronDev.Core/Services/TicketService.cs", vm.EditAppliesToArea);
        Assert.AreEqual("TicketService", vm.EditTags);
    }

    [TestMethod]
    public void DiscussSelectedDocument_BuildsChatPromptWithDocumentIdentity()
    {
        var vm = CreateViewModel();
        string? prompt = null;
        vm.OnDiscussDocumentInChat = value => prompt = value;

        vm.SelectedDocument = new ProjectContextDocument
        {
            Id = 42,
            DocumentType = "ArchitectureDecision",
            AuthorityLevel = "Binding",
            Status = "Accepted",
            Title = "Use SQL Server and Dapper",
            Summary = "Persist books in SQL Server.",
            Content = "Use SQL Server as the database and Dapper for data access."
        };

        vm.DiscussSelectedDocumentCommand.Execute(null);

        Assert.IsNotNull(prompt);
        StringAssert.Contains(prompt, "DocumentId: 42");
        StringAssert.Contains(prompt, "Type: ArchitectureDecision");
        StringAssert.Contains(prompt, "Use SQL Server and Dapper");
        StringAssert.Contains(prompt, "keep the DocumentId visible");
    }

    private static DecisionsWorkspaceViewModel CreateViewModel()
        => new(new FakeProjectMemoryService(), new FakeLookupService());

    private sealed class FakeLookupService : ILookupService
    {
        public Task<IReadOnlyList<LookupItem>> GetDecisionCategoriesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LookupItem>>(Array.Empty<LookupItem>());

        public Task<IReadOnlyList<LookupItem>> GetDecisionStatusesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LookupItem>>(Array.Empty<LookupItem>());
    }

    private sealed class FakeProjectMemoryService : IProjectMemoryService
    {
        public Task<ProjectSummary?> GetLatestSummaryAsync(int projectId, CancellationToken cancellationToken = default)
            => Task.FromResult<ProjectSummary?>(null);

        public Task<IReadOnlyList<ProjectDecision>> GetRecentDecisionsAsync(int projectId, int take = 10, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProjectDecision>>(Array.Empty<ProjectDecision>());

        public Task<ProjectDecision?> GetDecisionByIdAsync(long decisionId, CancellationToken cancellationToken = default)
            => Task.FromResult<ProjectDecision?>(null);

        public Task<long> SaveSummaryAsync(ProjectSummary summary, CancellationToken cancellationToken = default)
            => Task.FromResult(1L);

        public Task<IReadOnlyList<ProjectContextDocument>> GetContextDocumentsAsync(int projectId, string? documentType = null, string? authorityLevel = null, string? status = "Active", int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProjectContextDocument>>(Array.Empty<ProjectContextDocument>());

        public Task<IReadOnlyList<ProjectContextDocument>> GetRelevantContextDocumentsAsync(int projectId, string query, int take = 20, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProjectContextDocument>>(Array.Empty<ProjectContextDocument>());

        public Task<ProjectContextDocument?> GetContextDocumentByIdAsync(long documentId, CancellationToken cancellationToken = default)
            => Task.FromResult<ProjectContextDocument?>(null);

        public Task<long> SaveContextDocumentAsync(ProjectContextDocument document, CancellationToken cancellationToken = default)
            => Task.FromResult(1L);

        public Task<bool> ArchiveContextDocumentAsync(long documentId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<ProjectObservableState?> GetObservableStateAsync(int projectId, CancellationToken cancellationToken = default)
            => Task.FromResult<ProjectObservableState?>(null);

        public Task SaveObservableStateAsync(ProjectObservableState state, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ProjectImplementationPlan>> GetRecentPlansAsync(int projectId, int take = 10, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProjectImplementationPlan>>(Array.Empty<ProjectImplementationPlan>());

        public Task<ProjectImplementationPlan?> GetPlanByIdAsync(long planId, CancellationToken cancellationToken = default)
            => Task.FromResult<ProjectImplementationPlan?>(null);

        public Task<ProjectImplementationPlan?> GetPlanByTicketIdAsync(long ticketId, CancellationToken cancellationToken = default)
            => Task.FromResult<ProjectImplementationPlan?>(null);

        public Task<long> SavePlanAsync(ProjectImplementationPlan plan, CancellationToken cancellationToken = default)
            => Task.FromResult(1L);

        public Task<long> SaveDecisionAsync(ProjectDecision decision, CancellationToken cancellationToken = default)
            => Task.FromResult(1L);

        public Task<IReadOnlyList<ProjectRule>> GetProjectRulesAsync(int projectId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProjectRule>>(Array.Empty<ProjectRule>());

        public Task<long> SaveProjectRuleAsync(ProjectRule rule, CancellationToken cancellationToken = default)
            => Task.FromResult(1L);
    }
}
