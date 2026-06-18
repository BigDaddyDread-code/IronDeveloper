namespace IronDev.Core.Tools;

public enum ToolRequestKind
{
    Unknown = 0,
    PatchRunTest,
    PatchRunFinishTest,
    PatchRunInspection,
    FutureTool
}

public enum ToolKind
{
    Unknown = 0,
    ShellCommand,
    DotNetCommand,
    GitReadOnlyCommand
}

public enum ToolRiskClassification
{
    UnknownDangerous = 0,
    WorkspaceReadOnly,
    WorkspaceMutating,
    SourceControlDangerous,
    SourceApplyDangerous,
    ExternalMutationDangerous,
    ReleaseDangerous
}

public enum WorkspaceToolGateDecisionOutcome
{
    Block = 0,
    Allow
}

public sealed record ToolEvidenceRef
{
    public required string RefId { get; init; }
    public required string EvidenceKind { get; init; }
    public required string SafeSummary { get; init; }
}

public sealed record ToolCommandBoundary
{
    public bool WorkspaceOnly { get; init; } = true;
    public bool SourceRepositoryMutated { get; init; }
    public bool SourceApplied { get; init; }
    public bool GitCommitCreated { get; init; }
    public bool GitPushPerformed { get; init; }
    public bool PullRequestCreated { get; init; }
    public bool ApprovalGranted { get; init; }
    public bool PolicySatisfied { get; init; }
    public bool ReleaseApproved { get; init; }
    public bool WorkflowContinued { get; init; }
    public bool MemoryPromoted { get; init; }
    public bool AcceptedMemoryMutated { get; init; }
    public bool AgentDispatched { get; init; }
    public bool ModelCalled { get; init; }
    public bool ExternalToolExecuted { get; init; }

    public static ToolCommandBoundary None { get; } = new();
}

public sealed record ToolRequest
{
    public required string ToolRequestId { get; init; }
    public required string RunId { get; init; }
    public required ToolRequestKind RequestKind { get; init; }
    public required string ToolName { get; init; }
    public required ToolKind ToolKind { get; init; }
    public required string Command { get; init; }
    public required string ResolvedCommand { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string WorkspacePath { get; init; }
    public required string SourceRepoPath { get; init; }
    public required string RequestedBy { get; init; }
    public required string SourceComponent { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required ToolRiskClassification RiskClassification { get; init; }
    public ToolEvidenceRef[] EvidenceRefs { get; init; } = [];
    public ToolCommandBoundary Boundary { get; init; } = ToolCommandBoundary.None;
}

public sealed record WorkspaceToolGateDecision
{
    public required string ToolGateDecisionId { get; init; }
    public required string ToolRequestId { get; init; }
    public required string RunId { get; init; }
    public required WorkspaceToolGateDecisionOutcome Decision { get; init; }
    public string[] Reasons { get; init; } = [];
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string WorkspacePath { get; init; }
    public required string SourceRepoPath { get; init; }
    public required string Command { get; init; }
    public required ToolRiskClassification RiskClassification { get; init; }
    public ToolCommandBoundary Boundary { get; init; } = ToolCommandBoundary.None;
}

public sealed record ToolExecutionResult
{
    public required string ToolResultId { get; init; }
    public required string ToolRequestId { get; init; }
    public required string ToolGateDecisionId { get; init; }
    public required string RunId { get; init; }
    public required string Command { get; init; }
    public required string WorkingDirectory { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset FinishedAtUtc { get; init; }
    public int? ExitCode { get; init; }
    public string? StdoutPath { get; init; }
    public string? StderrPath { get; init; }
    public string? CombinedOutputPath { get; init; }
    public string? SummaryPath { get; init; }
    public required bool WasExecuted { get; init; }
    public ToolCommandBoundary Boundary { get; init; } = ToolCommandBoundary.None;
}

public static class ToolCommandRiskClassifier
{
    private static readonly string[] GitReadOnlyCommands =
    [
        "git status",
        "git diff",
        "git rev-parse",
        "git show",
        "git log"
    ];

    public static ToolKind DetectKind(string? command)
    {
        var normalized = NormalizeCommand(command);
        if (normalized.StartsWith("dotnet ", StringComparison.Ordinal) || string.Equals(normalized, "dotnet", StringComparison.Ordinal))
            return ToolKind.DotNetCommand;

        if (GitReadOnlyCommands.Any(item => normalized.StartsWith(item, StringComparison.Ordinal)))
            return ToolKind.GitReadOnlyCommand;

        if (string.IsNullOrWhiteSpace(normalized))
            return ToolKind.Unknown;

        return ToolKind.ShellCommand;
    }

    public static ToolRiskClassification Classify(string? command)
    {
        var normalized = NormalizeCommand(command);
        if (string.IsNullOrWhiteSpace(normalized))
            return ToolRiskClassification.UnknownDangerous;

        if (ContainsAny(normalized, "source apply", "git apply"))
            return ToolRiskClassification.SourceApplyDangerous;

        if (ContainsAny(normalized, "git push", "git commit", "git merge", "git tag", "gh pr", "pull request", "create pr"))
            return ToolRiskClassification.SourceControlDangerous;

        if (ContainsAny(normalized, "memory promotion", "promote memory", "accepted memory", "memory mutation", "mutate memory"))
            return ToolRiskClassification.ExternalMutationDangerous;

        if (ContainsAny(normalized, "workflow continue", "continue workflow", "workflow continuation"))
            return ToolRiskClassification.ExternalMutationDangerous;

        if (ContainsAny(normalized, "release approve", "release approval", "deployment approval", "deploy", "deployment", "merge approval", "approve merge"))
            return ToolRiskClassification.ReleaseDangerous;

        if (normalized.StartsWith("dotnet --version", StringComparison.Ordinal) ||
            normalized.StartsWith("dotnet build", StringComparison.Ordinal) ||
            normalized.StartsWith("dotnet test", StringComparison.Ordinal) ||
            GitReadOnlyCommands.Any(item => normalized.StartsWith(item, StringComparison.Ordinal)))
            return ToolRiskClassification.WorkspaceReadOnly;

        return ToolRiskClassification.WorkspaceMutating;
    }

    public static string[] ForbiddenReasons(string? command)
    {
        var normalized = NormalizeCommand(command);
        var reasons = new List<string>();

        if (ContainsAny(normalized, "source apply", "git apply"))
            reasons.Add("CommandRequestsSourceApply");

        if (ContainsAny(normalized, "git commit"))
            reasons.Add("CommandRequestsGitCommit");

        if (ContainsAny(normalized, "git push"))
            reasons.Add("CommandRequestsGitPush");

        if (ContainsAny(normalized, "git merge", "git tag"))
            reasons.Add("CommandForbidden");

        if (ContainsAny(normalized, "gh pr", "pull request", "create pr"))
            reasons.Add("CommandRequestsPullRequest");

        if (ContainsAny(normalized, "memory promotion", "promote memory", "accepted memory", "memory mutation", "mutate memory"))
            reasons.Add("CommandRequestsMemoryMutation");

        if (ContainsAny(normalized, "workflow continue", "continue workflow", "workflow continuation"))
            reasons.Add("CommandRequestsWorkflowContinuation");

        if (ContainsAny(normalized, "release approve", "release approval", "deployment approval", "deploy", "deployment", "merge approval", "approve merge"))
            reasons.Add("CommandRequestsReleaseOrDeployment");

        return reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool ContainsAny(string value, params string[] markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.Ordinal));

    private static string NormalizeCommand(string? command) =>
        string.Join(' ', (command ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
}

public static class WorkspaceToolGateEvaluator
{
    public static WorkspaceToolGateDecision Evaluate(ToolRequest request, Func<string, bool>? directoryExists = null)
    {
        directoryExists ??= Directory.Exists;
        var reasons = new List<string>();

        if (string.IsNullOrWhiteSpace(request.RunId))
            reasons.Add("MissingRun");

        if (string.IsNullOrWhiteSpace(request.WorkspacePath))
            reasons.Add("MissingWorkspace");
        else if (!directoryExists(request.WorkspacePath))
            reasons.Add("WorkspaceMissing");

        if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
            reasons.Add("WorkingDirectoryOutsideWorkspace");
        else if (!string.IsNullOrWhiteSpace(request.WorkspacePath) && !IsSameOrUnderPath(request.WorkingDirectory, request.WorkspacePath))
            reasons.Add("WorkingDirectoryOutsideWorkspace");

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory) &&
            !string.IsNullOrWhiteSpace(request.SourceRepoPath) &&
            IsSamePath(request.WorkingDirectory, request.SourceRepoPath))
            reasons.Add("WorkingDirectoryIsSourceRepo");

        reasons.AddRange(ToolCommandRiskClassifier.ForbiddenReasons(request.ResolvedCommand));

        return new WorkspaceToolGateDecision
        {
            ToolGateDecisionId = $"tool_gate_{Guid.NewGuid():N}",
            ToolRequestId = request.ToolRequestId,
            RunId = request.RunId,
            Decision = reasons.Count == 0 ? WorkspaceToolGateDecisionOutcome.Allow : WorkspaceToolGateDecisionOutcome.Block,
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            WorkingDirectory = request.WorkingDirectory,
            WorkspacePath = request.WorkspacePath,
            SourceRepoPath = request.SourceRepoPath,
            Command = request.ResolvedCommand,
            RiskClassification = request.RiskClassification,
            Boundary = ToolCommandBoundary.None
        };
    }

    private static bool IsSamePath(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(NormalizeForPathComparison(left), NormalizeForPathComparison(right), comparison);
    }

    private static bool IsSameOrUnderPath(string candidate, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var normalizedCandidate = NormalizeForPathComparison(candidate);
        var normalizedRoot = NormalizeForPathComparison(root);
        return string.Equals(normalizedCandidate, normalizedRoot, comparison) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static string NormalizeForPathComparison(string path) =>
        Path.GetFullPath(path.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
}
