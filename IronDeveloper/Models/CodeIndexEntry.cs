using System;
using System.Collections.Generic;

namespace IronDev.Agent.Models;

public class CodeIndexEntry
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public DateTime LastModifiedUtc { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public string? Namespace { get; set; }
    public string? TypeName { get; set; }
    public List<string> MethodNames { get; set; } = new();
    public List<string> ChunkText { get; set; } = new();
    public DateTime LastIndexedUtc { get; set; }
}