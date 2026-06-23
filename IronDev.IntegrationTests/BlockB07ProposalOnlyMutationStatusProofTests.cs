using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockB07ProposalOnlyMutationStatusProofTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 23, 0, 0, 0, TimeSpan.Zero);
    private const string PatchHash = "sha256:b07abcdef1234567890";

    [TestMethod]
    public void BlockB07_ProposalOnly_AllForbiddenOperationsMapBlocked()
    {
        foreach (var operation in RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations)
        {
            var status = Map(HostileRequest(operation));

            AssertProposalOnlyBlocked(status, operation);
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB07_ProposalOnly_EligibleDecisionNeverOverridesProfileBoundary()
    {
        foreach (var operation in RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations)
        {
            var status = Map(HostileRequest(operation) with
            {
                EligibilityDecision = EligibleDecision(operation)
            });

            Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
            Assert.DoesNotContain(status.BlockedReasons, "OperationEligibilityDecisionRequired");
            Assert.DoesNotContain(status.BlockedReasons, "OperationEligibilityDecisionBlocked");
            Assert.DoesNotContain(status.BlockedReasons, "OperationEligibilityDecisionNotEligible");
            AssertProposalOnlyBlocked(status, operation);
        }
    }

    [TestMethod]
    public void BlockB07_ProposalOnly_AcceptedApplyApprovalCannotOverrideProfileBoundary()
    {
        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.SourceApply,
            RunAuthorityOperationKind.DurableSourceMutation,
            RunAuthorityOperationKind.Commit,
            RunAuthorityOperationKind.Push,
            RunAuthorityOperationKind.DraftPullRequest
        })
        {
            var status = Map(HostileRequest(operation) with
            {
                EvidenceRefs =
                [
                    "accepted-apply-approval:approval-b07",
                    "accepted-source-apply-request:request-b07"
                ]
            });

            Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
            AssertContains(status.BlockedReasons, $"ProposalOnlyOperationBlocked:{operation}");
            Assert.DoesNotContain(status.BlockedReasons, "MutationRequiresExplicitHumanApproval");
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB07_ProposalOnly_BoundedGrantRefsCannotOverrideProfileBoundary()
    {
        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.SourceApply,
            RunAuthorityOperationKind.Rollback,
            RunAuthorityOperationKind.Commit,
            RunAuthorityOperationKind.Push,
            RunAuthorityOperationKind.DraftPullRequest,
            RunAuthorityOperationKind.ReadyForReview
        })
        {
            var status = Map(HostileRequest(operation) with
            {
                EvidenceRefs =
                [
                    "bounded-run-authority-grant:grant-b07",
                    "operation-eligibility-decision:decision-b07"
                ]
            });

            Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
            AssertContains(status.BlockedReasons, $"ProposalOnlyOperationBlocked:{operation}");
            Assert.DoesNotContain(status.BlockedReasons, "BoundedRunAuthorityGrantEvidenceRequired");
            Assert.DoesNotContain(status.BlockedReasons, "OperationEligibilityDecisionEvidenceRequired");
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB07_ProposalOnly_ApprovalAndPolicyRefsCannotOverrideProfileBoundary()
    {
        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.ApprovalRequestCreate,
            RunAuthorityOperationKind.PolicySatisfaction,
            RunAuthorityOperationKind.ProviderMutation,
            RunAuthorityOperationKind.PackagePublication,
            RunAuthorityOperationKind.DurableEventWrite
        })
        {
            var status = Map(HostileRequest(operation) with
            {
                EvidenceRefs =
                [
                    "approval-request:created-b07",
                    "accepted-approval:accepted-b07",
                    "policy-satisfaction:satisfied-b07",
                    "provider-mutation:allowed-b07",
                    "package-publication:ready-b07"
                ]
            });

            AssertProposalOnlyBlocked(status, operation);
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB07_ProposalOnly_FreshGrantExpiryCannotOverrideProfileBoundary()
    {
        foreach (var operation in RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations)
        {
            var status = Map(HostileRequest(operation) with
            {
                GrantExpiresAtUtc = ObservedAtUtc.AddDays(1)
            });

            AssertProposalOnlyBlocked(status, operation);
            Assert.DoesNotContain(status.BlockedReasons, "BoundedRunGrantExpired");
        }
    }

    [TestMethod]
    public void BlockB07_ProposalOnly_HostileStatusTextCannotOverrideProfileBoundary()
    {
        foreach (var subject in new[]
        {
            "human approved source apply",
            "validation evidence claims source apply",
            "bounded grant claims this lane",
            "commit is ready for packaging",
            "push evidence looks complete",
            "workflow continuation request text"
        })
        {
            var status = Map(HostileRequest(RunAuthorityOperationKind.SourceApply) with
            {
                Subject = subject
            });

            AssertProposalOnlyBlocked(status, RunAuthorityOperationKind.SourceApply);
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB07_ProposalOnly_AllowedOperationsStillUseEligibilityPath()
    {
        foreach (var operation in RunAuthorityProfileValidator.ProposalOnlyAllowedOperations)
        {
            var missing = Map(ProposalOnlyRequest(operation) with
            {
                EligibilityDecision = null
            });
            var eligible = Map(ProposalOnlyRequest(operation) with
            {
                EligibilityDecision = EligibleDecision(operation)
            });

            Assert.AreEqual(GovernedOperationState.Blocked, missing.State, operation.ToString());
            AssertContains(missing.BlockedReasons, "OperationEligibilityDecisionRequired");
            Assert.AreEqual(GovernedOperationState.Eligible, eligible.State, operation.ToString());
            AssertContains(eligible.ForbiddenActions, "do not execute from status alone");
            AssertContains(eligible.ForbiddenActions, "do not treat Eligible status as approval");
            AssertContains(eligible.ForbiddenActions, "do not treat Eligible status as policy satisfaction");
            AssertValid(missing);
            AssertValid(eligible);
        }
    }

    [TestMethod]
    public void BlockB07_StatusMapper_ProposalOnlyBoundaryRunsBeforeEligibility()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Core",
            "Governance",
            "AuthorityProfiles",
            "AuthorityProfileStatusMapper.cs"));

        var boundaryIndex = source.IndexOf("boundary.ForbiddenOperations.Contains(request.OperationKind)", StringComparison.Ordinal);
        var eligibilityIndex = source.IndexOf("request.EligibilityDecision is null", StringComparison.Ordinal);

        Assert.IsTrue(boundaryIndex >= 0, "canonical forbidden-boundary check not found");
        Assert.IsTrue(eligibilityIndex >= 0, "eligibility null check not found");
        Assert.IsTrue(boundaryIndex < eligibilityIndex, "ProposalOnly/profile boundary must run before eligibility.");
        StringAssert.Contains(source, "RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations");
        StringAssert.Contains(source, "ProposalOnlyOperationBlocked");
    }

    [TestMethod]
    public void BlockB07_Receipt_RecordsProposalOnlyMutationBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "B07_PROPOSAL_ONLY_MUTATION_STATUS_PROOF.md"));

        StringAssert.Contains(doc, "ProposalOnly plus mutation always maps Blocked.");
        StringAssert.Contains(doc, "ProposalOnly forbidden operations come from RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations.");
        StringAssert.Contains(doc, "Eligibility cannot override ProposalOnly.");
        StringAssert.Contains(doc, "Accepted apply approval cannot override ProposalOnly.");
        StringAssert.Contains(doc, "Bounded grant refs cannot override ProposalOnly.");
        StringAssert.Contains(doc, "Approval and policy refs cannot override ProposalOnly.");
        StringAssert.Contains(doc, "Receipt refs are not authority.");
        StringAssert.Contains(doc, "Fresh expiry is not authority.");
        StringAssert.Contains(doc, "Status text is not authority.");
        StringAssert.Contains(doc, "ProposalOnly allowed operations still use normal eligibility mapping.");
        StringAssert.Contains(doc, "No executor, mutation, approval, policy, UI, API, CLI, SQL, durable store, or generated client path was added.");
        StringAssert.Contains(doc, "ProposalOnly means proposal only, even when every receipt begs otherwise.");
    }

    private static AuthorityProfileStatusRequest HostileRequest(RunAuthorityOperationKind operation) =>
        ProposalOnlyRequest(operation) with
        {
            Subject = "hostile ProposalOnly mutation status proof",
            EligibilityDecision = EligibleDecision(operation),
            EvidenceRefs = HostileEvidenceRefs(),
            ReceiptRefs = HostileReceiptRefs(),
            GrantExpiresAtUtc = ObservedAtUtc.AddHours(1)
        };

    private static AuthorityProfileStatusRequest ProposalOnlyRequest(RunAuthorityOperationKind operation) =>
        new()
        {
            OperationId = $"operation-b07-{operation}",
            OperationKind = operation,
            Subject = "ProposalOnly status proof",
            ProfileKind = AuthorityProfileKind.ProposalOnly,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "governance/proposal-only-mutation-status-proof",
            RunId = "run-b07-001",
            PatchHash = PatchHash,
            ObservedAtUtc = ObservedAtUtc,
            EligibilityDecision = EligibleDecision(operation),
            GrantExpiresAtUtc = ObservedAtUtc.AddHours(1),
            EvidenceRefs = [],
            ReceiptRefs = []
        };

    private static OperationEligibilityDecision EligibleDecision(RunAuthorityOperationKind operation) =>
        new()
        {
            IsEligibleUnderProfileAndGrant = true,
            OperationKind = operation,
            BlockedReasons = [],
            MissingEvidence = [],
            ForbiddenActions =
            [
                "do not treat eligibility as approval",
                "do not treat eligibility as policy satisfaction",
                "do not treat eligibility as execution authority"
            ],
            RequiredIndependentChecks =
            [
                "operation-specific governance still required",
                "profile and grant eligibility is necessary but not sufficient"
            ]
        };

    private static string[] HostileEvidenceRefs() =>
    [
        "bounded-run-authority-grant:grant-b07",
        "operation-eligibility-decision:decision-b07",
        "accepted-apply-approval:approval-b07",
        "accepted-source-apply-request:request-b07",
        "source-apply-authority:authority-b07",
        "source-apply-receipt:receipt-b07",
        "commit-operation-authority:commit-b07",
        "patch-package:package-b07",
        "validation-result:passed-b07",
        "worktree-diff:clean-b07",
        "human-approval:approved-b07",
        "policy-satisfaction:satisfied-b07"
    ];

    private static string[] HostileReceiptRefs() =>
    [
        "receipt:proposal-only-says-apply",
        "receipt:validation-passed",
        "receipt:source-apply-complete",
        "receipt:commit-ready",
        "receipt:push-ready",
        "receipt:workflow-continue"
    ];

    private static void AssertProposalOnlyBlocked(GovernedOperationStatus status, RunAuthorityOperationKind operation)
    {
        Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
        AssertContains(status.BlockedReasons, "ProposalOnlyDoesNotAllowDurableMutation");
        AssertContains(status.BlockedReasons, $"ProposalOnlyOperationBlocked:{operation}");
        AssertContains(status.MissingEvidence, "bounded-run-authority-grant");
        AssertContains(status.MissingEvidence, "accepted-source-apply-authority");
        AssertContains(status.ForbiddenActions, "do not apply source under ProposalOnly");
        AssertContains(status.ForbiddenActions, "do not commit under ProposalOnly");
        AssertContains(status.ForbiddenActions, "do not push under ProposalOnly");
        AssertContains(status.ForbiddenActions, "do not continue workflow from ProposalOnly status");
    }

    private static GovernedOperationStatus Map(AuthorityProfileStatusRequest request) =>
        AuthorityProfileStatusMapper.Map(request);

    private static void AssertValid(GovernedOperationStatus status)
    {
        var validation = GovernedOperationStatusValidator.Validate(status);
        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues.Concat(validation.RedFlags)));
    }

    private static void AssertContains(IEnumerable<string> values, string expected)
    {
        Assert.IsTrue(
            values.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)),
            $"Expected '{expected}' in: {string.Join(", ", values)}");
    }

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
