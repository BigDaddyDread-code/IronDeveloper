using System.Collections.Generic;

namespace IronDev.Data.Models;

public sealed class ImplementationPlanResult
{
    public string Summary { get; set; } = string.Empty;
    public string TargetFilePath { get; set; } = string.Empty;
    public List<string> RelevantFiles { get; set; } = new();
    public List<string> ImplementationSteps { get; set; } = new();
    public List<string> Risks { get; set; } = new();
}

public sealed class CodeDraftResult
{
    public string FilePath { get; set; } = string.Empty;
    public string Language { get; set; } = "C#";
    public string Code { get; set; } = string.Empty;
}

public sealed class TestDraftResult
{
    public string FilePath { get; set; } = string.Empty;
    public string TestFramework { get; set; } = "MSTest";
    public string Code { get; set; } = string.Empty;
}
