using System.Text.Json;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Services;

public static class TicketsDocumentToTicketsSmokeCommand
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var projectName = MemorySmokeCommandSupport.ReadOption(args, "--project") ?? "BookSeller";
        var dogfoodRunId = MemorySmokeCommandSupport.ReadOption(args, "--dogfood-run-id") ?? $"document-to-tickets-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var repoRoot = MemorySmokeCommandSupport.FindRepositoryRoot();
        var connectionFactory = new CliConnectionFactory(MemorySmokeCommandSupport.ResolveIronDevConnectionString(args));

        await MemorySmokeCommandSupport.ApplySqlScriptAsync(
            connectionFactory,
            Path.Combine(repoRoot, "Database", "migrate_project_documents.sql"));

        var project = await MemorySmokeCommandSupport.ResolveProjectAsync(connectionFactory, projectName);
        if (project is null)
        {
            Console.Error.WriteLine($"Project not found: {projectName}");
            return 1;
        }

        var tenant = new CliTenantContext(project.TenantId);
        var documentService = new ProjectDocumentService(connectionFactory, tenant);
        var sourceReferenceService = new ArtifactSourceReferenceService(connectionFactory);
        var ticketService = new TicketService(connectionFactory, tenant, sourceReferenceService);
        var document = await CreateSourceDocumentAsync(documentService, project.ProjectId, dogfoodRunId);
        var sourceVersion = await documentService.GetCurrentVersionAsync(document.Id)
            ?? throw new InvalidOperationException("Document-to-tickets source version was not created.");

        var ticketIds = new List<long>();
        foreach (var ticket in BuildTickets(project, sourceVersion.Id))
        {
            var ticketId = await ticketService.SaveTicketAsync(ticket);
            ticketIds.Add(ticketId);
            await documentService.LinkVersionAsync(new LinkProjectDocumentVersionRequest
            {
                DocumentVersionId = sourceVersion.Id,
                LinkedEntityType = "Ticket",
                LinkedEntityId = ticketId,
                LinkType = "GeneratedTicket",
                CreatedBy = "TestAgent"
            });
        }

        var validations = new List<object>();
        var allTicketsLinked = true;
        var allTicketsResolve = true;
        foreach (var ticketId in ticketIds)
        {
            var savedTicket = await ticketService.GetTicketByIdAsync(ticketId)
                ?? throw new InvalidOperationException($"Generated ticket {ticketId} could not be reloaded.");
            var resolvedVersion = savedTicket.SourceDocumentVersionId is { } sourceDocumentVersionId
                ? await documentService.GetVersionAsync(sourceDocumentVersionId)
                : null;
            var references = await sourceReferenceService.GetForArtifactAsync(
                project.TenantId,
                project.ProjectId,
                "Ticket",
                ticketId);
            var hasArtifactReference = references.Any(reference =>
                reference.SourceType == "ProjectDocumentVersion" &&
                reference.SourceId == sourceVersion.Id &&
                reference.ReferenceType == "CreatedFrom");
            var resolves = resolvedVersion?.Id == sourceVersion.Id;
            var linked = savedTicket.SourceDocumentVersionId == sourceVersion.Id && hasArtifactReference;

            allTicketsLinked &= linked;
            allTicketsResolve &= resolves;
            validations.Add(new
            {
                ticketId,
                savedTicket.Title,
                savedTicket.SourceDocumentVersionId,
                hasArtifactReference,
                resolvesToSourceVersion = resolves
            });
        }

        var versionLinks = await documentService.GetLinksForVersionAsync(sourceVersion.Id);
        var generatedTicketLinks = versionLinks
            .Where(link => link.LinkedEntityType == "Ticket" && link.LinkType == "GeneratedTicket")
            .ToArray();
        var passed = ticketIds.Count == 3 &&
                     allTicketsLinked &&
                     allTicketsResolve &&
                     generatedTicketLinks.Length >= ticketIds.Count;

        var result = new
        {
            dogfoodRunId,
            projectId = project.ProjectId,
            projectName = project.ProjectName,
            sourceDocumentId = document.Id,
            sourceDocumentVersionId = sourceVersion.Id,
            sourceDocumentTitle = document.Title,
            ticketIds,
            generatedTicketLinkCount = generatedTicketLinks.Length,
            allTicketsLinked,
            allTicketsResolve,
            passed,
            validations,
            boundary = "028 proves document-to-tickets source traceability only; it does not assemble builder context or write code."
        };

        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return passed ? 0 : 1;
    }

    private static async Task<ProjectDocument> CreateSourceDocumentAsync(
        ProjectDocumentService documentService,
        int projectId,
        string dogfoodRunId)
    {
        var stamp = dogfoodRunId.Replace(':', '-').Replace('\\', '-').Replace('/', '-');
        var titleSeed = $"BOOKSELLER_DOCUMENT_TO_TICKETS_{stamp}_{Guid.NewGuid():N}";
        var title = titleSeed[..Math.Min(120, titleSeed.Length)];
        var content = """
        # BookSeller Document To Tickets Source

        Create implementation tickets for:
        - SQL-backed book inventory.
        - Storage location tracking.
        - Sales history capture.

        Boundary: this source document is for ticket generation only.
        """;

        return await documentService.CreateDocumentAsync(new CreateProjectDocumentRequest
        {
            ProjectId = projectId,
            Title = title,
            DocumentType = "BuildPlan",
            ContentMarkdown = content,
            ChangeSummary = "BookSeller document-to-tickets source plan.",
            CreatedBy = "TestAgent",
            SourceEntityType = "Discussion",
            SourceEntityId = 28028
        });
    }

    private static IEnumerable<ProjectTicket> BuildTickets(CliProjectContext project, long sourceVersionId)
    {
        string[] titles =
        [
            "BOOK-028-1 Add SQL-backed book inventory",
            "BOOK-028-2 Add storage location tracking",
            "BOOK-028-3 Add sales history capture"
        ];

        foreach (var title in titles)
        {
            yield return new ProjectTicket
            {
                TenantId = project.TenantId,
                ProjectId = project.ProjectId,
                SessionId = Guid.NewGuid(),
                Title = title,
                TicketType = "Feature",
                Priority = "Medium",
                Summary = "Generated from BookSeller document-to-tickets smoke source.",
                AcceptanceCriteria = "- Ticket preserves SourceDocumentVersionId.\n- Ticket source reference resolves to the source document version.",
                Status = "Draft",
                Content = $"Generated ticket '{title}' from ProjectDocumentVersion:{sourceVersionId}.",
                ContextSummary = $"Source ProjectDocumentVersion:{sourceVersionId}",
                IsGenerated = true,
                GenerationNote = "BookSeller 028 document-to-tickets smoke",
                SourceDocumentVersionId = sourceVersionId
            };
        }
    }
}
