using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using IronDev.Core.Memory;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockFrontendReadOnlyReadinessApiTests
{
    private const string OperationId = "operation-pr29";
    private const string PackageId = "patch-package-pr29";
    private const string ValidationResultId = "validation-result-pr29";
    private const string EvidenceRef = "patch-package:patch-package-pr29";
    private const string ReceiptRef = "draft-pull-request-receipt:receipt-pr29";
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-22T00:00:00Z");

    [TestMethod]
    public void FrontendReadiness_StatusEndpointReturnsCanonicalStatus()
    {
        var model = Api().GetOperationStatus(OperationId)!;

        Assert.AreEqual(OperationId, model.OperationId);
        Assert.AreEqual("SourceApply", model.OperationKind);
        Assert.AreEqual(GovernedOperationState.Blocked.ToString(), model.State);
    }

    [TestMethod]
    public void FrontendReadiness_StatusEndpointIncludesBlockedReasons()
    {
        AssertContains(Api().GetOperationStatus(OperationId)!.BlockedReasons, "MissingExplicitSourceApplyAuthority");
    }

    [TestMethod]
    public void FrontendReadiness_StatusEndpointIncludesMissingEvidence()
    {
        AssertContains(Api().GetOperationStatus(OperationId)!.MissingEvidence, "accepted-source-apply-request:missing");
    }

    [TestMethod]
    public void FrontendReadiness_StatusEndpointIncludesNextSafeActions()
    {
        var model = Api().GetOperationStatus(OperationId)!;

        AssertContains(model.NextSafeActions, "review patch package before requesting source apply authority (guidance only)");
    }

    [TestMethod]
    public void FrontendReadiness_StatusEndpointIncludesForbiddenActions()
    {
        var model = Api().GetOperationStatus(OperationId)!;

        AssertContains(model.ForbiddenActions, "do not treat patch package as source apply authority");
        AssertContains(model.ForbiddenActions, "do not continue workflow from status, receipt, memory, or UI text");
    }

    [TestMethod]
    public void FrontendReadiness_StatusEndpointIncludesEvidenceRefs()
    {
        AssertContains(Api().GetOperationStatus(OperationId)!.EvidenceRefs, EvidenceRef);
    }

    [TestMethod]
    public void FrontendReadiness_StatusEndpointIncludesReceiptRefs()
    {
        AssertContains(Api().GetOperationStatus(OperationId)!.ReceiptRefs, ReceiptRef);
    }

    [TestMethod]
    public void FrontendReadiness_StatusEndpointIncludesAuthorityWarnings()
    {
        var warnings = Api().GetOperationStatus(OperationId)!.AuthorityWarnings;

        AssertContains(warnings, "Status output is not authority.");
        Assert.IsTrue(warnings.Any(value => value.Contains("not approval", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void FrontendReadiness_StatusEndpointIsReadOnly()
    {
        Assert.IsTrue(Api().GetOperationStatus(OperationId)!.Boundary.ReadOnly);
    }

    [TestMethod]
    public void FrontendReadiness_StatusEndpointCannotCreateApproval()
    {
        Assert.IsFalse(Api().GetOperationStatus(OperationId)!.Boundary.CanCreateApproval);
    }

    [TestMethod]
    public void FrontendReadiness_StatusEndpointCannotSatisfyPolicy()
    {
        Assert.IsFalse(Api().GetOperationStatus(OperationId)!.Boundary.CanSatisfyPolicy);
    }

    [TestMethod]
    public void FrontendReadiness_StatusEndpointCannotExecute()
    {
        Assert.IsFalse(Api().GetOperationStatus(OperationId)!.Boundary.CanExecute);
    }

    [TestMethod]
    public void FrontendReadiness_StatusEndpointCannotMutateSource()
    {
        Assert.IsFalse(Api().GetOperationStatus(OperationId)!.Boundary.CanMutateSource);
    }

    [TestMethod]
    public void FrontendReadiness_StatusEndpointCannotPromoteMemory()
    {
        Assert.IsFalse(Api().GetOperationStatus(OperationId)!.Boundary.CanPromoteMemory);
    }

    [TestMethod]
    public void FrontendReadiness_StatusEndpointCannotContinueWorkflow()
    {
        Assert.IsFalse(Api().GetOperationStatus(OperationId)!.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void FrontendReadiness_TimelineEndpointReturnsReadOnlyTimeline()
    {
        var model = Api().GetOperationTimeline(OperationId)!;

        Assert.IsTrue(model.Boundary.ReadOnly);
        Assert.AreEqual(2, model.Entries.Count);
        AssertContains(model.Entries.First().EvidenceRefs, EvidenceRef);
    }

    [TestMethod]
    public void FrontendReadiness_TimelineDoesNotContinueWorkflow()
    {
        Assert.IsFalse(Api().GetOperationTimeline(OperationId)!.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void FrontendReadiness_TimelineDoesNotExposeHiddenReasoning()
    {
        var summary = Api().GetOperationTimeline(OperationId)!.Entries.Last().Summary;

        Assert.AreEqual("[redacted: private material]", summary);
    }

    [TestMethod]
    public void FrontendReadiness_PatchPackageMetadataEndpointReturnsMetadataOnly()
    {
        var model = Api().GetPatchPackageMetadata(PackageId)!;

        Assert.AreEqual(PackageId, model.PackageId);
        AssertContains(model.ProposedFilePaths, "IronDev.Core/Governance/Example.cs");
        Assert.IsTrue(model.Boundary.ReadOnly);
    }

    [TestMethod]
    public void FrontendReadiness_PatchPackageMetadataDoesNotAuthorizeApply()
    {
        var model = Api().GetPatchPackageMetadata(PackageId)!;

        Assert.IsFalse(model.Boundary.CanMutateSource);
        Assert.IsFalse(model.Boundary.CanExecute);
    }

    [TestMethod]
    public void FrontendReadiness_ValidationMetadataEndpointReturnsOutcomeAndStaleness()
    {
        var model = Api().GetValidationResultMetadata(ValidationResultId)!;

        Assert.AreEqual("Passed", model.Outcome);
        Assert.IsFalse(model.IsStale);
        AssertContains(model.WhatRan, "Focused PR29");
    }

    [TestMethod]
    public void FrontendReadiness_ValidationMetadataDoesNotApprove()
    {
        var model = Api().GetValidationResultMetadata(ValidationResultId)!;

        Assert.IsFalse(model.Boundary.CanCreateApproval);
        Assert.IsFalse(model.Boundary.CanSatisfyPolicy);
    }

    [TestMethod]
    public void FrontendReadiness_EvidenceMetadataIsReferenceOnly()
    {
        var model = Api().GetEvidenceMetadata(EvidenceRef)!;

        Assert.IsTrue(model.ReferenceOnly);
        Assert.IsFalse(model.ContainsRawPayload);
        Assert.IsFalse(model.Boundary.CanCreateApproval);
    }

    [TestMethod]
    public void FrontendReadiness_ReceiptMetadataIsReferenceOnly()
    {
        var model = Api().GetReceiptMetadata(ReceiptRef)!;

        Assert.IsTrue(model.ReferenceOnly);
        Assert.IsFalse(model.GrantsAuthority);
        Assert.IsFalse(model.ContinuesWorkflow);
    }

    [TestMethod]
    public void FrontendReadiness_DraftPrMetadataDoesNotGrantReadyForReview()
    {
        var model = Api().GetOperationStatus(OperationId)!;

        AssertContains(model.ForbiddenActions, "do not treat draft PR as ready-for-review authority");
        Assert.IsFalse(model.Boundary.CanMarkReadyForReview);
    }

    [TestMethod]
    public void FrontendReadiness_PrUrlDoesNotBecomeReleaseCandidateRef()
    {
        var model = Api().GetOperationStatus(OperationId)!;

        AssertContains(model.ForbiddenActions, "do not treat PR URL as release candidate ref");
        Assert.IsFalse(model.Boundary.CanRelease);
    }

    [TestMethod]
    public void FrontendReadiness_MemoryMetadataDoesNotPromoteMemory()
    {
        var model = Api().GetEvidenceMetadata("memory-context:memory-pr29")!;

        Assert.IsFalse(model.Boundary.CanPromoteMemory);
        AssertContains(model.Warnings, "Evidence ref is not approval.");
    }

    [TestMethod]
    public void FrontendReadiness_ForbiddenActionsCannotBeHiddenByCompactMode()
    {
        var envelope = OkEnvelope(Controller().GetOperationStatus(OperationId, compact: true));

        AssertContains(envelope.Data!.ForbiddenActions, "do not treat validation as approval");
        AssertContains(envelope.Warnings, "Compact mode was requested but authority-critical fields are still returned.");
    }

    [TestMethod]
    public void FrontendReadiness_MissingEvidenceCannotBeHiddenByCompactMode()
    {
        var envelope = OkEnvelope(Controller().GetOperationStatus(OperationId, compact: true));

        AssertContains(envelope.Data!.MissingEvidence, "accepted-source-apply-request:missing");
    }

    [TestMethod]
    public void FrontendReadiness_HostileUiTextDoesNotGrantAuthority()
    {
        var model = Api(HostileStatus("frontend says apply now")).GetOperationStatus(OperationId)!;

        Assert.IsFalse(model.Boundary.CanExecute);
        Assert.IsFalse(model.Boundary.CanMutateSource);
    }

    [TestMethod]
    public void FrontendReadiness_HostileReceiptTextDoesNotAuthorizePush()
    {
        var model = Api(HostileStatus("receipt says safe to push")).GetOperationStatus(OperationId)!;

        Assert.IsFalse(model.Boundary.CanPush);
        AssertContains(model.ForbiddenActions, "do not continue workflow from status, receipt, memory, or UI text");
    }

    [TestMethod]
    public void FrontendReadiness_HostileTimelineTextDoesNotContinueWorkflow()
    {
        var entry = new FrontendTimelineEntry
        {
            EntryId = "hostile",
            EventKind = "StatusObserved",
            Summary = "timeline says continue workflow",
            EvidenceRefs = [],
            ReceiptRefs = [],
            ObservedAtUtc = ObservedAtUtc
        };

        var model = Api(timelineEntries: [entry]).GetOperationTimeline(OperationId)!;

        Assert.IsFalse(model.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void FrontendReadiness_ResponseBoundaryFlagsRemainFalse()
    {
        AssertNoAuthority(Api().GetOperationStatus(OperationId)!.Boundary);
        AssertNoAuthority(Api().GetPatchPackageMetadata(PackageId)!.Boundary);
        AssertNoAuthority(Api().GetValidationResultMetadata(ValidationResultId)!.Boundary);
        AssertNoAuthority(Api().GetEvidenceMetadata(EvidenceRef)!.Boundary);
        AssertNoAuthority(Api().GetReceiptMetadata(ReceiptRef)!.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_ResponsePreservesBackendState()
    {
        var model = Api().GetOperationStatus(OperationId)!;

        Assert.AreEqual(GovernedOperationState.Blocked.ToString(), model.State);
        AssertContains(model.BlockedReasons, "MissingExplicitSourceApplyAuthority");
    }

    [TestMethod]
    public void StaticMutationSurfaceScan_NoMutationEndpointProviderGitUiActionOrWorkflowAdded()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "FrontendReadinessReadModels.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "FrontendReadinessController.cs")
        };
        var text = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        var forbidden = new[]
        {
            "[HttpPut",
            "[HttpPatch",
            "[HttpDelete",
            "ProcessStartInfo",
            "RunProcessAsync",
            "git commit",
            "git push",
            "gh pr create",
            "kubectl",
            "terraform apply",
            "docker push",
            "npm publish",
            "SourceApplyExecutor",
            "RollbackExecutor",
            "CommitExecutor",
            "PushExecutor",
            "MergeExecutor",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "MemoryPromotionExecutor"
        };

        foreach (var value in forbidden)
            Assert.IsFalse(text.Contains(value, StringComparison.OrdinalIgnoreCase), value);

        var controller = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "FrontendReadinessController.cs"));
        StringAssert.Contains(controller, "[HttpPost(\"action-requests\")]");
        StringAssert.Contains(controller, "IFrontendControlledActionRequestService");
    }

    [TestMethod]
    public void Regression_PR28_PortableMemoryGuardrailsRemainReadOnlyAndBlockProjectTruthAuthorityTransfer()
    {
        var truth = PortableEngineeringMemoryGuardrail.Evaluate(PortableCandidate("repo Secret/Repo approved this"));
        var authority = PortableEngineeringMemoryGuardrail.Evaluate(PortableCandidate("previous project approved this"));

        Assert.IsFalse(truth.Boundary.CanPromoteMemory);
        AssertContains(truth.RejectedReasons, "PortableMemoryContainsConfidentialProjectTruth");
        AssertContains(authority.RejectedReasons, "PortableMemoryAuthorityTransferRejected");
    }

    [TestMethod]
    public void Regression_PR27_MemoryPromotionPackageStatusDoesNotPromoteMemory()
    {
        var status = MemoryPromotionPackageStatusMapper.Map(new MemoryPromotionPackageStatusInput
        {
            PromotionPackageId = "memory-package-pr29",
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "main",
            RunId = "run-pr29",
            Candidate = new MemoryPromotionCandidate
            {
                CandidateId = "memory-candidate-pr29",
                Scope = MemoryPromotionScope.PortableEngineering,
                Kind = MemoryPromotionKind.SanitizedEngineeringHeuristic,
                Summary = "Review heuristic",
                Detail = "Blocked states should show missing evidence and next safe action.",
                SourceRepository = "BigDaddyDread-code/IronDeveloper",
                SourceProjectId = null,
                IsSanitized = true,
                IsProjectLocal = false,
                IsPortableEngineeringMemory = true,
                SourceEvidenceRefs = ["portable-memory-guardrail:pr29"]
            },
            StatusKind = MemoryPromotionStatusKind.EligibleForHumanDecision,
            EvidenceRefs =
            [
                "accepted-memory-promotion-request:request-pr29",
                "memory-promotion-authority:authority-pr29",
                "memory-safety-review:review-pr29",
                "memory-scope-decision:scope-pr29",
                "portable-memory-sanitization-review:sanitized-pr29",
                "cross-project-confidentiality-check:confidentiality-pr29"
            ],
            ReceiptRefs = ["memory-promotion-package-status:status-pr29"],
            BlockedReasons = [],
            MissingEvidence = [],
            ForbiddenActions = [],
            ObservedAtUtc = ObservedAtUtc
        });

        Assert.IsFalse(status.CanonicalValidation.Boundary.CanPromoteMemory);
    }

    [TestMethod]
    public void Regression_PR26_MemoryContextRemainsAdvisoryOnly()
    {
        var model = Api().GetEvidenceMetadata("memory-context:memory-pr29")!;

        Assert.IsTrue(model.ReferenceOnly);
        Assert.IsFalse(model.Boundary.CanSatisfyPolicy);
        Assert.IsFalse(model.Boundary.CanPromoteMemory);
    }

    [TestMethod]
    public void Regression_PR25_FormatterRemainsPresentationOnly()
    {
        var message = GovernedStatusUserMessageFormatter.Format(Status());

        Assert.IsFalse(message.CanExecute);
        Assert.IsFalse(message.CanMutateSource);
        Assert.IsFalse(message.CanContinueWorkflow);
    }

    [TestMethod]
    public void Regression_PR24_BoundedAuthorityLaneStillStopsBeforeDownstreamAuthority()
    {
        var boundary = Api().GetOperationStatus(OperationId)!.Boundary;

        Assert.IsFalse(boundary.CanMarkReadyForReview);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void Regression_PR23_AskBeforeLaneStillBlocksSourceApplyWithoutAuthority()
    {
        var model = Api().GetOperationStatus(OperationId)!;

        Assert.AreEqual(GovernedOperationState.Blocked.ToString(), model.State);
        Assert.IsFalse(model.Boundary.CanMutateSource);
    }

    [TestMethod]
    public void Regression_PR22_NoApprovalLaneStillProducesEvidenceOnly()
    {
        var model = Api().GetPatchPackageMetadata(PackageId)!;

        AssertContains(model.EvidenceRefs, EvidenceRef);
        Assert.IsFalse(model.Boundary.CanCreateApproval);
    }

    [TestMethod]
    public void Regression_PR21_FreshnessGuardRemainsExplanationOnly()
    {
        var model = Api().GetOperationStatus(OperationId)!;

        AssertContains(model.ForbiddenActions, "do not treat freshness as authority");
        Assert.IsFalse(model.Boundary.CanMutateSource);
    }

    [TestMethod]
    public void Regression_PR20_RecoveryRemainsReadOnly()
    {
        var timeline = Api().GetOperationTimeline(OperationId)!;

        Assert.IsTrue(timeline.Entries.Any(entry => entry.EventKind == "RecoveryDiagnosisCreated"));
        Assert.IsFalse(timeline.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void Regression_CA_RollbackExecutorStillRequiresSeparateRollbackAuthority()
    {
        var model = Api().GetOperationStatus(OperationId)!;

        Assert.IsFalse(model.Boundary.CanRollback);
        AssertContains(model.ForbiddenActions, "do not continue workflow from status, receipt, memory, or UI text");
    }

    [TestMethod]
    public void Regression_ProposalOnlyStillForbidsMutation()
    {
        var result = ProposalOnlyRunProfileEvaluator.Evaluate(new ProposalOnlyRunProfileEvaluationRequest
        {
            OperationId = "proposal-only-pr29",
            OperationKind = "SourceApply",
            Subject = "repo:BigDaddyDread-code/IronDeveloper branch:main",
            RepoId = "BigDaddyDread-code/IronDeveloper",
            Branch = "main",
            EvidenceRefs = ["profile:proposal-only"],
            ObservedAtUtc = ObservedAtUtc
        });

        Assert.IsFalse(result.IsAllowed);
        Assert.IsFalse(result.StatusValidation.Boundary.CanSourceApply);
    }

    [TestMethod]
    public void FrontendReadiness_ControllerExposesOnlyGetEndpoints()
    {
        var methods = typeof(FrontendReadinessController)
            .GetMethods()
            .Where(method => method.DeclaringType == typeof(FrontendReadinessController))
            .ToArray();

        Assert.IsTrue(methods.Any(method => HasGetRoute(method, "operations/{operationId}/status")));
        Assert.IsTrue(methods.Any(method => HasGetRoute(method, "operations/{operationId}/timeline")));
        Assert.IsTrue(methods.Any(method => HasGetRoute(method, "patch-packages/{packageId}/metadata")));
        Assert.IsTrue(methods.Any(method => HasGetRoute(method, "validation-results/{validationResultId}/metadata")));
        Assert.IsTrue(methods.Any(method => HasGetRoute(method, "evidence/{evidenceRef}/metadata")));
        Assert.IsTrue(methods.Any(method => HasGetRoute(method, "receipts/{receiptRef}/metadata")));
        Assert.IsTrue(methods.Any(method => HasPostRoute(method, "action-requests")));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPutAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPatchAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpDeleteAttribute), inherit: false).Any()));
    }

    [TestMethod]
    public void FrontendReadiness_ReceiptRecordsReadOnlyBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "PR29_READ_ONLY_FRONTEND_READINESS_API.md"));

        StringAssert.Contains(doc, "The frontend reads backend truth. It does not invent authority.");
        StringAssert.Contains(doc, "Read API is not action API.");
        StringAssert.Contains(doc, "Forbidden actions remain visible.");
        StringAssert.Contains(doc, "Missing evidence remains visible.");
        StringAssert.Contains(doc, "The first frontend surface is a window, not a cockpit.");
    }

    private static bool HasGetRoute(System.Reflection.MethodInfo method, string route) =>
        method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false)
            .OfType<HttpGetAttribute>()
            .Any(attribute => string.Equals(attribute.Template, route, StringComparison.OrdinalIgnoreCase));

    private static bool HasPostRoute(System.Reflection.MethodInfo method, string route) =>
        method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false)
            .OfType<HttpPostAttribute>()
            .Any(attribute => string.Equals(attribute.Template, route, StringComparison.OrdinalIgnoreCase));

    private static FrontendReadinessController Controller(GovernedOperationStatus? status = null) =>
        new(Api(status));

    private static IFrontendReadinessReadApi Api(
        GovernedOperationStatus? status = null,
        IReadOnlyCollection<FrontendTimelineEntry>? timelineEntries = null) =>
        new FrontendReadinessReadApi(Snapshot(status ?? Status(), timelineEntries));

    private static FrontendReadinessReadSnapshot Snapshot(
        GovernedOperationStatus status,
        IReadOnlyCollection<FrontendTimelineEntry>? timelineEntries = null)
    {
        var timelines = new Dictionary<string, FrontendOperationTimelineReadModel>(StringComparer.OrdinalIgnoreCase)
        {
            [OperationId] = new()
            {
                OperationId = OperationId,
                Entries = timelineEntries ?? TimelineEntries(),
                Boundary = FrontendReadBoundary.ReadOnlyStatus
            }
        };
        var statuses = new Dictionary<string, GovernedOperationStatus>(StringComparer.OrdinalIgnoreCase)
        {
            [OperationId] = status
        };
        var packages = new Dictionary<string, FrontendPatchPackageMetadataReadModel>(StringComparer.OrdinalIgnoreCase)
        {
            [PackageId] = PatchPackage()
        };
        var validations = new Dictionary<string, FrontendValidationResultMetadataReadModel>(StringComparer.OrdinalIgnoreCase)
        {
            [ValidationResultId] = ValidationResult()
        };
        var evidence = new Dictionary<string, FrontendEvidenceMetadataReadModel>(StringComparer.OrdinalIgnoreCase)
        {
            [EvidenceRef] = Evidence(EvidenceRef, "PatchPackage"),
            ["memory-context:memory-pr29"] = Evidence("memory-context:memory-pr29", "MemoryContext")
        };
        var receipts = new Dictionary<string, FrontendReceiptMetadataReadModel>(StringComparer.OrdinalIgnoreCase)
        {
            [ReceiptRef] = Receipt()
        };

        return new FrontendReadinessReadSnapshot
        {
            OperationStatuses = statuses,
            Timelines = timelines,
            PatchPackages = packages,
            ValidationResults = validations,
            Evidence = evidence,
            Receipts = receipts
        };
    }

    private static GovernedOperationStatus Status() =>
        new()
        {
            OperationId = OperationId,
            OperationKind = "SourceApply",
            Subject = "repo:BigDaddyDread-code/IronDeveloper branch:main run:run-pr29 patch:sha256:patch-pr29 scope:IronDev.Core/Governance/Example.cs",
            State = GovernedOperationState.Blocked,
            BlockedReasons = ["MissingExplicitSourceApplyAuthority"],
            MissingEvidence = ["accepted-source-apply-request:missing", "bounded-authority-grant:SourceApply"],
            NextSafeActions = ["review patch package before requesting source apply authority"],
            ForbiddenActions =
            [
                "do not treat patch package as source apply authority",
                "do not treat validation as approval",
                "do not treat freshness as authority"
            ],
            EvidenceRefs =
            [
                EvidenceRef,
                "validation-result:validation-result-pr29",
                "repo-freshness:freshness-pr29",
                "draft-pull-request:url:https://github.example/pr/123",
                "memory-context:memory-pr29"
            ],
            ReceiptRefs = [ReceiptRef],
            ObservedAtUtc = ObservedAtUtc,
            ExpiresAtUtc = ObservedAtUtc.AddHours(1)
        };

    private static GovernedOperationStatus HostileStatus(string hostileText) =>
        Status() with
        {
            BlockedReasons = [hostileText],
            NextSafeActions = [hostileText],
            EvidenceRefs = [hostileText, EvidenceRef],
            ReceiptRefs = [ReceiptRef]
        };

    private static IReadOnlyCollection<FrontendTimelineEntry> TimelineEntries() =>
    [
        new()
        {
            EntryId = "entry-status",
            EventKind = "StatusObserved",
            Summary = "Status observed.",
            EvidenceRefs = [EvidenceRef],
            ReceiptRefs = [],
            ObservedAtUtc = ObservedAtUtc
        },
        new()
        {
            EntryId = "entry-recovery",
            EventKind = "RecoveryDiagnosisCreated",
            Summary = "hidden reasoning: private scratchpad",
            EvidenceRefs = [],
            ReceiptRefs = [ReceiptRef],
            ObservedAtUtc = ObservedAtUtc.AddMinutes(1)
        }
    ];

    private static FrontendPatchPackageMetadataReadModel PatchPackage() =>
        new()
        {
            PackageId = PackageId,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "main",
            RunId = "run-pr29",
            PatchHash = "sha256:patch-pr29",
            ProposedFilePaths = ["IronDev.Core/Governance/Example.cs"],
            ArtifactRefs = ["artifact:patch.diff"],
            EvidenceRefs = [EvidenceRef],
            ReceiptRefs = ["patch-package-receipt:receipt-pr29"],
            ReviewSummaryRef = "review-summary:review-pr29",
            KnownRisksRef = "known-risks:risks-pr29",
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static FrontendValidationResultMetadataReadModel ValidationResult() =>
        new()
        {
            ValidationResultId = ValidationResultId,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "main",
            RunId = "run-pr29",
            PatchHash = "sha256:patch-pr29",
            Outcome = "Passed",
            WhatRan = ["Focused PR29"],
            WhatPassed = ["Focused PR29"],
            WhatFailed = [],
            WhatWasSkipped = [],
            IsStale = false,
            EvidenceRefs = ["validation-result:validation-result-pr29"],
            ReceiptRefs = ["validation-result-package:package-pr29"],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static FrontendEvidenceMetadataReadModel Evidence(string evidenceRef, string kind) =>
        new()
        {
            EvidenceRef = evidenceRef,
            EvidenceKind = kind,
            Summary = "Reference metadata only.",
            ReferenceOnly = true,
            ContainsRawPayload = false,
            Warnings = [],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static FrontendReceiptMetadataReadModel Receipt() =>
        new()
        {
            ReceiptRef = ReceiptRef,
            ReceiptKind = "DraftPullRequest",
            Summary = "Draft PR receipt reference only.",
            ReferenceOnly = true,
            GrantsAuthority = false,
            ContinuesWorkflow = false,
            Warnings = [],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static PortableEngineeringMemoryCandidate PortableCandidate(string detail) =>
        new()
        {
            CandidateId = "candidate-pr29",
            SourceRef = "source-pr29",
            Summary = "Review heuristic",
            Detail = detail,
            SourceRepository = "BigDaddyDread-code/IronDeveloper",
            SourceProjectId = "project-pr29",
            ClaimedSanitized = true,
            EvidenceRefs = ["evidence:pr29"],
            ObservedAtUtc = ObservedAtUtc
        };

    private static FrontendReadinessApiEnvelope<T> OkEnvelope<T>(ActionResult<FrontendReadinessApiEnvelope<T>> result)
    {
        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        return (FrontendReadinessApiEnvelope<T>)ok.Value!;
    }

    private static void AssertNoAuthority(FrontendReadBoundary boundary)
    {
        Assert.IsTrue(boundary.ReadOnly);
        Assert.IsTrue(boundary.StatusOnly);
        Assert.IsFalse(boundary.CanCreateApproval);
        Assert.IsFalse(boundary.CanAcceptApproval);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
        Assert.IsFalse(boundary.CanExecute);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanRollback);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanCreatePullRequest);
        Assert.IsFalse(boundary.CanMarkReadyForReview);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
    }

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(values.Contains(expected, StringComparer.OrdinalIgnoreCase), string.Join(", ", values));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
