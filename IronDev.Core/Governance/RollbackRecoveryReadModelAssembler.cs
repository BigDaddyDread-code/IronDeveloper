namespace IronDev.Core.Governance;

public static class RollbackRecoveryReadModelAssembler
{
    public static RollbackRecoveryReadModel Assemble(RollbackRecoveryReadModelRequest? request)
    {
        var validation = RollbackRecoveryReadModelValidator.ValidateRequest(request);
        if (!validation.IsValid || request is null)
        {
            return InvalidResult(
                request?.TenantId ?? string.Empty,
                request?.ProjectId ?? string.Empty,
                request?.OperationId ?? string.Empty,
                request?.AsOfUtc ?? default,
                validation.Issues);
        }

        var ambiguous = FindAmbiguity(request);
        if (ambiguous.Count > 0)
        {
            var material = request.Materials.Count == 0 ? null : LastMaterial(request.Materials);
            var assessment = BuildAssessment(
                request,
                material,
                RollbackRecoveryStateKind.Ambiguous,
                RollbackRecoveryGapKind.Ambiguous,
                "AmbiguousRollbackRecoveryMaterial");

            return Result(
                request,
                RollbackRecoveryReadModelStatus.AmbiguousMaterial,
                assessment,
                ambiguous,
                []);
        }

        if (request.Materials.Count == 0 &&
            !HasInterruptedDiagnostic(request.DiagnosticSnapshot))
        {
            return Result(
                request,
                RollbackRecoveryReadModelStatus.NoMaterial,
                null,
                [],
                []);
        }

        var resolved = Resolve(request);
        return Result(
            request,
            resolved.Status,
            resolved.Assessment,
            [],
            []);
    }

    private static (RollbackRecoveryReadModelStatus Status, RollbackRecoveryAssessment Assessment) Resolve(
        RollbackRecoveryReadModelRequest request)
    {
        var materials = request.Materials.ToArray();
        var kinds = materials.Select(static material => material.MaterialKind).ToHashSet();

        if (kinds.Contains(RollbackRecoveryMaterialKind.RecoveryExecutionFailed))
        {
            var material = LastOfKind(materials, RollbackRecoveryMaterialKind.RecoveryExecutionFailed);
            return (
                RollbackRecoveryReadModelStatus.FailureObserved,
                BuildAssessment(request, material, RollbackRecoveryStateKind.RecoveryFailed, RollbackRecoveryGapKind.RecoveryFailed, "RecoveryExecutionFailedObserved"));
        }

        if (kinds.Contains(RollbackRecoveryMaterialKind.RollbackExecutionFailed))
        {
            var material = LastOfKind(materials, RollbackRecoveryMaterialKind.RollbackExecutionFailed);
            return (
                RollbackRecoveryReadModelStatus.FailureObserved,
                BuildAssessment(request, material, RollbackRecoveryStateKind.RollbackFailed, RollbackRecoveryGapKind.RollbackFailed, "RollbackExecutionFailedObserved"));
        }

        if (kinds.Contains(RollbackRecoveryMaterialKind.RollbackExecutionObserved) &&
            kinds.Contains(RollbackRecoveryMaterialKind.RecoveryExecutionObserved))
        {
            var material = LastMaterial(materials.Where(material =>
                material.MaterialKind is RollbackRecoveryMaterialKind.RollbackExecutionObserved or RollbackRecoveryMaterialKind.RecoveryExecutionObserved));
            return (
                RollbackRecoveryReadModelStatus.Assessed,
                BuildAssessment(request, material, RollbackRecoveryStateKind.RollbackAndRecoveryObserved, RollbackRecoveryGapKind.NoneObserved, "RollbackAndRecoveryExecutionObserved"));
        }

        if (kinds.Contains(RollbackRecoveryMaterialKind.RecoveryExecutionObserved))
        {
            var material = LastOfKind(materials, RollbackRecoveryMaterialKind.RecoveryExecutionObserved);
            return (
                RollbackRecoveryReadModelStatus.Assessed,
                BuildAssessment(request, material, RollbackRecoveryStateKind.RecoveryObserved, RollbackRecoveryGapKind.NoneObserved, "RecoveryExecutionObserved"));
        }

        if (kinds.Contains(RollbackRecoveryMaterialKind.RollbackExecutionObserved))
        {
            var material = LastOfKind(materials, RollbackRecoveryMaterialKind.RollbackExecutionObserved);
            return (
                RollbackRecoveryReadModelStatus.Assessed,
                BuildAssessment(request, material, RollbackRecoveryStateKind.RollbackObserved, RollbackRecoveryGapKind.NoneObserved, "RollbackExecutionObserved"));
        }

        if (kinds.Contains(RollbackRecoveryMaterialKind.RecoveryPlan) &&
            !kinds.Contains(RollbackRecoveryMaterialKind.RecoveryEvidence))
        {
            var material = LastOfKind(materials, RollbackRecoveryMaterialKind.RecoveryPlan);
            return (
                RollbackRecoveryReadModelStatus.MissingMaterial,
                BuildAssessment(request, material, RollbackRecoveryStateKind.RecoveryMaterialMissing, RollbackRecoveryGapKind.RecoveryPlanNoEvidence, "RecoveryPlanWithoutEvidence"));
        }

        if (kinds.Contains(RollbackRecoveryMaterialKind.RecoveryEvidence) &&
            !kinds.Contains(RollbackRecoveryMaterialKind.RecoveryReceipt))
        {
            var material = LastOfKind(materials, RollbackRecoveryMaterialKind.RecoveryEvidence);
            return (
                RollbackRecoveryReadModelStatus.MissingMaterial,
                BuildAssessment(request, material, RollbackRecoveryStateKind.RecoveryMaterialMissing, RollbackRecoveryGapKind.RecoveryEvidenceNoReceipt, "RecoveryEvidenceWithoutReceipt"));
        }

        if (kinds.Contains(RollbackRecoveryMaterialKind.RollbackPlan) &&
            !kinds.Contains(RollbackRecoveryMaterialKind.RollbackEvidence))
        {
            var material = LastOfKind(materials, RollbackRecoveryMaterialKind.RollbackPlan);
            return (
                RollbackRecoveryReadModelStatus.MissingMaterial,
                BuildAssessment(request, material, RollbackRecoveryStateKind.RollbackMaterialMissing, RollbackRecoveryGapKind.RollbackPlanNoEvidence, "RollbackPlanWithoutEvidence"));
        }

        if (kinds.Contains(RollbackRecoveryMaterialKind.RollbackEvidence) &&
            !kinds.Contains(RollbackRecoveryMaterialKind.RollbackReceipt))
        {
            var material = LastOfKind(materials, RollbackRecoveryMaterialKind.RollbackEvidence);
            return (
                RollbackRecoveryReadModelStatus.MissingMaterial,
                BuildAssessment(request, material, RollbackRecoveryStateKind.RollbackMaterialMissing, RollbackRecoveryGapKind.RollbackEvidenceNoReceipt, "RollbackEvidenceWithoutReceipt"));
        }

        if (HasInterruptedDiagnostic(request.DiagnosticSnapshot) &&
            !kinds.Contains(RollbackRecoveryMaterialKind.RollbackPlan))
        {
            return (
                RollbackRecoveryReadModelStatus.MissingMaterial,
                BuildAssessment(request, LastOrNull(materials), RollbackRecoveryStateKind.RollbackMaterialMissing, RollbackRecoveryGapKind.InterruptedNoRollbackPlan, "InterruptedOperationWithoutRollbackPlan"));
        }

        if (HasInterruptedDiagnostic(request.DiagnosticSnapshot) &&
            !kinds.Contains(RollbackRecoveryMaterialKind.RecoveryPlan))
        {
            return (
                RollbackRecoveryReadModelStatus.MissingMaterial,
                BuildAssessment(request, LastOrNull(materials), RollbackRecoveryStateKind.RecoveryMaterialMissing, RollbackRecoveryGapKind.InterruptedNoRecoveryPlan, "InterruptedOperationWithoutRecoveryPlan"));
        }

        if (kinds.Contains(RollbackRecoveryMaterialKind.RollbackPlan) &&
            kinds.Contains(RollbackRecoveryMaterialKind.RollbackEvidence) &&
            kinds.Contains(RollbackRecoveryMaterialKind.RollbackReceipt))
        {
            var material = LastMaterial(materials.Where(material =>
                material.MaterialKind is RollbackRecoveryMaterialKind.RollbackPlan or RollbackRecoveryMaterialKind.RollbackEvidence or RollbackRecoveryMaterialKind.RollbackReceipt));
            return (
                RollbackRecoveryReadModelStatus.Assessed,
                BuildAssessment(request, material, RollbackRecoveryStateKind.RollbackMaterialAvailable, RollbackRecoveryGapKind.NoneObserved, "RollbackMaterialAvailable"));
        }

        if (kinds.Contains(RollbackRecoveryMaterialKind.RecoveryPlan) &&
            kinds.Contains(RollbackRecoveryMaterialKind.RecoveryEvidence) &&
            kinds.Contains(RollbackRecoveryMaterialKind.RecoveryReceipt))
        {
            var material = LastMaterial(materials.Where(material =>
                material.MaterialKind is RollbackRecoveryMaterialKind.RecoveryPlan or RollbackRecoveryMaterialKind.RecoveryEvidence or RollbackRecoveryMaterialKind.RecoveryReceipt));
            return (
                RollbackRecoveryReadModelStatus.Assessed,
                BuildAssessment(request, material, RollbackRecoveryStateKind.RecoveryMaterialAvailable, RollbackRecoveryGapKind.NoneObserved, "RecoveryMaterialAvailable"));
        }

        return (
            RollbackRecoveryReadModelStatus.Assessed,
            BuildAssessment(request, LastOrNull(materials), RollbackRecoveryStateKind.NoRollbackOrRecoveryObserved, RollbackRecoveryGapKind.NoneObserved, "NoRollbackOrRecoveryConcernObserved"));
    }

    private static IReadOnlyList<string> FindAmbiguity(RollbackRecoveryReadModelRequest request)
    {
        var ambiguous = new List<string>();

        AddDuplicateValues(
            request.Materials.Select(static material => material.MaterialId),
            "DuplicateRollbackRecoveryMaterialId",
            ambiguous);
        AddDuplicateValues(
            request.Materials.Select(static material => material.AppendPosition.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "DuplicateRollbackRecoveryMaterialAppendPosition",
            ambiguous);

        foreach (var group in request.Materials.GroupBy(static material => material.MaterialId, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Select(MaterialFingerprint).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                ambiguous.Add($"ConflictingRollbackRecoveryMaterialMetadata:{group.Key}");
            }
        }

        var kinds = request.Materials.Select(static material => material.MaterialKind).ToHashSet();
        if (kinds.Contains(RollbackRecoveryMaterialKind.RollbackExecutionObserved) &&
            kinds.Contains(RollbackRecoveryMaterialKind.RollbackExecutionFailed))
        {
            ambiguous.Add("RollbackExecutionObservedAndFailed");
        }

        if (kinds.Contains(RollbackRecoveryMaterialKind.RecoveryExecutionObserved) &&
            kinds.Contains(RollbackRecoveryMaterialKind.RecoveryExecutionFailed))
        {
            ambiguous.Add("RecoveryExecutionObservedAndFailed");
        }

        if (kinds.Contains(RollbackRecoveryMaterialKind.RollbackExecutionObserved) &&
            !kinds.Contains(RollbackRecoveryMaterialKind.RollbackPlan))
        {
            ambiguous.Add("RollbackExecutionObservedWithoutRollbackPlan");
        }

        if (kinds.Contains(RollbackRecoveryMaterialKind.RecoveryExecutionObserved) &&
            !kinds.Contains(RollbackRecoveryMaterialKind.RecoveryPlan))
        {
            ambiguous.Add("RecoveryExecutionObservedWithoutRecoveryPlan");
        }

        if (kinds.Contains(RollbackRecoveryMaterialKind.RollbackReceipt) &&
            !kinds.Contains(RollbackRecoveryMaterialKind.RollbackEvidence))
        {
            ambiguous.Add("RollbackReceiptWithoutRollbackEvidence");
        }

        if (kinds.Contains(RollbackRecoveryMaterialKind.RecoveryReceipt) &&
            !kinds.Contains(RollbackRecoveryMaterialKind.RecoveryEvidence))
        {
            ambiguous.Add("RecoveryReceiptWithoutRecoveryEvidence");
        }

        return ambiguous
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool HasInterruptedDiagnostic(RollbackRecoveryDiagnosticSnapshot? snapshot) =>
        snapshot is not null &&
        (snapshot.InterruptedRunStatus == InterruptedRunReadModelStatus.Interrupted ||
         snapshot.InterruptedRunStatus == InterruptedRunReadModelStatus.AmbiguousCheckpoints ||
         snapshot.InterruptedRunState is InterruptedRunStateKind.Interrupted or InterruptedRunStateKind.Failed or InterruptedRunStateKind.Ambiguous);

    private static void AddDuplicateValues(
        IEnumerable<string> values,
        string issuePrefix,
        ICollection<string> issues)
    {
        foreach (var duplicate in values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .OrderBy(static value => value, StringComparer.Ordinal))
        {
            issues.Add($"{issuePrefix}:{duplicate}");
        }
    }

    private static string MaterialFingerprint(RollbackRecoveryMaterialObservation material) =>
        string.Join(
            "|",
            material.TenantId,
            material.ProjectId,
            material.OperationId,
            material.CorrelationId,
            material.MaterialId,
            material.MaterialKind,
            material.AppendPosition,
            material.ObservedAtUtc.ToUnixTimeMilliseconds(),
            material.RecordedAtUtc.ToUnixTimeMilliseconds(),
            material.SurfaceKind,
            material.SurfaceId,
            material.ReferenceKind,
            material.ReferenceId ?? string.Empty,
            material.Source,
            material.IsRedacted,
            material.RedactionReason ?? string.Empty);

    private static RollbackRecoveryMaterialObservation LastMaterial(IEnumerable<RollbackRecoveryMaterialObservation> materials) =>
        materials
            .OrderBy(static material => material.AppendPosition)
            .ThenBy(static material => material.ObservedAtUtc)
            .ThenBy(static material => material.MaterialId, StringComparer.Ordinal)
            .Last();

    private static RollbackRecoveryMaterialObservation? LastOrNull(IEnumerable<RollbackRecoveryMaterialObservation> materials)
    {
        var array = materials.ToArray();
        return array.Length == 0 ? null : LastMaterial(array);
    }

    private static RollbackRecoveryMaterialObservation LastOfKind(
        IEnumerable<RollbackRecoveryMaterialObservation> materials,
        RollbackRecoveryMaterialKind kind) =>
        LastMaterial(materials.Where(material => material.MaterialKind == kind));

    private static RollbackRecoveryAssessment BuildAssessment(
        RollbackRecoveryReadModelRequest request,
        RollbackRecoveryMaterialObservation? material,
        RollbackRecoveryStateKind stateKind,
        RollbackRecoveryGapKind gapKind,
        string reason) =>
        new()
        {
            StateKind = stateKind,
            GapKind = gapKind,
            LastMaterialId = material?.MaterialId,
            LastMaterialKind = material?.MaterialKind ?? RollbackRecoveryMaterialKind.Unknown,
            LastMaterialObservedAtUtc = material?.ObservedAtUtc,
            LastMaterialRecordedAtUtc = material?.RecordedAtUtc,
            DiagnosticSummary = DiagnosticSummary(request.DiagnosticSnapshot),
            Reason = reason,
            SurfaceKind = material?.SurfaceKind ?? OperationCorrelationSurfaceKind.Unknown,
            SurfaceId = material?.SurfaceId,
            ReferenceKind = material?.ReferenceKind ?? OperationReferenceKind.Unknown,
            ReferenceId = material?.ReferenceId,
            IsRedacted = material?.IsRedacted ?? false
        };

    private static string? DiagnosticSummary(RollbackRecoveryDiagnosticSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        return string.Join(
            "; ",
            $"interruptedStatus={snapshot.InterruptedRunStatus}",
            $"interruptedState={snapshot.InterruptedRunState}",
            $"interruptedGap={snapshot.InterruptedRunGap}",
            $"projected={snapshot.ProjectedStatusKind}",
            $"missingEvidence={snapshot.MissingEvidenceStatus}",
            $"forbiddenActions={snapshot.ForbiddenActionStatus}",
            $"receipt={snapshot.ReceiptResolutionStatus}",
            $"evidence={snapshot.EvidenceResolutionStatus}",
            $"validation={snapshot.ValidationStalenessStatus}",
            $"patchBase={snapshot.PatchBaseFreshnessStatus}",
            $"worktreeBaseHead={snapshot.WorktreeBaseHeadFreshnessStatus}");
    }

    private static RollbackRecoveryReadModel InvalidResult(
        string tenantId,
        string projectId,
        string operationId,
        DateTimeOffset asOfUtc,
        IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = false,
            ResolutionStatus = RollbackRecoveryReadModelStatus.InvalidRequest,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            AsOfUtc = asOfUtc,
            Assessment = null,
            MaterialIds = [],
            AmbiguousMaterial = [],
            Issues = issues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
            Warnings = [],
            ForbiddenAuthorityImplications = RollbackRecoveryReadModelValidator.ForbiddenAuthorityImplications
        };

    private static RollbackRecoveryReadModel Result(
        RollbackRecoveryReadModelRequest request,
        RollbackRecoveryReadModelStatus status,
        RollbackRecoveryAssessment? assessment,
        IReadOnlyList<string> ambiguous,
        IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = true,
            ResolutionStatus = status,
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            OperationId = request.OperationId,
            AsOfUtc = request.AsOfUtc,
            Assessment = assessment,
            MaterialIds = request.Materials
                .Select(static material => material.MaterialId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static materialId => materialId, StringComparer.Ordinal)
                .ToArray(),
            AmbiguousMaterial = ambiguous,
            Issues = issues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
            Warnings = RollbackRecoveryReadModelValidator.Warnings(),
            ForbiddenAuthorityImplications = RollbackRecoveryReadModelValidator.ForbiddenAuthorityImplications
        };
}
