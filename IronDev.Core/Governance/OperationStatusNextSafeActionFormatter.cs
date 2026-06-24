namespace IronDev.Core.Governance;

public static class OperationStatusNextSafeActionFormatter
{
    public static OperationStatusNextSafeActionFormatterResult Format(
        OperationStatusNextSafeActionFormatterRequest? request)
    {
        var validation = OperationStatusNextSafeActionFormatterValidator.ValidateRequest(request);
        if (!validation.IsValid || request is null)
        {
            return Result(
                request,
                false,
                OperationStatusNextSafeActionFormatterStatus.InvalidRequest,
                validation.Issues,
                [Line(
                    OperationStatusNextSafeActionDisplayKind.ReviewInvalidRequest,
                    OperationStatusNextSafeActionSeverity.BlockedDisplay,
                    "Review formatter request",
                    "The supplied formatter input is incomplete or inconsistent.",
                    "The formatter can only render bounded guidance from scoped input.",
                    request?.Source ?? "d19-formatter")]);
        }

        var candidates = new List<Candidate>();
        AddEnvelopeCandidates(request.ReadEnvelope, candidates);
        AddDiagnosticCandidates(request.DiagnosticFacts, candidates);

        var lines = candidates
            .OrderBy(static candidate => candidate.Priority)
            .ThenBy(static candidate => candidate.Line.DisplayKind)
            .GroupBy(static candidate => candidate.Line.DisplayKind)
            .Select(static group => group.First().Line)
            .Take(OperationStatusNextSafeActionFormatterValidator.MaxLineCount)
            .ToArray();

        if (lines.Length == 0)
        {
            lines =
            [
                Line(
                    OperationStatusNextSafeActionDisplayKind.NoGuidance,
                    OperationStatusNextSafeActionSeverity.Info,
                    "No display guidance",
                    "The supplied facts do not identify a review area.",
                    "The formatter does not infer guidance from missing facts.",
                    request.Source)
            ];
        }

        return Result(
            request,
            true,
            DetermineStatus(lines),
            [],
            lines);
    }

    private static void AddEnvelopeCandidates(
        OperationStatusReadEnvelope? envelope,
        List<Candidate> candidates)
    {
        if (envelope is null)
        {
            return;
        }

        switch (envelope.EnvelopeKind)
        {
            case OperationStatusReadEnvelopeKind.InvalidRequest:
                Add(candidates, 10, OperationStatusNextSafeActionDisplayKind.ReviewInvalidRequest, OperationStatusNextSafeActionSeverity.BlockedDisplay, "Review invalid read input", "The read envelope reports invalid scoped input.", "Input validity must be reviewed before interpreting status.", envelope.Source);
                break;
            case OperationStatusReadEnvelopeKind.Error:
                Add(candidates, 20, OperationStatusNextSafeActionDisplayKind.ManualReviewRequired, OperationStatusNextSafeActionSeverity.BlockedDisplay, "Manual review required", "The read envelope reports an internal read-model problem.", "Internal read failures are diagnostic facts only.", envelope.Source);
                break;
            case OperationStatusReadEnvelopeKind.NotFound:
                Add(candidates, 30, OperationStatusNextSafeActionDisplayKind.ReviewNotFound, OperationStatusNextSafeActionSeverity.Notice, "Review missing status", "No operation status was found for the supplied scope.", "A missing status is not denial or permission.", envelope.Source);
                break;
            case OperationStatusReadEnvelopeKind.Ambiguous:
                Add(candidates, 40, OperationStatusNextSafeActionDisplayKind.ReviewAmbiguousStatus, OperationStatusNextSafeActionSeverity.Warning, "Review ambiguous status", "The read envelope reports more than one possible status interpretation.", "Ambiguous input must not choose a winner.", envelope.Source);
                break;
            case OperationStatusReadEnvelopeKind.Unassessable:
                Add(candidates, 50, OperationStatusNextSafeActionDisplayKind.ReviewUnassessableStatus, OperationStatusNextSafeActionSeverity.Warning, "Review unassessable status", "The read envelope reports status cannot be assessed from supplied facts.", "Unassessable state must remain diagnostic.", envelope.Source);
                break;
            case OperationStatusReadEnvelopeKind.Redacted:
                Add(candidates, 60, OperationStatusNextSafeActionDisplayKind.ReviewRedactedStatus, OperationStatusNextSafeActionSeverity.Notice, "Review redacted status", "The read envelope reports redacted metadata.", "Redaction protects hidden material and does not decide authority.", envelope.Source);
                break;
        }

        if (envelope.PageSummary is not null)
        {
            Add(candidates, 140, OperationStatusNextSafeActionDisplayKind.ReviewStatusPage, OperationStatusNextSafeActionSeverity.Info, "Review status page", "The supplied envelope contains page metadata.", "Page metadata is only display context.", envelope.Source);
        }
    }

    private static void AddDiagnosticCandidates(
        OperationStatusNextSafeActionDiagnosticFacts? facts,
        List<Candidate> candidates)
    {
        if (facts is null)
        {
            return;
        }

        if (facts.EnvelopeKind is OperationStatusReadEnvelopeKind.InvalidRequest)
        {
            Add(candidates, 10, OperationStatusNextSafeActionDisplayKind.ReviewInvalidRequest, OperationStatusNextSafeActionSeverity.BlockedDisplay, "Review invalid diagnostic input", "Diagnostic facts report invalid status input.", "Invalid input should stay diagnostic until reviewed.", facts.Source);
        }

        if (facts.EnvelopeKind is OperationStatusReadEnvelopeKind.Error ||
            facts.EnvelopeErrorCode is OperationStatusReadErrorCode.OperationStatusReadModelError)
        {
            Add(candidates, 20, OperationStatusNextSafeActionDisplayKind.ManualReviewRequired, OperationStatusNextSafeActionSeverity.BlockedDisplay, "Manual review required", "Diagnostic facts report a read-model problem.", "Read-model problems must not be converted into action.", facts.Source);
        }

        if (facts.ForbiddenActionStatus is ForbiddenActionResolutionStatus.Forbidden or
            ForbiddenActionResolutionStatus.AmbiguousFacts)
        {
            Add(candidates, 70, OperationStatusNextSafeActionDisplayKind.ReviewForbiddenActionFacts, OperationStatusNextSafeActionSeverity.BlockedDisplay, "Review forbidden action facts", "Supplied facts report a forbidden action condition.", "Forbidden action facts explain a block without changing it.", facts.Source);
        }

        if (facts.MissingEvidenceStatus is MissingEvidenceResolutionStatus.MissingEvidence or
            MissingEvidenceResolutionStatus.AmbiguousEvidence)
        {
            Add(candidates, 80, OperationStatusNextSafeActionDisplayKind.ReviewMissingEvidence, OperationStatusNextSafeActionSeverity.Warning, "Review missing evidence", "Supplied facts report missing or ambiguous evidence.", "Evidence gaps remain diagnostic until resolved elsewhere.", facts.Source);
        }

        if (facts.InterruptedRunStatus is InterruptedRunReadModelStatus.Interrupted or
            InterruptedRunReadModelStatus.Failed or
            InterruptedRunReadModelStatus.Cancelled or
            InterruptedRunReadModelStatus.AmbiguousCheckpoints or
            InterruptedRunReadModelStatus.Unassessable)
        {
            Add(candidates, 90, OperationStatusNextSafeActionDisplayKind.ReviewInterruptedRun, OperationStatusNextSafeActionSeverity.Warning, "Review interrupted run", "Supplied facts report interrupted or unclear run state.", "Interrupted-run facts explain state without recovery permission.", facts.Source);
        }

        if (facts.RollbackRecoveryStatus is RollbackRecoveryReadModelStatus.MissingMaterial or
            RollbackRecoveryReadModelStatus.FailureObserved or
            RollbackRecoveryReadModelStatus.AmbiguousMaterial or
            RollbackRecoveryReadModelStatus.Unassessable)
        {
            Add(candidates, 100, OperationStatusNextSafeActionDisplayKind.ReviewRollbackRecoveryMaterial, OperationStatusNextSafeActionSeverity.Warning, "Review rollback material", "Supplied facts report rollback or recovery material needing review.", "Rollback material is not rollback execution.", facts.Source);
        }

        if (facts.WorktreeBaseHeadFreshnessStatus is WorktreeBaseHeadFreshnessResolutionStatus.MixedFreshness or
            WorktreeBaseHeadFreshnessResolutionStatus.MissingExpectations or
            WorktreeBaseHeadFreshnessResolutionStatus.MissingObservations or
            WorktreeBaseHeadFreshnessResolutionStatus.MissingRules or
            WorktreeBaseHeadFreshnessResolutionStatus.AmbiguousObservations or
            WorktreeBaseHeadFreshnessResolutionStatus.Unassessable)
        {
            Add(candidates, 110, OperationStatusNextSafeActionDisplayKind.ReviewWorktreeBaseHeadFreshness, OperationStatusNextSafeActionSeverity.Warning, "Review worktree freshness", "Supplied facts report worktree, base, or head freshness concerns.", "Freshness facts are evidence, not mutation authority.", facts.Source);
        }

        if (facts.PatchBaseFreshnessStatus is PatchBaseFreshnessResolutionStatus.MixedFreshness or
            PatchBaseFreshnessResolutionStatus.MissingRules or
            PatchBaseFreshnessResolutionStatus.MissingBaseObservations or
            PatchBaseFreshnessResolutionStatus.AmbiguousPatchBaseMetadata or
            PatchBaseFreshnessResolutionStatus.Unassessable)
        {
            Add(candidates, 120, OperationStatusNextSafeActionDisplayKind.ReviewPatchBaseFreshness, OperationStatusNextSafeActionSeverity.Warning, "Review patch freshness", "Supplied facts report patch or base freshness concerns.", "Patch freshness display is not source apply authority.", facts.Source);
        }

        if (facts.ValidationStalenessStatus is ValidationStalenessResolutionStatus.MixedStaleness or
            ValidationStalenessResolutionStatus.MissingRules or
            ValidationStalenessResolutionStatus.AmbiguousValidationResults or
            ValidationStalenessResolutionStatus.Unassessable)
        {
            Add(candidates, 130, OperationStatusNextSafeActionDisplayKind.ReviewValidationStaleness, OperationStatusNextSafeActionSeverity.Warning, "Review validation staleness", "Supplied facts report validation staleness or ambiguity.", "Validation display is not approval or policy satisfaction.", facts.Source);
        }

        if (facts.ReceiptResolutionStatus is ReceiptReferenceResolutionStatus.PartiallyResolved or
            ReceiptReferenceResolutionStatus.NotFound or
            ReceiptReferenceResolutionStatus.AmbiguousReferences)
        {
            Add(candidates, 150, OperationStatusNextSafeActionDisplayKind.ReviewReceiptReferences, OperationStatusNextSafeActionSeverity.Notice, "Review receipt references", "Supplied facts report unresolved receipt references.", "Receipt references are not authority.", facts.Source);
        }

        if (facts.EvidenceResolutionStatus is EvidenceResolutionStatus.PartiallyResolved or
            EvidenceResolutionStatus.NotFound or
            EvidenceResolutionStatus.AmbiguousEvidence or
            EvidenceResolutionStatus.RedactionFailed)
        {
            Add(candidates, 160, OperationStatusNextSafeActionDisplayKind.ReviewEvidenceReferences, OperationStatusNextSafeActionSeverity.Notice, "Review evidence references", "Supplied facts report unresolved evidence references.", "Evidence references are not permission.", facts.Source);
        }
    }

    private static OperationStatusNextSafeActionFormatterStatus DetermineStatus(
        IReadOnlyList<OperationStatusNextSafeActionLine> lines)
    {
        if (lines.Any(static line => line.DisplayKind == OperationStatusNextSafeActionDisplayKind.ReviewAmbiguousStatus))
        {
            return OperationStatusNextSafeActionFormatterStatus.AmbiguousInput;
        }

        if (lines.Any(static line => line.DisplayKind == OperationStatusNextSafeActionDisplayKind.ReviewUnassessableStatus))
        {
            return OperationStatusNextSafeActionFormatterStatus.Unassessable;
        }

        return lines.Count == 1 && lines[0].DisplayKind == OperationStatusNextSafeActionDisplayKind.NoGuidance
            ? OperationStatusNextSafeActionFormatterStatus.NoGuidance
            : OperationStatusNextSafeActionFormatterStatus.Formatted;
    }

    private static void Add(
        List<Candidate> candidates,
        int priority,
        OperationStatusNextSafeActionDisplayKind displayKind,
        OperationStatusNextSafeActionSeverity severity,
        string title,
        string detail,
        string rationale,
        string source)
    {
        candidates.Add(new Candidate(
            priority,
            Line(displayKind, severity, title, detail, rationale, source)));
    }

    private static OperationStatusNextSafeActionLine Line(
        OperationStatusNextSafeActionDisplayKind displayKind,
        OperationStatusNextSafeActionSeverity severity,
        string title,
        string detail,
        string rationale,
        string source) =>
        new()
        {
            DisplayKind = displayKind,
            Severity = severity,
            Title = title,
            Detail = detail,
            Rationale = rationale,
            AuthorityBoundary = OperationStatusNextSafeActionFormatterValidator.RequiredAuthorityBoundary,
            Source = source
        };

    private static OperationStatusNextSafeActionFormatterResult Result(
        OperationStatusNextSafeActionFormatterRequest? request,
        bool isValid,
        OperationStatusNextSafeActionFormatterStatus status,
        IReadOnlyList<string> issues,
        IReadOnlyList<OperationStatusNextSafeActionLine> lines) =>
        new()
        {
            IsValid = isValid,
            FormatterStatus = status,
            TenantId = request?.TenantId ?? string.Empty,
            ProjectId = request?.ProjectId ?? string.Empty,
            OperationId = request?.OperationId ?? string.Empty,
            CorrelationId = request?.CorrelationId,
            AsOfUtc = request?.AsOfUtc ?? default,
            Lines = lines,
            Issues = issues,
            Warnings = OperationStatusNextSafeActionFormatterValidator.RequiredWarnings,
            ForbiddenAuthorityImplications = OperationStatusNextSafeActionFormatterValidator.RequiredForbiddenAuthorityImplications
        };

    private sealed record Candidate(int Priority, OperationStatusNextSafeActionLine Line);
}
