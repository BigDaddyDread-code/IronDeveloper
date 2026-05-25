namespace IronDev.Core.Models;

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
