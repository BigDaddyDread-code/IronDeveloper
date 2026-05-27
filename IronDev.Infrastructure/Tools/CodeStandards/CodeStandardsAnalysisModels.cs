namespace IronDev.Infrastructure.Tools.CodeStandards;

public sealed record CodeStandardsAnalysisInput
{
    public string PatchText { get; init; } = string.Empty;
    public IReadOnlyList<CodeStandardsChangedFile> ChangedFiles { get; init; } = [];
    public string TargetLanguage { get; init; } = "csharp";
    public string Stack { get; init; } = ".NET";
}

public sealed record CodeStandardsChangedFile
{
    public required string Path { get; init; }
    public string Content { get; init; } = string.Empty;
    public string Patch { get; init; } = string.Empty;
}

public sealed record CodeStandardsAnalysisResult
{
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<CodeStandardsFinding> Findings { get; init; } = [];
    public int FilesAnalysed { get; init; }
    public bool HasBlockingFindings { get; init; }
    public string Boundary { get; init; } = CodeStandardsAnalysisTool.ToolBoundary;
}

public sealed record CodeStandardsFinding
{
    public required string Severity { get; init; }
    public required string RuleId { get; init; }
    public required string Message { get; init; }
    public string? Path { get; init; }
    public string Evidence { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
}
