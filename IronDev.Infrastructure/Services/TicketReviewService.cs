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
public sealed class TicketReviewService : ITicketReviewService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ITicketService _tickets;
    private readonly IProjectDocumentService _documents;
    private readonly DiscussionCodeScenarioCatalog _scenarios;

    public TicketReviewService(
        ITicketService tickets,
        IProjectDocumentService documents,
        DiscussionCodeScenarioCatalog scenarios)
    {
        _tickets = tickets;
        _documents = documents;
        _scenarios = scenarios;
    }

    public async Task<TicketReviewResult?> ReviewAsync(
        int projectId,
        long ticketId,
        RunTicketReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || ticket.ProjectId != projectId)
            return null;

        var result = CreateDeterministicReview(ticket);
        await _documents.CreateDocumentAsync(new CreateProjectDocumentRequest
        {
            ProjectId = ticket.ProjectId,
            Title = $"Ticket Review {result.ReviewId} for Ticket {ticket.Id}",
            DocumentType = "TicketReview",
            ContentMarkdown = JsonSerializer.Serialize(result, JsonOptions),
            ChangeSummary = "Captured deterministic ticket review.",
            CreatedBy = "IronDev"
        }, cancellationToken).ConfigureAwait(false);

        return result;
    }

    public async Task<TicketReviewResult?> GetReviewAsync(
        int projectId,
        long ticketId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || ticket.ProjectId != projectId || string.IsNullOrWhiteSpace(reviewId))
            return null;

        var documents = await _documents.GetDocumentsAsync(new GetProjectDocumentsRequest
        {
            ProjectId = ticket.ProjectId,
            DocumentType = "TicketReview",
            Status = "Active"
        }, cancellationToken).ConfigureAwait(false);

        foreach (var document in documents)
        {
            var version = document.CurrentVersionId.HasValue
                ? await _documents.GetVersionAsync(document.CurrentVersionId.Value, cancellationToken).ConfigureAwait(false)
                : null;
            if (version is null || !version.ContentMarkdown.Contains(reviewId, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var result = JsonSerializer.Deserialize<TicketReviewResult>(version.ContentMarkdown, JsonOptions);
                if (result is not null &&
                    result.TicketId == ticketId &&
                    result.ProjectId == projectId &&
                    string.Equals(result.ReviewId, reviewId, StringComparison.OrdinalIgnoreCase))
                {
                    return result;
                }
            }
            catch (JsonException)
            {
            }
        }

        return null;
    }

    private TicketReviewResult CreateDeterministicReview(ProjectTicket ticket)
    {
        var reviewId = $"ticket-review-{ticket.Id}-{Guid.NewGuid():N}";
        var scenario = _scenarios.Match($"{ticket.Title} {ticket.Summary} {ticket.AcceptanceCriteria}");
        return new TicketReviewResult
        {
            ReviewId = reviewId,
            ProjectId = ticket.ProjectId,
            TicketId = ticket.Id,
            ScenarioId = scenario?.Scenario.ScenarioId ?? "none",
            Contributions =
            [
                new TicketReviewContribution
                {
                    Role = "Plan",
                    Summary = "Use the smallest deterministic code proposal that proves the disposable backend path.",
                    Recommendations = ["Generate only the scenario files and a minimal SDK-style csproj."]
                },
                new TicketReviewContribution
                {
                    Role = "Proposal",
                    Summary = "Create generated files in the backend-owned disposable workspace only.",
                    Concerns = ["No real repository mutation is allowed."],
                    Recommendations = ["Use a code proposal and backend-owned command profile."]
                },
                new TicketReviewContribution
                {
                    Role = "Validation",
                    Summary = "Run dotnet build and dotnet run, then verify the expected output.",
                    Recommendations = ["Persist stdout and stderr logs as evidence."]
                },
                new TicketReviewContribution
                {
                    Role = "Governance",
                    Summary = "Proceed only if the run stays disposable and pauses for human review.",
                    Concerns = ["Do not apply generated code to the real repository."],
                    Recommendations = ["End successful execution in PausedForApproval."]
                }
            ],
            Decision = new TicketReviewDecision
            {
                Proceed = scenario is not null,
                RecommendedNextStep = scenario is not null
                    ? "Start disposable code run from a generated code proposal."
                    : "Refine the ticket before disposable code execution.",
                Guardrails =
                [
                    "Generate code only in the disposable workspace.",
                    "Do not mutate the real repository.",
                    "Use backend-owned dotnet build and run commands.",
                    "Persist evidence and pause for human review."
                ]
            }
        };
    }
}

