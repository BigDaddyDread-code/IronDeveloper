using System;

namespace IronDev.Data.Models;

public sealed class CodeIndexEntry
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int ProjectId { get; set; }
    public long FileId { get; set; }
    public string? Namespace { get; set; }
    public string? SymbolName { get; set; }
    public string? SymbolType { get; set; } // Class, Method, etc.
    public string? Summary { get; set; }
    public string ChunkText { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    
    // Joined field
    public string? FilePath { get; set; }
}
