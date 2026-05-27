using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Core.Runs;
using IronDev.Core.Tools;
using IronDev.Data.Models;
using IronDev.Infrastructure.Tools.CodeStandards;
using IronDev.Services;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;
public sealed class TicketFromDocumentService : ITicketFromDocumentService
{
    private readonly IProjectDocumentService _documents;
    private readonly ITicketService _tickets;
    private readonly DiscussionCodeScenarioCatalog _scenarios;

    public TicketFromDocumentService(
        IProjectDocumentService documents,
        ITicketService tickets,
        DiscussionCodeScenarioCatalog scenarios)
    {
        _documents = documents;
        _tickets = tickets;
        _scenarios = scenarios;
    }

    public async Task<CreateTicketFromDocumentResponse?> CreateTicketAsync(
        int projectId,
        long documentVersionId,
        CreateTicketFromDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        var version = await _documents.GetVersionAsync(documentVersionId, cancellationToken).ConfigureAwait(false);
        if (version is null)
            return null;

        var document = await _documents.GetDocumentAsync(version.DocumentId, cancellationToken).ConfigureAwait(false);
        if (document is null || document.ProjectId != projectId)
            return null;

        var scenario = _scenarios.Match(version.ContentMarkdown);
        var title = !string.IsNullOrWhiteSpace(request.RequestedTitle)
            ? request.RequestedTitle.Trim()
            : scenario is not null
                ? scenario.Title
                : $"Implement discussion: {document.Title}";

        var ticket = scenario is not null
            ? BuildScenarioTicket(document.ProjectId, documentVersionId, title, scenario)
            : BuildDiscussionTicket(document.ProjectId, documentVersionId, title, version.ContentMarkdown);

        ticket.Id = await _tickets.SaveTicketAsync(ticket, cancellationToken).ConfigureAwait(false);

        await _documents.LinkVersionAsync(new LinkProjectDocumentVersionRequest
        {
            DocumentVersionId = documentVersionId,
            LinkedEntityType = "Ticket",
            LinkedEntityId = ticket.Id,
            LinkType = "GeneratedTicket",
            CreatedBy = "IronDev"
        }, cancellationToken).ConfigureAwait(false);

        return new CreateTicketFromDocumentResponse
        {
            TicketId = ticket.Id,
            SourceDocumentVersionId = documentVersionId
        };
    }

    private static ProjectTicket BuildScenarioTicket(
        int projectId,
        long documentVersionId,
        string title,
        ScenarioDefinition scenario) => new()
    {
        ProjectId = projectId,
        SessionId = Guid.NewGuid(),
        Title = title,
        TicketType = "Scenario",
        Priority = "High",
        Summary = scenario.Summary,
        Problem = scenario.Problem,
        AcceptanceCriteria = string.Join(Environment.NewLine, scenario.AcceptanceCriteria),
        TechnicalNotes = $"Generated deterministically from a discussion document using scenario fixture '{scenario.Scenario.ScenarioId}'.",
        Status = "Draft",
        Content = scenario.Summary,
        IsGenerated = true,
        GenerationNote = $"Source: Discussion document. Deterministic scenario fixture: {scenario.Scenario.ScenarioId}.",
        SourceDocumentVersionId = documentVersionId
    };

    private static ProjectTicket BuildDiscussionTicket(int projectId, long documentVersionId, string title, string content) => new()
    {
        ProjectId = projectId,
        SessionId = Guid.NewGuid(),
        Title = title,
        TicketType = "Discussion",
        Priority = "Medium",
        Summary = content,
        Problem = "Generated from discussion document.",
        AcceptanceCriteria = "- Discussion document is linked as ticket source.",
        TechnicalNotes = "Deterministic discussion ticket.",
        Status = "Draft",
        Content = content,
        IsGenerated = true,
        GenerationNote = "Source: Discussion document.",
        SourceDocumentVersionId = documentVersionId
    };
}

