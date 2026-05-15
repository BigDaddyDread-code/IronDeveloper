using System;
using System.Collections.Generic;

namespace IronDev.Data.Models;

public sealed class ProjectProfile
{
    public long ProjectProfileId { get; set; }
    public int TenantId { get; set; }
    public int ProjectId { get; set; }
    public bool IsExternalProject { get; set; }
    public string? ApplicationType { get; set; }
    public string? PrimaryLanguage { get; set; }
    public string? Framework { get; set; }
    public string? RuntimeVersion { get; set; }
    public string? DatabaseEngine { get; set; }
    public string? DataAccessStyle { get; set; }
    public string? TestFramework { get; set; }
    public string? SolutionFile { get; set; }
    public string? SafeWriteRoot { get; set; }
    public bool AllowBuilderApply { get; set; }
    public bool AllowWritesOutsideProjectRoot { get; set; }
    public string? ProfileNotes { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }
}

public sealed class ProjectCommand
{
    public long ProjectCommandId { get; set; }
    public int TenantId { get; set; }
    public int ProjectId { get; set; }
    public string CommandType { get; set; } = "Build"; // Build, Test, Run, Lint, Format
    public string CommandText { get; set; } = string.Empty;
    public string? WorkingDirectory { get; set; }
    public int TimeoutSeconds { get; set; } = 300;
    public bool IsDefault { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }
}

public sealed class ProjectProfileOption
{
    public int ProjectProfileOptionId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
