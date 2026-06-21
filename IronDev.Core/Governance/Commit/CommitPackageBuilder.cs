using IronDev.Core.Governance;

namespace IronDev.Core.Governance.Commit;

public static class CommitPackageBuilder
{
    private static readonly string[] AllowedMessageSources =
    [
        "HumanProvided",
        "ReviewedProposal"
    ];

    private static readonly string[] ForbiddenMessageSources =
    [
        "Memory",
        "ModelImplied",
        "UiState",
        "OldReceipt",
        "Inferred",
        "Unknown"
    ];

    private static readonly string[] PackageForbiddenActions =
    [
        "do not commit from package alone",
        "do not push from commit package",
        "do not create PR from commit package",
        "do not merge from commit package",
        "do not release from commit package",
        "do not deploy from commit package",
        "do not continue workflow from commit package",
        "do not promote memory from commit package",
        "do not treat source apply receipt as commit authority",
        "do not treat source apply authority as commit authority",
        "do not treat patch proposal as commit authority",
        "do not treat patch package as commit authority",
        "do not treat validation evidence as commit authority",
        "do not treat clean expected diff as commit authority",
        "do not treat commit message as commit authority",
        "executor must independently re-check source apply receipt, diff, commit authority, message, validation, branch, and worktree state"
    ];

    public static CommitPackageResult Build(CommitPackageRequest? request)
    {
        var blocked = new List<string>();
        var missing = new List<string>();

        if (request is null)
        {
            blocked.Add("CommitPackageRequestRequired");
            missing.Add("commit-package-request");
            return BuildResult(null, false, blocked, missing);
        }

        ValidateRequestEnvelope(request, blocked, missing);
        ValidateSourceApplyReceipt(request, blocked, missing);
        ValidateExpectedDiff(request, blocked, missing);
        ValidateCommitAuthority(request, blocked, missing);
        ValidateMessageEvidence(request, blocked, missing);
        ValidateValidationRequirement(request, blocked, missing);

        var isPackageCreated = blocked.Count == 0 && missing.Count == 0;
        return BuildResult(request, isPackageCreated, blocked, missing);
    }

    private static void ValidateRequestEnvelope(
        CommitPackageRequest request,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        RequireText(request.PackageId, "CommitPackageIdRequired", missing);
        ValidateSingleExplicitScope(request.Repository, "Repository", blocked);
        ValidateSingleExplicitScope(request.Branch, "Branch", blocked);
        ValidateSingleExplicitScope(request.RunId, "RunId", blocked);

        if (string.IsNullOrWhiteSpace(request.PatchHash))
            missing.Add("PatchHashRequired");
        else if (!OperationEligibilityPatchHashRules.IsSafePatchHash(request.PatchHash))
            blocked.Add("PatchHashInvalid");

        if (request.ObservedAtUtc == default)
            blocked.Add("ObservedAtUtcRequired");
    }

    private static void ValidateSourceApplyReceipt(
        CommitPackageRequest request,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        var receipt = request.SourceApplyReceipt;
        if (receipt is null)
        {
            blocked.Add("SourceApplyReceiptRequired");
            missing.Add("source-apply-receipt");
            return;
        }

        if (string.IsNullOrWhiteSpace(receipt.ReceiptRef))
            missing.Add("source-apply-receipt");
        else if (!receipt.ReceiptRef.StartsWith("source-apply-receipt:", StringComparison.OrdinalIgnoreCase))
            blocked.Add("SourceApplyReceiptRefInvalid");

        Match(receipt.Repository, request.Repository, "SourceApplyReceiptRepositoryMismatch", blocked);
        Match(receipt.Branch, request.Branch, "SourceApplyReceiptBranchMismatch", blocked);
        Match(receipt.RunId, request.RunId, "SourceApplyReceiptRunIdMismatch", blocked);
        Match(receipt.PatchHash, request.PatchHash, "SourceApplyReceiptPatchHashMismatch", blocked);
        ValidateSingleExplicitScope(receipt.Repository, "SourceApplyReceiptRepository", blocked);
        ValidateSingleExplicitScope(receipt.Branch, "SourceApplyReceiptBranch", blocked);
        ValidateSingleExplicitScope(receipt.RunId, "SourceApplyReceiptRunId", blocked);
        ValidateFilePaths(receipt.AppliedFilePaths, "SourceApplyReceiptAppliedFilePaths", blocked, missing);

        if (receipt.AppliedAtUtc == default)
            blocked.Add("SourceApplyReceiptAppliedAtUtcRequired");
        RequireText(receipt.AppliedByAuthorityPath, "SourceApplyReceiptAuthorityPathRequired", missing);
    }

    private static void ValidateExpectedDiff(
        CommitPackageRequest request,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        var diff = request.ExpectedDiff;
        if (diff is null)
        {
            blocked.Add("ExpectedDiffEvidenceRequired");
            missing.Add("expected-diff");
            return;
        }

        if (string.IsNullOrWhiteSpace(diff.EvidenceRef))
            missing.Add("expected-diff");
        else if (!diff.EvidenceRef.StartsWith("expected-diff:", StringComparison.OrdinalIgnoreCase) &&
                 !diff.EvidenceRef.StartsWith("worktree-diff:", StringComparison.OrdinalIgnoreCase))
        {
            blocked.Add("ExpectedDiffEvidenceRefInvalid");
        }

        Match(diff.Repository, request.Repository, "ExpectedDiffRepositoryMismatch", blocked);
        Match(diff.Branch, request.Branch, "ExpectedDiffBranchMismatch", blocked);
        Match(diff.RunId, request.RunId, "ExpectedDiffRunIdMismatch", blocked);
        Match(diff.PatchHash, request.PatchHash, "ExpectedDiffPatchHashMismatch", blocked);
        ValidateSingleExplicitScope(diff.Repository, "ExpectedDiffRepository", blocked);
        ValidateSingleExplicitScope(diff.Branch, "ExpectedDiffBranch", blocked);
        ValidateSingleExplicitScope(diff.RunId, "ExpectedDiffRunId", blocked);

        if (string.IsNullOrWhiteSpace(diff.ExpectedDiffHash))
            missing.Add("expected-diff-hash");
        else if (!OperationEligibilityPatchHashRules.IsSafePatchHash(diff.ExpectedDiffHash))
            blocked.Add("ExpectedDiffHashInvalid");

        ValidateFilePaths(diff.ExpectedChangedFilePaths, "ExpectedDiffChangedFilePaths", blocked, missing);

        if (!diff.IsCleanExpectedDiff)
            blocked.Add("ExpectedDiffNotClean");

        if (request.SourceApplyReceipt is not null &&
            HasValues(request.SourceApplyReceipt.AppliedFilePaths) &&
            HasValues(diff.ExpectedChangedFilePaths) &&
            !SameSet(request.SourceApplyReceipt.AppliedFilePaths, diff.ExpectedChangedFilePaths))
        {
            blocked.Add("ExpectedDiffDoesNotMatchSourceApplyReceipt");
        }
    }

    private static void ValidateCommitAuthority(
        CommitPackageRequest request,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        var authority = request.CommitAuthority;
        if (authority is null)
        {
            blocked.Add("CommitOperationAuthorityRequired");
            missing.Add("commit-operation-authority");
            return;
        }

        if (string.IsNullOrWhiteSpace(authority.EvidenceRef))
            missing.Add("commit-operation-authority");
        else if (!authority.EvidenceRef.StartsWith("operation-eligibility-decision:", StringComparison.OrdinalIgnoreCase) &&
                 !authority.EvidenceRef.StartsWith("commit-operation-authority:", StringComparison.OrdinalIgnoreCase))
        {
            blocked.Add("CommitOperationAuthorityEvidenceRefInvalid");
        }

        Match(authority.Repository, request.Repository, "CommitAuthorityRepositoryMismatch", blocked);
        Match(authority.Branch, request.Branch, "CommitAuthorityBranchMismatch", blocked);
        Match(authority.RunId, request.RunId, "CommitAuthorityRunIdMismatch", blocked);
        Match(authority.PatchHash, request.PatchHash, "CommitAuthorityPatchHashMismatch", blocked);
        ValidateSingleExplicitScope(authority.Repository, "CommitAuthorityRepository", blocked);
        ValidateSingleExplicitScope(authority.Branch, "CommitAuthorityBranch", blocked);
        ValidateSingleExplicitScope(authority.RunId, "CommitAuthorityRunId", blocked);
        ValidateFilePaths(authority.FilePaths, "CommitAuthorityFilePaths", blocked, missing);

        if (request.ExpectedDiff is not null &&
            HasValues(request.ExpectedDiff.ExpectedChangedFilePaths) &&
            HasValues(authority.FilePaths) &&
            !SameSet(request.ExpectedDiff.ExpectedChangedFilePaths, authority.FilePaths))
        {
            blocked.Add("CommitAuthorityDoesNotMatchExpectedDiff");
        }

        if (request.SourceApplyReceipt is not null &&
            HasValues(request.SourceApplyReceipt.AppliedFilePaths) &&
            HasValues(authority.FilePaths) &&
            !SameSet(request.SourceApplyReceipt.AppliedFilePaths, authority.FilePaths))
        {
            blocked.Add("CommitAuthorityDoesNotMatchSourceApplyReceipt");
        }

        var decision = authority.Decision;
        if (decision is null)
        {
            blocked.Add("CommitOperationAuthorityRequired");
            missing.Add("commit-operation-eligibility-decision");
            return;
        }

        if (decision.OperationKind != RunAuthorityOperationKind.Commit)
        {
            blocked.Add("CommitOperationAuthorityRequired");
            blocked.Add($"CommitEligibilityOperationKindMismatch:{decision.OperationKind}");
        }

        if (!decision.IsEligibleUnderProfileAndGrant)
        {
            blocked.Add("CommitOperationAuthorityRequired");
            blocked.Add("CommitEligibilityDecisionNotEligible");
        }

        if (HasValues(decision.BlockedReasons))
        {
            blocked.Add("CommitEligibilityDecisionBlocked");
            foreach (var reason in Clean(decision.BlockedReasons))
                blocked.Add($"CommitEligibilityDecision:{reason}");
        }

        if (HasValues(decision.MissingEvidence))
        {
            blocked.Add("CommitEligibilityDecisionMissingEvidence");
            foreach (var item in Clean(decision.MissingEvidence))
                missing.Add($"CommitEligibilityDecision:{item}");
        }
    }

    private static void ValidateMessageEvidence(
        CommitPackageRequest request,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        var message = request.MessageEvidence;
        if (message is null)
        {
            blocked.Add("CommitMessageEvidenceRequired");
            missing.Add("commit-message");
            return;
        }

        if (string.IsNullOrWhiteSpace(message.EvidenceRef))
            missing.Add("commit-message");
        else if (!message.EvidenceRef.StartsWith("commit-message:", StringComparison.OrdinalIgnoreCase))
            blocked.Add("CommitMessageEvidenceRefInvalid");

        if (string.IsNullOrWhiteSpace(message.Subject))
            missing.Add("commit-message-subject");
        else if (message.Subject.Any(char.IsControl))
            blocked.Add("CommitMessageSubjectUnsafe");

        if (string.IsNullOrWhiteSpace(message.MessageSource))
        {
            missing.Add("commit-message-source");
            return;
        }

        if (ForbiddenMessageSources.Contains(message.MessageSource, StringComparer.OrdinalIgnoreCase) ||
            !AllowedMessageSources.Contains(message.MessageSource, StringComparer.OrdinalIgnoreCase))
        {
            blocked.Add($"CommitMessageSourceForbidden:{message.MessageSource}");
        }
    }

    private static void ValidateValidationRequirement(
        CommitPackageRequest request,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        var validation = request.ValidationRequirement;
        if (validation is null)
        {
            blocked.Add("CommitValidationRequirementRequired");
            missing.Add("commit-validation-requirement");
            return;
        }

        if (validation.IsSatisfied && validation.IsExplicitlyBlocked)
        {
            blocked.Add("CommitValidationRequirementInvalid");
            return;
        }

        if (!validation.IsSatisfied && !validation.IsExplicitlyBlocked)
        {
            blocked.Add("CommitValidationRequirementRequired");
            missing.Add("commit-validation-evidence");
            return;
        }

        if (validation.IsSatisfied)
        {
            if (!HasValues(validation.ValidationEvidenceRefs))
                missing.Add("commit-validation-evidence");
            return;
        }

        blocked.Add("CommitValidationRequirementBlocked");
        missing.Add("satisfied commit validation requirement");
        if (!HasValues(validation.BlockedReasons))
            blocked.Add("CommitValidationBlockedReasonsRequired");
    }

    private static CommitPackageResult BuildResult(
        CommitPackageRequest? request,
        bool isPackageCreated,
        IEnumerable<string> blocked,
        IEnumerable<string> missing)
    {
        var blockedList = Clean(blocked);
        var missingList = Clean(missing);
        if (blockedList.Count == 0 && missingList.Count > 0)
            blockedList = Clean([.. blockedList, "CommitPackageEvidenceMissing"]);

        var status = BuildStatus(request, isPackageCreated, blockedList, missingList);
        var manifest = isPackageCreated && request is not null
            ? BuildManifest(request, status)
            : null;
        var statusValidation = GovernedOperationStatusValidator.Validate(status);
        return new CommitPackageResult
        {
            IsPackageCreated = isPackageCreated,
            PackageId = CleanText(request?.PackageId, "commit-package-blocked"),
            Manifest = manifest,
            OperationStatus = status,
            StatusValidation = statusValidation,
            Issues = Clean(blockedList.Concat(missingList).Concat(statusValidation.Issues).Concat(statusValidation.RedFlags))
        };
    }

    private static CommitPackageManifest BuildManifest(
        CommitPackageRequest request,
        GovernedOperationStatus status) =>
        new()
        {
            PackageId = request.PackageId,
            Repository = request.Repository,
            Branch = request.Branch,
            RunId = request.RunId,
            PatchHash = request.PatchHash,
            SourceApplyReceiptRef = request.SourceApplyReceipt?.ReceiptRef ?? string.Empty,
            ExpectedDiffEvidenceRef = request.ExpectedDiff?.EvidenceRef ?? string.Empty,
            ExpectedDiffHash = request.ExpectedDiff?.ExpectedDiffHash ?? string.Empty,
            CommitMessageEvidenceRef = request.MessageEvidence?.EvidenceRef ?? string.Empty,
            CommitSubject = request.MessageEvidence?.Subject ?? string.Empty,
            FilePaths = Clean(request.ExpectedDiff?.ExpectedChangedFilePaths),
            EvidenceRefs = status.EvidenceRefs,
            ReceiptRefs = status.ReceiptRefs,
            OperationStatus = status
        };

    private static GovernedOperationStatus BuildStatus(
        CommitPackageRequest? request,
        bool isPackageCreated,
        IReadOnlyList<string> blocked,
        IReadOnlyList<string> missing) =>
        new()
        {
            OperationId = CleanText(request?.PackageId, "commit-package-blocked"),
            OperationKind = RunAuthorityOperationKind.Commit.ToString(),
            Subject = BuildSubject(request),
            State = isPackageCreated ? GovernedOperationState.Eligible : GovernedOperationState.Blocked,
            BlockedReasons = blocked,
            MissingEvidence = missing,
            NextSafeActions = isPackageCreated
                ? ["request controlled commit executor review for independent authority re-check"]
                : BuildBlockedNextSafeActions(missing),
            ForbiddenActions = PackageForbiddenActions,
            EvidenceRefs = BuildEvidenceRefs(request),
            ReceiptRefs = BuildReceiptRefs(request),
            ExpiresAtUtc = null,
            ObservedAtUtc = request?.ObservedAtUtc == default ? DateTimeOffset.UnixEpoch : request?.ObservedAtUtc ?? DateTimeOffset.UnixEpoch
        };

    private static IReadOnlyList<string> BuildBlockedNextSafeActions(IReadOnlyCollection<string> missing) =>
        HasValues(missing)
            ? ["collect missing commit package evidence", "request commit operation eligibility evidence", "review source apply receipt and expected diff evidence"]
            : ["review blocked commit package evidence", "request corrected commit package evidence"];

    private static IReadOnlyList<string> BuildEvidenceRefs(CommitPackageRequest? request) =>
        Clean(
        [
            Ref("commit-package", request?.PackageId),
            Ref("repo", request?.Repository),
            Ref("branch", request?.Branch),
            Ref("run", request?.RunId),
            Ref("patch-hash", request?.PatchHash),
            request?.CommitAuthority?.EvidenceRef,
            request?.ExpectedDiff?.EvidenceRef,
            request?.MessageEvidence?.EvidenceRef,
            .. ReferenceValuesOrEmpty(request?.ValidationRequirement?.ValidationEvidenceRefs),
            .. ReferenceValuesOrEmpty(request?.EvidenceRefs)
        ]);

    private static IReadOnlyList<string> BuildReceiptRefs(CommitPackageRequest? request) =>
        Clean(
        [
            request?.SourceApplyReceipt?.ReceiptRef,
            .. ReferenceValuesOrEmpty(request?.ReceiptRefs)
        ]);

    private static string BuildSubject(CommitPackageRequest? request) =>
        request is null
            ? "commit package request"
            : $"commit package for {CleanText(request.Repository, "unknown-repository")} {CleanText(request.Branch, "unknown-branch")} {CleanText(request.PatchHash, "unknown-patch")}";

    private static void ValidateSingleExplicitScope(string? value, string label, ICollection<string> blocked)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            blocked.Add($"{label}Required");
            return;
        }

        var trimmed = value.Trim();
        if (trimmed.Contains('*', StringComparison.Ordinal) ||
            trimmed.Contains('?', StringComparison.Ordinal) ||
            trimmed.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            blocked.Add($"{label}MustBeSingleExplicitScope");
        }
    }

    private static void ValidateFilePaths(
        IReadOnlyCollection<string>? filePaths,
        string label,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        if (filePaths is null || filePaths.Count == 0)
        {
            missing.Add($"{label}Required");
            return;
        }

        foreach (var filePath in filePaths)
        {
            if (!BoundedRunAuthorityGrantFileScope.IsSafeRelativeGlob(filePath))
                blocked.Add($"{label}Unsafe:{filePath}");
        }
    }

    private static void Match(string? actual, string? expected, string issue, ICollection<string> blocked)
    {
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            blocked.Add(issue);
    }

    private static bool SameSet(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right)
    {
        var leftSet = Clean(left);
        var rightSet = Clean(right);
        return leftSet.Count == rightSet.Count && !leftSet.Except(rightSet, StringComparer.OrdinalIgnoreCase).Any();
    }

    private static void RequireText(string? value, string issue, ICollection<string> missing)
    {
        if (string.IsNullOrWhiteSpace(value))
            missing.Add(issue);
    }

    private static string Ref(string prefix, string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : $"{prefix}:{value.Trim()}";

    private static string CleanText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static bool HasValues(IEnumerable<string?>? values) =>
        ValuesOrEmpty(values).Any(value => !string.IsNullOrWhiteSpace(value));

    private static IEnumerable<string?> ReferenceValuesOrEmpty(IEnumerable<string?>? values) =>
        ValuesOrEmpty(values).Where(IsReferenceLike);

    private static bool IsReferenceLike(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return trimmed.Contains(':', StringComparison.Ordinal) &&
               !trimmed.Any(char.IsControl) &&
               !trimmed.Any(char.IsWhiteSpace);
    }

    private static IReadOnlyList<string> Clean(IEnumerable<string?>? values) =>
        ValuesOrEmpty(values)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string?> ValuesOrEmpty(IEnumerable<string?>? values) =>
        values ?? [];
}
