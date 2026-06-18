using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IronDev.Core.Governance;

namespace IronDev.Core.Memory;

public enum MemoryScope
{
    Run = 0,
    Project,
    PortableEngineering
}

public enum MemoryKind
{
    EngineeringLesson = 0,
    FailureMode,
    ProjectConvention,
    TestCommandLesson,
    ToolGateLesson,
    AiAssistanceLesson,
    RiskPattern,
    ReviewHeuristic
}

public enum MemoryEvidenceKind
{
    PatchRun = 0,
    PatchDiff,
    ChangedFiles,
    TestSummary,
    ToolResult,
    GovernanceEvent,
    ModelResponse,
    AiReview,
    KnownRisks,
    ManualApplyInstructions,
    ReviewSummary,
    AiAssistSummary
}

public enum MemoryKeyGateDecision
{
    Allow = 0,
    Block
}

public enum MemoryPromotionDecision
{
    Accepted = 0,
    Blocked
}

public sealed record MemoryBoundary
{
    public bool SourceRepoMutated { get; init; }
    public bool SourceApplied { get; init; }
    public bool GitCommitCreated { get; init; }
    public bool GitPushPerformed { get; init; }
    public bool PullRequestCreated { get; init; }
    public bool ApprovalGranted { get; init; }
    public bool PolicySatisfied { get; init; }
    public bool ReleaseApproved { get; init; }
    public bool DeploymentApproved { get; init; }
    public bool MergeApproved { get; init; }
    public bool WorkflowContinued { get; init; }
    public bool AgentDispatched { get; init; }
    public bool ModelCalled { get; init; }
    public bool MemoryPromoted { get; init; }
    public bool MemorySelfPromoted { get; init; }
    public bool AcceptedMemoryVersionAppended { get; init; }
    public bool HiddenChainOfThoughtStored { get; init; }
    public bool RawSourceCodeStored { get; init; }

    public static MemoryBoundary None { get; } = new();
}

public sealed record MemoryEvidenceRef
{
    public required string RefId { get; init; }
    public required MemoryEvidenceKind EvidenceKind { get; init; }
    public required string Path { get; init; }
    public required string SafeSummary { get; init; }
    public string? Sha256 { get; init; }
}

public sealed record MemoryProposalSafetyFlags
{
    public bool ContainsHiddenChainOfThought { get; init; }
    public bool ContainsSecretShape { get; init; }
    public bool ContainsAuthorityClaim { get; init; }
    public bool ContainsRawSourceCode { get; init; }
    public bool ContainsProjectSpecificPortableDetail { get; init; }
}

public sealed record MemoryProposal
{
    public required string MemoryProposalId { get; init; }
    public required string RunId { get; init; }
    public required string SourceProjectId { get; init; }
    public required string SourceRepoPath { get; init; }
    public required string SourceRepoIdentity { get; init; }
    public required MemoryScope ProposedScope { get; init; }
    public required MemoryKind MemoryKind { get; init; }
    public required string ProposedKey { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string Content { get; init; }
    public required string SanitisedContent { get; init; }
    public MemoryEvidenceRef[] EvidenceRefs { get; init; } = [];
    public required string CreatedBy { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required string ProposedConfidence { get; init; }
    public bool RequiresHumanReview { get; init; } = true;
    public MemoryProposalSafetyFlags SafetyFlags { get; init; } = new();
    public MemoryBoundary Boundary { get; init; } = MemoryBoundary.None;
    public string? SourceRunPath { get; init; }
}

public sealed record PatchRunMemorySource
{
    public required string RunId { get; init; }
    public required string SourceProjectId { get; init; }
    public required string SourceRepoPath { get; init; }
    public required string SourceRepoIdentity { get; init; }
    public required string RunPath { get; init; }
    public required string CreatedBy { get; init; }
}

public static class MemoryProposalBuilder
{
    private static readonly string[] SafeContentArtifacts =
    [
        "review-summary.md",
        "known-risks.md",
        "test-output-summary.md",
        "ai-assist-summary.md",
        "ai-review.md"
    ];

    public static MemoryProposal BuildFromPatchRun(
        PatchRunMemorySource source,
        IReadOnlyDictionary<string, string> artifactText,
        IReadOnlyDictionary<string, string> artifactHashes,
        DateTimeOffset? now = null)
    {
        var createdAt = now ?? DateTimeOffset.UtcNow;
        var sections = SafeContentArtifacts
            .Where(artifactText.ContainsKey)
            .Select(name => $"## {name}\n{MemoryContentSafety.SanitiseSummary(artifactText[name])}")
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        var content = sections.Length == 0
            ? $"Patch run {source.RunId} produced review evidence that may contain an engineering lesson. Human review remains required."
            : string.Join("\n\n", sections);

        var summary = MemoryContentSafety.SanitiseSummary(FirstUsefulSummary(artifactText) ?? $"Patch run {source.RunId} produced reviewable evidence.");
        var key = MemoryKeyNormalizer.BuildProjectKey(source.SourceProjectId, $"patch-run-{source.RunId}-lesson");
        var evidence = BuildEvidenceRefs(source.RunPath, artifactText.Keys, artifactHashes).ToArray();

        return new MemoryProposal
        {
            MemoryProposalId = $"mem_prop_{Guid.NewGuid():N}",
            RunId = source.RunId.Trim(),
            SourceProjectId = source.SourceProjectId.Trim(),
            SourceRepoPath = source.SourceRepoPath.Trim(),
            SourceRepoIdentity = source.SourceRepoIdentity.Trim(),
            ProposedScope = MemoryScope.Project,
            MemoryKind = MemoryKind.EngineeringLesson,
            ProposedKey = key,
            Title = $"Patch run lesson: {source.RunId}",
            Summary = summary,
            Content = content,
            SanitisedContent = MemoryContentSafety.SanitiseMemoryContent(content),
            EvidenceRefs = evidence,
            CreatedBy = source.CreatedBy.Trim(),
            CreatedAtUtc = createdAt,
            ProposedConfidence = "Medium",
            RequiresHumanReview = true,
            SafetyFlags = MemoryContentSafety.Flags(content, MemoryScope.Project, source),
            Boundary = MemoryBoundary.None,
            SourceRunPath = source.RunPath
        };
    }

    private static string? FirstUsefulSummary(IReadOnlyDictionary<string, string> artifactText)
    {
        foreach (var name in SafeContentArtifacts)
        {
            if (!artifactText.TryGetValue(name, out var text))
                continue;

            var line = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .FirstOrDefault(item => item.Length > 0 && !item.StartsWith("#", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(line))
                return line;
        }

        return null;
    }

    private static IEnumerable<MemoryEvidenceRef> BuildEvidenceRefs(string runPath, IEnumerable<string> artifactNames, IReadOnlyDictionary<string, string> artifactHashes)
    {
        foreach (var name in artifactNames.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            yield return new MemoryEvidenceRef
            {
                RefId = name,
                EvidenceKind = EvidenceKindFor(name),
                Path = name,
                SafeSummary = name.Equals("patch.diff", StringComparison.OrdinalIgnoreCase)
                    ? "Patch diff is referenced as evidence only; raw diff is not memory content."
                    : $"Safe summary artifact for {name}.",
                Sha256 = artifactHashes.TryGetValue(name, out var hash) ? hash : null
            };
        }
    }

    private static MemoryEvidenceKind EvidenceKindFor(string name) => name.ToLowerInvariant() switch
    {
        "run.json" => MemoryEvidenceKind.PatchRun,
        "patch.diff" => MemoryEvidenceKind.PatchDiff,
        "changed-files.txt" => MemoryEvidenceKind.ChangedFiles,
        "test-output-summary.md" => MemoryEvidenceKind.TestSummary,
        "tool-results.jsonl" => MemoryEvidenceKind.ToolResult,
        "governance-events.jsonl" => MemoryEvidenceKind.GovernanceEvent,
        "model-responses.jsonl" => MemoryEvidenceKind.ModelResponse,
        "ai-review.md" => MemoryEvidenceKind.AiReview,
        "known-risks.md" => MemoryEvidenceKind.KnownRisks,
        "manual-apply-instructions.md" => MemoryEvidenceKind.ManualApplyInstructions,
        "review-summary.md" => MemoryEvidenceKind.ReviewSummary,
        "ai-assist-summary.md" => MemoryEvidenceKind.AiAssistSummary,
        _ => MemoryEvidenceKind.PatchRun
    };
}

public sealed record MemoryKey
{
    public required MemoryScope Scope { get; init; }
    public string? ProjectId { get; init; }
    public required string Key { get; init; }
    public required string Topic { get; init; }
    public required int Version { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record MemoryKeyGateResult
{
    public required string MemoryKeyGateResultId { get; init; }
    public required string MemoryProposalId { get; init; }
    public required string ProposedKey { get; init; }
    public required MemoryScope ProposedScope { get; init; }
    public required MemoryKeyGateDecision Decision { get; init; }
    public string[] Reasons { get; init; } = [];
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public MemoryBoundary Boundary { get; init; } = MemoryBoundary.None;
}

public static class MemoryKeyNormalizer
{
    private static readonly Regex UnsafeCharacters = new("[^a-z0-9:-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DuplicateDash = new("-+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string BuildRunKey(string runId, string topic) => $"run:{Slug(runId)}:{Slug(topic)}";

    public static string BuildProjectKey(string projectId, string topic) => $"project:{Slug(projectId)}:{Slug(topic)}";

    public static string BuildPortableEngineeringKey(string topic) => $"portable-engineering:{Slug(topic)}";

    public static string Slug(string value)
    {
        var lower = (value ?? string.Empty).Trim().ToLowerInvariant();
        var replaced = UnsafeCharacters.Replace(lower, "-").Trim('-');
        return DuplicateDash.Replace(replaced, "-");
    }
}

public static class MemoryContentSafety
{
    private static readonly string[] HiddenReasoningMarkers =
    [
        "hidden chain-of-thought",
        "chain of thought",
        "chain-of-thought",
        "private scratchpad",
        "scratchpad",
        "private reasoning",
        "raw prompt",
        "raw completion",
        "raw tool output"
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "approval granted",
        "policy satisfied",
        "release approved",
        "deployment approved",
        "merge approved",
        "workflow continued",
        "source apply approved",
        "safe to merge",
        "safe to deploy"
    ];

    private static readonly Regex SecretShape = new(@"(api[_-]?key|secret|token|password|-----BEGIN [A-Z ]+PRIVATE KEY-----)", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CommitHash = new(@"\b[0-9a-f]{7,40}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PullRequestNumber = new(@"\b(PR|pull request|issue)\s*#?\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PathLike = new(@"([a-zA-Z]:\\|\\|/|\b\w+\.cs\b|\b\w+\.sql\b|\b\w+\.ts\b|\b\w+\.tsx\b)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CodeLike = new(@"\b(namespace|public class|private class|using System|SELECT \* FROM|CREATE TABLE|function\s+\w+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static MemoryProposalSafetyFlags Flags(string? content, MemoryScope scope, PatchRunMemorySource? source = null) => new()
    {
        ContainsHiddenChainOfThought = ContainsHiddenReasoning(content),
        ContainsSecretShape = ContainsSecretShape(content),
        ContainsAuthorityClaim = ContainsAuthorityClaim(content),
        ContainsRawSourceCode = ContainsRawSourceCode(content),
        ContainsProjectSpecificPortableDetail = scope == MemoryScope.PortableEngineering && ContainsProjectSpecificDetail(content, source)
    };

    public static bool ContainsHiddenReasoning(string? content) => ContainsAny(content, HiddenReasoningMarkers);

    public static bool ContainsAuthorityClaim(string? content) => ContainsAny(content, AuthorityMarkers);

    public static bool ContainsSecretShape(string? content) => !string.IsNullOrWhiteSpace(content) && SecretShape.IsMatch(content);

    public static bool ContainsRawSourceCode(string? content) => !string.IsNullOrWhiteSpace(content) && CodeLike.IsMatch(content);

    public static bool ContainsPathLikeText(string? content) => !string.IsNullOrWhiteSpace(content) && PathLike.IsMatch(content);

    public static bool ContainsProjectSpecificDetail(string? content, PatchRunMemorySource? source = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        if (PathLike.IsMatch(content) || CommitHash.IsMatch(content) || PullRequestNumber.IsMatch(content))
            return true;

        if (source is null)
            return false;

        return ContainsLiteral(content, source.SourceRepoIdentity) ||
            ContainsLiteral(content, source.SourceProjectId) ||
            ContainsLiteral(content, Path.GetFileName(source.SourceRepoPath));
    }

    public static string SanitiseSummary(string? content)
    {
        var sanitised = SanitiseMemoryContent(content);
        if (sanitised.Length <= 240)
            return sanitised;

        return sanitised[..240].TrimEnd() + "...";
    }

    public static string SanitiseMemoryContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var value = SecretShape.Replace(content, "[REDACTED]");
        foreach (var marker in HiddenReasoningMarkers)
            value = value.Replace(marker, "[REDACTED]", StringComparison.OrdinalIgnoreCase);
        return value.Trim();
    }

    private static bool ContainsAny(string? content, IEnumerable<string> markers) =>
        !string.IsNullOrWhiteSpace(content) && markers.Any(marker => content.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsLiteral(string content, string? value) =>
        !string.IsNullOrWhiteSpace(value) && content.Contains(value.Trim(), StringComparison.OrdinalIgnoreCase);
}

public static class MemoryKeyGate
{
    public static MemoryKeyGateResult Evaluate(MemoryProposal? proposal, DateTimeOffset? now = null)
    {
        var reasons = new List<string>();
        if (proposal is null)
        {
            return Result("missing", string.Empty, MemoryScope.Run, ["MissingProposal"], now);
        }

        if (proposal.EvidenceRefs.Length == 0)
            reasons.Add("MissingEvidence");

        if (string.IsNullOrWhiteSpace(proposal.ProposedKey) || proposal.ProposedKey.Length > 160)
            reasons.Add("InvalidKey");

        if (IsTooBroad(proposal.ProposedKey))
            reasons.Add("KeyTooBroad");

        if (proposal.ProposedKey.Contains('\\') || proposal.ProposedKey.Contains('/'))
            reasons.Add("KeyContainsPath");

        if (MemoryContentSafety.ContainsSecretShape(proposal.ProposedKey))
            reasons.Add("KeyContainsSecretShape");

        if (proposal.ProposedScope == MemoryScope.Project && string.IsNullOrWhiteSpace(proposal.SourceProjectId))
            reasons.Add("ProjectMemoryMissingProjectId");

        if (MemoryContentSafety.ContainsHiddenReasoning(proposal.Content) || proposal.SafetyFlags.ContainsHiddenChainOfThought)
            reasons.Add("ContentContainsHiddenReasoning");

        if (MemoryContentSafety.ContainsAuthorityClaim(proposal.Content) || proposal.SafetyFlags.ContainsAuthorityClaim)
            reasons.Add("ContentContainsAuthorityClaim");

        if (MemoryContentSafety.ContainsSecretShape(proposal.Content) || proposal.SafetyFlags.ContainsSecretShape)
            reasons.Add("ContentContainsSecretShape");

        if (proposal.Content.Length > 4000 || proposal.SanitisedContent.Length > 4000)
            reasons.Add("ContentTooLong");

        if (!Enum.IsDefined(proposal.MemoryKind))
            reasons.Add("UnsafeMemoryKind");

        if (proposal.ProposedScope == MemoryScope.PortableEngineering)
        {
            var source = new PatchRunMemorySource
            {
                RunId = proposal.RunId,
                SourceProjectId = proposal.SourceProjectId,
                SourceRepoPath = proposal.SourceRepoPath,
                SourceRepoIdentity = proposal.SourceRepoIdentity,
                RunPath = proposal.SourceRunPath ?? string.Empty,
                CreatedBy = proposal.CreatedBy
            };

            if (MemoryContentSafety.ContainsProjectSpecificDetail(proposal.Content, source) || proposal.SafetyFlags.ContainsProjectSpecificPortableDetail)
                reasons.Add("PortableMemoryContainsProjectSpecificDetail");
            if (MemoryContentSafety.ContainsRawSourceCode(proposal.Content) || proposal.SafetyFlags.ContainsRawSourceCode)
                reasons.Add("PortableMemoryContainsCode");
            if (proposal.Content.Contains("client", StringComparison.OrdinalIgnoreCase) || proposal.Content.Contains("customer", StringComparison.OrdinalIgnoreCase))
                reasons.Add("PortableMemoryContainsClientDetail");
        }

        return Result(proposal.MemoryProposalId, proposal.ProposedKey, proposal.ProposedScope, reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), now);
    }

    private static MemoryKeyGateResult Result(string proposalId, string key, MemoryScope scope, string[] reasons, DateTimeOffset? now) => new()
    {
        MemoryKeyGateResultId = $"mem_key_gate_{Guid.NewGuid():N}",
        MemoryProposalId = proposalId,
        ProposedKey = key,
        ProposedScope = scope,
        Decision = reasons.Length == 0 ? MemoryKeyGateDecision.Allow : MemoryKeyGateDecision.Block,
        Reasons = reasons,
        EvaluatedAtUtc = now ?? DateTimeOffset.UtcNow,
        Boundary = MemoryBoundary.None
    };

    private static bool IsTooBroad(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return true;

        var topic = key.Split(':', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? key;
        return topic is "all" or "global" or "everything" or "memory" or "project" or "repo";
    }
}

public sealed record AcceptedMemoryRecord
{
    public required string MemoryId { get; init; }
    public required MemoryScope Scope { get; init; }
    public string? ProjectId { get; init; }
    public required string Key { get; init; }
    public required int CurrentVersion { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public bool IsSuperseded { get; init; }
    public string? SupersededByMemoryId { get; init; }
}

public sealed record AcceptedMemoryVersion
{
    public required string MemoryVersionId { get; init; }
    public required string MemoryId { get; init; }
    public required int Version { get; init; }
    public required string ProposalId { get; init; }
    public required string PromotionRequestId { get; init; }
    public required string ConscienceDecisionId { get; init; }
    public required string ThoughtLedgerRef { get; init; }
    public required string Content { get; init; }
    public required string SanitisedContent { get; init; }
    public MemoryEvidenceRef[] EvidenceRefs { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required string ContentHash { get; init; }
    public MemoryBoundary Boundary { get; init; } = MemoryBoundary.None;
}

public sealed record MemoryPromotionRequest
{
    public required string MemoryPromotionRequestId { get; init; }
    public required string MemoryProposalId { get; init; }
    public required MemoryScope ProposedScope { get; init; }
    public required string ProposedKey { get; init; }
    public required string RequestedBy { get; init; }
    public string? ConscienceDecisionRef { get; init; }
    public string? ThoughtLedgerRef { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public MemoryBoundary Boundary { get; init; } = MemoryBoundary.None;
}

public sealed record MemoryPromotionReceipt
{
    public required string MemoryPromotionReceiptId { get; init; }
    public required string MemoryPromotionRequestId { get; init; }
    public required string MemoryProposalId { get; init; }
    public required MemoryPromotionDecision Decision { get; init; }
    public string[] Reasons { get; init; } = [];
    public string? AcceptedMemoryId { get; init; }
    public string? AcceptedMemoryVersionId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public MemoryBoundary Boundary { get; init; } = MemoryBoundary.None;
}

public sealed record AcceptedMemoryAppendResult(AcceptedMemoryRecord Record, AcceptedMemoryVersion Version);

public sealed class AcceptedMemoryStore
{
    private const string IndexArtifact = "accepted-memory-index.json";
    private const string LogArtifact = "accepted-memory.jsonl";
    private static readonly JsonSerializerOptions StoreJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly JsonSerializerOptions JsonLineOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public AcceptedMemoryStore(string rootPath)
    {
        RootPath = string.IsNullOrWhiteSpace(rootPath) ? Path.Combine(Path.GetTempPath(), "irondev-memory") : Path.GetFullPath(rootPath);
    }

    public string RootPath { get; }

    public AcceptedMemoryAppendResult AppendVersion(MemoryProposal proposal, MemoryPromotionRequest request, ConscienceDecision conscienceDecision, string thoughtLedgerRef, DateTimeOffset? now = null)
    {
        Directory.CreateDirectory(RootPath);
        var createdAt = now ?? DateTimeOffset.UtcNow;
        var records = LoadRecords().ToList();
        var existingIndex = records.FindIndex(item => string.Equals(item.Key, proposal.ProposedKey, StringComparison.OrdinalIgnoreCase) && item.Scope == proposal.ProposedScope && string.Equals(item.ProjectId ?? string.Empty, proposal.SourceProjectId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        var memoryId = existingIndex >= 0 ? records[existingIndex].MemoryId : $"mem_{ShortHash($"{proposal.ProposedScope}|{proposal.SourceProjectId}|{proposal.ProposedKey}")}";
        var nextVersion = existingIndex >= 0 ? records[existingIndex].CurrentVersion + 1 : 1;
        var record = new AcceptedMemoryRecord
        {
            MemoryId = memoryId,
            Scope = proposal.ProposedScope,
            ProjectId = proposal.ProposedScope == MemoryScope.Project ? proposal.SourceProjectId : null,
            Key = proposal.ProposedKey,
            CurrentVersion = nextVersion,
            CreatedAtUtc = existingIndex >= 0 ? records[existingIndex].CreatedAtUtc : createdAt,
            UpdatedAtUtc = createdAt,
            IsSuperseded = false,
            SupersededByMemoryId = null
        };

        if (existingIndex >= 0)
            records[existingIndex] = record;
        else
            records.Add(record);

        var contentHash = Sha256Hex(proposal.SanitisedContent);
        var version = new AcceptedMemoryVersion
        {
            MemoryVersionId = $"mem_ver_{Guid.NewGuid():N}",
            MemoryId = memoryId,
            Version = nextVersion,
            ProposalId = proposal.MemoryProposalId,
            PromotionRequestId = request.MemoryPromotionRequestId,
            ConscienceDecisionId = conscienceDecision.DecisionId,
            ThoughtLedgerRef = thoughtLedgerRef.Trim(),
            Content = proposal.SanitisedContent,
            SanitisedContent = proposal.SanitisedContent,
            EvidenceRefs = proposal.EvidenceRefs,
            CreatedAtUtc = createdAt,
            ContentHash = contentHash,
            Boundary = MemoryBoundary.None with { AcceptedMemoryVersionAppended = true }
        };

        WriteVersion(version);
        File.AppendAllText(Path.Combine(RootPath, LogArtifact), JsonSerializer.Serialize(record, JsonLineOptions) + Environment.NewLine);
        WriteIndex(records);
        return new AcceptedMemoryAppendResult(record, version);
    }

    public AcceptedMemoryRecord[] List() => LoadRecords();

    public AcceptedMemoryRecord? GetByKey(string key) =>
        LoadRecords().FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));

    public AcceptedMemoryVersion[] Versions(string memoryId)
    {
        var folder = Path.Combine(RootPath, "accepted-memory-versions", memoryId);
        if (!Directory.Exists(folder))
            return [];

        return Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => JsonSerializer.Deserialize<AcceptedMemoryVersion>(File.ReadAllText(path), StoreJsonOptions))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.Version)
            .ToArray();
    }

    private AcceptedMemoryRecord[] LoadRecords()
    {
        var path = Path.Combine(RootPath, IndexArtifact);
        if (!File.Exists(path))
            return [];

        var index = JsonSerializer.Deserialize<AcceptedMemoryIndex>(File.ReadAllText(path), StoreJsonOptions);
        return index?.Records ?? [];
    }

    private void WriteIndex(IReadOnlyList<AcceptedMemoryRecord> records)
    {
        var path = Path.Combine(RootPath, IndexArtifact);
        File.WriteAllText(path, JsonSerializer.Serialize(new AcceptedMemoryIndex { Records = records.ToArray() }, StoreJsonOptions));
    }

    private void WriteVersion(AcceptedMemoryVersion version)
    {
        var folder = Path.Combine(RootPath, "accepted-memory-versions", version.MemoryId);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{version.Version}.json");
        if (File.Exists(path))
            throw new InvalidOperationException("accepted memory version already exists and cannot be rewritten.");

        File.WriteAllText(path, JsonSerializer.Serialize(version, StoreJsonOptions));
    }

    private sealed record AcceptedMemoryIndex
    {
        public AcceptedMemoryRecord[] Records { get; init; } = [];
    }

    private static string ShortHash(string value) => Sha256Hex(value)[..16];

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}

public sealed record MemoryPromotionEvaluationResult
{
    public required MemoryPromotionReceipt Receipt { get; init; }
    public AcceptedMemoryRecord? Record { get; init; }
    public AcceptedMemoryVersion? Version { get; init; }
}

public static class MemoryPromotionEvaluator
{
    public static MemoryPromotionEvaluationResult EvaluateAndPromote(
        MemoryProposal? proposal,
        MemoryPromotionRequest request,
        ConscienceDecision? conscienceDecision,
        AcceptedMemoryStore store,
        DateTimeOffset? now = null)
    {
        var createdAt = now ?? DateTimeOffset.UtcNow;
        var reasons = new List<string>();
        if (proposal is null)
            reasons.Add("MissingProposal");

        var gate = MemoryKeyGate.Evaluate(proposal, createdAt);
        if (gate.Decision == MemoryKeyGateDecision.Block)
            reasons.AddRange(gate.Reasons);

        if (conscienceDecision is null)
            reasons.Add("MissingConscienceDecision");
        else
        {
            if (conscienceDecision.ActionKind != GovernedActionKind.MemoryPromotion)
                reasons.Add("ConscienceDecisionActionMismatch");
            if (!string.Equals(conscienceDecision.SubjectId, request.MemoryProposalId, StringComparison.Ordinal))
                reasons.Add("ConscienceDecisionSubjectMismatch");
            if (conscienceDecision.Decision != ConscienceDecisionOutcome.Allow)
                reasons.Add("ConscienceDecisionDoesNotAllow");
            if (conscienceDecision.ExpiresAtUtc is not null && conscienceDecision.ExpiresAtUtc <= createdAt)
                reasons.Add("ConscienceDecisionExpired");
            if (!string.IsNullOrWhiteSpace(conscienceDecision.DecisionHash) && !string.Equals(conscienceDecision.DecisionHash, ConscienceDecisionHash.Compute(conscienceDecision), StringComparison.OrdinalIgnoreCase))
                reasons.Add("ConscienceDecisionHashMismatch");
        }

        if (string.IsNullOrWhiteSpace(request.ThoughtLedgerRef))
            reasons.Add("MissingThoughtLedgerFailClosed");

        if (proposal is not null && proposal.EvidenceRefs.Length == 0)
            reasons.Add("MissingEvidence");

        if (proposal is not null && string.Equals(proposal.CreatedBy, request.RequestedBy, StringComparison.OrdinalIgnoreCase) && request.RequestedBy.Contains("agent", StringComparison.OrdinalIgnoreCase))
            reasons.Add("AgentSelfApprovalBlocked");

        if (proposal is null || reasons.Count > 0)
        {
            return new MemoryPromotionEvaluationResult
            {
                Receipt = Receipt(request, proposal?.MemoryProposalId ?? request.MemoryProposalId, MemoryPromotionDecision.Blocked, reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), createdAt, null, null, MemoryBoundary.None)
            };
        }

        var append = store.AppendVersion(proposal, request, conscienceDecision!, request.ThoughtLedgerRef!, createdAt);
        return new MemoryPromotionEvaluationResult
        {
            Receipt = Receipt(request, proposal.MemoryProposalId, MemoryPromotionDecision.Accepted, [], createdAt, append.Record.MemoryId, append.Version.MemoryVersionId, MemoryBoundary.None with { MemoryPromoted = true, AcceptedMemoryVersionAppended = true }),
            Record = append.Record,
            Version = append.Version
        };
    }

    private static MemoryPromotionReceipt Receipt(MemoryPromotionRequest request, string proposalId, MemoryPromotionDecision decision, string[] reasons, DateTimeOffset createdAt, string? memoryId, string? versionId, MemoryBoundary boundary) => new()
    {
        MemoryPromotionReceiptId = $"mem_promo_receipt_{Guid.NewGuid():N}",
        MemoryPromotionRequestId = request.MemoryPromotionRequestId,
        MemoryProposalId = proposalId,
        Decision = decision,
        Reasons = reasons,
        AcceptedMemoryId = memoryId,
        AcceptedMemoryVersionId = versionId,
        CreatedAtUtc = createdAt,
        Boundary = boundary
    };
}
