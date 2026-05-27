using IronDev.Core.Tools;

namespace IronDev.Infrastructure.Tools.CodeStandards;

public sealed class CodeStandardsAnalysisTool :
    IGovernedTool<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>,
    IGovernedToolRegistration
{
    public const string ToolName = "code_standards.analyse_patch";
    public const string ToolBoundary = "Code standards analysis is deterministic and read-only. It does not write files, run commands, call agents, call other tools, mutate tickets, mutate memory, use the network, or approve changes.";

    public GovernedToolDefinition Definition { get; } = new()
    {
        Name = ToolName,
        Description = "Analyse a proposed patch or changed-file packet for IronDev code-standard risks.",
        InputType = typeof(CodeStandardsAnalysisInput),
        OutputType = typeof(CodeStandardsAnalysisResult),
        AllowedCallers = ["BuilderAgent", "TestingAgent", "TesterAgent"],
        MutatesState = false,
        AllowsNestedCalls = false,
        AllowsFileWrites = false,
        AllowsProcessExecution = false,
        AllowsNetworkAccess = false,
        AllowsWorkspaceMutation = false,
        EvidenceKinds = ["CodeStandardsFinding"],
        Boundary = ToolBoundary
    };

    public Task<GovernedToolResult<CodeStandardsAnalysisResult>> ExecuteAsync(
        GovernedToolRequest<CodeStandardsAnalysisInput> request,
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var input = request.Input;
        var analysisText = BuildAnalysisText(input);
        if (string.IsNullOrWhiteSpace(analysisText))
        {
            return Task.FromResult(new GovernedToolResult<CodeStandardsAnalysisResult>
            {
                RequestId = request.RequestId,
                ToolName = ToolName,
                Status = GovernedToolStatus.Rejected,
                Summary = "Code standards analysis requires patch text or changed file content.",
                Output = new CodeStandardsAnalysisResult
                {
                    Status = "Rejected",
                    Summary = "No patch text or changed files were supplied.",
                    Findings = [Finding("High", "CS000", "No analysable patch or file content was supplied.", null, "empty input", "Provide patch text or changed-file content.")],
                    FilesAnalysed = 0,
                    HasBlockingFindings = true
                },
                BlockedActions = ["No analysable patch or file content was supplied."],
                StartedAtUtc = started,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Boundary = ToolBoundary
            });
        }

        var findings = Analyse(input, analysisText);
        var hasBlocking = findings.Any(finding =>
            string.Equals(finding.Severity, "High", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(finding.Severity, "Critical", StringComparison.OrdinalIgnoreCase));
        var status = hasBlocking ? "Blocked" : "Passed";
        var summary = findings.Count == 0
            ? "Code standards analysis found no blocking issues."
            : $"Code standards analysis found {findings.Count} finding(s), including {findings.Count(IsBlocking)} blocking finding(s).";

        return Task.FromResult(new GovernedToolResult<CodeStandardsAnalysisResult>
        {
            RequestId = request.RequestId,
            ToolName = ToolName,
            Status = GovernedToolStatus.Succeeded,
            Summary = summary,
            Output = new CodeStandardsAnalysisResult
            {
                Status = status,
                Summary = summary,
                Findings = findings,
                FilesAnalysed = input.ChangedFiles.Count,
                HasBlockingFindings = hasBlocking
            },
            EvidenceRefs = findings
                .Select(finding => string.IsNullOrWhiteSpace(finding.Path) ? finding.RuleId : $"{finding.Path}:{finding.RuleId}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StartedAtUtc = started,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Boundary = ToolBoundary
        });
    }

    private static IReadOnlyList<CodeStandardsFinding> Analyse(CodeStandardsAnalysisInput input, string analysisText)
    {
        var findings = new List<CodeStandardsFinding>();
        AddIfContains(findings, input, analysisText, "CodeStandardsAgent", "Critical", "CS001",
            "Code standards must be a governed tool/service, not a passive agent.",
            "Keep code standards in CodeStandardsAnalysisTool and do not add CodeStandardsAgent.");
        AddIfContains(findings, input, analysisText, "AgentCapabilityTool", "Critical", "CS002",
            "Generic agent-capability wrappers are forbidden in this governed tool path.",
            "Register strongly typed governed tools directly through IGovernedToolRegistry.");
        AddIfContains(findings, input, analysisText, "object " + "Input", "Critical", "CS003",
            "Governed tools must not accept an untyped object input payload.",
            "Use GovernedToolRequest<TInput> with a concrete input record.");
        AddIfContains(findings, input, analysisText, "DateTime.Now", "High", "CS010",
            "Product timestamps must be UTC-first.",
            "Use DateTimeOffset.UtcNow or DateTime.UtcNow and expose UTC semantics in DTO names.");
        AddIfContains(findings, input, analysisText, "File.WriteAllText", "High", "CS020",
            "This code path appears to write files.",
            "Keep the code standards tool read-only; move writes behind an explicit reviewed workflow if needed.");
        AddIfContains(findings, input, analysisText, "Process.Start", "High", "CS021",
            "This code path appears to execute a process.",
            "Code standards analysis must not run commands.");
        AddIfContains(findings, input, analysisText, "HttpClient", "Medium", "CS030",
            "This code path appears to add network access.",
            "Code standards analysis should remain local and deterministic unless a reviewed endpoint contract exists.");
        AddIfContains(findings, input, analysisText, "IGovernedToolRegistry", "Medium", "CS040",
            "Tool code appears to reference the governed tool registry.",
            "Individual tools must not call other tools; orchestration belongs outside the tool body.");

        return findings
            .OrderByDescending(SeverityRank)
            .ThenBy(finding => finding.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddIfContains(
        ICollection<CodeStandardsFinding> findings,
        CodeStandardsAnalysisInput input,
        string analysisText,
        string needle,
        string severity,
        string ruleId,
        string message,
        string recommendation)
    {
        if (!analysisText.Contains(needle, StringComparison.OrdinalIgnoreCase))
            return;

        findings.Add(Finding(
            severity,
            ruleId,
            message,
            FindPath(input, needle),
            needle,
            recommendation));
    }

    private static string BuildAnalysisText(CodeStandardsAnalysisInput input)
    {
        var fileText = string.Join(
            Environment.NewLine,
            input.ChangedFiles.Select(file => $"{file.Path}{Environment.NewLine}{file.Patch}{Environment.NewLine}{file.Content}"));
        return $"{input.PatchText}{Environment.NewLine}{fileText}".Trim();
    }

    private static string? FindPath(CodeStandardsAnalysisInput input, string needle) =>
        input.ChangedFiles.FirstOrDefault(file =>
            file.Patch.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            file.Content.Contains(needle, StringComparison.OrdinalIgnoreCase))?.Path;

    private static CodeStandardsFinding Finding(
        string severity,
        string ruleId,
        string message,
        string? path,
        string evidence,
        string recommendation) =>
        new()
        {
            Severity = severity,
            RuleId = ruleId,
            Message = message,
            Path = path,
            Evidence = evidence,
            Recommendation = recommendation
        };

    private static bool IsBlocking(CodeStandardsFinding finding) =>
        string.Equals(finding.Severity, "High", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(finding.Severity, "Critical", StringComparison.OrdinalIgnoreCase);

    private static int SeverityRank(CodeStandardsFinding finding) =>
        finding.Severity.ToLowerInvariant() switch
        {
            "critical" => 4,
            "high" => 3,
            "medium" => 2,
            "low" => 1,
            _ => 0
        };
}
