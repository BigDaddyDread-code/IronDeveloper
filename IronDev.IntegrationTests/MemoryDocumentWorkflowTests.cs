using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

    [TestMethod]
    public void PrefillFromChat_WithSourceDocumentId_UpdatesExistingKnowledgeItemDraft()
    {
        var vm = CreateViewModel();

        vm.PrefillFromChat(
            "BookSeller SQL Server and Dapper",
            "Use SQL Server for persistent storage and Dapper for data access.",
            null,
            null,
            sourceDocumentId: 2);

        Assert.IsTrue(vm.HasDetail);
        Assert.IsTrue(vm.IsEditingDocument);
        Assert.AreEqual(2, vm.EditId);
        Assert.AreEqual("ArchitectureDecision", vm.EditDocumentType);
        Assert.AreEqual("Binding", vm.EditAuthorityLevel);
        Assert.AreEqual("Accepted", vm.EditStatus);
        Assert.AreEqual("BookSeller SQL Server and Dapper", vm.EditTitle);
        Assert.AreEqual("Use SQL Server for persistent storage and Dapper for data access.", vm.EditContent);
        Assert.AreEqual("Chat update of DocumentId 2", vm.EditSource);
    }

    [TestMethod]
    public void SaveDecisionCommand_UsesDecisionTagAndSourceDocumentIdFromChat()
    {
        var vm = new ChatWorkspaceViewModel(null!, null!, null!, null!, null!, null!, null!);
        var userMessage = new IronDev.Agent.Models.ChatSummary
        {
            Role = "user",
            MessageText = "Discuss this project memory item.\n\nDocumentId: 2\nTitle: perssit data"
        };
        var assistantMessage = new IronDev.Agent.Models.ChatSummary
        {
            Role = "assistant",
            MessageText = "SQL Server and Dapper selected.\n\n<decision>BookSeller SQL Server and Dapper | Implement SQL Server for persistent data storage and Dapper for data access in the BookSeller project.</decision>"
        };

        string? title = null;
        string? detail = null;
        long? sourceDocumentId = null;
        vm.OnCreateDecisionFromChat = (proposedTitle, proposedDetail, _, _, documentId) =>
        {
            title = proposedTitle;
            detail = proposedDetail;
            sourceDocumentId = documentId;
        };

        vm.Messages.Add(userMessage);
        vm.Messages.Add(assistantMessage);

        vm.SaveDecisionCommand.Execute(assistantMessage);

        Assert.AreEqual("BookSeller SQL Server and Dapper", title);
        Assert.AreEqual("Implement SQL Server for persistent data storage and Dapper for data access in the BookSeller project.", detail);
        Assert.AreEqual(2, sourceDocumentId);
    }

    [TestMethod]
    public async Task SaveDocumentAsync_ReselectsSavedDocumentEvenWhenFilterWouldHideIt()
    {
        var vm = CreateViewModel();
        SetActiveProjectId(vm, 17);

        vm.FilterStatus = "Active";
        vm.NewDocumentCommand.Execute(null);
        vm.EditTitle = "Persistence discussion";
        vm.EditContent = "Capture the SQL Server and Dapper persistence discussion.";
        vm.EditStatus = "Pending";

        await vm.SaveDocumentCommand.ExecuteAsync(null);

        Assert.AreEqual("Saved", vm.SaveStatus);
        Assert.IsFalse(vm.IsEditingDocument);
        Assert.IsNotNull(vm.SelectedDocument);
        Assert.AreEqual("Persistence discussion", vm.SelectedDocument.Title);
        Assert.AreEqual("Pending", vm.SelectedDocument.Status);
        Assert.HasCount(1, vm.Documents);
        Assert.AreEqual("Persistence discussion", vm.Documents[0].Title);
    }

    private static DecisionsWorkspaceViewModel CreateViewModel()
        => new(new FakeProjectMemoryService(), new FakeLookupService());

    private static void SetActiveProjectId(DecisionsWorkspaceViewModel vm, int projectId)
    {
        var field = typeof(DecisionsWorkspaceViewModel).GetField("_activeProjectId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        field.SetValue(vm, projectId);
    }

    private sealed class FakeLookupService : ILookupService
    {
        public Task<IReadOnlyList<LookupItem>> GetDecisionCategoriesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LookupItem>>(Array.Empty<LookupItem>());

        public Task<IReadOnlyList<LookupItem>> GetDecisionStatusesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LookupItem>>(Array.Empty<LookupItem>());
    }

    private sealed class FakeProjectMemoryService : IProjectMemoryService
    {
        private readonly List<ProjectContextDocument> _documents = [];
        private long _nextDocumentId = 1;

        public Task<ProjectSummary?> GetLatestSummaryAsync(int projectId, CancellationToken cancellationToken = default)
            => Task.FromResult<ProjectSummary?>(null);

        public Task<IReadOnlyList<ProjectDecision>> GetRecentDecisionsAsync(int projectId, int take = 10, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProjectDecision>>(Array.Empty<ProjectDecision>());

        public Task<ProjectDecision?> GetDecisionByIdAsync(long decisionId, CancellationToken cancellationToken = default)
            => Task.FromResult<ProjectDecision?>(null);

        public Task<long> SaveSummaryAsync(ProjectSummary summary, CancellationToken cancellationToken = default)
            => Task.FromResult(1L);

        public Task<IReadOnlyList<ProjectContextDocument>> GetContextDocumentsAsync(int projectId, string? documentType = null, string? authorityLevel = null, string? status = "Active", int take = 100, CancellationToken cancellationToken = default)
        {
            var documents = _documents
                .Where(d => d.ProjectId == projectId)
                .Where(d => string.IsNullOrWhiteSpace(documentType) || d.DocumentType == documentType)
                .Where(d => string.IsNullOrWhiteSpace(authorityLevel) || d.AuthorityLevel == authorityLevel)
                .Where(d => string.IsNullOrWhiteSpace(status) || d.Status == status)
                .Take(take)
                .ToList();

            return Task.FromResult<IReadOnlyList<ProjectContextDocument>>(documents);
        }

        public Task<IReadOnlyList<ProjectContextDocument>> GetRelevantContextDocumentsAsync(int projectId, string query, int take = 20, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProjectContextDocument>>(Array.Empty<ProjectContextDocument>());

        public Task<ProjectContextDocument?> GetContextDocumentByIdAsync(long documentId, CancellationToken cancellationToken = default)
            => Task.FromResult(_documents.FirstOrDefault(d => d.Id == documentId));

        public Task<long> SaveContextDocumentAsync(ProjectContextDocument document, CancellationToken cancellationToken = default)
        {
            if (document.Id <= 0)
            {
                document.Id = _nextDocumentId++;
                _documents.Add(document);
                return Task.FromResult(document.Id);
            }

            var index = _documents.FindIndex(d => d.Id == document.Id);
            if (index >= 0)
                _documents[index] = document;
            else
                _documents.Add(document);

            return Task.FromResult(document.Id);
        }

        public Task<bool> ArchiveContextDocumentAsync(long documentId, CancellationToken cancellationToken = default)
        {
            var document = _documents.FirstOrDefault(d => d.Id == documentId);
            if (document == null)
                return Task.FromResult(false);

            document.Status = "Archived";
            return Task.FromResult(true);
        }

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
