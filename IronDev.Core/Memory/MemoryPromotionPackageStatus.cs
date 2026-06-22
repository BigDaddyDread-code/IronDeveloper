using IronDev.Core.Governance;

namespace IronDev.Core.Memory;

public sealed record MemoryPromotionPackageStatusInput
{
    public required string PromotionPackageId { get; init; }
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }

    public required MemoryPromotionCandidate Candidate { get; init; }

    public required MemoryPromotionStatusKind StatusKind { get; init; }

    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }

    public required IReadOnlyCollection<string> BlockedReasons { get; init; }
    public required IReadOnlyCollection<string> MissingEvidence { get; init; }
    public required IReadOnlyCollection<string> ForbiddenActions { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

public sealed record MemoryPromotionCandidate
{
    public required string CandidateId { get; init; }
    public required MemoryPromotionScope Scope { get; init; }
    public required MemoryPromotionKind Kind { get; init; }

    public required string Summary { get; init; }
    public required string Detail { get; init; }

    public required string SourceRepository { get; init; }
    public string? SourceProjectId { get; init; }

    public required bool IsSanitized { get; init; }
    public required bool IsProjectLocal { get; init; }
    public required bool IsPortableEngineeringMemory { get; init; }

    public required IReadOnlyCollection<string> SourceEvidenceRefs { get; init; }
}

public enum MemoryPromotionStatusKind
{
    CandidateCreated,
    BlockedMissingAuthority,
    BlockedUnsafeContent,
    BlockedCrossRepoAuthority,
    BlockedUnsanitizedPortableMemory,
    BlockedSelfPromotionAttempt,
    EligibleForHumanDecision,
    Failed
}

public enum MemoryPromotionScope
{
    ProjectLocal,
    RunLocal,
    PortableEngineering
}

public enum MemoryPromotionKind
{
    PriorFailureHint,
    ProjectConvention,
    PreviousPattern,
    SanitizedEngineeringHeuristic
}

public sealed record MemoryPromotionPackageStatusResult
{
    public required GovernedOperationStatus Status { get; init; }
    public required GovernedOperationStatusValidationResult CanonicalValidation { get; init; }
    public required MemoryPromotionBoundary Boundary { get; init; }
    public required IReadOnlyCollection<string> Issues { get; init; }
    public required IReadOnlyCollection<string> RedFlags { get; init; }
    public required bool IsValid { get; init; }
}

public sealed record MemoryPromotionBoundary
{
    public bool StatusOnly { get; init; } = true;
    public bool CandidateOnly { get; init; } = true;
    public bool CanPromoteMemory { get; init; }
    public bool CanSelfPromote { get; init; }
    public bool CanApprove { get; init; }
    public bool CanSatisfyPolicy { get; init; }
    public bool CanAuthorizeSourceApply { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanTransferCrossRepoAuthority { get; init; }

    public static MemoryPromotionBoundary Status { get; } = new();
}

public static class MemoryPromotionPackageStatusMapper
{
    public const string OperationKind = "MemoryPromotionPackage";

    private static readonly string[] RequiredPromotionAuthorityRefs =
    [
        "accepted-memory-promotion-request",
        "memory-promotion-authority",
        "memory-safety-review",
        "memory-scope-decision"
    ];

    private static readonly string[] PortablePromotionAuthorityRefs =
    [
        "portable-memory-sanitization-review",
        "cross-project-confidentiality-check"
    ];

    private static readonly string[] ProjectLocalPromotionAuthorityRefs =
    [
        "project-local-memory-scope-confirmation"
    ];

    private static readonly string[] ApprovalMarkers =
    [
        "memory says this was approved",
        "approved",
        "approval granted",
        "already approved"
    ];

    private static readonly string[] PolicyMarkers =
    [
        "policy was satisfied",
        "policy satisfied"
    ];

    private static readonly string[] SourceApplyMarkers =
    [
        "apply source",
        "source apply"
    ];

    private static readonly string[] RollbackMarkers =
    [
        "rollback is safe",
        "rollback authority"
    ];

    private static readonly string[] CommitPushPrMarkers =
    [
        "commit and push",
        "open pr",
        "create pr",
        "pull request authority"
    ];

    private static readonly string[] WorkflowMarkers =
    [
        "continue workflow",
        "workflow continuation"
    ];

    private static readonly string[] ReleaseDeployMarkers =
    [
        "portable memory says release",
        "release",
        "deploy"
    ];

    private static readonly string[] SelfPromotionMarkers =
    [
        "memory says promote this memory",
        "promote this memory",
        "already approved for promotion",
        "self-promote",
        "self promotion",
        "self-promotion",
        "memory can promote itself",
        "can self-promote",
        "promotion authority",
        "durable memory write permission"
    ];

    private static readonly string[] CrossRepoAuthorityMarkers =
    [
        "previous project approved this",
        "cross-repo approval should transfer",
        "other repo policy was satisfied",
        "old validation passed so skip validation",
        "previous employer",
        "client x used this schema",
        "client schema says this is correct",
        "ticket from another project proves this"
    ];

    private static readonly string[] CrossProjectTruthMarkers =
    [
        "client schema",
        "client facts",
        "ticket from another project",
        "schema from another project",
        "repository facts from another project",
        "confidential business"
    ];

    public static MemoryPromotionPackageStatusResult Map(MemoryPromotionPackageStatusInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var mapperIssues = ValidateInput(input).ToList();
        var detectedReasons = DetectBlockedReasons(input).ToArray();
        var missingAuthority = MissingAuthorityRefs(input).ToArray();
        var status = BuildStatus(input, detectedReasons, missingAuthority);
        var canonical = GovernedOperationStatusValidator.Validate(status);
        var redFlags = BuildRedFlags(input, detectedReasons)
            .Concat(canonical.RedFlags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var issues = mapperIssues
            .Concat(canonical.Issues)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MemoryPromotionPackageStatusResult
        {
            Status = status,
            CanonicalValidation = canonical,
            Boundary = MemoryPromotionBoundary.Status,
            Issues = issues,
            RedFlags = redFlags,
            IsValid = issues.Length == 0 && redFlags.Length == 0 && canonical.IsValid
        };
    }

    private static GovernedOperationStatus BuildStatus(
        MemoryPromotionPackageStatusInput input,
        IReadOnlyCollection<string> detectedReasons,
        IReadOnlyCollection<string> missingAuthority)
    {
        var state = ResolveState(input, detectedReasons, missingAuthority);
        return new GovernedOperationStatus
        {
            OperationId = input.PromotionPackageId,
            OperationKind = OperationKind,
            Subject = Subject(input),
            State = state,
            BlockedReasons = BuildBlockedReasons(input, detectedReasons, missingAuthority, state),
            MissingEvidence = BuildMissingEvidence(input, missingAuthority, state),
            NextSafeActions = BuildNextSafeActions(input, state),
            ForbiddenActions = BuildForbiddenActions(input),
            EvidenceRefs = BuildEvidenceRefs(input),
            ReceiptRefs = Clean(input.ReceiptRefs),
            ExpiresAtUtc = input.ExpiresAtUtc,
            ObservedAtUtc = input.ObservedAtUtc
        };
    }

    private static GovernedOperationState ResolveState(
        MemoryPromotionPackageStatusInput input,
        IReadOnlyCollection<string> detectedReasons,
        IReadOnlyCollection<string> missingAuthority)
    {
        if (input.StatusKind == MemoryPromotionStatusKind.Failed)
            return GovernedOperationState.Failed;
        if (detectedReasons.Count > 0 || missingAuthority.Count > 0)
            return GovernedOperationState.Blocked;

        return input.StatusKind == MemoryPromotionStatusKind.EligibleForHumanDecision
            ? GovernedOperationState.Eligible
            : GovernedOperationState.Blocked;
    }

    private static IReadOnlyList<string> BuildBlockedReasons(
        MemoryPromotionPackageStatusInput input,
        IReadOnlyCollection<string> detectedReasons,
        IReadOnlyCollection<string> missingAuthority,
        GovernedOperationState state)
    {
        if (state == GovernedOperationState.Eligible)
            return [];

        var reasons = new List<string>();
        reasons.AddRange(Clean(input.BlockedReasons));
        reasons.AddRange(detectedReasons);

        if (input.StatusKind is MemoryPromotionStatusKind.CandidateCreated or MemoryPromotionStatusKind.BlockedMissingAuthority ||
            missingAuthority.Count > 0)
        {
            reasons.Add("Missing explicit memory promotion authority");
        }

        if (input.StatusKind == MemoryPromotionStatusKind.BlockedUnsafeContent)
            reasons.Add("UnsafeMemoryPromotionCandidate");
        if (input.StatusKind == MemoryPromotionStatusKind.BlockedCrossRepoAuthority)
            reasons.Add("CrossRepoAuthorityCannotTransferThroughMemory");
        if (input.StatusKind == MemoryPromotionStatusKind.BlockedUnsanitizedPortableMemory)
            reasons.Add("UnsanitizedPortableMemoryRejected");
        if (input.StatusKind == MemoryPromotionStatusKind.BlockedSelfPromotionAttempt)
            reasons.Add("MemorySelfPromotionAttempt");
        if (input.StatusKind == MemoryPromotionStatusKind.Failed)
            reasons.Add("Memory promotion package status failed.");

        return Clean(reasons);
    }

    private static IReadOnlyList<string> BuildMissingEvidence(
        MemoryPromotionPackageStatusInput input,
        IReadOnlyCollection<string> missingAuthority,
        GovernedOperationState state)
    {
        if (state == GovernedOperationState.Eligible)
            return [];

        return Clean([.. input.MissingEvidence, .. missingAuthority]);
    }

    private static IReadOnlyList<string> BuildNextSafeActions(
        MemoryPromotionPackageStatusInput input,
        GovernedOperationState state)
    {
        if (state == GovernedOperationState.Eligible)
        {
            return Clean(
            [
                $"review candidate {CandidateId(input)} and make a separate human memory-promotion decision before any durable memory write"
            ]);
        }

        if (state == GovernedOperationState.Failed)
        {
            return Clean(
            [
                "review memory promotion package failure evidence",
                "prepare a new memory promotion candidate package if the candidate remains useful"
            ]);
        }

        return Clean(
        [
            $"review candidate {CandidateId(input)} and create a governed memory-promotion request bound to this candidate, repo, run, scope, and sanitized content hash {CandidateContentHash(input)}",
            "collect memory safety review evidence",
            "collect memory scope decision evidence",
            "inspect missing promotion authority before any durable memory write"
        ]);
    }

    private static IReadOnlyList<string> BuildForbiddenActions(MemoryPromotionPackageStatusInput input) =>
        Clean(
        [
            .. input.ForbiddenActions,
            "do not write durable memory from candidate package",
            "do not promote memory from memory content",
            "do not treat memory candidate as approval",
            "do not satisfy policy from memory promotion status",
            "do not authorize source apply, rollback, commit, push, PR, release, deploy, or workflow continuation from memory",
            "do not continue workflow from memory promotion status"
        ]);

    private static IReadOnlyList<string> BuildEvidenceRefs(MemoryPromotionPackageStatusInput input) =>
        Clean(
        [
            Ref("memory-promotion-package", input.PromotionPackageId),
            Ref("memory-candidate", input.Candidate?.CandidateId),
            Ref("memory-candidate-content-hash", CandidateContentHash(input)),
            Ref("repo", input.Repository),
            Ref("branch", input.Branch),
            Ref("run", input.RunId),
            Ref("memory-scope", input.Candidate?.Scope.ToString()),
            Ref("memory-kind", input.Candidate?.Kind.ToString()),
            Ref("source-repository", input.Candidate?.SourceRepository),
            .. ValuesOrEmpty(input.Candidate?.SourceEvidenceRefs).Select(value => Ref("memory-source-evidence", value)),
            .. ValuesOrEmpty(input.EvidenceRefs)
        ]);

    private static IEnumerable<string> ValidateInput(MemoryPromotionPackageStatusInput input)
    {
        if (string.IsNullOrWhiteSpace(input.PromotionPackageId))
            yield return "MemoryPromotionPackageIdRequired";
        if (string.IsNullOrWhiteSpace(input.Repository))
            yield return "MemoryPromotionRepositoryRequired";
        if (string.IsNullOrWhiteSpace(input.Branch))
            yield return "MemoryPromotionBranchRequired";
        if (string.IsNullOrWhiteSpace(input.RunId))
            yield return "MemoryPromotionRunIdRequired";
        if (!Enum.IsDefined(input.StatusKind))
            yield return "MemoryPromotionStatusKindRequired";
        if (input.ObservedAtUtc == default)
            yield return "MemoryPromotionObservedAtUtcRequired";

        foreach (var issue in ValidateCandidate(input.Candidate))
            yield return issue;

        if (input.StatusKind == MemoryPromotionStatusKind.EligibleForHumanDecision &&
            ValuesOrEmpty(input.BlockedReasons).Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            yield return "EligibleMemoryPromotionStatusCannotCarryBlockedReasons";
        }
        if (input.StatusKind == MemoryPromotionStatusKind.EligibleForHumanDecision &&
            ValuesOrEmpty(input.MissingEvidence).Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            yield return "EligibleMemoryPromotionStatusCannotCarryMissingEvidence";
        }
    }

    private static IEnumerable<string> ValidateCandidate(MemoryPromotionCandidate? candidate)
    {
        if (candidate is null)
        {
            yield return "MemoryPromotionCandidateRequired";
            yield break;
        }

        if (string.IsNullOrWhiteSpace(candidate.CandidateId))
            yield return "MemoryPromotionCandidateIdRequired";
        if (!Enum.IsDefined(candidate.Scope))
            yield return "MemoryPromotionScopeRequired";
        if (!Enum.IsDefined(candidate.Kind))
            yield return "MemoryPromotionKindRequired";
        if (string.IsNullOrWhiteSpace(candidate.Summary))
            yield return "MemoryPromotionCandidateSummaryRequired";
        if (string.IsNullOrWhiteSpace(candidate.Detail))
            yield return "MemoryPromotionCandidateDetailRequired";
        if (string.IsNullOrWhiteSpace(candidate.SourceRepository))
            yield return "MemoryPromotionCandidateSourceRepositoryRequired";
        if (candidate.SourceEvidenceRefs is null || candidate.SourceEvidenceRefs.Count == 0)
            yield return "MemoryPromotionCandidateSourceEvidenceRequired";
    }

    private static IEnumerable<string> DetectBlockedReasons(MemoryPromotionPackageStatusInput input)
    {
        if (input.Candidate is null)
            yield break;

        var text = CandidateText(input.Candidate);
        if (ContainsAny(text, SelfPromotionMarkers))
            yield return "MemorySelfPromotionAttempt";
        if (ContainsAny(text, CrossRepoAuthorityMarkers))
            yield return "CrossRepoAuthorityCannotTransferThroughMemory";
        if (ContainsAny(text, CrossProjectTruthMarkers))
            yield return "CrossRepoProjectTruthRejected";
        if (ContainsAny(text, ApprovalMarkers))
            yield return "MemoryApprovalTextRejected";
        if (ContainsAny(text, PolicyMarkers))
            yield return "MemoryPolicySatisfactionTextRejected";
        if (ContainsAny(text, SourceApplyMarkers))
            yield return "MemorySourceApplyAuthorityTextRejected";
        if (ContainsAny(text, RollbackMarkers))
            yield return "MemoryRollbackAuthorityTextRejected";
        if (ContainsAny(text, CommitPushPrMarkers))
            yield return "MemoryCommitPushPrAuthorityTextRejected";
        if (ContainsAny(text, WorkflowMarkers))
            yield return "MemoryWorkflowContinuationTextRejected";
        if (ContainsAny(text, ReleaseDeployMarkers))
            yield return "MemoryReleaseDeployAuthorityTextRejected";
        if (MemoryContentSafety.ContainsAuthorityClaim(text))
            yield return "MemoryAuthorityTextRejected";

        if (input.Candidate.Scope == MemoryPromotionScope.PortableEngineering &&
            (!input.Candidate.IsPortableEngineeringMemory ||
             !input.Candidate.IsSanitized ||
             MemoryContentSafety.ContainsProjectSpecificDetail(text)))
        {
            yield return "UnsanitizedPortableMemoryRejected";
        }

        if (input.Candidate.Scope == MemoryPromotionScope.ProjectLocal &&
            (!input.Candidate.IsProjectLocal || !Same(input.Candidate.SourceRepository, input.Repository)))
        {
            yield return "CrossRepoProjectTruthRejected";
        }
    }

    private static IEnumerable<string> MissingAuthorityRefs(MemoryPromotionPackageStatusInput input)
    {
        if (input.StatusKind != MemoryPromotionStatusKind.EligibleForHumanDecision)
            return RequiredAuthorityRefsFor(input)
                .Where(prefix => !HasRefPrefix(input.EvidenceRefs, prefix));

        return RequiredAuthorityRefsFor(input)
            .Where(prefix => !HasRefPrefix(input.EvidenceRefs, prefix));
    }

    private static IEnumerable<string> RequiredAuthorityRefsFor(MemoryPromotionPackageStatusInput input)
    {
        foreach (var prefix in RequiredPromotionAuthorityRefs)
            yield return prefix;

        if (input.Candidate?.Scope == MemoryPromotionScope.PortableEngineering)
        {
            foreach (var prefix in PortablePromotionAuthorityRefs)
                yield return prefix;
        }

        if (input.Candidate?.Scope == MemoryPromotionScope.ProjectLocal)
        {
            foreach (var prefix in ProjectLocalPromotionAuthorityRefs)
                yield return prefix;
        }
    }

    private static IEnumerable<string> BuildRedFlags(
        MemoryPromotionPackageStatusInput input,
        IReadOnlyCollection<string> detectedReasons)
    {
        foreach (var reason in detectedReasons)
        {
            yield return reason switch
            {
                "MemorySelfPromotionAttempt" => "MemoryCannotPromoteItself",
                "CrossRepoAuthorityCannotTransferThroughMemory" => "CrossRepoAuthorityCannotTransferThroughMemory",
                "CrossRepoProjectTruthRejected" => "PortableMemoryCannotCarryProjectTruth",
                var value when value.Contains("Approval", StringComparison.OrdinalIgnoreCase) => "MemoryCandidateCannotApprove",
                var value when value.Contains("Policy", StringComparison.OrdinalIgnoreCase) => "MemoryCandidateCannotSatisfyPolicy",
                var value when value.Contains("SourceApply", StringComparison.OrdinalIgnoreCase) => "MemoryCandidateCannotAuthorizeSourceApply",
                var value when value.Contains("Workflow", StringComparison.OrdinalIgnoreCase) => "MemoryCandidateCannotContinueWorkflow",
                _ => "MemoryCandidateCannotGrantAuthority"
            };
        }
    }

    private static string Subject(MemoryPromotionPackageStatusInput input) =>
        $"repo:{input.Repository} branch:{input.Branch} run:{input.RunId} candidate:{CandidateId(input)} scope:{input.Candidate?.Scope.ToString() ?? "unknown"} kind:{input.Candidate?.Kind.ToString() ?? "unknown"}";

    private static string CandidateId(MemoryPromotionPackageStatusInput input) =>
        string.IsNullOrWhiteSpace(input.Candidate?.CandidateId) ? "missing-candidate-id" : input.Candidate.CandidateId.Trim();

    private static string CandidateContentHash(MemoryPromotionPackageStatusInput input)
    {
        if (input.Candidate is null)
            return "sha256:missing-candidate";

        var sanitized = MemoryContentSafety.SanitiseMemoryContent($"{input.Candidate.Summary}\n{input.Candidate.Detail}");
        return $"sha256:{MemoryContentSafety.ContentHash(sanitized)}";
    }

    private static string CandidateText(MemoryPromotionCandidate candidate) =>
        $"{candidate.Summary}\n{candidate.Detail}";

    private static string Ref(string prefix, string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : $"{prefix}:{value.Trim()}";

    private static bool HasRefPrefix(IReadOnlyCollection<string>? values, string prefix) =>
        ValuesOrEmpty(values).Any(value => value.Trim().StartsWith($"{prefix}:", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string? value, IReadOnlyCollection<string> markers) =>
        !string.IsNullOrWhiteSpace(value) &&
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> Clean(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string> ValuesOrEmpty(IEnumerable<string>? values) =>
        values ?? [];
}
