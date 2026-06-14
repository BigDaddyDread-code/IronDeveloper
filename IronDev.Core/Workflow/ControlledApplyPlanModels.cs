using System.Reflection;

namespace IronDev.Core.Workflow;

public interface IControlledApplyPlanWorkflow
{
    ControlledApplyPlanResult Prepare(ControlledApplyPlanRequest? request);
}

public sealed class ControlledApplyPlanWorkflow : IControlledApplyPlanWorkflow
{
    private static readonly string[] UnsafeMarkers =
    [
        "private reasoning",
        "hidden reasoning",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "entire patch",
        "entirepatch",
        "patch payload",
        "patch applied",
        "ready to apply",
        "validation passed",
        "rollback completed",
        "approval granted",
        "policy satisfied",
        "execution allowed",
        "tool executed",
        "source mutated",
        "memory promoted",
        "authority transferred",
        "release approved"
    ];

    public ControlledApplyPlanResult Prepare(ControlledApplyPlanRequest? request)
    {
        if (request is null)
        {
            return BuildResult(
                ControlledApplyPlanStatus.InvalidRequest,
                null,
                [],
                [],
                [ControlledApplyPlanReason.MissingRequest],
                [],
                []);
        }

        var reasons = BoundaryReasons().ToList();
        var issues = new List<ControlledApplyPlanIssue>();
        var missing = new List<ControlledApplyMissingReference>();
        var warnings = new List<string>();

        RequireId(request.WorkflowRunId, ControlledApplyPlanReason.MissingWorkflowRunId, issues);
        RequireId(request.WorkflowStepId, ControlledApplyPlanReason.MissingWorkflowStepId, issues);
        RequireId(request.ControlledApplyPlanReferenceId, ControlledApplyPlanReason.MissingControlledApplyPlanReferenceId, issues);
        RequireId(request.ProjectReferenceId, ControlledApplyPlanReason.MissingProjectReferenceId, issues);
        RequireId(request.TargetReferenceId, ControlledApplyPlanReason.MissingTargetReferenceId, issues);

        if (request.TargetKind is ControlledApplyPlanTargetKind.Unknown)
        {
            AddIssue(issues, ControlledApplyPlanReason.UnknownTargetKind, "Target kind is required.");
        }

        ValidateSafeText(request.WorkflowRunId, ControlledApplyPlanReason.UnsafeReferenceText, issues);
        ValidateSafeText(request.WorkflowStepId, ControlledApplyPlanReason.UnsafeReferenceText, issues);
        ValidateSafeText(request.ControlledApplyPlanReferenceId, ControlledApplyPlanReason.UnsafeReferenceText, issues);
        ValidateSafeText(request.ProjectReferenceId, ControlledApplyPlanReason.UnsafeReferenceText, issues);
        ValidateSafeText(request.TargetReferenceId, ControlledApplyPlanReason.UnsafeReferenceText, issues);
        ValidateSafeText(request.CorrelationId, ControlledApplyPlanReason.UnsafeReferenceText, issues);
        ValidateSafeText(request.SafePlanSummary, ControlledApplyPlanReason.UnsafePlanSummary, issues);

        ValidatePlanPhases(request.PlanPhases, issues);
        ValidatePreconditions(request.Preconditions, issues);
        ValidateValidationSteps(request.ValidationSteps, issues);
        ValidateRollbackNotes(request.RollbackNotes, issues);
        ValidateEvidenceReferences(request.EvidenceReferences, issues);
        ValidateGateHints(request.GateHints, issues);
        ValidateRisks(request.Risks, issues);

        ValidateUpstreamStatus(
            request.SourceApplyApprovalRequirement,
            "ApprovalRequired",
            ControlledApplyPlanReason.MissingSourceApplyApprovalRequirement,
            ControlledApplyPlanReason.SourceApplyApprovalRequirementNotReady,
            issues,
            missing);

        ValidateUpstreamStatus(
            request.PatchProposalEvidencePackage,
            "PatchProposalEvidencePackageProduced",
            ControlledApplyPlanReason.MissingPatchProposalEvidencePackage,
            ControlledApplyPlanReason.PatchProposalEvidencePackageNotReady,
            issues,
            missing);

        ValidateUpstreamStatus(
            request.HumanApprovalPackage,
            "ApprovalPackageProduced",
            ControlledApplyPlanReason.MissingHumanApprovalPackage,
            ControlledApplyPlanReason.HumanApprovalPackageNotReady,
            issues,
            missing);

        ValidateStepEvaluation(request.StepEvaluation, issues);
        ValidateDryRun(request.DryRunResult, issues);
        ValidateRouteSuggestion(request.RouteSuggestion, issues);

        if (issues.Count > 0)
        {
            reasons.AddRange(issues.Select(issue => issue.Reason));
            return BuildResult(
                ControlledApplyPlanStatus.InvalidRequest,
                request,
                issues,
                missing,
                reasons,
                warnings,
                []);
        }

        AddMissingIfBlank(request.SafePlanSummary, ControlledApplyPlanReason.MissingSafePlanSummary, missing);
        AddMissingIfEmpty(request.PlanPhases, ControlledApplyPlanReason.MissingPlanPhase, missing);
        AddMissingIfEmpty(request.Preconditions, ControlledApplyPlanReason.MissingPreconditionReference, missing);
        AddMissingIfEmpty(request.ValidationSteps, ControlledApplyPlanReason.MissingValidationReference, missing);
        AddMissingIfEmpty(request.RollbackNotes, ControlledApplyPlanReason.MissingRollbackReference, missing);
        AddMissingIfEmpty(request.EvidenceReferences, ControlledApplyPlanReason.MissingEvidenceReference, missing);
        AddMissingIfEmpty(request.GateHints, ControlledApplyPlanReason.MissingGateHint, missing);

        if (missing.Count > 0)
        {
            reasons.Add(ControlledApplyPlanReason.MissingRequiredPlanEvidence);
            return BuildResult(
                ControlledApplyPlanStatus.MissingRequiredPlanEvidence,
                request,
                issues,
                missing,
                reasons,
                warnings,
                PlanSummaries(false));
        }

        if (warnings.Count > 0)
        {
            reasons.Add(ControlledApplyPlanReason.BlockedByWorkflowGate);
            return BuildResult(
                ControlledApplyPlanStatus.BlockedByWorkflowGate,
                request,
                issues,
                missing,
                reasons,
                warnings,
                PlanSummaries(false));
        }

        reasons.Add(ControlledApplyPlanReason.ControlledApplyPlanPrepared);
        return BuildResult(
            ControlledApplyPlanStatus.ControlledApplyPlanPrepared,
            request,
            issues,
            missing,
            reasons,
            warnings,
            PlanSummaries(true));
    }

    private static ControlledApplyPlanResult BuildResult(
        ControlledApplyPlanStatus status,
        ControlledApplyPlanRequest? request,
        IReadOnlyList<ControlledApplyPlanIssue> issues,
        IReadOnlyList<ControlledApplyMissingReference> missing,
        IReadOnlyList<ControlledApplyPlanReason> reasons,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> summaries)
    {
        var includeRequestMaterial = request is not null && status is not ControlledApplyPlanStatus.InvalidRequest;

        return new ControlledApplyPlanResult
        {
            Status = status,
            WorkflowRunId = Normalize(request?.WorkflowRunId),
            WorkflowStepId = Normalize(request?.WorkflowStepId),
            ControlledApplyPlanReferenceId = Normalize(request?.ControlledApplyPlanReferenceId),
            ProjectReferenceId = Normalize(request?.ProjectReferenceId),
            TargetReferenceId = Normalize(request?.TargetReferenceId),
            TargetKind = request?.TargetKind ?? ControlledApplyPlanTargetKind.Unknown,
            SafePlanSummary = includeRequestMaterial ? Normalize(request?.SafePlanSummary) : string.Empty,
            PlanPhases = includeRequestMaterial ? NormalizeList(request!.PlanPhases) : [],
            Preconditions = includeRequestMaterial ? NormalizeList(request!.Preconditions) : [],
            ValidationSteps = includeRequestMaterial ? NormalizeList(request!.ValidationSteps) : [],
            RollbackNotes = includeRequestMaterial ? NormalizeList(request!.RollbackNotes) : [],
            EvidenceReferences = includeRequestMaterial ? NormalizeList(request!.EvidenceReferences) : [],
            GateHints = includeRequestMaterial ? NormalizeList(request!.GateHints) : [],
            Risks = includeRequestMaterial ? NormalizeList(request!.Risks) : [],
            MissingReferences = missing,
            Issues = issues,
            Reasons = reasons.Distinct().ToArray(),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            PlanSummaries = summaries,
            IsPlanOnly = true,
            IsExecution = false,
            IsSourceApply = false,
            IsPatchApplication = false,
            IsRollbackExecution = false,
            CanApplySource = false,
            CanApplyPatch = false,
            CanMutateFiles = false,
            CanReadSourceFiles = false,
            CanRunCommand = false,
            CanInvokeTool = false,
            CanDispatchAgent = false,
            CanCallModel = false,
            CanBuildPrompt = false,
            CanRunValidation = false,
            CanRollback = false,
            CanSatisfyApproval = false,
            CanSatisfyPolicy = false,
            CanTransitionWorkflow = false,
            CanCreateTicket = false,
            CanPromoteMemory = false,
            CanActivateRetrieval = false,
            CanWriteSql = false
        };
    }

    private static IReadOnlyList<string> PlanSummaries(bool prepared)
    {
        var summaries = new List<string>
        {
            "Controlled apply plan was prepared from supplied references only.",
            "Plan steps are not execution steps.",
            "Apply placeholders are not executable.",
            "Validation references are not validation execution.",
            "Rollback notes are not rollback execution.",
            "Source apply remains unimplemented.",
            "Patch application remains unimplemented.",
            "Execution remains unimplemented.",
            "Source was not applied.",
            "Patch was not applied.",
            "Files were not mutated.",
            "Validation was not run.",
            "Rollback was not run.",
            "Approval was not satisfied.",
            "Policy was not satisfied.",
            "Workflow was not transitioned."
        };

        if (!prepared)
        {
            summaries.Insert(0, "Controlled apply plan was not prepared because required supplied references were missing or blocked.");
        }

        return summaries;
    }

    private static IEnumerable<ControlledApplyPlanReason> BoundaryReasons()
    {
        yield return ControlledApplyPlanReason.PlanOnly;
        yield return ControlledApplyPlanReason.SuppliedEvidenceOnly;
        yield return ControlledApplyPlanReason.ExecutionNotImplemented;
        yield return ControlledApplyPlanReason.SourceApplyNotImplemented;
        yield return ControlledApplyPlanReason.PatchApplicationNotImplemented;
        yield return ControlledApplyPlanReason.RollbackNotImplemented;
        yield return ControlledApplyPlanReason.SourceNotApplied;
        yield return ControlledApplyPlanReason.PatchNotApplied;
        yield return ControlledApplyPlanReason.FilesNotMutated;
        yield return ControlledApplyPlanReason.SourceFilesNotRead;
        yield return ControlledApplyPlanReason.CommandNotRun;
        yield return ControlledApplyPlanReason.ToolNotInvoked;
        yield return ControlledApplyPlanReason.AgentNotDispatched;
        yield return ControlledApplyPlanReason.ModelNotCalled;
        yield return ControlledApplyPlanReason.PromptNotBuilt;
        yield return ControlledApplyPlanReason.ValidationNotRun;
        yield return ControlledApplyPlanReason.RollbackNotRun;
        yield return ControlledApplyPlanReason.ApprovalNotSatisfied;
        yield return ControlledApplyPlanReason.PolicyNotSatisfied;
        yield return ControlledApplyPlanReason.WorkflowNotTransitioned;
        yield return ControlledApplyPlanReason.TicketNotCreated;
        yield return ControlledApplyPlanReason.MemoryNotPromoted;
        yield return ControlledApplyPlanReason.RetrievalNotActivated;
        yield return ControlledApplyPlanReason.SqlNotWritten;
    }

    private static void ValidatePlanPhases(IReadOnlyList<ControlledApplyPlanPhase> phases, List<ControlledApplyPlanIssue> issues)
    {
        foreach (var phase in phases)
        {
            RequireId(phase.PhaseId, ControlledApplyPlanReason.MissingPlanPhase, issues);
            ValidateSafeText(phase.PhaseId, ControlledApplyPlanReason.UnsafeReferenceText, issues);
            ValidateSafeText(phase.SafeSummary, ControlledApplyPlanReason.UnsafePlanPhase, issues);

            if (phase.Kind is ControlledApplyPlanPhaseKind.Unknown)
            {
                AddIssue(issues, ControlledApplyPlanReason.InvalidPlanPhase, "Plan phase kind is required.");
            }

            if (phase.IsExecutionStep || phase.AppliesSource || phase.AppliesPatch || phase.MutatesFiles ||
                phase.RunsCommand || phase.InvokesTool || phase.RunsValidation || phase.RunsRollback ||
                phase.SatisfiesApproval || phase.SatisfiesPolicy || phase.TransitionsWorkflow)
            {
                AddIssue(issues, ControlledApplyPlanReason.PlanPhaseClaimsAuthority, "Plan phases must remain descriptive only.");
            }
        }
    }

    private static void ValidatePreconditions(IReadOnlyList<ControlledApplyPreconditionReference> preconditions, List<ControlledApplyPlanIssue> issues)
    {
        foreach (var precondition in preconditions)
        {
            RequireId(precondition.PreconditionId, ControlledApplyPlanReason.MissingPreconditionReference, issues);
            RequireId(precondition.ReferenceType, ControlledApplyPlanReason.MissingPreconditionReference, issues);
            ValidateSafeText(precondition.PreconditionId, ControlledApplyPlanReason.UnsafeReferenceText, issues);
            ValidateSafeText(precondition.ReferenceType, ControlledApplyPlanReason.UnsafeReferenceText, issues);
            ValidateSafeText(precondition.SafeSummary, ControlledApplyPlanReason.UnsafePrecondition, issues);

            if (precondition.IsApprovalSatisfied || precondition.IsPolicySatisfied || precondition.IsExecutionPermission || precondition.IsSourceApplied)
            {
                AddIssue(issues, ControlledApplyPlanReason.PreconditionClaimsAuthority, "Preconditions cannot satisfy approval, policy, execution, or source application.");
            }
        }
    }

    private static void ValidateValidationSteps(IReadOnlyList<ControlledApplyValidationReference> validationSteps, List<ControlledApplyPlanIssue> issues)
    {
        foreach (var validation in validationSteps)
        {
            RequireId(validation.ValidationReferenceId, ControlledApplyPlanReason.MissingValidationReference, issues);
            ValidateSafeText(validation.ValidationReferenceId, ControlledApplyPlanReason.UnsafeReferenceText, issues);
            ValidateSafeText(validation.SafeSummary, ControlledApplyPlanReason.UnsafeValidationReference, issues);

            if (validation.IsValidationExecution || validation.IsValidationResult || validation.RunsValidation)
            {
                AddIssue(issues, ControlledApplyPlanReason.ValidationReferenceClaimsAuthority, "Validation references cannot run or prove validation.");
            }
        }
    }

    private static void ValidateRollbackNotes(IReadOnlyList<ControlledApplyRollbackReference> rollbackNotes, List<ControlledApplyPlanIssue> issues)
    {
        foreach (var rollback in rollbackNotes)
        {
            RequireId(rollback.RollbackReferenceId, ControlledApplyPlanReason.MissingRollbackReference, issues);
            ValidateSafeText(rollback.RollbackReferenceId, ControlledApplyPlanReason.UnsafeReferenceText, issues);
            ValidateSafeText(rollback.SafeSummary, ControlledApplyPlanReason.UnsafeRollbackReference, issues);

            if (rollback.IsRollbackExecution || rollback.RunsRollback || rollback.RestoresFiles)
            {
                AddIssue(issues, ControlledApplyPlanReason.RollbackReferenceClaimsAuthority, "Rollback notes cannot run rollback or restore files.");
            }
        }
    }

    private static void ValidateEvidenceReferences(IReadOnlyList<ControlledApplyEvidenceReference> evidenceReferences, List<ControlledApplyPlanIssue> issues)
    {
        foreach (var evidence in evidenceReferences)
        {
            RequireId(evidence.EvidenceId, ControlledApplyPlanReason.MissingEvidenceReference, issues);
            RequireId(evidence.EvidenceType, ControlledApplyPlanReason.MissingEvidenceReference, issues);
            ValidateSafeText(evidence.EvidenceId, ControlledApplyPlanReason.UnsafeReferenceText, issues);
            ValidateSafeText(evidence.EvidenceType, ControlledApplyPlanReason.UnsafeReferenceText, issues);
            ValidateSafeText(evidence.SafeSummary, ControlledApplyPlanReason.UnsafeEvidenceReference, issues);

            if (evidence.IsAuthoritativeForAction || evidence.ContainsUnsafeReasoningMaterial || evidence.ContainsPatchMaterial ||
                evidence.ClaimsApproval || evidence.ClaimsPolicySatisfied || evidence.ClaimsExecutionPermission ||
                evidence.ClaimsSourceApplied || evidence.ClaimsMemoryPromoted)
            {
                AddIssue(issues, ControlledApplyPlanReason.EvidenceReferenceClaimsAuthority, "Evidence references cannot grant authority or carry unsafe payloads.");
            }
        }
    }

    private static void ValidateGateHints(IReadOnlyList<ControlledApplyGateHint> gateHints, List<ControlledApplyPlanIssue> issues)
    {
        foreach (var gate in gateHints)
        {
            RequireId(gate.GateId, ControlledApplyPlanReason.MissingGateHint, issues);
            ValidateSafeText(gate.GateId, ControlledApplyPlanReason.UnsafeReferenceText, issues);
            ValidateSafeText(gate.SafeSummary, ControlledApplyPlanReason.UnsafeGateHint, issues);

            if (gate.Kind is ControlledApplyGateHintKind.Unknown)
            {
                AddIssue(issues, ControlledApplyPlanReason.InvalidGateHint, "Gate hint kind is required.");
            }

            if (gate.IsSatisfied || gate.GrantsApproval || gate.AllowsExecution || gate.AllowsSourceApply || gate.AllowsPatchApplication)
            {
                AddIssue(issues, ControlledApplyPlanReason.GateHintClaimsAuthority, "Gate hints cannot satisfy gates or allow action.");
            }
        }
    }

    private static void ValidateRisks(IReadOnlyList<ControlledApplyRisk> risks, List<ControlledApplyPlanIssue> issues)
    {
        foreach (var risk in risks)
        {
            RequireId(risk.RiskId, ControlledApplyPlanReason.MissingRiskReference, issues);
            ValidateSafeText(risk.RiskId, ControlledApplyPlanReason.UnsafeReferenceText, issues);
            ValidateSafeText(risk.SafeSummary, ControlledApplyPlanReason.UnsafeRiskReference, issues);

            if (risk.Kind is ControlledApplyRiskKind.Unknown)
            {
                AddIssue(issues, ControlledApplyPlanReason.InvalidRisk, "Risk kind is required.");
            }
        }
    }

    private static void ValidateUpstreamStatus(
        object? upstream,
        string expectedStatus,
        ControlledApplyPlanReason missingReason,
        ControlledApplyPlanReason notReadyReason,
        List<ControlledApplyPlanIssue> issues,
        List<ControlledApplyMissingReference> missing)
    {
        if (upstream is null)
        {
            missing.Add(new ControlledApplyMissingReference
            {
                ReferenceKind = missingReason.ToString(),
                ReferenceId = missingReason.ToString(),
                SafeSummary = "Required supplied upstream reference is missing."
            });
            return;
        }

        var status = StatusName(upstream);
        if (!string.Equals(status, expectedStatus, StringComparison.Ordinal))
        {
            AddIssue(issues, notReadyReason, $"Upstream status must be {expectedStatus}.");
        }
    }

    private static void ValidateStepEvaluation(object? evaluation, List<ControlledApplyPlanIssue> issues)
    {
        if (evaluation is null)
        {
            return;
        }

        if (BooleanFlag(evaluation, "Can", "Execute") ||
            BooleanFlag(evaluation, "Can", "ApplySource") ||
            BooleanFlag(evaluation, "Can", "MutateFiles") ||
            BooleanFlag(evaluation, "Can", "PromoteMemory") ||
            BooleanFlag(evaluation, "Can", "TransitionWorkflow") ||
            StatusLooksBlocked(evaluation))
        {
            AddIssue(issues, ControlledApplyPlanReason.StepEvaluationNotEligible, "Workflow step evaluation is not eligible for plan preparation.");
        }
    }

    private static void ValidateDryRun(object? dryRun, List<ControlledApplyPlanIssue> issues)
    {
        if (dryRun is null)
        {
            return;
        }

        if (BooleanFlag(dryRun, "Can", "Execute") ||
            BooleanFlag(dryRun, "Can", "ApplySource") ||
            BooleanFlag(dryRun, "Can", "MutateFiles") ||
            StatusLooksBlocked(dryRun))
        {
            AddIssue(issues, ControlledApplyPlanReason.DryRunNotEligible, "Dry-run evidence is not eligible for plan preparation.");
        }
    }

    private static void ValidateRouteSuggestion(object? route, List<ControlledApplyPlanIssue> issues)
    {
        if (route is null)
        {
            return;
        }

        if (BooleanFlag(route, "Can", "Execute") ||
            BooleanFlag(route, "Can", "DispatchAgent") ||
            BooleanFlag(route, "Policy", "SatisfactionAllowed") ||
            BooleanFlag(route, "Memory", "PromotionAllowed") ||
            BooleanFlag(route, "Retrieval", "ActivationAllowed") ||
            StatusLooksBlocked(route))
        {
            AddIssue(issues, ControlledApplyPlanReason.RouteSuggestionNotEligible, "Route suggestion is not eligible for plan preparation.");
        }
    }

    private static bool StatusLooksBlocked(object value)
    {
        var status = StatusName(value);
        return status.Contains("Invalid", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("Blocked", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("Rejected", StringComparison.OrdinalIgnoreCase);
    }

    private static string StatusName(object value)
    {
        var property = value.GetType().GetProperty("Status", BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(value)?.ToString() ?? string.Empty;
    }

    private static bool BooleanFlag(object value, string prefix, string suffix)
    {
        var property = value.GetType().GetProperty(prefix + suffix, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(value) is true;
    }

    private static void AddMissingIfBlank(string? value, ControlledApplyPlanReason reason, List<ControlledApplyMissingReference> missing)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        missing.Add(new ControlledApplyMissingReference
        {
            ReferenceKind = reason.ToString(),
            ReferenceId = reason.ToString(),
            SafeSummary = "Required supplied plan evidence is missing."
        });
    }

    private static void AddMissingIfEmpty<T>(IReadOnlyList<T> values, ControlledApplyPlanReason reason, List<ControlledApplyMissingReference> missing)
    {
        if (values.Count > 0)
        {
            return;
        }

        missing.Add(new ControlledApplyMissingReference
        {
            ReferenceKind = reason.ToString(),
            ReferenceId = reason.ToString(),
            SafeSummary = "Required supplied plan evidence is missing."
        });
    }

    private static void RequireId(string? value, ControlledApplyPlanReason reason, List<ControlledApplyPlanIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddIssue(issues, reason, "Required identifier is missing.");
        }
    }

    private static void ValidateSafeText(string? value, ControlledApplyPlanReason reason, List<ControlledApplyPlanIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            AddIssue(issues, reason, "Text contains unsafe or authority-claiming material.");
        }
    }

    private static void AddIssue(List<ControlledApplyPlanIssue> issues, ControlledApplyPlanReason reason, string message)
    {
        issues.Add(new ControlledApplyPlanIssue
        {
            Reason = reason,
            Message = message
        });
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static IReadOnlyList<ControlledApplyPlanPhase> NormalizeList(IReadOnlyList<ControlledApplyPlanPhase> values)
    {
        return values.Select(value => value with
        {
            PhaseId = Normalize(value.PhaseId),
            SafeSummary = Normalize(value.SafeSummary)
        }).ToArray();
    }

    private static IReadOnlyList<ControlledApplyPreconditionReference> NormalizeList(IReadOnlyList<ControlledApplyPreconditionReference> values)
    {
        return values.Select(value => value with
        {
            PreconditionId = Normalize(value.PreconditionId),
            ReferenceType = Normalize(value.ReferenceType),
            SafeSummary = Normalize(value.SafeSummary)
        }).ToArray();
    }

    private static IReadOnlyList<ControlledApplyValidationReference> NormalizeList(IReadOnlyList<ControlledApplyValidationReference> values)
    {
        return values.Select(value => value with
        {
            ValidationReferenceId = Normalize(value.ValidationReferenceId),
            SafeSummary = Normalize(value.SafeSummary)
        }).ToArray();
    }

    private static IReadOnlyList<ControlledApplyRollbackReference> NormalizeList(IReadOnlyList<ControlledApplyRollbackReference> values)
    {
        return values.Select(value => value with
        {
            RollbackReferenceId = Normalize(value.RollbackReferenceId),
            SafeSummary = Normalize(value.SafeSummary)
        }).ToArray();
    }

    private static IReadOnlyList<ControlledApplyEvidenceReference> NormalizeList(IReadOnlyList<ControlledApplyEvidenceReference> values)
    {
        return values.Select(value => value with
        {
            EvidenceId = Normalize(value.EvidenceId),
            EvidenceType = Normalize(value.EvidenceType),
            SafeSummary = Normalize(value.SafeSummary)
        }).ToArray();
    }

    private static IReadOnlyList<ControlledApplyGateHint> NormalizeList(IReadOnlyList<ControlledApplyGateHint> values)
    {
        return values.Select(value => value with
        {
            GateId = Normalize(value.GateId),
            SafeSummary = Normalize(value.SafeSummary)
        }).ToArray();
    }

    private static IReadOnlyList<ControlledApplyRisk> NormalizeList(IReadOnlyList<ControlledApplyRisk> values)
    {
        return values.Select(value => value with
        {
            RiskId = Normalize(value.RiskId),
            SafeSummary = Normalize(value.SafeSummary)
        }).ToArray();
    }
}

public sealed record ControlledApplyPlanRequest
{
    public string WorkflowRunId { get; init; } = string.Empty;
    public string WorkflowStepId { get; init; } = string.Empty;
    public string ControlledApplyPlanReferenceId { get; init; } = string.Empty;
    public string ProjectReferenceId { get; init; } = string.Empty;
    public string TargetReferenceId { get; init; } = string.Empty;
    public ControlledApplyPlanTargetKind TargetKind { get; init; } = ControlledApplyPlanTargetKind.Unknown;
    public string SafePlanSummary { get; init; } = string.Empty;
    public SourceApplyApprovalRequirementResult? SourceApplyApprovalRequirement { get; init; }
    public PatchProposalEvidencePackageResult? PatchProposalEvidencePackage { get; init; }
    public HumanApprovalPackageCandidateResult? HumanApprovalPackage { get; init; }
    public IReadOnlyList<ControlledApplyPlanPhase> PlanPhases { get; init; } = [];
    public IReadOnlyList<ControlledApplyPreconditionReference> Preconditions { get; init; } = [];
    public IReadOnlyList<ControlledApplyValidationReference> ValidationSteps { get; init; } = [];
    public IReadOnlyList<ControlledApplyRollbackReference> RollbackNotes { get; init; } = [];
    public IReadOnlyList<ControlledApplyEvidenceReference> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<ControlledApplyGateHint> GateHints { get; init; } = [];
    public IReadOnlyList<ControlledApplyRisk> Risks { get; init; } = [];
    public WorkflowStepRunnerEvaluation? StepEvaluation { get; init; }
    public WorkflowDryRunResult? DryRunResult { get; init; }
    public BoxedLangGraphRouteSuggestion? RouteSuggestion { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
}

public sealed record ControlledApplyPlanResult
{
    public ControlledApplyPlanStatus Status { get; init; } = ControlledApplyPlanStatus.Unknown;
    public string WorkflowRunId { get; init; } = string.Empty;
    public string WorkflowStepId { get; init; } = string.Empty;
    public string ControlledApplyPlanReferenceId { get; init; } = string.Empty;
    public string ProjectReferenceId { get; init; } = string.Empty;
    public string TargetReferenceId { get; init; } = string.Empty;
    public ControlledApplyPlanTargetKind TargetKind { get; init; } = ControlledApplyPlanTargetKind.Unknown;
    public string SafePlanSummary { get; init; } = string.Empty;
    public IReadOnlyList<ControlledApplyPlanPhase> PlanPhases { get; init; } = [];
    public IReadOnlyList<ControlledApplyPreconditionReference> Preconditions { get; init; } = [];
    public IReadOnlyList<ControlledApplyValidationReference> ValidationSteps { get; init; } = [];
    public IReadOnlyList<ControlledApplyRollbackReference> RollbackNotes { get; init; } = [];
    public IReadOnlyList<ControlledApplyEvidenceReference> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<ControlledApplyGateHint> GateHints { get; init; } = [];
    public IReadOnlyList<ControlledApplyRisk> Risks { get; init; } = [];
    public IReadOnlyList<ControlledApplyMissingReference> MissingReferences { get; init; } = [];
    public IReadOnlyList<ControlledApplyPlanIssue> Issues { get; init; } = [];
    public IReadOnlyList<ControlledApplyPlanReason> Reasons { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> PlanSummaries { get; init; } = [];
    public bool IsPlanOnly { get; init; } = true;
    public bool IsExecution { get; init; }
    public bool IsSourceApply { get; init; }
    public bool IsPatchApplication { get; init; }
    public bool IsRollbackExecution { get; init; }
    public bool CanApplySource { get; init; }
    public bool CanApplyPatch { get; init; }
    public bool CanMutateFiles { get; init; }
    public bool CanReadSourceFiles { get; init; }
    public bool CanRunCommand { get; init; }
    public bool CanInvokeTool { get; init; }
    public bool CanDispatchAgent { get; init; }
    public bool CanCallModel { get; init; }
    public bool CanBuildPrompt { get; init; }
    public bool CanRunValidation { get; init; }
    public bool CanRollback { get; init; }
    public bool CanSatisfyApproval { get; init; }
    public bool CanSatisfyPolicy { get; init; }
    public bool CanTransitionWorkflow { get; init; }
    public bool CanCreateTicket { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanActivateRetrieval { get; init; }
    public bool CanWriteSql { get; init; }
}

public sealed record ControlledApplyPlanIssue
{
    public ControlledApplyPlanReason Reason { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed record ControlledApplyMissingReference
{
    public string ReferenceKind { get; init; } = string.Empty;
    public string ReferenceId { get; init; } = string.Empty;
    public string SafeSummary { get; init; } = string.Empty;
}

public sealed record ControlledApplyPlanPhase
{
    public string PhaseId { get; init; } = string.Empty;
    public ControlledApplyPlanPhaseKind Kind { get; init; } = ControlledApplyPlanPhaseKind.Unknown;
    public string SafeSummary { get; init; } = string.Empty;
    public bool IsExecutionStep { get; init; }
    public bool AppliesSource { get; init; }
    public bool AppliesPatch { get; init; }
    public bool MutatesFiles { get; init; }
    public bool RunsCommand { get; init; }
    public bool InvokesTool { get; init; }
    public bool RunsValidation { get; init; }
    public bool RunsRollback { get; init; }
    public bool SatisfiesApproval { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool TransitionsWorkflow { get; init; }
}

public sealed record ControlledApplyPreconditionReference
{
    public string PreconditionId { get; init; } = string.Empty;
    public string ReferenceType { get; init; } = string.Empty;
    public string SafeSummary { get; init; } = string.Empty;
    public bool IsApprovalSatisfied { get; init; }
    public bool IsPolicySatisfied { get; init; }
    public bool IsExecutionPermission { get; init; }
    public bool IsSourceApplied { get; init; }
}

public sealed record ControlledApplyValidationReference
{
    public string ValidationReferenceId { get; init; } = string.Empty;
    public string SafeSummary { get; init; } = string.Empty;
    public bool IsValidationExecution { get; init; }
    public bool IsValidationResult { get; init; }
    public bool RunsValidation { get; init; }
}

public sealed record ControlledApplyRollbackReference
{
    public string RollbackReferenceId { get; init; } = string.Empty;
    public string SafeSummary { get; init; } = string.Empty;
    public bool IsRollbackExecution { get; init; }
    public bool RunsRollback { get; init; }
    public bool RestoresFiles { get; init; }
}

public sealed record ControlledApplyEvidenceReference
{
    public string EvidenceId { get; init; } = string.Empty;
    public string EvidenceType { get; init; } = string.Empty;
    public string SafeSummary { get; init; } = string.Empty;
    public bool IsAuthoritativeForAction { get; init; }
    public bool ContainsUnsafeReasoningMaterial { get; init; }
    public bool ContainsPatchMaterial { get; init; }
    public bool ClaimsApproval { get; init; }
    public bool ClaimsPolicySatisfied { get; init; }
    public bool ClaimsExecutionPermission { get; init; }
    public bool ClaimsSourceApplied { get; init; }
    public bool ClaimsMemoryPromoted { get; init; }
}

public sealed record ControlledApplyGateHint
{
    public string GateId { get; init; } = string.Empty;
    public ControlledApplyGateHintKind Kind { get; init; } = ControlledApplyGateHintKind.Unknown;
    public string SafeSummary { get; init; } = string.Empty;
    public bool IsSatisfied { get; init; }
    public bool GrantsApproval { get; init; }
    public bool AllowsExecution { get; init; }
    public bool AllowsSourceApply { get; init; }
    public bool AllowsPatchApplication { get; init; }
}

public sealed record ControlledApplyRisk
{
    public string RiskId { get; init; } = string.Empty;
    public ControlledApplyRiskKind Kind { get; init; } = ControlledApplyRiskKind.Unknown;
    public ControlledApplyRiskSeverity Severity { get; init; } = ControlledApplyRiskSeverity.Unknown;
    public string SafeSummary { get; init; } = string.Empty;
}

public enum ControlledApplyPlanTargetKind
{
    Unknown = 0,
    SourceApplyCandidate = 1,
    PatchReviewCandidate = 2,
    RepositoryChangeCandidate = 3
}

public enum ControlledApplyPlanStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    BlockedByWorkflowGate = 2,
    MissingRequiredPlanEvidence = 3,
    ControlledApplyPlanPrepared = 4
}

public enum ControlledApplyPlanReason
{
    Unknown = 0,
    PlanOnly,
    SuppliedEvidenceOnly,
    ExecutionNotImplemented,
    SourceApplyNotImplemented,
    PatchApplicationNotImplemented,
    RollbackNotImplemented,
    SourceNotApplied,
    PatchNotApplied,
    FilesNotMutated,
    SourceFilesNotRead,
    CommandNotRun,
    ToolNotInvoked,
    AgentNotDispatched,
    ModelNotCalled,
    PromptNotBuilt,
    ValidationNotRun,
    RollbackNotRun,
    ApprovalNotSatisfied,
    PolicyNotSatisfied,
    WorkflowNotTransitioned,
    TicketNotCreated,
    MemoryNotPromoted,
    RetrievalNotActivated,
    SqlNotWritten,
    MissingRequest,
    MissingWorkflowRunId,
    MissingWorkflowStepId,
    MissingControlledApplyPlanReferenceId,
    MissingProjectReferenceId,
    MissingTargetReferenceId,
    UnknownTargetKind,
    UnsafeReferenceText,
    UnsafePlanSummary,
    MissingSafePlanSummary,
    MissingSourceApplyApprovalRequirement,
    SourceApplyApprovalRequirementNotReady,
    MissingPatchProposalEvidencePackage,
    PatchProposalEvidencePackageNotReady,
    MissingHumanApprovalPackage,
    HumanApprovalPackageNotReady,
    MissingPlanPhase,
    InvalidPlanPhase,
    UnsafePlanPhase,
    PlanPhaseClaimsAuthority,
    MissingPreconditionReference,
    UnsafePrecondition,
    PreconditionClaimsAuthority,
    MissingValidationReference,
    UnsafeValidationReference,
    ValidationReferenceClaimsAuthority,
    MissingRollbackReference,
    UnsafeRollbackReference,
    RollbackReferenceClaimsAuthority,
    MissingEvidenceReference,
    UnsafeEvidenceReference,
    EvidenceReferenceClaimsAuthority,
    MissingGateHint,
    InvalidGateHint,
    UnsafeGateHint,
    GateHintClaimsAuthority,
    MissingRiskReference,
    InvalidRisk,
    UnsafeRiskReference,
    StepEvaluationNotEligible,
    DryRunNotEligible,
    RouteSuggestionNotEligible,
    BlockedByWorkflowGate,
    MissingRequiredPlanEvidence,
    ControlledApplyPlanPrepared
}

public enum ControlledApplyPlanPhaseKind
{
    Unknown = 0,
    PreApplyReview = 1,
    ApprovalCheck = 2,
    PolicyCheck = 3,
    PatchMaterialReview = 4,
    ApplyStepPlaceholder = 5,
    ValidationStepPlaceholder = 6,
    RollbackReview = 7,
    PostApplyReview = 8
}

public enum ControlledApplyGateHintKind
{
    Unknown = 0,
    SourceChangeForbidden = 1,
    PatchApplicationForbidden = 2,
    ValidationForbidden = 3,
    RollbackForbidden = 4,
    ApprovalStillRequired = 5,
    PolicyStillRequired = 6,
    WorkflowTransitionForbidden = 7
}

public enum ControlledApplyRiskKind
{
    Unknown = 0,
    SourceChangeRisk = 1,
    PatchApplicationRisk = 2,
    ValidationRisk = 3,
    RollbackRisk = 4,
    ApprovalRisk = 5,
    PolicyRisk = 6,
    WorkflowRisk = 7
}

public enum ControlledApplyRiskSeverity
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3
}
