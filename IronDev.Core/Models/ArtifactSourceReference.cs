using System;

namespace IronDev.Core.Models;

public sealed class ArtifactSourceReference
{
    public long ArtifactSourceReferenceId { get; set; }

    public int TenantId { get; set; }
    public int ProjectId { get; set; }

    public string ArtifactType { get; set; } = string.Empty;
    public long ArtifactId { get; set; }

    public string SourceType { get; set; } = string.Empty;
    public long? SourceId { get; set; }

    public string? SourcePath { get; set; }
    public string? SourceSymbol { get; set; }
    public string? SourceSection { get; set; }
    public string? SourceAnchor { get; set; }

    public string ReferenceType { get; set; } = string.Empty;
    public string? Summary { get; set; }

    public decimal? RelevanceScore { get; set; }
    public bool IsRequired { get; set; }

    public DateTime CreatedUtc { get; set; }
    public string? CreatedBy { get; set; }
}
