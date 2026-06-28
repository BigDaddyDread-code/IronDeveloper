namespace IronDev.UnitTests.Release;

[TestClass]
public sealed class ReleaseReadinessGateEvaluatorUnitTests
{
    [TestMethod]
    public void CompleteEvidenceMapsToReadyEvidenceSatisfied()
    {
        var decision = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate();

        ReleaseReadinessGateEvaluatorTestFixtures.AssertReady(decision);
        Assert.AreEqual(ReleaseReadinessGateEvaluatorTestFixtures.ReleaseReadinessGateRequestId, decision.ReleaseReadinessDecisionRecordId);
        Assert.AreEqual(ReleaseReadinessGateEvaluatorTestFixtures.ProjectId, decision.ProjectId);
        Assert.AreEqual(ReleaseReadinessGateEvaluatorTestFixtures.ReleaseReadinessReportId, decision.ReleaseReadinessReportId);
        Assert.AreEqual(ReleaseReadinessGateEvaluatorTestFixtures.WorkflowRunId, decision.WorkflowRunId);
        Assert.AreEqual(ReleaseReadinessGateEvaluatorTestFixtures.SubjectKind, decision.SubjectKind);
    }

    [TestMethod]
    public void ReadyEvidenceSatisfiedAddsHumanReviewRequiredWarning()
    {
        var decision = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate();

        ReleaseReadinessGateEvaluatorTestFixtures.AssertReason(decision, "HumanReviewRequiredForReleaseApproval");
        Assert.IsTrue(decision.HumanReviewRequiredForReleaseApproval);
        Assert.IsTrue(decision.HumanReviewRequiredForDeployment);
        Assert.IsTrue(decision.HumanReviewRequiredForMerge);
    }

    [TestMethod]
    public void ReadyDecisionRecordKeepsReleaseDeploymentMergeAndExecutionFlagsFalse()
    {
        var decision = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate();

        ReleaseReadinessGateEvaluatorTestFixtures.AssertAllAuthorityFalse(decision);
    }

    [TestMethod]
    public void ReadyDecisionRecordIncludesEvidenceReferencesAndBoundaryMaxims()
    {
        var decision = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate();

        CollectionAssert.Contains(decision.EvidenceReferences.ToList(), "release-readiness-report:g06");
        CollectionAssert.Contains(decision.EvidenceReferences.ToList(), "approval:evidence:g06");
        CollectionAssert.Contains(decision.BoundaryMaxims.ToList(), "Release readiness gate evaluator is not release approval.");
        CollectionAssert.Contains(decision.BoundaryMaxims.ToList(), "Human review remains required for release approval, deployment, and merge.");
    }

    [TestMethod]
    public void ReadyDecisionRecordHashIsDeterministic()
    {
        var first = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate();
        var second = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate();

        Assert.AreEqual(first.ReleaseReadinessDecisionRecordHash, second.ReleaseReadinessDecisionRecordHash);
        Assert.AreEqual(ReleaseReadinessDecisionRecordHashing.ComputeHash(first), first.ReleaseReadinessDecisionRecordHash);
    }

    [TestMethod]
    public void NullRequestBlocksByMissingEvidence()
    {
        var decision = new ReleaseReadinessGateEvaluator().Evaluate(null);

        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
            decision,
            ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence,
            "ReportBlockedByMissingEvidence");
        ReleaseReadinessGateEvaluatorTestFixtures.AssertReason(decision, "ReportRequired");
        ReleaseReadinessGateEvaluatorTestFixtures.AssertAllAuthorityFalse(decision);
    }

    [TestMethod]
    public void MissingReportBlocksByMissingEvidence()
    {
        var decision = new ReleaseReadinessGateEvaluator().Evaluate(
            ReleaseReadinessGateEvaluatorTestFixtures.Request(report: null!) with { ReleaseReadinessReport = null! });

        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
            decision,
            ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence,
            "ReportRequired");
    }

    [TestMethod]
    public void RequestShapeMissingValuesBlockByMissingEvidence()
    {
        var cases = new (string Reason, Func<ReleaseReadinessGateRequest, ReleaseReadinessGateRequest> Mutate)[]
        {
            ("ReleaseReadinessGateRequestIdRequired", request => request with { ReleaseReadinessGateRequestId = Guid.Empty }),
            ("ProjectIdRequired", request => request with { ProjectId = Guid.Empty }),
            ("RequestedAtRequired", request => request with { RequestedAtUtc = default }),
            ("GateEvidenceReferencesRequired", request => request with { EvidenceReferences = [] }),
            ("GateBoundaryMaximsRequired", request => request with { BoundaryMaxims = [] }),
            ("GateBoundaryRequired", request => request with { Boundary = "" })
        };

        foreach (var (reason, mutate) in cases)
        {
            var decision = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(mutate);

            ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
                decision,
                ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence,
                reason);
        }
    }

    [TestMethod]
    public void ProjectMismatchBlocksByFailedEvidence()
    {
        var report = ReleaseReadinessGateEvaluatorTestFixtures.CompleteReport();
        var decision = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
        {
            ProjectId = Guid.Parse("48ebf499-7702-4307-8a75-26819194b602"),
            ReleaseReadinessReport = report
        });

        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
            decision,
            ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence,
            "ReportProjectMismatch");
    }

    [TestMethod]
    public void InvalidReportShapeAndHashMismatchBlockByFailedEvidence()
    {
        var invalid = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
        {
            ReleaseReadinessReport = ReleaseReadinessGateEvaluatorTestFixtures.Rehash(
                request.ReleaseReadinessReport with { HumanReviewRequiredForReadiness = false })
        });
        var hashMismatch = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
        {
            ReleaseReadinessReport = request.ReleaseReadinessReport with { WorkflowRunId = "workflow:changed-without-rehash" }
        });

        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
            invalid,
            ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence,
            "ReportValidation.HumanReviewForReadinessRequired");
        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
            hashMismatch,
            ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence,
            "ReportValidation.ReportHashMismatch");
        ReleaseReadinessGateEvaluatorTestFixtures.AssertReason(hashMismatch, "ReportHashMismatch");
    }

    [TestMethod]
    public void ReportStatusMapsToMissingOrFailedEvidenceDecisions()
    {
        var missing = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
        {
            ReleaseReadinessReport = ReleaseReadinessGateEvaluatorTestFixtures.Rehash(
                request.ReleaseReadinessReport with { Status = ReleaseReadinessReportStatuses.BlockedByMissingEvidence })
        });
        var failed = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
        {
            ReleaseReadinessReport = ReleaseReadinessGateEvaluatorTestFixtures.Rehash(
                request.ReleaseReadinessReport with { Status = ReleaseReadinessReportStatuses.BlockedByFailedEvidence })
        });
        var unsupported = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
        {
            ReleaseReadinessReport = ReleaseReadinessGateEvaluatorTestFixtures.Rehash(
                request.ReleaseReadinessReport with { Status = "Unsupported" })
        });

        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
            missing,
            ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence,
            "ReportBlockedByMissingEvidence");
        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
            failed,
            ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence,
            "ReportBlockedByFailedEvidence");
        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
            unsupported,
            ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence,
            "ReportStatusUnsupported");
    }

    [TestMethod]
    public void BlockingFindingBlocksByFailedEvidence()
    {
        var decision = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
        {
            ReleaseReadinessReport = ReleaseReadinessGateEvaluatorTestFixtures.Rehash(
                request.ReleaseReadinessReport with
                {
                    Findings =
                    [
                        ReleaseReadinessGateEvaluatorTestFixtures.Finding(
                            "ReleaseEvidenceBlocked",
                            ReleaseReadinessFindingSeverities.Blocking,
                            "Evidence review blocked.")
                    ]
                })
        });

        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
            decision,
            ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence,
            "ReportHasBlockingFindings");
    }

    [TestMethod]
    public void RequiredApprovalAndPolicyEvidenceMustBePresent()
    {
        var missingApproval = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
        {
            ReleaseReadinessReport = ReleaseReadinessGateEvaluatorTestFixtures.Rehash(
                request.ReleaseReadinessReport with { ApprovalEvidencePresent = false })
        });
        var missingPolicy = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
        {
            ReleaseReadinessReport = ReleaseReadinessGateEvaluatorTestFixtures.Rehash(
                request.ReleaseReadinessReport with { PolicyEvidencePresent = false })
        });

        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
            missingApproval,
            ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence,
            "ApprovalEvidenceMissing");
        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
            missingPolicy,
            ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence,
            "PolicyEvidenceMissing");
    }

    [TestMethod]
    public void SourceApplyFailureRequiresSuccessfulRollbackRecovery()
    {
        var failedApply = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
        {
            ReleaseReadinessReport = ReleaseReadinessGateEvaluatorTestFixtures.Rehash(
                request.ReleaseReadinessReport with { SourceApplySucceeded = false })
        });
        var partialApply = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
        {
            ReleaseReadinessReport = ReleaseReadinessGateEvaluatorTestFixtures.Rehash(
                request.ReleaseReadinessReport with { SourceApplySucceeded = false, SourceApplyPartial = true })
        });
        var recovered = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
        {
            ReleaseReadinessReport = ReleaseReadinessGateEvaluatorTestFixtures.Rehash(
                request.ReleaseReadinessReport with
                {
                    SourceApplySucceeded = false,
                    SourceApplyPartial = false,
                    RollbackWasExecuted = true,
                    RollbackSucceeded = true,
                    RollbackPartial = false,
                    RollbackAuditConsistent = true,
                    RollbackExecutionReceiptId = ReleaseReadinessGateEvaluatorTestFixtures.RollbackExecutionReceiptId,
                    RollbackExecutionReceiptHash = ReleaseReadinessGateEvaluatorTestFixtures.RollbackExecutionReceiptHash,
                    RollbackExecutionAuditReportId = ReleaseReadinessGateEvaluatorTestFixtures.RollbackExecutionAuditReportId,
                    RollbackExecutionAuditReportHash = ReleaseReadinessGateEvaluatorTestFixtures.RollbackExecutionAuditReportHash
                })
        });

        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
            failedApply,
            ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence,
            "FailedSourceApplyWithoutRollbackRecovery");
        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
            partialApply,
            ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence,
            "PartialSourceApplyWithoutRollbackRecovery");
        ReleaseReadinessGateEvaluatorTestFixtures.AssertReady(recovered);
        ReleaseReadinessGateEvaluatorTestFixtures.AssertReason(recovered, "RollbackRecoveryEvidenceSatisfied");
    }

    [TestMethod]
    public void FailedPartialOrInconsistentRollbackEvidenceBlocks()
    {
        var cases = new (string Name, Func<ReleaseReadinessReport, ReleaseReadinessReport> Mutate)[]
        {
            ("failed", report => report with { RollbackWasExecuted = true, RollbackSucceeded = false, RollbackPartial = false, RollbackAuditConsistent = true }),
            ("partial", report => report with { RollbackWasExecuted = true, RollbackSucceeded = true, RollbackPartial = true, RollbackAuditConsistent = true }),
            ("audit", report => report with { RollbackWasExecuted = true, RollbackSucceeded = true, RollbackPartial = false, RollbackAuditConsistent = false })
        };

        foreach (var (_, mutate) in cases)
        {
            var decision = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
            {
                ReleaseReadinessReport = ReleaseReadinessGateEvaluatorTestFixtures.Rehash(mutate(request.ReleaseReadinessReport))
            });

            ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
                decision,
                ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence,
                "RollbackRecoveryEvidenceFailed");
        }
    }

    [TestMethod]
    public void WorkflowContinuationAndTransitionEvidenceMustBeSatisfied()
    {
        var continuation = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
        {
            ReleaseReadinessReport = ReleaseReadinessGateEvaluatorTestFixtures.Rehash(
                request.ReleaseReadinessReport with { WorkflowContinuationSucceeded = false })
        });
        var transition = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
        {
            ReleaseReadinessReport = ReleaseReadinessGateEvaluatorTestFixtures.Rehash(
                request.ReleaseReadinessReport with { WorkflowTransitionRecordValid = false })
        });

        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
            continuation,
            ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence,
            "WorkflowContinuationEvidenceUnsatisfied");
        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
            transition,
            ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence,
            "WorkflowTransitionEvidenceInvalid");
    }

    [TestMethod]
    public void ReportAuthorityAndExecutionClaimsBlockByFailedEvidence()
    {
        var cases = new (string Reason, Func<ReleaseReadinessReport, ReleaseReadinessReport> Mutate)[]
        {
            ("ReportClaimsReleaseReadinessDecision", report => report with { ReleaseReadinessDecided = true }),
            ("ReportClaimsReleaseAuthority", report => report with { ReleaseReady = true }),
            ("ReportClaimsReleaseAuthority", report => report with { ReleaseApproved = true }),
            ("ReportClaimsReleaseAuthority", report => report with { DeploymentApproved = true }),
            ("ReportClaimsReleaseAuthority", report => report with { MergeApproved = true }),
            ("ReportClaimsExecutionAuthority", report => report with { SourceApplyExecutedByReport = true }),
            ("ReportClaimsExecutionAuthority", report => report with { RollbackExecutedByReport = true }),
            ("ReportClaimsExecutionAuthority", report => report with { WorkflowMutatedByReport = true }),
            ("ReportClaimsExecutionAuthority", report => report with { GitOperationExecutedByReport = true })
        };

        foreach (var (reason, mutate) in cases)
        {
            var decision = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
            {
                ReleaseReadinessReport = ReleaseReadinessGateEvaluatorTestFixtures.Rehash(mutate(request.ReleaseReadinessReport))
            });

            ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(
                decision,
                ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence,
                reason);
            ReleaseReadinessGateEvaluatorTestFixtures.AssertAllAuthorityFalse(decision);
        }
    }

    [TestMethod]
    public void PrivateRawAndAuthorityTextAreRejectedButSafeNegativeAuthorityTextIsAllowed()
    {
        var rawBoundary = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with { Boundary = "raw prompt material" });
        var rawEvidence = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with { EvidenceReferences = ["raw prompt material"] });
        var rawFinding = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
        {
            ReleaseReadinessReport = ReleaseReadinessGateEvaluatorTestFixtures.Rehash(request.ReleaseReadinessReport with
            {
                Findings = [ReleaseReadinessGateEvaluatorTestFixtures.Finding("RawMaterialFinding", ReleaseReadinessFindingSeverities.Info, "raw prompt material")]
            })
        });
        var authorityBoundary = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with { Boundary = "release approved by gate" });
        var authorityEvidence = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with { EvidenceReferences = ["release approved by gate"] });
        var authorityFinding = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
        {
            ReleaseReadinessReport = ReleaseReadinessGateEvaluatorTestFixtures.Rehash(request.ReleaseReadinessReport with
            {
                Findings = [ReleaseReadinessGateEvaluatorTestFixtures.Finding("AuthorityFinding", ReleaseReadinessFindingSeverities.Info, "release approved by gate")]
            })
        });
        var safeNegative = ReleaseReadinessGateEvaluatorTestFixtures.Evaluate(request => request with
        {
            Boundary = "This gate is not release approved.",
            EvidenceReferences = ["release-readiness-report:g06", "this evidence is not release approved"],
            BoundaryMaxims = ["release readiness evidence is not release approved"],
            ReleaseReadinessReport = ReleaseReadinessGateEvaluatorTestFixtures.Rehash(request.ReleaseReadinessReport with
            {
                Findings = [ReleaseReadinessGateEvaluatorTestFixtures.Finding("SafeNegativeAuthority", ReleaseReadinessFindingSeverities.Info, "this report is not release approved")]
            })
        });

        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(rawBoundary, ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence, "PrivateRawMaterialRejected");
        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(rawEvidence, ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence, "PrivateRawMaterialRejected");
        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(rawFinding, ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence, "PrivateRawMaterialRejected");
        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(authorityBoundary, ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence, "AuthorityClaimRejected");
        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(authorityEvidence, ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence, "AuthorityClaimRejected");
        ReleaseReadinessGateEvaluatorTestFixtures.AssertBlocked(authorityFinding, ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence, "AuthorityClaimRejected");
        ReleaseReadinessGateEvaluatorTestFixtures.AssertReady(safeNegative);
    }

    [TestMethod]
    public void ReleaseUnitTestsRemainFastLaneAndDependencyClean()
    {
        var root = ReleaseReadinessGateEvaluatorTestFixtures.RepoRoot();
        var projectPath = Path.Combine(root, "IronDev.UnitTests", "IronDev.UnitTests.csproj");
        var releaseTestFiles = Directory.GetFiles(Path.Combine(root, "IronDev.UnitTests", "Release"), "*.cs");
        var combinedReleaseTests = string.Join(Environment.NewLine, releaseTestFiles.Select(File.ReadAllText));
        var project = XDocument.Load(projectPath);

        var projectReferences = project.Descendants("ProjectReference")
            .Select(static reference => reference.Attribute("Include")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var packageReferences = project.Descendants("PackageReference")
            .Select(static reference => reference.Attribute("Include")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        CollectionAssert.AreEqual(new[] { @"..\IronDev.Core\IronDev.Core.csproj" }, projectReferences);
        CollectionAssert.AreEquivalent(
            new[] { "Microsoft.NET.Test.Sdk", "MSTest.TestAdapter", "MSTest.TestFramework" },
            packageReferences);

        foreach (var forbidden in ForbiddenFastLaneDependencyTokens())
        {
            Assert.IsFalse(combinedReleaseTests.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
    }

    private static IReadOnlyList<string> ForbiddenFastLaneDependencyTokens() =>
    [
        string.Concat("IronDev", ".Api"),
        string.Concat("IronDev", ".Cli"),
        string.Concat("IronDev", ".Integration", "Tests"),
        string.Concat("IronDev", ".Infrastructure"),
        string.Concat("Web", "Application", "Factory"),
        string.Concat("Test", "Server"),
        string.Concat("Http", "Client"),
        string.Concat("Db", "Context"),
        string.Concat("Sql", "Connection"),
        string.Concat("Test", "containers"),
        string.Concat("Git", "Hub"),
        string.Concat("Governed", "Release", "Gate", "Service"),
        string.Concat("Release", "Execution", "Service"),
        string.Concat("Deployment", "Execution"),
        string.Concat("Merge", "Execution"),
        string.Concat("Process", ".Start"),
        string.Concat("File", ".Write"),
        string.Concat("DateTimeOffset", ".Utc", "Now"),
        string.Concat("DateTimeOffset", ".Now")
    ];
}
