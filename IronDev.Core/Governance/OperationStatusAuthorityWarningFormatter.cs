namespace IronDev.Core.Governance;

public static class OperationStatusAuthorityWarningFormatter
{
    public static OperationStatusAuthorityWarningFormatterResult Format(
        OperationStatusAuthorityWarningFormatterRequest? request)
    {
        var validation = OperationStatusAuthorityWarningFormatterValidator.ValidateRequest(request);
        if (!validation.IsValid || request is null)
        {
            return Result(
                request,
                false,
                OperationStatusAuthorityWarningFormatterStatus.InvalidRequest,
                validation.Issues,
                [Line(
                    OperationStatusAuthorityWarningKind.ManualAuthorityReviewRequired,
                    OperationStatusAuthorityWarningSeverity.BoundaryWarning,
                    "Review warning input",
                    "The supplied warning input is incomplete or inconsistent.",
                    request?.Source ?? "d20-formatter")]);
        }

        var candidates = new List<Candidate>();
        AddNextSafeActionCandidates(request.NextSafeActionFormatterResult, candidates);
        AddEnvelopeCandidates(request.ReadEnvelope, candidates);
        AddFactCandidates(request.WarningFacts, candidates);

        var lines = candidates
            .OrderBy(static candidate => candidate.Priority)
            .ThenBy(static candidate => candidate.Line.WarningKind)
            .GroupBy(static candidate => candidate.Line.WarningKind)
            .Select(static group => group.First().Line)
            .Take(OperationStatusAuthorityWarningFormatterValidator.MaxLineCount)
            .ToArray();

        if (lines.Length == 0)
        {
            lines =
            [
                Line(
                    OperationStatusAuthorityWarningKind.NoWarning,
                    OperationStatusAuthorityWarningSeverity.Info,
                    "No boundary warning",
                    "The supplied facts do not identify authority-warning display material.",
                    request.Source)
            ];
        }

        return Result(
            request,
            true,
            DetermineStatus(request, lines),
            [],
            lines);
    }

    private static void AddNextSafeActionCandidates(
        OperationStatusNextSafeActionFormatterResult? result,
        List<Candidate> candidates)
    {
        if (result is null)
        {
            return;
        }

        Add(candidates, 10, OperationStatusAuthorityWarningKind.NextSafeActionTextIsDisplayOnly, OperationStatusAuthorityWarningSeverity.BoundaryWarning, "Next guidance is display only", "Review guidance text is not workflow authority.", result.SourceOrFallback());
    }

    private static void AddEnvelopeCandidates(
        OperationStatusReadEnvelope? envelope,
        List<Candidate> candidates)
    {
        if (envelope is null)
        {
            return;
        }

        Add(candidates, 120, OperationStatusAuthorityWarningKind.EnvelopeIsNotAuthority, OperationStatusAuthorityWarningSeverity.Notice, "Envelope is not authority", "Read envelopes describe status visibility only.", envelope.Source);

        if (envelope.SafeSummary is not null &&
            envelope.SafeSummary.ProjectedStatus != OperationProjectedStatusKind.Unknown &&
            envelope.SafeSummary.ProjectedStatus != OperationProjectedStatusKind.NoEvents)
        {
            Add(candidates, 20, OperationStatusAuthorityWarningKind.StatusIsNotAuthority, OperationStatusAuthorityWarningSeverity.BoundaryWarning, "Status is not authority", "Projected status is display state only.", envelope.Source);
        }

        if (envelope.PageSummary is not null)
        {
            Add(candidates, 110, OperationStatusAuthorityWarningKind.PaginationIsNotActionQueue, OperationStatusAuthorityWarningSeverity.Notice, "Page data is not a queue", "Pagination metadata is display context only.", envelope.Source);
            Add(candidates, 140, OperationStatusAuthorityWarningKind.UiStateIsNotAuthority, OperationStatusAuthorityWarningSeverity.Notice, "Display state is not authority", "List and page state are read-only context.", envelope.Source);
        }

        if (envelope.EnvelopeKind == OperationStatusReadEnvelopeKind.Redacted ||
            envelope.SafeSummary?.IsRedacted == true)
        {
            Add(candidates, 130, OperationStatusAuthorityWarningKind.RedactionIsNotDenial, OperationStatusAuthorityWarningSeverity.Notice, "Redaction is not denial", "Redacted metadata only limits display detail.", envelope.Source);
        }

        if (envelope.EnvelopeKind is OperationStatusReadEnvelopeKind.Ambiguous)
        {
            Add(candidates, 150, OperationStatusAuthorityWarningKind.ManualAuthorityReviewRequired, OperationStatusAuthorityWarningSeverity.Warning, "Review ambiguous boundary", "Ambiguous status input cannot imply authority.", envelope.Source);
        }
    }

    private static void AddFactCandidates(
        OperationStatusAuthorityWarningFacts? facts,
        List<Candidate> candidates)
    {
        if (facts is null)
        {
            return;
        }

        if (facts.NextSafeActionFormatterStatus != OperationStatusNextSafeActionFormatterStatus.Unknown ||
            facts.HasNextSafeActionDisplayLines)
        {
            Add(candidates, 10, OperationStatusAuthorityWarningKind.NextSafeActionTextIsDisplayOnly, OperationStatusAuthorityWarningSeverity.BoundaryWarning, "Next guidance is display only", "Review guidance text is not workflow authority.", facts.Source);
        }

        if (facts.ProjectedStatus != OperationProjectedStatusKind.Unknown &&
            facts.ProjectedStatus != OperationProjectedStatusKind.NoEvents)
        {
            Add(candidates, 20, OperationStatusAuthorityWarningKind.StatusIsNotAuthority, OperationStatusAuthorityWarningSeverity.BoundaryWarning, "Status is not authority", "Projected status is display state only.", facts.Source);
        }

        if (facts.EvidenceResolutionStatus is EvidenceResolutionStatus.Resolved or
            EvidenceResolutionStatus.PartiallyResolved)
        {
            Add(candidates, 30, OperationStatusAuthorityWarningKind.EvidenceIsNotApproval, OperationStatusAuthorityWarningSeverity.BoundaryWarning, "Evidence is not approval", "Evidence references are review material only.", facts.Source);
        }

        if (facts.ReceiptResolutionStatus is ReceiptReferenceResolutionStatus.Resolved or
            ReceiptReferenceResolutionStatus.PartiallyResolved)
        {
            Add(candidates, 40, OperationStatusAuthorityWarningKind.ReceiptIsNotExecutionProof, OperationStatusAuthorityWarningSeverity.BoundaryWarning, "Receipt reference is not proof", "Receipt references do not prove execution by themselves.", facts.Source);
        }

        if (facts.ValidationStalenessStatus is ValidationStalenessResolutionStatus.Assessed)
        {
            Add(candidates, 50, OperationStatusAuthorityWarningKind.ValidationIsNotApproval, OperationStatusAuthorityWarningSeverity.BoundaryWarning, "Validation is not approval", "Validation metadata is evidence only.", facts.Source);
        }

        if (facts.ValidationStalenessStatus is ValidationStalenessResolutionStatus.Assessed ||
            facts.PatchBaseFreshnessStatus is PatchBaseFreshnessResolutionStatus.Assessed ||
            facts.WorktreeBaseHeadFreshnessStatus is WorktreeBaseHeadFreshnessResolutionStatus.Assessed)
        {
            Add(candidates, 60, OperationStatusAuthorityWarningKind.FreshnessIsNotPermission, OperationStatusAuthorityWarningSeverity.BoundaryWarning, "Freshness is not permission", "Freshness metadata is not source mutation permission.", facts.Source);
        }

        if (facts.WorktreeBaseHeadFreshnessStatus is WorktreeBaseHeadFreshnessResolutionStatus.Assessed)
        {
            Add(candidates, 70, OperationStatusAuthorityWarningKind.WorktreeStateIsNotMutationAuthority, OperationStatusAuthorityWarningSeverity.BoundaryWarning, "Worktree state is not mutation authority", "Clean worktree facts remain read-only evidence.", facts.Source);
        }

        if (facts.InterruptedRunStatus != InterruptedRunReadModelStatus.Unknown &&
            facts.InterruptedRunStatus != InterruptedRunReadModelStatus.InvalidRequest &&
            facts.InterruptedRunStatus != InterruptedRunReadModelStatus.NoCheckpoints)
        {
            Add(candidates, 80, OperationStatusAuthorityWarningKind.InterruptedRunIsNotRetryAuthority, OperationStatusAuthorityWarningSeverity.BoundaryWarning, "Interrupted run is not retry authority", "Interrupted-run metadata only explains prior state.", facts.Source);
        }

        if (facts.RollbackRecoveryStatus is RollbackRecoveryReadModelStatus.Assessed or
            RollbackRecoveryReadModelStatus.MissingMaterial or
            RollbackRecoveryReadModelStatus.FailureObserved or
            RollbackRecoveryReadModelStatus.AmbiguousMaterial or
            RollbackRecoveryReadModelStatus.Unassessable)
        {
            Add(candidates, 90, OperationStatusAuthorityWarningKind.RollbackPlanIsNotRollbackExecution, OperationStatusAuthorityWarningSeverity.BoundaryWarning, "Rollback material is not rollback execution", "Rollback material is review material only.", facts.Source);
            Add(candidates, 100, OperationStatusAuthorityWarningKind.RecoveryPlanIsNotRecoveryAuthority, OperationStatusAuthorityWarningSeverity.BoundaryWarning, "Recovery material is not recovery authority", "Recovery material is review material only.", facts.Source);
        }

        if (facts.HasPageSummary)
        {
            Add(candidates, 110, OperationStatusAuthorityWarningKind.PaginationIsNotActionQueue, OperationStatusAuthorityWarningSeverity.Notice, "Page data is not a queue", "Pagination metadata is display context only.", facts.Source);
            Add(candidates, 140, OperationStatusAuthorityWarningKind.UiStateIsNotAuthority, OperationStatusAuthorityWarningSeverity.Notice, "Display state is not authority", "List and page state are read-only context.", facts.Source);
        }

        if (facts.EnvelopeKind != OperationStatusReadEnvelopeKind.Unknown)
        {
            Add(candidates, 120, OperationStatusAuthorityWarningKind.EnvelopeIsNotAuthority, OperationStatusAuthorityWarningSeverity.Notice, "Envelope is not authority", "Read envelopes describe status visibility only.", facts.Source);
        }

        if (facts.HasRedactedSummary ||
            facts.EnvelopeKind == OperationStatusReadEnvelopeKind.Redacted)
        {
            Add(candidates, 130, OperationStatusAuthorityWarningKind.RedactionIsNotDenial, OperationStatusAuthorityWarningSeverity.Notice, "Redaction is not denial", "Redacted metadata only limits display detail.", facts.Source);
        }

        if (facts.EnvelopeKind is OperationStatusReadEnvelopeKind.Ambiguous)
        {
            Add(candidates, 150, OperationStatusAuthorityWarningKind.ManualAuthorityReviewRequired, OperationStatusAuthorityWarningSeverity.Warning, "Review ambiguous boundary", "Ambiguous status input cannot imply authority.", facts.Source);
        }
    }

    private static OperationStatusAuthorityWarningFormatterStatus DetermineStatus(
        OperationStatusAuthorityWarningFormatterRequest request,
        IReadOnlyList<OperationStatusAuthorityWarningLine> lines)
    {
        if (request.ReadEnvelope?.EnvelopeKind == OperationStatusReadEnvelopeKind.Ambiguous ||
            request.WarningFacts?.EnvelopeKind == OperationStatusReadEnvelopeKind.Ambiguous)
        {
            return OperationStatusAuthorityWarningFormatterStatus.AmbiguousInput;
        }

        if (request.ReadEnvelope?.EnvelopeKind == OperationStatusReadEnvelopeKind.Unassessable ||
            request.WarningFacts?.EnvelopeKind == OperationStatusReadEnvelopeKind.Unassessable)
        {
            return OperationStatusAuthorityWarningFormatterStatus.Unassessable;
        }

        return lines.Count == 1 && lines[0].WarningKind == OperationStatusAuthorityWarningKind.NoWarning
            ? OperationStatusAuthorityWarningFormatterStatus.NoWarnings
            : OperationStatusAuthorityWarningFormatterStatus.Formatted;
    }

    private static void Add(
        List<Candidate> candidates,
        int priority,
        OperationStatusAuthorityWarningKind warningKind,
        OperationStatusAuthorityWarningSeverity severity,
        string title,
        string detail,
        string source)
    {
        candidates.Add(new Candidate(
            priority,
            Line(warningKind, severity, title, detail, source)));
    }

    private static OperationStatusAuthorityWarningLine Line(
        OperationStatusAuthorityWarningKind warningKind,
        OperationStatusAuthorityWarningSeverity severity,
        string title,
        string detail,
        string source) =>
        new()
        {
            WarningKind = warningKind,
            Severity = severity,
            Title = title,
            Detail = detail,
            Boundary = OperationStatusAuthorityWarningFormatterValidator.RequiredBoundary,
            Source = source
        };

    private static OperationStatusAuthorityWarningFormatterResult Result(
        OperationStatusAuthorityWarningFormatterRequest? request,
        bool isValid,
        OperationStatusAuthorityWarningFormatterStatus status,
        IReadOnlyList<string> issues,
        IReadOnlyList<OperationStatusAuthorityWarningLine> lines) =>
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
            Warnings = OperationStatusAuthorityWarningFormatterValidator.RequiredWarnings,
            ForbiddenAuthorityImplications = OperationStatusAuthorityWarningFormatterValidator.RequiredForbiddenAuthorityImplications
        };

    private static string SourceOrFallback(this OperationStatusNextSafeActionFormatterResult result) =>
        result.Lines.FirstOrDefault()?.Source ?? "d19-result";

    private sealed record Candidate(int Priority, OperationStatusAuthorityWarningLine Line);
}
