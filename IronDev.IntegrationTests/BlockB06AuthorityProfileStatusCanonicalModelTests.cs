using System.Text.RegularExpressions;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockB06AuthorityProfileStatusCanonicalModelTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 23, 0, 0, 0, TimeSpan.Zero);
    private const string PatchHash = "sha256:b06abcdef1234567890";

    [TestMethod]
    public void BlockB06_CanonicalProfileBoundaries_CoverEveryKnownOperation()
    {
        var known = Enum.GetValues<RunAuthorityOperationKind>()
            .Where(operation => operation != RunAuthorityOperationKind.Unknown)
            .ToArray();

        AssertBoundaryComplete(
            RunAuthorityProfileValidator.ProposalOnlyAllowedOperations,
            RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations,
            known);
        AssertBoundaryComplete(
            RunAuthorityProfileValidator.AskBeforeMutationAllowedOperations,
            RunAuthorityProfileValidator.AskBeforeMutationForbiddenOperations,
            known);
        AssertBoundaryComplete(
            RunAuthorityProfileValidator.BoundedRunAuthorityAllowedOperations,
            RunAuthorityProfileValidator.BoundedRunAuthorityForbiddenOperations,
            known);
    }

    [TestMethod]
    public void BlockB06_StatusMapper_UsesCanonicalProfileOperationBoundaries()
    {
        AssertForbiddenOperationsBlock(
            AuthorityProfileKind.ProposalOnly,
            RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations,
            operation => $"ProposalOnlyOperationBlocked:{operation}");
        AssertForbiddenOperationsBlock(
            AuthorityProfileKind.AskBeforeMutation,
            RunAuthorityProfileValidator.AskBeforeMutationForbiddenOperations,
            operation => $"AskBeforeMutationOperationBlocked:{operation}",
            AcceptedApplyEvidenceRefs());
        AssertForbiddenOperationsBlock(
            AuthorityProfileKind.BoundedRunAuthority,
            RunAuthorityProfileValidator.BoundedRunAuthorityForbiddenOperations,
            operation => $"BoundedRunAuthorityOperationBlocked:{operation}",
            BoundedEvidenceRefs());
    }

    [TestMethod]
    public void BlockB06_StatusMapper_UnknownOperationKindFailsClosed()
    {
        foreach (var operation in new[] { RunAuthorityOperationKind.Unknown, (RunAuthorityOperationKind)999 })
        {
            var status = Map(Request(AuthorityProfileKind.BoundedRunAuthority, operation) with
            {
                EvidenceRefs = BoundedEvidenceRefs(),
                EligibilityDecision = EligibleDecision(operation)
            });

            Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
            AssertContains(status.BlockedReasons, "AuthorityProfileOperationKnownRequired");
            AssertContains(status.MissingEvidence, "known-authority-profile-operation");
            AssertContains(status.ForbiddenActions, "do not infer authority from unknown operation");
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB06_ProfileAllowedOperationStillRequiresEligibilityDecision()
    {
        var cases = new[]
        {
            Request(AuthorityProfileKind.ProposalOnly, RunAuthorityOperationKind.PatchPackageWrite) with
            {
                EligibilityDecision = null
            },
            Request(AuthorityProfileKind.AskBeforeMutation, RunAuthorityOperationKind.SourceApply) with
            {
                EvidenceRefs = AcceptedApplyEvidenceRefs(),
                EligibilityDecision = null
            },
            Request(AuthorityProfileKind.BoundedRunAuthority, RunAuthorityOperationKind.Commit) with
            {
                EvidenceRefs = BoundedEvidenceRefs(),
                EligibilityDecision = null
            }
        };

        foreach (var request in cases)
        {
            var status = Map(request);

            Assert.AreEqual(GovernedOperationState.Blocked, status.State, request.OperationKind.ToString());
            AssertContains(status.BlockedReasons, "OperationEligibilityDecisionRequired");
            AssertContains(status.MissingEvidence, "operation-eligibility-decision");
            AssertContains(status.ForbiddenActions, "do not infer eligibility from profile/grant text");
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB06_EligibilityDecisionOperationMismatchStillBlocks()
    {
        var status = Map(Request(AuthorityProfileKind.BoundedRunAuthority, RunAuthorityOperationKind.Commit) with
        {
            EvidenceRefs = BoundedEvidenceRefs(),
            EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.Push)
        });

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        AssertContains(status.BlockedReasons, "OperationEligibilityDecisionOperationMismatch");
        AssertContains(status.MissingEvidence, "matching operation eligibility decision");
        AssertContains(status.ForbiddenActions, "do not reuse eligibility decision from another operation");
        AssertValid(status);
    }

    [TestMethod]
    public void BlockB06_ProposalOnly_EligibleDecisionCannotOverrideProfileBoundary()
    {
        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.SourceApply,
            RunAuthorityOperationKind.Commit,
            RunAuthorityOperationKind.Push,
            RunAuthorityOperationKind.DraftPullRequest
        })
        {
            var status = Map(Request(AuthorityProfileKind.ProposalOnly, operation) with
            {
                EligibilityDecision = EligibleDecision(operation)
            });

            Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
            AssertContains(status.BlockedReasons, $"ProposalOnlyOperationBlocked:{operation}");
            AssertContains(status.ForbiddenActions, "do not apply source under ProposalOnly");
            AssertContains(status.ForbiddenActions, "do not commit under ProposalOnly");
            AssertContains(status.ForbiddenActions, "do not push under ProposalOnly");
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB06_AskBeforeMutation_AcceptedApplyApprovalCannotAuthorizeLaterLanes()
    {
        foreach (var operation in RunAuthorityProfileValidator.AskBeforeMutationForbiddenOperations)
        {
            var status = Map(Request(AuthorityProfileKind.AskBeforeMutation, operation) with
            {
                EvidenceRefs = AcceptedApplyEvidenceRefs(),
                EligibilityDecision = EligibleDecision(operation)
            });

            Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
            AssertContains(status.BlockedReasons, $"AskBeforeMutationOperationBlocked:{operation}");
            AssertContains(status.ForbiddenActions, "do not treat accepted apply approval as authority for later mutation lanes");
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB06_AskBeforeMutation_SourceApplyStillRequiresAcceptedApplyApproval()
    {
        var missing = Map(Request(AuthorityProfileKind.AskBeforeMutation, RunAuthorityOperationKind.SourceApply) with
        {
            EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.SourceApply),
            EvidenceRefs = ["patch-package:package-b06", "validation-result:passed"]
        });

        Assert.AreEqual(GovernedOperationState.Blocked, missing.State);
        AssertContains(missing.BlockedReasons, "MutationRequiresExplicitHumanApproval");

        var accepted = Map(Request(AuthorityProfileKind.AskBeforeMutation, RunAuthorityOperationKind.SourceApply) with
        {
            EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.SourceApply),
            EvidenceRefs = AcceptedApplyEvidenceRefs()
        });

        Assert.AreEqual(GovernedOperationState.Eligible, accepted.State);
        AssertContains(accepted.ForbiddenActions, "do not execute from status alone");
        AssertContains(accepted.ForbiddenActions, "do not apply source from status alone");
        AssertValid(missing);
        AssertValid(accepted);
    }

    [TestMethod]
    public void BlockB06_BoundedRunAuthority_ForbiddenOperationsBlockBeforeEligibility()
    {
        foreach (var operation in RunAuthorityProfileValidator.BoundedRunAuthorityForbiddenOperations)
        {
            var status = Map(Request(AuthorityProfileKind.BoundedRunAuthority, operation) with
            {
                EvidenceRefs = BoundedEvidenceRefs(),
                EligibilityDecision = EligibleDecision(operation)
            });

            Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
            AssertContains(status.BlockedReasons, $"BoundedRunAuthorityOperationBlocked:{operation}");
            AssertContains(status.ForbiddenActions, $"do not perform {operation} under BoundedRunAuthority");
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB06_BoundedRunAuthority_BoundedLanesRequireVisibleGrantEvidence()
    {
        foreach (var operation in BoundedMutationLanes())
        {
            var status = Map(Request(AuthorityProfileKind.BoundedRunAuthority, operation) with
            {
                EvidenceRefs = ["operation-eligibility-decision:decision-b06"],
                EligibilityDecision = EligibleDecision(operation)
            });

            Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
            AssertContains(status.BlockedReasons, "BoundedRunAuthorityGrantEvidenceRequired");
            AssertContains(status.MissingEvidence, "bounded-run-authority-grant");
            AssertContains(status.ForbiddenActions, "do not infer bounded authority from profile kind alone");
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB06_BoundedRunAuthority_BoundedLanesRequireVisibleEligibilityEvidence()
    {
        foreach (var operation in BoundedMutationLanes())
        {
            var status = Map(Request(AuthorityProfileKind.BoundedRunAuthority, operation) with
            {
                EvidenceRefs = ["bounded-run-authority-grant:grant-b06"],
                EligibilityDecision = EligibleDecision(operation)
            });

            Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
            AssertContains(status.BlockedReasons, "OperationEligibilityDecisionEvidenceRequired");
            AssertContains(status.MissingEvidence, "operation-eligibility-decision");
            AssertContains(status.ForbiddenActions, "do not infer bounded authority from eligibility text alone");
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB06_BoundedRunAuthority_EligibleStatusStillNotExecution()
    {
        var status = Map(Request(AuthorityProfileKind.BoundedRunAuthority, RunAuthorityOperationKind.Commit) with
        {
            EvidenceRefs = BoundedEvidenceRefs(),
            EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.Commit)
        });

        Assert.AreEqual(GovernedOperationState.Eligible, status.State);
        AssertContains(status.ForbiddenActions, "do not execute from status alone");
        AssertContains(status.ForbiddenActions, "do not treat Eligible status as approval");
        AssertContains(status.ForbiddenActions, "do not treat Eligible status as policy satisfaction");
        AssertContains(status.ForbiddenActions, "do not apply source from status alone");
        AssertContains(status.ForbiddenActions, "executor must independently re-check profile/grant/scope/patch hash/validation/mutation budget/worktree state");
        AssertValid(status);
    }

    [TestMethod]
    public void BlockB06_StatusMapper_DoesNotOwnSecondProfileOperationModel()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Core",
            "Governance",
            "AuthorityProfiles",
            "AuthorityProfileStatusMapper.cs"));

        StringAssert.Contains(source, "RunAuthorityProfileValidator.ProposalOnlyAllowedOperations");
        StringAssert.Contains(source, "RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations");
        StringAssert.Contains(source, "RunAuthorityProfileValidator.AskBeforeMutationAllowedOperations");
        StringAssert.Contains(source, "RunAuthorityProfileValidator.AskBeforeMutationForbiddenOperations");
        StringAssert.Contains(source, "RunAuthorityProfileValidator.BoundedRunAuthorityAllowedOperations");
        StringAssert.Contains(source, "RunAuthorityProfileValidator.BoundedRunAuthorityForbiddenOperations");

        foreach (var forbiddenDefinition in new[]
        {
            "ProposalOnlyAllowedOperations",
            "AskBeforeMutationAllowedOperations",
            "BoundedRunAuthorityAllowedOperations",
            "ForbiddenOperationsByProfile",
            "AllowedOperationsByProfile"
        })
        {
            Assert.IsFalse(
                Regex.IsMatch(source, $@"(?:private|internal|public)\s+[^=\r\n]*\b{forbiddenDefinition}\b\s*=", RegexOptions.IgnoreCase),
                forbiddenDefinition);
        }

        foreach (var forbiddenTypeName in new[]
        {
            "AuthorityProfileKindMapper",
            "AuthorityProfileKindBridge",
            "AuthorityProfileKindTranslator",
            "AuthorityProfileKindAdapter",
            "RunAuthorityProfileKind",
            "LegacyAuthorityProfileKind"
        })
        {
            Assert.IsFalse(source.Contains(forbiddenTypeName, StringComparison.Ordinal), forbiddenTypeName);
        }
    }

    [TestMethod]
    public void BlockB06_Receipt_RecordsCanonicalStatusBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "B06_AUTHORITY_PROFILE_STATUS_CANONICAL_MODEL.md"));

        StringAssert.Contains(doc, "AuthorityProfileStatusMapper reflects the canonical profile model.");
        StringAssert.Contains(doc, "Status is not authority.");
        StringAssert.Contains(doc, "Profile kind is not authority.");
        StringAssert.Contains(doc, "Eligibility is not execution.");
        StringAssert.Contains(doc, "Eligible status is not execution.");
        StringAssert.Contains(doc, "Profile-forbidden operations block before eligibility.");
        StringAssert.Contains(doc, "Unknown operations fail closed.");
        StringAssert.Contains(doc, "Future unmapped operations must fail closed.");
        StringAssert.Contains(doc, "AskBeforeMutation accepted apply approval cannot authorize later lanes.");
        StringAssert.Contains(doc, "BoundedRunAuthority requires bounded grant evidence refs for bounded lanes.");
        StringAssert.Contains(doc, "No executor, mutation, approval, policy, UI, API, CLI, SQL, durable store, or generated client path was added.");
        StringAssert.Contains(doc, "Status may explain the gate. It must not become the gate.");
    }

    private static void AssertForbiddenOperationsBlock(
        AuthorityProfileKind profileKind,
        IReadOnlyCollection<RunAuthorityOperationKind> forbiddenOperations,
        Func<RunAuthorityOperationKind, string> expectedReason,
        IReadOnlyCollection<string>? evidenceRefs = null)
    {
        foreach (var operation in forbiddenOperations)
        {
            var status = Map(Request(profileKind, operation) with
            {
                EvidenceRefs = evidenceRefs ?? [],
                EligibilityDecision = EligibleDecision(operation)
            });

            Assert.AreEqual(GovernedOperationState.Blocked, status.State, $"{profileKind}:{operation}");
            AssertContains(status.BlockedReasons, expectedReason(operation));
            Assert.IsFalse(status.BlockedReasons.Any(reason => string.Equals(reason, "OperationEligibilityDecisionRequired", StringComparison.OrdinalIgnoreCase)));
            AssertValid(status);
        }
    }

    private static void AssertBoundaryComplete(
        IReadOnlyCollection<RunAuthorityOperationKind> allowed,
        IReadOnlyCollection<RunAuthorityOperationKind> forbidden,
        IReadOnlyCollection<RunAuthorityOperationKind> known)
    {
        var covered = allowed.Concat(forbidden).Distinct().Order().ToArray();
        var overlap = allowed.Intersect(forbidden).ToArray();

        CollectionAssert.AreEquivalent(known.ToArray(), covered);
        Assert.AreEqual(0, overlap.Length, string.Join(", ", overlap));
    }

    private static AuthorityProfileStatusRequest Request(
        AuthorityProfileKind profileKind,
        RunAuthorityOperationKind operationKind) =>
        new()
        {
            OperationId = $"operation-b06-{profileKind}-{operationKind}",
            OperationKind = operationKind,
            Subject = "B06 authority profile status mapping",
            ProfileKind = profileKind,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "governance/authority-status-canonical-profile-model",
            RunId = "run-b06-001",
            PatchHash = PatchHash,
            ObservedAtUtc = ObservedAtUtc,
            EligibilityDecision = EligibleDecision(operationKind),
            GrantExpiresAtUtc = ObservedAtUtc.AddHours(1),
            EvidenceRefs = [],
            ReceiptRefs = []
        };

    private static OperationEligibilityDecision EligibleDecision(RunAuthorityOperationKind operationKind) =>
        new()
        {
            IsEligibleUnderProfileAndGrant = true,
            OperationKind = operationKind,
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

    private static RunAuthorityOperationKind[] BoundedMutationLanes() =>
    [
        RunAuthorityOperationKind.SourceApply,
        RunAuthorityOperationKind.DurableSourceMutation,
        RunAuthorityOperationKind.Rollback,
        RunAuthorityOperationKind.Commit,
        RunAuthorityOperationKind.Push,
        RunAuthorityOperationKind.DraftPullRequest
    ];

    private static string[] AcceptedApplyEvidenceRefs() =>
    [
        "accepted-apply-approval:approval-b06",
        "accepted-source-apply-request:request-b06"
    ];

    private static string[] BoundedEvidenceRefs() =>
    [
        "bounded-run-authority-grant:grant-b06",
        "operation-eligibility-decision:decision-b06"
    ];

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
