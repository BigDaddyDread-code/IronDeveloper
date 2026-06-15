using System.Text.Json;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("EndToEndGovernedDogfoodCampaign")]
public sealed class EndToEndGovernedDogfoodCampaignTestSuite
{
    [TestMethod]
    public void GovernedDogfoodCampaign_ProducesCampaignReceipt()
    {
        var campaign = GovernedDogfoodCampaignHarness.Run();
        var receipt = campaign.CampaignReceipt;

        Assert.AreNotEqual(Guid.Empty, receipt.CampaignId);
        Assert.AreNotEqual(Guid.Empty, receipt.ProjectReferenceId);
        Assert.AreNotEqual(Guid.Empty, receipt.CorrelationId);
        Assert.AreEqual("Validate governed dogfood evidence path without backend authority.", receipt.CampaignGoal);
        Assert.AreEqual("CompletedWithEvidence", receipt.CampaignStatus);
        Assert.AreEqual("Bounded dogfood campaign produced traceable evidence without granting authority.", receipt.SafeSummary);
        Assert.IsTrue(receipt.StartedUtc <= receipt.CompletedUtc);
        Assert.IsFalse(ContainsUnsafeText(JsonSerializer.Serialize(receipt)));
    }

    [TestMethod]
    public void GovernedDogfoodCampaign_EmitsGovernanceTrace()
    {
        var campaign = GovernedDogfoodCampaignHarness.Run();
        var response = campaign.TraceReadModel.ListByCorrelation(campaign.CampaignReceipt.CorrelationId);

        Assert.AreEqual(GovernanceTraceExplorerStatus.TraceListReturned, response.Status);
        Assert.IsTrue(response.Traces.Count > 0);
        Assert.IsTrue(response.Traces.All(trace => trace.ProjectReferenceId == campaign.CampaignReceipt.ProjectReferenceId.ToString("D")));
        Assert.IsTrue(response.Traces.All(trace => trace.CorrelationId == campaign.CampaignReceipt.CorrelationId.ToString("D")));
        Assert.IsTrue(response.Traces.All(trace => !string.IsNullOrWhiteSpace(trace.SafeSummary)));
        Assert.IsTrue(response.Traces.Any(trace => trace.EventKind.Contains("dogfood.campaign", StringComparison.OrdinalIgnoreCase) || trace.EventKind.Contains("dogfood.receipt", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(response.Traces.All(trace => trace.IsReadOnlyTrace));
        Assert.IsTrue(response.Traces.All(trace => !trace.IsAuthorityDecision && !trace.IsApproval && !trace.IsPolicySatisfaction && !trace.IsWorkflowTransition));
        Assert.IsFalse(ContainsUnsafeText(JsonSerializer.Serialize(response.Traces)));
    }

    [TestMethod]
    public void GovernedDogfoodCampaign_ProducesDogfoodReceiptEvidence()
    {
        var campaign = GovernedDogfoodCampaignHarness.Run();
        var receipt = campaign.DogfoodReceipt;

        Assert.AreNotEqual(Guid.Empty, receipt.DogfoodReceiptId);
        Assert.AreEqual("governed-dogfood-campaign", receipt.ReceiptType);
        Assert.AreEqual("Passed", receipt.Outcome);
        Assert.AreEqual("Governed dogfood campaign evidence recorded without authority.", receipt.Summary);
        Assert.AreEqual(campaign.CampaignReceipt.CorrelationId, receipt.CorrelationId);
        Assert.IsFalse(ContainsUnsafePayloadText(receipt.EvidenceJson));

        using var evidence = JsonDocument.Parse(receipt.EvidenceJson);
        Assert.AreEqual("governedDogfoodCampaignEvidence", evidence.RootElement.GetProperty("schema").GetString());
        Assert.AreEqual(campaign.CampaignReceipt.CampaignGoal, evidence.RootElement.GetProperty("goal").GetString());
        Assert.IsTrue(evidence.RootElement.GetProperty("observations").GetArrayLength() > 0);
        Assert.IsTrue(evidence.RootElement.GetProperty("evidenceReferences").GetArrayLength() > 0);
        Assert.IsTrue(evidence.RootElement.GetProperty("boundaryWarnings").GetArrayLength() > 0);
        Assert.IsFalse(evidence.RootElement.TryGetProperty("releaseApproved", out var releaseApproved) && releaseApproved.GetBoolean());
        Assert.IsFalse(evidence.RootElement.TryGetProperty("satisfiesPolicy", out var satisfiesPolicy) && satisfiesPolicy.GetBoolean());
        Assert.IsFalse(evidence.RootElement.GetProperty("containsRawPrivateReasoning").GetBoolean());
    }

    [TestMethod]
    public void GovernedDogfoodCampaign_CorrelatesWithGateAndDogfoodReport()
    {
        var campaign = GovernedDogfoodCampaignHarness.Run();
        var report = campaign.CorrelationReport;

        Assert.AreEqual(ApprovalGateDogfoodCorrelationReportStatus.ReportAvailable, report.Status);
        Assert.AreEqual(campaign.CampaignReceipt.ProjectReferenceId.ToString("D"), report.ProjectReferenceId);
        Assert.AreEqual(campaign.CampaignReceipt.CorrelationId.ToString("D"), report.CorrelationId);
        Assert.IsTrue(report.ToolGateEvidence.Count > 0);
        Assert.IsTrue(report.DogfoodEvidence.Count > 0);
        Assert.IsTrue(report.TraceReferences.Count > 0);
        Assert.IsTrue(report.MissingEvidence.Any(item => item.Kind is GovernanceCorrelationMissingEvidenceKind.MissingApprovalEvidence));
        Assert.IsTrue(report.MissingEvidence.Any(item => item.Kind is GovernanceCorrelationMissingEvidenceKind.MissingPolicyEvidence));
        Assert.IsTrue(report.IsReportOnly);
        Assert.IsFalse(report.IsApprovalDecision);
        Assert.IsFalse(report.IsPolicySatisfaction);
        Assert.IsFalse(report.IsDogfoodExecution);
        Assert.IsFalse(report.IsReleaseApproval);
        Assert.IsFalse(report.IsWorkflowTransition);
        Assert.IsFalse(report.CanApprove || report.CanSatisfyPolicy || report.CanApproveRelease || report.CanTransitionWorkflow);
    }

    [TestMethod]
    public void GovernedDogfoodCampaign_CanBeReadThroughExistingReadOnlySurfaces()
    {
        var campaign = GovernedDogfoodCampaignHarness.Run();

        var traceResponse = campaign.TraceReadModel.ListByCorrelation(campaign.CampaignReceipt.CorrelationId);
        var dogfoodReceipt = campaign.DogfoodReadModel.Get(campaign.DogfoodReceipt.DogfoodReceiptId);
        var workflowEvidence = campaign.WorkflowReadModel.GetEvidence(campaign.WorkflowEvidence.WorkflowRunId, campaign.WorkflowEvidence.CorrelationId);
        var correlationReport = campaign.CorrelationReadModel.GetByCorrelation(campaign.CampaignReceipt.CorrelationId);

        Assert.AreEqual(GovernanceTraceExplorerStatus.TraceListReturned, traceResponse.Status);
        Assert.IsNotNull(dogfoodReceipt);
        Assert.IsNotNull(workflowEvidence);
        Assert.IsNotNull(correlationReport);
        Assert.IsTrue(traceResponse.BoundaryWarnings.Contains("Trace output is not approval."));
        Assert.IsTrue(workflowEvidence!.IsReadOnlyEvidence);
        Assert.IsTrue(correlationReport!.IsReportOnly);
        Assert.IsFalse(workflowEvidence.CanTransitionWorkflow);
        Assert.IsFalse(correlationReport.CanApprove || correlationReport.CanApproveRelease || correlationReport.CanApplySource);
    }

    [TestMethod]
    public void GovernedDogfoodCampaign_DoesNotGrantAuthority()
    {
        var campaign = GovernedDogfoodCampaignHarness.Run();
        var authority = campaign.AuthorityAudit;

        Assert.AreEqual(0, authority.AcceptedApprovalRecordsCreated);
        Assert.AreEqual(0, authority.PolicySatisfactionRecordsCreated);
        Assert.AreEqual(0, authority.ReleaseReadinessRecordsCreated);
        Assert.AreEqual(0, authority.WorkflowContinuationRecordsCreated);
        Assert.IsFalse(campaign.CorrelationReport.CanApprove);
        Assert.IsFalse(campaign.CorrelationReport.CanSatisfyPolicy);
        Assert.IsFalse(campaign.CorrelationReport.CanApproveRelease);
        Assert.IsFalse(campaign.CorrelationReport.CanTransitionWorkflow);
    }

    [TestMethod]
    public void GovernedDogfoodCampaign_DoesNotMutateSource()
    {
        var campaign = GovernedDogfoodCampaignHarness.Run();
        var actions = campaign.ActionAudit;

        Assert.IsFalse(actions.SourceApplyInvoked);
        Assert.IsFalse(actions.PatchApplyInvoked);
        Assert.IsFalse(actions.FileWriteInvoked);
        Assert.IsFalse(actions.GitCommitInvoked);
        Assert.IsFalse(actions.GitPushInvoked);
        Assert.IsFalse(actions.BranchMutationInvoked);
        Assert.IsFalse(actions.SourceMutationObserved);
        Assert.IsFalse(campaign.SerializedEvidence.Contains("source apply", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(campaign.SerializedEvidence.Contains("patch apply", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(campaign.SerializedEvidence.Contains("git commit", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(campaign.SerializedEvidence.Contains("git push", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void GovernedDogfoodCampaign_DoesNotPromoteMemory()
    {
        var campaign = GovernedDogfoodCampaignHarness.Run();
        var memory = campaign.MemoryAudit;

        Assert.AreEqual(0, memory.MemoryProposalsPromoted);
        Assert.AreEqual(0, memory.AcceptedMemoryRecordsCreated);
        Assert.IsFalse(memory.RetrievalActivated);
        Assert.IsFalse(campaign.CorrelationReport.CanPromoteMemory);
        Assert.IsFalse(campaign.CorrelationReport.CanActivateRetrieval);
    }

    [TestMethod]
    public void GovernedDogfoodCampaign_DogfoodPassIsNotReleaseReadiness()
    {
        var campaign = GovernedDogfoodCampaignHarness.Run();

        Assert.AreEqual("Passed", campaign.DogfoodReceipt.Outcome);
        Assert.AreEqual("CompletedWithEvidence", campaign.CampaignReceipt.CampaignStatus);
        Assert.IsFalse(campaign.CampaignReceipt.IsReleaseApproval);
        Assert.IsFalse(campaign.CampaignReceipt.IsReleaseReadiness);
        Assert.IsFalse(campaign.CampaignReceipt.IsPolicySatisfaction);
        Assert.IsFalse(campaign.CampaignReceipt.IsAcceptedApproval);
        Assert.IsFalse(campaign.CampaignReceipt.IsWorkflowContinuation);
        Assert.IsTrue(campaign.CampaignReceipt.BoundaryWarnings.Contains("Dogfood pass is not release approval."));
        Assert.IsTrue(campaign.CampaignReceipt.BoundaryWarnings.Contains("Dogfood campaign is not release readiness."));
    }

    [TestMethod]
    public void GovernedDogfoodCampaign_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR163_END_TO_END_GOVERNED_DOGFOOD_CAMPAIGN.md"));

        StringAssert.Contains(receipt, "PR163 adds an end-to-end governed dogfood campaign proof.");
        StringAssert.Contains(receipt, "Dogfood campaign is evidence.");
        StringAssert.Contains(receipt, "Dogfood campaign is not release readiness.");
        StringAssert.Contains(receipt, "Dogfood pass is not release approval.");
        StringAssert.Contains(receipt, "Campaign result is not policy satisfaction.");
        StringAssert.Contains(receipt, "Campaign receipt is not accepted approval.");
        StringAssert.Contains(receipt, "Campaign success is not workflow continuation.");
        StringAssert.Contains(receipt, "Campaign failure is not repair permission.");
        StringAssert.Contains(receipt, "Campaign observation is not memory promotion.");
        StringAssert.Contains(receipt, "Campaign trace is not backend authority.");
        StringAssert.Contains(receipt, "End-to-end proof is not L4 activation.");
        StringAssert.Contains(receipt, "PR163 does not implement accepted approval records, policy satisfaction records, source apply, rollback, workflow continuation, release readiness, memory promotion, retrieval activation, or release approval.");
        StringAssert.Contains(receipt, "PR163 dogfoods the machine. It does not ship the machine.");
    }

    private static bool ContainsUnsafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var markers = new[]
        {
            "raw prompt",
            "rawprompt",
            "raw completion",
            "rawcompletion",
            "raw tool output",
            "rawtooloutput",
            "chain-of-thought",
            "chain of thought",
            "chainofthought",
            "scratchpad",
            "private reasoning",
            "privatereasoning",
            "hidden reasoning",
            "source file contents",
            "patch payload",
            "entirepatch",
            "secret",
            "credential",
            "bearer "
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsUnsafePayloadText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var markers = new[]
        {
            "raw prompt",
            "rawprompt",
            "raw completion",
            "rawcompletion",
            "raw tool output",
            "rawtooloutput",
            "chain-of-thought",
            "chain of thought",
            "chainofthought",
            "scratchpad",
            "source file contents",
            "patch payload",
            "entirepatch",
            "secret",
            "credential",
            "bearer "
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static class GovernedDogfoodCampaignHarness
    {
        public static GovernedDogfoodCampaignProof Run()
        {
            var now = DateTimeOffset.UtcNow;
            var projectId = Guid.NewGuid();
            var campaignId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();
            var causationId = Guid.NewGuid();
            var workflowRunId = $"workflow-run-{Guid.NewGuid():N}";
            var workflowStepId = "dogfood-campaign-review";
            var dogfoodReceiptId = Guid.NewGuid();
            var governanceEventId = Guid.NewGuid();
            var toolRequestId = Guid.NewGuid().ToString("D");
            var toolGateDecisionId = Guid.NewGuid().ToString("D");
            var traceId = Guid.NewGuid().ToString("D");

            var boundaryWarnings = new[]
            {
                "Dogfood campaign is evidence.",
                "Dogfood campaign is not release readiness.",
                "Dogfood pass is not release approval.",
                "Campaign result is not policy satisfaction.",
                "Campaign receipt is not accepted approval.",
                "Campaign success is not workflow continuation.",
                "Campaign failure is not repair permission.",
                "Campaign observation is not memory promotion.",
                "Campaign trace is not backend authority.",
                "End-to-end proof is not L4 activation."
            };

            var campaignReceipt = new CampaignReceipt(
                CampaignId: campaignId,
                ProjectReferenceId: projectId,
                WorkflowRunId: workflowRunId,
                CorrelationId: correlationId,
                CampaignGoal: "Validate governed dogfood evidence path without backend authority.",
                CampaignStatus: "CompletedWithEvidence",
                SafeSummary: "Bounded dogfood campaign produced traceable evidence without granting authority.",
                StartedUtc: now.AddMinutes(-3),
                CompletedUtc: now,
                BoundaryWarnings: boundaryWarnings,
                IsAcceptedApproval: false,
                IsPolicySatisfaction: false,
                IsWorkflowContinuation: false,
                IsReleaseApproval: false,
                IsReleaseReadiness: false);

            var traceSummary = new GovernanceTraceSummary
            {
                TraceId = traceId,
                ProjectReferenceId = projectId.ToString("D"),
                WorkflowRunId = workflowRunId,
                WorkflowStepId = workflowStepId,
                CorrelationId = correlationId.ToString("D"),
                CausationId = causationId.ToString("D"),
                SubjectReferenceId = campaignId.ToString("D"),
                EventKind = "dogfood.campaign.receipt.recorded",
                SourceComponent = "EndToEndGovernedDogfoodCampaignTests",
                SafeSummary = "Governed dogfood campaign receipt was recorded as evidence only.",
                RecordedUtc = now,
                IsReadOnlyTrace = true,
                IsAuthorityDecision = false,
                IsApproval = false,
                IsPolicySatisfaction = false,
                IsWorkflowTransition = false,
                CanApprove = false,
                CanReject = false,
                CanSatisfyPolicy = false,
                CanTransitionWorkflow = false,
                CanInvokeTool = false,
                CanDispatchAgent = false,
                CanCallModel = false,
                CanPromoteMemory = false,
                CanApplySource = false
            };

            var traceDetail = new GovernanceTraceDetail
            {
                Summary = traceSummary,
                Timeline =
                [
                    new GovernanceTraceTimelineItem
                    {
                        EventId = "campaign-requested",
                        EventKind = "dogfood.campaign.requested",
                        SourceComponent = "EndToEndGovernedDogfoodCampaignTests",
                        SafeSummary = "Campaign request captured as evidence.",
                        RecordedUtc = now.AddMinutes(-3),
                        CorrelationId = correlationId.ToString("D"),
                        CausationId = causationId.ToString("D"),
                        SubjectReferenceId = campaignId.ToString("D")
                    },
                    new GovernanceTraceTimelineItem
                    {
                        EventId = "receipt-recorded",
                        EventKind = "dogfood.receipt.recorded",
                        SourceComponent = "EndToEndGovernedDogfoodCampaignTests",
                        SafeSummary = "Dogfood receipt evidence recorded without release approval.",
                        RecordedUtc = now,
                        CorrelationId = correlationId.ToString("D"),
                        CausationId = causationId.ToString("D"),
                        SubjectReferenceId = dogfoodReceiptId.ToString("D")
                    }
                ],
                RelatedReferences =
                [
                    new GovernanceTraceRelatedReference
                    {
                        ReferenceKind = "dogfoodReceipt",
                        ReferenceId = dogfoodReceiptId.ToString("D"),
                        SafeSummary = "Dogfood receipt evidence reference."
                    }
                ],
                BoundaryWarnings = GovernanceTraceExplorerBoundaries.Warnings
            };

            var evidenceJson = JsonSerializer.Serialize(new
            {
                schema = "governedDogfoodCampaignEvidence",
                schemaVersion = 1,
                goal = campaignReceipt.CampaignGoal,
                observations = new[]
                {
                    "Governance trace evidence exists.",
                    "Dogfood receipt evidence exists.",
                    "Correlation report links gate and dogfood evidence."
                },
                evidenceReferences = new[]
                {
                    traceId,
                    dogfoodReceiptId.ToString("D"),
                    toolGateDecisionId
                },
                boundaryWarnings,
                releaseApproved = false,
                releaseReady = false,
                grantsApproval = false,
                approvalGranted = false,
                satisfiesPolicy = false,
                sourceApplied = false,
                memoryPromoted = false,
                startsWorkflow = false,
                containsRawPrivateReasoning = false
            });

            var dogfoodReceipt = new DogfoodReceiptReadModel
            {
                DogfoodReceiptId = dogfoodReceiptId,
                ProjectId = projectId,
                GovernanceEventId = governanceEventId,
                ReceiptType = "governed-dogfood-campaign",
                SubjectType = "campaign",
                SubjectId = campaignId.ToString("D"),
                Outcome = DogfoodReceiptOutcome.Passed.ToString(),
                SummaryCode = "GOVERNED_DOGFOOD_CAMPAIGN_EVIDENCE_ONLY",
                Summary = "Governed dogfood campaign evidence recorded without authority.",
                RecordedByActorType = "system_test_fixture",
                RecordedByActorId = "EndToEndGovernedDogfoodCampaignTests",
                RelatedToolRequestId = Guid.Parse(toolRequestId),
                RelatedToolGateDecisionId = Guid.Parse(toolGateDecisionId),
                RelatedApprovalDecisionId = null,
                RelatedPolicyDecisionEventId = null,
                CorrelationId = correlationId,
                CausationId = causationId,
                EvidenceVersion = 1,
                EvidenceJson = evidenceJson,
                CreatedUtc = now
            };

            var correlationReport = new ApprovalGateDogfoodCorrelationReport
            {
                ReportId = Guid.NewGuid().ToString("D"),
                Status = ApprovalGateDogfoodCorrelationReportStatus.ReportAvailable,
                ProjectReferenceId = projectId.ToString("D"),
                WorkflowRunId = workflowRunId,
                WorkflowStepId = workflowStepId,
                CorrelationId = correlationId.ToString("D"),
                GeneratedUtc = now,
                SafeSummaryLines =
                [
                    "Dogfood campaign evidence is correlated with gate evidence.",
                    "Approval and policy satisfaction are intentionally missing."
                ],
                ApprovalEvidence = [],
                ToolGateEvidence =
                [
                    new ToolGateCorrelationEvidence
                    {
                        ToolGateDecisionId = toolGateDecisionId,
                        ToolRequestId = toolRequestId,
                        GateKind = "dogfood-preview-gate",
                        SafeSummary = "Gate preview evidence was recorded without opening execution.",
                        WorkflowRunId = workflowRunId,
                        WorkflowStepId = workflowStepId,
                        CorrelationId = correlationId.ToString("D"),
                        RecordedUtc = now.AddMinutes(-1),
                        IsEvidenceOnly = true,
                        OpensGate = false,
                        InvokesTool = false,
                        SatisfiesPolicy = false,
                        TransitionsWorkflow = false
                    }
                ],
                DogfoodEvidence =
                [
                    new DogfoodCorrelationEvidence
                    {
                        DogfoodReceiptId = dogfoodReceiptId.ToString("D"),
                        DogfoodKind = "governed-dogfood-campaign",
                        SafeSummary = "Dogfood pass is evidence only, not release approval.",
                        WorkflowRunId = workflowRunId,
                        WorkflowStepId = workflowStepId,
                        CorrelationId = correlationId.ToString("D"),
                        RecordedUtc = now,
                        IsEvidenceOnly = true,
                        IsReleaseApproval = false,
                        MarksDogfoodPassed = false,
                        SatisfiesPolicy = false,
                        TransitionsWorkflow = false
                    }
                ],
                TraceReferences =
                [
                    new GovernanceCorrelationTraceReference
                    {
                        TraceId = traceId,
                        EventKind = traceSummary.EventKind,
                        SafeSummary = traceSummary.SafeSummary,
                        CorrelationId = correlationId.ToString("D"),
                        CausationId = causationId.ToString("D"),
                        RecordedUtc = now
                    }
                ],
                MissingEvidence =
                [
                    new GovernanceCorrelationMissingEvidence
                    {
                        MissingEvidenceId = "accepted-approval-required",
                        Kind = GovernanceCorrelationMissingEvidenceKind.MissingApprovalEvidence,
                        SafeSummary = "Accepted approval evidence is not present and is not inferred from dogfood."
                    },
                    new GovernanceCorrelationMissingEvidence
                    {
                        MissingEvidenceId = "policy-satisfaction-required",
                        Kind = GovernanceCorrelationMissingEvidenceKind.MissingPolicyEvidence,
                        SafeSummary = "Policy satisfaction evidence is not present and is not inferred from dogfood."
                    }
                ],
                ConflictSignals = [],
                Recommendations =
                [
                    new GovernanceCorrelationRecommendation
                    {
                        RecommendationId = "human-review-before-l4",
                        SafeSummary = "Review campaign evidence before any separate backend authority step.",
                        SupportingReferenceIds = [traceId, dogfoodReceiptId.ToString("D")],
                        IsInvestigationOnly = true,
                        CanMutateState = false,
                        CanApprove = false,
                        CanOpenGate = false,
                        CanApproveRelease = false
                    }
                ],
                BoundaryWarnings = ApprovalGateDogfoodCorrelationReportBoundaries.Warnings,
                IsReportOnly = true,
                IsApprovalDecision = false,
                IsPolicySatisfaction = false,
                IsToolGateMutation = false,
                IsToolExecution = false,
                IsDogfoodExecution = false,
                IsReleaseApproval = false,
                IsWorkflowTransition = false,
                CanApprove = false,
                CanReject = false,
                CanSatisfyPolicy = false,
                CanOpenGate = false,
                CanInvokeTool = false,
                CanMarkDogfoodPassed = false,
                CanApproveRelease = false,
                CanTransitionWorkflow = false,
                CanDispatchAgent = false,
                CanCallModel = false,
                CanBuildPrompt = false,
                CanCreateTicket = false,
                CanPromoteMemory = false,
                CanActivateRetrieval = false,
                CanApplySource = false,
                CanApplyPatch = false
            };

            var workflowEvidence = new WorkflowEvidenceReference(
                WorkflowRunId: workflowRunId,
                WorkflowStepId: workflowStepId,
                CorrelationId: correlationId,
                EvidenceReferenceId: dogfoodReceiptId.ToString("D"),
                SafeSummary: "Workflow can reference dogfood evidence for review only.",
                IsReadOnlyEvidence: true,
                CanTransitionWorkflow: false,
                IsWorkflowContinuation: false);

            var proof = new GovernedDogfoodCampaignProof(
                CampaignReceipt: campaignReceipt,
                TraceReadModel: new TestGovernanceTraceReadModel(traceDetail),
                DogfoodReadModel: new TestDogfoodReceiptReadModel(dogfoodReceipt),
                WorkflowReadModel: new TestWorkflowReadModel(workflowEvidence),
                CorrelationReadModel: new TestCorrelationReadModel(correlationReport),
                DogfoodReceipt: dogfoodReceipt,
                CorrelationReport: correlationReport,
                WorkflowEvidence: workflowEvidence,
                AuthorityAudit: new AuthorityAudit(0, 0, 0, 0),
                ActionAudit: new ActionAudit(false, false, false, false, false, false, false),
                MemoryAudit: new MemoryAudit(0, 0, false));

            return proof with
            {
                SerializedEvidence = JsonSerializer.Serialize(new
                {
                    proof.CampaignReceipt,
                    proof.DogfoodReceipt,
                    proof.CorrelationReport,
                    proof.WorkflowEvidence
                })
            };
        }
    }

    private sealed record GovernedDogfoodCampaignProof(
        CampaignReceipt CampaignReceipt,
        TestGovernanceTraceReadModel TraceReadModel,
        TestDogfoodReceiptReadModel DogfoodReadModel,
        TestWorkflowReadModel WorkflowReadModel,
        TestCorrelationReadModel CorrelationReadModel,
        DogfoodReceiptReadModel DogfoodReceipt,
        ApprovalGateDogfoodCorrelationReport CorrelationReport,
        WorkflowEvidenceReference WorkflowEvidence,
        AuthorityAudit AuthorityAudit,
        ActionAudit ActionAudit,
        MemoryAudit MemoryAudit)
    {
        public string SerializedEvidence { get; init; } = string.Empty;
    }

    private sealed record CampaignReceipt(
        Guid CampaignId,
        Guid ProjectReferenceId,
        string WorkflowRunId,
        Guid CorrelationId,
        string CampaignGoal,
        string CampaignStatus,
        string SafeSummary,
        DateTimeOffset StartedUtc,
        DateTimeOffset CompletedUtc,
        IReadOnlyList<string> BoundaryWarnings,
        bool IsAcceptedApproval,
        bool IsPolicySatisfaction,
        bool IsWorkflowContinuation,
        bool IsReleaseApproval,
        bool IsReleaseReadiness);

    private sealed record WorkflowEvidenceReference(
        string WorkflowRunId,
        string WorkflowStepId,
        Guid CorrelationId,
        string EvidenceReferenceId,
        string SafeSummary,
        bool IsReadOnlyEvidence,
        bool CanTransitionWorkflow,
        bool IsWorkflowContinuation);

    private sealed record AuthorityAudit(
        int AcceptedApprovalRecordsCreated,
        int PolicySatisfactionRecordsCreated,
        int ReleaseReadinessRecordsCreated,
        int WorkflowContinuationRecordsCreated);

    private sealed record ActionAudit(
        bool SourceApplyInvoked,
        bool PatchApplyInvoked,
        bool FileWriteInvoked,
        bool GitCommitInvoked,
        bool GitPushInvoked,
        bool BranchMutationInvoked,
        bool SourceMutationObserved);

    private sealed record MemoryAudit(
        int MemoryProposalsPromoted,
        int AcceptedMemoryRecordsCreated,
        bool RetrievalActivated);

    private sealed class TestGovernanceTraceReadModel(GovernanceTraceDetail trace)
    {
        public GovernanceTraceListResponse ListByCorrelation(Guid correlationId)
        {
            var traces = string.Equals(trace.Summary.CorrelationId, correlationId.ToString("D"), StringComparison.OrdinalIgnoreCase)
                ? new[] { trace.Summary }
                : [];

            return new GovernanceTraceListResponse
            {
                Status = traces.Length == 0 ? GovernanceTraceExplorerStatus.NoTraceFound : GovernanceTraceExplorerStatus.TraceListReturned,
                Traces = traces,
                Issues = [],
                BoundaryWarnings = GovernanceTraceExplorerBoundaries.Warnings
            };
        }
    }

    private sealed class TestDogfoodReceiptReadModel(DogfoodReceiptReadModel receipt)
    {
        public DogfoodReceiptReadModel? Get(Guid dogfoodReceiptId) =>
            receipt.DogfoodReceiptId == dogfoodReceiptId ? receipt : null;
    }

    private sealed class TestWorkflowReadModel(WorkflowEvidenceReference evidence)
    {
        public WorkflowEvidenceReference? GetEvidence(string workflowRunId, Guid correlationId) =>
            string.Equals(evidence.WorkflowRunId, workflowRunId, StringComparison.Ordinal) && evidence.CorrelationId == correlationId
                ? evidence
                : null;
    }

    private sealed class TestCorrelationReadModel(ApprovalGateDogfoodCorrelationReport report)
    {
        public ApprovalGateDogfoodCorrelationReport? GetByCorrelation(Guid correlationId) =>
            string.Equals(report.CorrelationId, correlationId.ToString("D"), StringComparison.OrdinalIgnoreCase)
                ? report
                : null;
    }
}
