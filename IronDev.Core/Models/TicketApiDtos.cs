namespace IronDev.Core.Models;

public enum TicketVersionedUpdateStatus
{
    Succeeded = 0,
    NotFound = 1,
    StaleWrite = 2
}

public sealed record TicketVersionedUpdateResult(
    TicketVersionedUpdateStatus Status,
    IronDev.Data.Models.ProjectTicket? Ticket);

public sealed class CreateProjectTicketRequest
{
    public string Title { get; init; } = string.Empty;
    public string? Type { get; init; }
    public string? Priority { get; init; }
    public string? Summary { get; init; }
    public string? Problem { get; init; }
    public string? ProposedChange { get; init; }
    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];
    public IReadOnlyList<ExternalReferenceDto> ExternalReferences { get; init; } = [];
    public TicketProvenanceDto? Provenance { get; init; }

    /// <summary>
    /// DOGFOOD-2 finding F-J: repo-relative paths the work is expected to touch.
    /// The single most reliability-critical Builder input (HERO-2, cycle 001:
    /// without it the live builder writes plausible code at guessed paths), and
    /// the form-shaped ticket path could not carry it at all.
    /// </summary>
    public IReadOnlyList<string> LinkedFilePaths { get; init; } = [];
}

public sealed class ImportExternalTicketRequest
{
    public string Title { get; init; } = string.Empty;
    public string? Type { get; init; }
    public string? Priority { get; init; }
    public string? Summary { get; init; }
    public string? Problem { get; init; }
    public string? ProposedChange { get; init; }
    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];
    public ExternalReferenceDto ExternalReference { get; init; } = new();
    public TicketProvenanceDto? Provenance { get; init; }
}

public sealed class GenerateTicketFromDiscussionRequest
{
    public string Discussion { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string? Type { get; init; }
    public string? Priority { get; init; }
    public TicketProvenanceDto? Provenance { get; init; }
}

public sealed class ExternalReferenceDto
{
    public string Provider { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string ExternalId { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string? Title { get; init; }
}

public sealed class TicketProvenanceDto
{
    public string Source { get; init; } = string.Empty;
    public string? CreatedBy { get; init; }
    public string? Notes { get; init; }
}
