using System;

namespace IronDev.Data.Models;

public sealed class Project
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LocalPath { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public DateTime? LastIndexedUtc { get; set; }
    public string? IndexingStatus { get; set; }
}

public sealed class ProjectFile
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int ProjectId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime LastIndexedDate { get; set; }
}

public sealed class CodeIndexResult
{
    public int FilesScanned { get; set; }
    public int FilesAdded { get; set; }
    public int FilesUpdated { get; set; }
    public int FilesUnchanged { get; set; }
    public int FilesSkipped { get; set; }
}

public sealed class ChatMessage
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int ProjectId { get; set; }
    public Guid SessionId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public DateTime CreatedDate { get; set; }
}

public sealed class ProjectSummary
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int ProjectId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public long? SourceChatMessageId { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }
}

public sealed class ProjectDecision
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public long? SourceChatMessageId { get; set; }
    public string? LinkedFilePaths { get; set; }
    public string? LinkedCodeIndexEntryIds { get; set; }
    public string? LinkedSymbols { get; set; }
    public DateTime CreatedDate { get; set; }
}

public sealed class ProjectTicket
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int ProjectId { get; set; }
    public Guid SessionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string TicketType { get; set; } = "Task";
    public string Priority { get; set; } = "Medium";
    public string? Summary { get; set; }
    public string? Background { get; set; }
    public string? Problem { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public string? TechnicalNotes { get; set; }
    public string Status { get; set; } = "Draft";
    public string Content { get; set; } = string.Empty;
    public string? LinkedFilePaths { get; set; }
    public string? LinkedCodeIndexEntryIds { get; set; }
    public string? LinkedSymbols { get; set; }
    public DateTime CreatedDate { get; set; }
}
