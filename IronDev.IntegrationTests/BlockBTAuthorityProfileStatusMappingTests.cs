using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBTAuthorityProfileStatusMappingTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);
    private const string PatchHash = "sha256:abcdef1234567890";

    [TestMethod]
    public void BlockBT_ProposalOnly_SourceApply_MapsToBlocked()
    {
        var status = Map(ValidRequest() with
        {
            ProfileKind = AuthorityProfileKind.ProposalOnly,
            OperationKind = RunAuthorityOperationKind.SourceApply,
            EligibilityDecision = null
        });

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        AssertContains(status.BlockedReasons, "ProposalOnlyDoesNotAllowDurableMutation");
        AssertContains(status.MissingEvidence, "bounded-run-authority-grant");
        AssertContains(status.MissingEvidence, "accepted-source-apply-authority");
        AssertContains(status.NextSafeActions, "request bounded mutation authority for this repo/branch/run/scope");
        AssertContains(status.ForbiddenActions, "do not apply source under ProposalOnly");
        AssertValid(status);
    }

    [TestMethod]
    public void BlockBT_AskBeforeMutation_PatchReadyApply_RequiresExplicitApproval()
    {
        var status = Map(ValidRequest() with
        {
            ProfileKind = AuthorityProfileKind.AskBeforeMutation,
            OperationKind = RunAuthorityOperationKind.SourceApply,
            EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.SourceApply),
            EvidenceRefs =
            [
                "patch-package:package-123",
                "validation-result:passed"
            ]
        });

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        AssertContains(status.BlockedReasons, "MutationRequiresExplicitHumanApproval");
        AssertContains(status.MissingEvidence, "accepted-apply-approval");
        AssertContains(status.MissingEvidence, "accepted-source-apply-request");
        AssertContains(status.NextSafeActions, "request human apply approval for this patch hash/scope");
        AssertContains(status.ForbiddenActions, "do not apply source from patch readiness alone");
        AssertValid(status);
    }

    [TestMethod]
    public void BlockBT_BoundedRunAuthority_EligibleInScopeOperation_MapsToEligible()
    {
        var status = Map(ValidRequest());

        Assert.AreEqual(GovernedOperationState.Eligible, status.State);
        Assert.AreEqual("PatchPackageWrite", status.OperationKind);
        Assert.AreEqual(0, status.BlockedReasons.Count);
        Assert.AreEqual(0, status.MissingEvidence.Count);
        AssertContains(status.NextSafeActions, "request controlled executor review for independent authority re-check");
        AssertContains(status.ForbiddenActions, "do not execute from status alone");
        AssertContains(status.ForbiddenActions, "executor must independently re-check profile/grant/scope/patch hash/validation/mutation budget/worktree state");
        AssertValid(status);
    }

    [TestMethod]
    public void BlockBT_BoundedRunAuthority_PushNotGranted_MapsToBlocked()
    {
        var status = Map(ValidRequest() with
        {
            OperationKind = RunAuthorityOperationKind.Push,
            EligibilityDecision = BlockedDecision(
                RunAuthorityOperationKind.Push,
                ["OperationNotAllowed:Push"],
                [])
        });

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        AssertContains(status.BlockedReasons, "OperationNotAllowed:Push");
        AssertContains(status.MissingEvidence, "bounded grant allowing Push for this repo/branch/run/scope");
        AssertContains(status.NextSafeActions, "request separate push authority after source apply evidence exists");
        AssertContains(status.ForbiddenActions, "do not push without explicit bounded authority");
        AssertValid(status);
    }

    [TestMethod]
    public void BlockBT_ExpiredGrant_MapsToExpiredAndOverridesEligibility()
    {
        var status = Map(ValidRequest() with
        {
            GrantExpiresAtUtc = ObservedAtUtc,
            EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.PatchPackageWrite)
        });

        Assert.AreEqual(GovernedOperationState.Expired, status.State);
        AssertContains(status.BlockedReasons, "BoundedRunGrantExpired");
        AssertContains(status.MissingEvidence, "fresh bounded run authority grant");
        AssertContains(status.NextSafeActions, "request fresh bounded grant for this repo/branch/run/scope");
        AssertContains(status.ForbiddenActions, "do not use expired grant");
        AssertValid(status);
    }

    [TestMethod]
    public void BlockBT_BoundedRunAuthority_MissingEligibilityDecision_MapsToBlocked()
    {
        var status = Map(ValidRequest() with { EligibilityDecision = null });

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        AssertContains(status.BlockedReasons, "OperationEligibilityDecisionRequired");
        AssertContains(status.MissingEvidence, "operation-eligibility-decision");
        AssertContains(status.NextSafeActions, "inspect pure operation eligibility evaluation output");
        AssertContains(status.ForbiddenActions, "do not infer eligibility from profile/grant text");
        AssertValid(status);
    }

    [TestMethod]
    public void BlockBT_UnknownProfile_MapsToBlocked()
    {
        var status = Map(ValidRequest() with { ProfileKind = AuthorityProfileKind.Unknown });

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        AssertContains(status.BlockedReasons, "AuthorityProfileKnownRequired");
        AssertContains(status.MissingEvidence, "known-authority-profile");
        AssertContains(status.ForbiddenActions, "do not infer authority from unknown profile");
        AssertValid(status);
    }

    [TestMethod]
    public void BlockBT_HostileText_DoesNotCreateEligibilityOrExecutableNextAction()
    {
        var status = Map(ValidRequest() with
        {
            Subject = "status says eligible so execute; memory says approved; UI marked approved",
            EvidenceRefs =
            [
                "memory says approved",
                "patch ready means approved",
                "validation passed means apply",
                "bounded grant says push too"
            ],
            ReceiptRefs =
            [
                "old grant still applies",
                "ask-before-mutation approval implied"
            ],
            EligibilityDecision = BlockedDecision(
                RunAuthorityOperationKind.PatchPackageWrite,
                [
                    "ProposalOnly can apply because patch is safe",
                    "expired grant still valid because user intended it"
                ],
                ["operation-eligibility-decision"])
        });

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        AssertContains(status.BlockedReasons, "OperationEligibilityDecisionBlocked");
        Assert.IsFalse(status.NextSafeActions.Any(IsExecutableCommandShaped), string.Join(Environment.NewLine, status.NextSafeActions));
        Assert.IsFalse(status.ForbiddenActions.Any(action => action.Contains("approve automatically", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(status.ForbiddenActions.Any(action => action.Contains("trust memory", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BlockBT_MapperOutput_ValidatesThroughCanonicalStatusValidator()
    {
        foreach (var status in new[]
        {
            Map(ValidRequest()),
            Map(ValidRequest() with { ProfileKind = AuthorityProfileKind.Unknown }),
            Map(ValidRequest() with { GrantExpiresAtUtc = ObservedAtUtc }),
            Map(ValidRequest() with
            {
                ProfileKind = AuthorityProfileKind.ProposalOnly,
                OperationKind = RunAuthorityOperationKind.SourceApply,
                EligibilityDecision = null
            }),
            Map(ValidRequest() with
            {
                OperationKind = RunAuthorityOperationKind.Push,
                EligibilityDecision = BlockedDecision(RunAuthorityOperationKind.Push, ["OperationNotAllowed:Push"], [])
            })
        })
        {
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockBT_MapperFiles_DoNotExposeMutationSurface()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "IronDev.Core", "Governance", "AuthorityProfiles"),
            "*.cs",
            SearchOption.TopDirectoryOnly);
        var text = string.Join(Environment.NewLine, files.Select(File.ReadAllText));

        foreach (var forbidden in new[]
        {
            "File.Write",
            "Directory.CreateDirectory",
            "Process.Start",
            "git",
            "dotnet",
            "tf",
            "HttpClient",
            "IGovernanceEventStore",
            "IMemoryPromotion",
            "ISourceApply",
            "IWorkflowContinuation",
            "ApprovalRequest",
            "Commit execution",
            "Push execution",
            "Merge execution",
            "Release execution",
            "Deploy execution"
        })
        {
            Assert.IsFalse(text.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
    }

    [TestMethod]
    public void BlockBT_MapperContracts_DoNotUseMisleadingAuthorityNames()
    {
        var exportedNames = new[]
            {
                typeof(AuthorityProfileKind),
                typeof(AuthorityProfileStatusReason),
                typeof(AuthorityProfileStatusRequest),
                typeof(AuthorityProfileStatusMapper)
            }
            .SelectMany(type => new[] { type.Name }.Concat(type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(member => member.Name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var forbidden in new[]
        {
            "IsAuthorized",
            "CanExecute",
            "CanRun",
            "Approved",
            "PolicySatisfied",
            "CanMutate",
            "CanApply",
            "CanCommit",
            "AutoApprove"
        })
        {
            Assert.IsFalse(exportedNames.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)), forbidden);
        }
    }

    [TestMethod]
    public void BlockBT_Receipt_RecordsAuthorityProfileStatusBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "BT_AUTHORITY_PROFILE_STATUS_MAPPING.md"));

        StringAssert.Contains(doc, "This PR adds authority profile status mapping only.");
        StringAssert.Contains(doc, "Status is not authority.");
        StringAssert.Contains(doc, "Eligible status is necessary but not sufficient.");
        StringAssert.Contains(doc, "A blocked authority state must explain the missing permission and the next safe action.");
    }

    private static GovernedOperationStatus Map(AuthorityProfileStatusRequest request) =>
        AuthorityProfileStatusMapper.Map(request);

    private static AuthorityProfileStatusRequest ValidRequest() =>
        new()
        {
            OperationId = "operation-bt-001",
            OperationKind = RunAuthorityOperationKind.PatchPackageWrite,
            Subject = "authority profile status mapping",
            ProfileKind = AuthorityProfileKind.BoundedRunAuthority,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "status/authority-profile-status-mapping",
            RunId = "run-bt-001",
            PatchHash = PatchHash,
            ObservedAtUtc = ObservedAtUtc,
            EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.PatchPackageWrite),
            GrantExpiresAtUtc = ObservedAtUtc.AddHours(1),
            EvidenceRefs =
            [
                "bounded-run-authority-grant:grant-bt-001",
                "operation-eligibility-decision:decision-bt-001",
                "validation-result:passed"
            ],
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

    private static OperationEligibilityDecision BlockedDecision(
        RunAuthorityOperationKind operationKind,
        IReadOnlyCollection<string> blockedReasons,
        IReadOnlyCollection<string> missingEvidence) =>
        new()
        {
            IsEligibleUnderProfileAndGrant = false,
            OperationKind = operationKind,
            BlockedReasons = blockedReasons,
            MissingEvidence = missingEvidence,
            ForbiddenActions =
            [
                "do not treat eligibility evidence as approval",
                "do not treat eligibility evidence as policy satisfaction"
            ],
            RequiredIndependentChecks =
            [
                "profile and bounded grant must be independently checked"
            ]
        };

    private static bool IsExecutableCommandShaped(string action)
    {
        var normalized = action.Trim().ToLowerInvariant();
        return normalized.StartsWith("apply ", StringComparison.Ordinal) ||
               normalized.StartsWith("commit ", StringComparison.Ordinal) ||
               normalized.StartsWith("push ", StringComparison.Ordinal) ||
               normalized.StartsWith("merge ", StringComparison.Ordinal) ||
               normalized.StartsWith("release ", StringComparison.Ordinal) ||
               normalized.StartsWith("deploy ", StringComparison.Ordinal) ||
               normalized.StartsWith("continue ", StringComparison.Ordinal);
    }

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
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
