using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBUSourceApplyConsumesBoundedAuthorityTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);
    private const string PatchHash = "sha256:abcdef1234567890";
    private const string FilePath = "IronDev.Core/Governance/SourceApply/Example.cs";

    [TestMethod]
    public void BlockBU_AcceptedApplyRequestPath_EligibleWhenTypedEvidenceMatches()
    {
        var decision = SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with { BoundedRunAuthorityGrant = null });

        Assert.IsTrue(decision.IsEligibleForControlledSourceApply, Describe(decision));
        Assert.AreEqual(SourceApplyAuthorityPath.AcceptedApplyRequest, decision.AuthorityPath);
        AssertContains(decision.ForbiddenActions, "do not apply source from authority decision alone");
        AssertContains(decision.ForbiddenActions, "do not push from source apply authority");
        AssertContains(decision.RequiredIndependentChecks, "executor must independently re-check repo/branch/run/patch hash/file scope/expiry/worktree state");
        AssertContains(decision.RequiredIndependentChecks, "executor must resolve and validate accepted apply request record before source apply");
    }

    [TestMethod]
    public void BlockBU_BoundedRunAuthorityPath_EligibleWhenSourceApplyGrantMatches()
    {
        var decision = SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with { AcceptedApplyRequest = null });

        Assert.IsTrue(decision.IsEligibleForControlledSourceApply, Describe(decision));
        Assert.AreEqual(SourceApplyAuthorityPath.BoundedRunAuthority, decision.AuthorityPath);
        AssertContains(decision.ForbiddenActions, "do not apply source from authority decision alone");
        AssertContains(decision.ForbiddenActions, "do not push from source apply authority");
        AssertContains(decision.RequiredIndependentChecks, "executor must independently re-check repo/branch/run/patch hash/file scope/expiry/worktree state");
        AssertContains(decision.RequiredIndependentChecks, "validation evidence references still require resolver verification");
    }

    [TestMethod]
    public void BlockBU_BothAuthorityPaths_EligibleOnlyWhenSameScope()
    {
        var same = SourceApplyAuthorityEvaluator.Evaluate(ValidRequest());
        var conflict = SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with
        {
            AcceptedApplyRequest = ValidAcceptedRequest() with { PatchHash = "sha256:other" }
        });

        Assert.IsTrue(same.IsEligibleForControlledSourceApply, Describe(same));
        Assert.AreEqual(SourceApplyAuthorityPath.BoundedRunAuthority, same.AuthorityPath);
        AssertBlocked(conflict, "ConflictingSourceApplyAuthorityPaths");
    }

    [TestMethod]
    public void BlockBU_MissingAuthority_FailsClosed()
    {
        var decision = SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with
        {
            AcceptedApplyRequest = null,
            BoundedRunAuthorityGrant = null
        });

        Assert.IsFalse(decision.IsEligibleForControlledSourceApply);
        AssertContains(decision.MissingEvidence, "AcceptedApplyRequestOrBoundedRunAuthorityGrantRequired");
        AssertContains(decision.ForbiddenActions, "do not use string refs alone as source apply authority");
    }

    [TestMethod]
    public void BlockBU_CommonRequestEnvelope_BindsPatchHashAndFileScope()
    {
        AssertBlocked(SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with { PatchHash = "latest" }), "PatchHashInvalid");
        AssertMissing(SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with { PatchHash = "" }), "PatchHashRequired");
        AssertMissing(SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with { AffectedFilePaths = [] }), "AffectedFilePathsRequired");
        AssertBlocked(SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with { AffectedFilePaths = ["../outside.cs"] }), "AffectedFileUnsafe:../outside.cs");
        AssertBlocked(SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with { ObservedAtUtc = default }), "ObservedAtUtcRequired");
    }

    [TestMethod]
    public void BlockBU_AcceptedApplyRequestPath_BlocksBindingFailures()
    {
        AssertBlockedForAccepted(ValidAcceptedRequest() with { Repository = "other/repo" }, "AcceptedApplyRequest:RepositoryMismatch");
        AssertBlockedForAccepted(ValidAcceptedRequest() with { Branch = "other-branch" }, "AcceptedApplyRequest:BranchMismatch");
        AssertBlockedForAccepted(ValidAcceptedRequest() with { RunId = "other-run" }, "AcceptedApplyRequest:RunIdMismatch");
        AssertBlockedForAccepted(ValidAcceptedRequest() with { PatchHash = "sha256:other" }, "AcceptedApplyRequest:PatchHashMismatch");
        AssertMissingForAccepted(ValidAcceptedRequest() with { PatchHash = "" }, "AcceptedApplyRequest:PatchHashRequired");
        AssertBlockedForAccepted(ValidAcceptedRequest() with { PatchHash = "current" }, "AcceptedApplyRequest:PatchHashInvalid");
        AssertBlockedForAccepted(ValidAcceptedRequest() with { AllowedFileGlobs = ["Docs/**"] }, $"AcceptedApplyRequest:AffectedFileNotAllowed:{FilePath}");
        AssertBlockedForAccepted(ValidAcceptedRequest() with { ForbiddenFileGlobs = ["IronDev.Core/Governance/SourceApply/**"] }, $"AcceptedApplyRequest:AffectedFileForbidden:{FilePath}");
        AssertBlockedForAccepted(ValidAcceptedRequest() with { ExpiresAtUtc = default }, "AcceptedApplyRequest:AcceptedApplyRequestExpiresAtUtcRequired");
        AssertBlockedForAccepted(ValidAcceptedRequest() with { ExpiresAtUtc = ObservedAtUtc.AddTicks(-1) }, "AcceptedApplyRequest:AcceptedApplyRequestExpired");
        AssertBlockedForAccepted(ValidAcceptedRequest() with { ExpiresAtUtc = ObservedAtUtc }, "AcceptedApplyRequest:AcceptedApplyRequestExpired");
    }

    [TestMethod]
    public void BlockBU_AcceptedApplyRequestPath_RejectsNonHumanPrincipals()
    {
        foreach (var principalKind in new[]
        {
            "Memory",
            "Model",
            "Agent",
            "UiState",
            "HistoricalReceipt",
            "Inferred",
            "Unknown"
        })
        {
            AssertBlockedForAccepted(
                ValidAcceptedRequest() with { AcceptedByPrincipalKind = principalKind },
                $"AcceptedApplyRequest:AcceptedByPrincipalKindForbidden:{principalKind}");
        }

        var human = SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with { BoundedRunAuthorityGrant = null });
        Assert.IsTrue(human.IsEligibleForControlledSourceApply, Describe(human));
    }

    [TestMethod]
    public void BlockBU_BoundedRunAuthorityPath_BlocksBindingFailures()
    {
        AssertBlockedForGrant(ValidGrant() with { Repository = "other/repo" }, "BoundedRunAuthority:BoundedRunRepositoryMismatch");
        AssertBlockedForGrant(ValidGrant() with { Branch = "other-branch" }, "BoundedRunAuthority:BoundedRunBranchMismatch");
        AssertBlockedForGrant(ValidGrant() with { RunId = "other-run" }, "BoundedRunAuthority:BoundedRunRunIdMismatch");
        AssertBlockedForGrant(ValidGrant() with { PatchHash = "sha256:other" }, "BoundedRunAuthority:BoundedRunPatchHashMismatch");
        AssertMissingForGrant(ValidGrant() with { PatchHash = null }, "BoundedRunAuthority:BoundedRunPatchHashRequired");
        AssertBlockedForGrant(ValidGrant() with { PatchHash = "approved" }, "BoundedRunAuthority:BoundedRunPatchHashInvalid");
        AssertBlockedForGrant(ValidGrant() with { AllowedFileGlobs = ["Docs/**"] }, $"BoundedRunAuthority:AffectedFileNotAllowed:{FilePath}");
        AssertBlockedForGrant(ValidGrant() with { ForbiddenFileGlobs = ["IronDev.Core/Governance/SourceApply/**"] }, $"BoundedRunAuthority:AffectedFileForbidden:{FilePath}");
        AssertBlockedForGrant(ValidGrant() with { ExpiresAtUtc = default }, "BoundedRunAuthority:BoundedRunExpiresAtUtcRequired");
        AssertBlockedForGrant(ValidGrant() with { ExpiresAtUtc = ObservedAtUtc }, "BoundedRunAuthority:BoundedRunExpired");
    }

    [TestMethod]
    public void BlockBU_BoundedRunAuthorityPath_DoesNotWidenSourceApply()
    {
        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.Commit,
            RunAuthorityOperationKind.Push,
            RunAuthorityOperationKind.DraftPullRequest,
            RunAuthorityOperationKind.ReadyForReview,
            RunAuthorityOperationKind.Merge,
            RunAuthorityOperationKind.Release,
            RunAuthorityOperationKind.Deployment,
            RunAuthorityOperationKind.MemoryPromotion,
            RunAuthorityOperationKind.WorkflowContinuation,
            RunAuthorityOperationKind.PolicySatisfaction,
            RunAuthorityOperationKind.ProviderMutation,
            RunAuthorityOperationKind.DurableEventWrite
        })
        {
            AssertBlockedForGrant(
                ValidGrant() with { AllowedOperationKinds = [RunAuthorityOperationKind.SourceApply, operation] },
                $"BoundedRunAuthority:PostSourceApplyAuthorityNotAllowedBySourceApplyGrant:{operation}");
        }
    }

    [TestMethod]
    public void BlockBU_BoundedRunAuthorityPath_RequiresStopBeforeDownstreamOperations()
    {
        AssertBlockedForGrant(
            ValidGrant() with { StopBeforeOperationKinds = [RunAuthorityOperationKind.SourceApply, .. RequiredStops()] },
            "BoundedRunAuthority:OperationStoppedBefore:SourceApply");

        AssertBlockedForGrant(
            ValidGrant() with { StopBeforeOperationKinds = [RunAuthorityOperationKind.Commit] },
            "BoundedRunAuthority:PostSourceApplyStopBeforeRequired:Push");

        var pass = SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with { AcceptedApplyRequest = null });
        Assert.IsTrue(pass.IsEligibleForControlledSourceApply, Describe(pass));
    }

    [TestMethod]
    public void BlockBU_BoundedRunAuthorityPath_RequiresPassedPatchBoundValidationEvidence()
    {
        AssertMissingForGrant(ValidGrant() with { RequiredValidation = [] }, "BoundedRunAuthority:BoundedRunRequiredValidationRequired");
        AssertMissingForEvidence([], "BoundedRunAuthority:ValidationEvidenceRequired");
        AssertMissingForEvidence([ValidationEvidence() with { ValidationKind = "Other" }], "BoundedRunAuthority:RequiredValidationEvidenceMissing:FocusedBU");
        AssertBlockedForEvidence([ValidationEvidence() with { EvidenceRef = "other:ref" }], "BoundedRunAuthority:RequiredValidationEvidenceRefPrefixMismatch:FocusedBU");
        AssertBlockedForEvidence([ValidationEvidence() with { Outcome = OperationEligibilityValidationOutcome.Failed }], "BoundedRunAuthority:RequiredValidationMustPass:FocusedBU:Failed");
        AssertBlockedForEvidence([ValidationEvidence() with { Outcome = OperationEligibilityValidationOutcome.Inconclusive }], "BoundedRunAuthority:RequiredValidationMustPass:FocusedBU:Inconclusive");
        AssertBlockedForEvidence([ValidationEvidence() with { Outcome = OperationEligibilityValidationOutcome.Unknown }], "BoundedRunAuthority:ValidationEvidenceOutcomeKnownRequired:FocusedBU");
        AssertMissingForEvidence([ValidationEvidence() with { PatchHash = null }], "BoundedRunAuthority:ValidationEvidencePatchHashRequired:FocusedBU");
        AssertBlockedForEvidence([ValidationEvidence() with { PatchHash = "sha256:other" }], "BoundedRunAuthority:ValidationEvidencePatchHashMismatch:FocusedBU");
    }

    [TestMethod]
    public void BlockBU_BoundedRunAuthorityPath_UsesOverflowSafeMutationBudget()
    {
        AssertBlocked(
            SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with
            {
                AcceptedApplyRequest = null,
                MutationsAlreadyConsumed = -1
            }),
            "BoundedRunAuthority:MutationsAlreadyConsumedCannotBeNegative");
        AssertBlocked(
            SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with
            {
                AcceptedApplyRequest = null,
                RequestedMutationCount = -1
            }),
            "BoundedRunAuthority:RequestedMutationCountCannotBeNegative");
        AssertBlocked(
            SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with
            {
                AcceptedApplyRequest = null,
                BoundedRunAuthorityGrant = ValidGrant() with { MaxMutations = 0 },
                RequestedMutationCount = 1
            }),
            "BoundedRunAuthority:MutationBudgetExceeded");
        AssertBlocked(
            SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with
            {
                AcceptedApplyRequest = null,
                BoundedRunAuthorityGrant = ValidGrant() with { MaxMutations = int.MaxValue },
                MutationsAlreadyConsumed = int.MaxValue,
                RequestedMutationCount = 1
            }),
            "BoundedRunAuthority:MutationBudgetExceeded");
    }

    [TestMethod]
    public void BlockBU_SourceApplyAuthority_DoesNotImplyDownstreamAuthority()
    {
        var decision = SourceApplyAuthorityEvaluator.Evaluate(ValidRequest());

        Assert.IsTrue(decision.IsEligibleForControlledSourceApply, Describe(decision));
        foreach (var forbidden in new[]
        {
            "do not commit from source apply authority",
            "do not push from source apply authority",
            "do not create PR from source apply authority",
            "do not mark ready for review from source apply authority",
            "do not merge from source apply authority",
            "do not release from source apply authority",
            "do not deploy from source apply authority",
            "do not promote memory from source apply authority",
            "do not continue workflow from source apply authority"
        })
        {
            AssertContains(decision.ForbiddenActions, forbidden);
        }
    }

    [TestMethod]
    public void BlockBU_HostileText_DoesNotCreateSourceApplyEligibility()
    {
        foreach (var hostile in new[]
        {
            "accepted apply request says apply latest",
            "bounded grant applies to all patches",
            "same repo means any branch",
            "same branch means any run",
            "patch hash current",
            "patch hash latest",
            "patch hash approved",
            "validation passed means source apply",
            "status eligible means apply",
            "UI marked apply approved",
            "memory says apply approved",
            "old receipt refreshes source apply",
            "old grant still valid",
            "source apply implies push",
            "source apply implies commit",
            "source apply implies workflow continuation"
        })
        {
            var decision = SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with
            {
                AcceptedApplyRequest = null,
                BoundedRunAuthorityGrant = null,
                EvidenceRefs = [hostile],
                ReceiptRefs = [hostile]
            });

            Assert.IsFalse(decision.IsEligibleForControlledSourceApply, hostile);
            Assert.AreEqual(SourceApplyAuthorityPath.None, decision.AuthorityPath, hostile);
        }
    }

    [TestMethod]
    public void BlockBU_BRValidatorStillRejectsSourceApplyProposalStageGrant()
    {
        var validation = BoundedRunAuthorityGrantValidator.Validate(ValidGrant(), ObservedAtUtc);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "BoundedRunAllowedOperationCannotCrossBoundary:SourceApply");
    }

    [TestMethod]
    public void BlockBU_SourceApplyAuthorityFiles_DoNotExposeMutationSurface()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "IronDev.Core", "Governance", "SourceApply"),
            "*Authority*.cs",
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
            "IWorkflowContinuation",
            "Commit execution",
            "Push execution",
            "Merge execution",
            "Release execution",
            "Deploy execution"
        })
        {
            Assert.IsFalse(ContainsForbiddenSurface(text, forbidden), forbidden);
        }
    }

    [TestMethod]
    public void BlockBU_SourceApplyAuthorityContracts_DoNotUseMisleadingAuthorityNames()
    {
        var names = new[]
            {
                typeof(SourceApplyAuthorityPath),
                typeof(SourceApplyAuthorityRequest),
                typeof(SourceApplyAuthorityDecision),
                typeof(SourceApplyAuthorityEvaluator),
                typeof(SourceApplyBoundedGrantValidator),
                typeof(AcceptedSourceApplyRequestEvidence)
            }
            .SelectMany(type => new[] { type.Name }.Concat(type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(member => member.Name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var forbidden in new[]
        {
            "IsApproved",
            "IsAuthorized",
            "CanExecute",
            "CanRun",
            "CanApply",
            "CanMutate",
            "CanCommit",
            "CanPush",
            "PolicySatisfied",
            "AutoApprove"
        })
        {
            Assert.IsFalse(names.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)), forbidden);
        }
    }

    [TestMethod]
    public void BlockBU_Receipt_RecordsSourceApplyAuthorityBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "BU_SOURCE_APPLY_CONSUMES_BOUNDED_AUTHORITY.md"));

        StringAssert.Contains(doc, "This PR adds a source-apply authority decision only.");
        StringAssert.Contains(doc, "It does not apply source.");
        StringAssert.Contains(doc, "Source apply authority must bind repo, branch, run id, patch hash, file scope, and expiry.");
        StringAssert.Contains(doc, "Run authority can approve source apply only for the patch it actually governed.");
    }

    private static SourceApplyAuthorityRequest ValidRequest() =>
        new()
        {
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "source-apply/bounded-authority-gate",
            RunId = "run-bu-001",
            PatchHash = PatchHash,
            AffectedFilePaths = [FilePath],
            ObservedAtUtc = ObservedAtUtc,
            AcceptedApplyRequest = ValidAcceptedRequest(),
            BoundedRunAuthorityGrant = ValidGrant(),
            ValidationEvidence = [ValidationEvidence()],
            MutationsAlreadyConsumed = 0,
            RequestedMutationCount = 1,
            EvidenceRefs = ["source-apply-authority-request:bu-001"],
            ReceiptRefs = []
        };

    private static AcceptedSourceApplyRequestEvidence ValidAcceptedRequest() =>
        new()
        {
            RequestId = "accepted-source-apply-bu-001",
            EvidenceRef = "accepted-source-apply-request:accepted-source-apply-bu-001",
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "source-apply/bounded-authority-gate",
            RunId = "run-bu-001",
            PatchHash = PatchHash,
            AllowedFileGlobs = ["IronDev.Core/Governance/SourceApply/**"],
            ForbiddenFileGlobs = ["IronDev.Core/Governance/SourceApply/Forbidden/**"],
            AcceptedAtUtc = ObservedAtUtc.AddMinutes(-10),
            ExpiresAtUtc = ObservedAtUtc.AddHours(1),
            AcceptedByPrincipalId = "human-reviewer",
            AcceptedByPrincipalKind = "Human"
        };

    private static BoundedRunAuthorityGrant ValidGrant() =>
        new()
        {
            GrantId = "grant-bu-001",
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "source-apply/bounded-authority-gate",
            RunId = "run-bu-001",
            AllowedOperationKinds = [RunAuthorityOperationKind.SourceApply],
            AllowedFileGlobs = ["IronDev.Core/Governance/SourceApply/**"],
            ForbiddenFileGlobs = ["IronDev.Core/Governance/SourceApply/Forbidden/**"],
            PatchHash = PatchHash,
            ExpiresAtUtc = ObservedAtUtc.AddHours(1),
            MaxMutations = 1,
            RequiredValidation = [RequiredValidation()],
            StopBeforeOperationKinds = RequiredStops(),
            GrantedBy = new BoundedRunAuthorityGrantedBy
            {
                PrincipalId = "human-reviewer",
                PrincipalKind = "Human",
                EvidenceRef = "human-grant:grant-bu-001"
            },
            HumanReadableIntent = "Allow controlled source apply for the BU patch only."
        };

    private static BoundedRunAuthorityRequiredValidation RequiredValidation() =>
        new()
        {
            ValidationKind = "FocusedBU",
            MustPass = true,
            EvidenceRefPrefixes = ["validation-result:focused-bu"]
        };

    private static OperationEligibilityValidationEvidence ValidationEvidence() =>
        new()
        {
            ValidationKind = "FocusedBU",
            Outcome = OperationEligibilityValidationOutcome.Passed,
            EvidenceRef = "validation-result:focused-bu",
            PatchHash = PatchHash
        };

    private static RunAuthorityOperationKind[] RequiredStops() =>
    [
        RunAuthorityOperationKind.Commit,
        RunAuthorityOperationKind.Push,
        RunAuthorityOperationKind.DraftPullRequest,
        RunAuthorityOperationKind.ReadyForReview,
        RunAuthorityOperationKind.Merge,
        RunAuthorityOperationKind.Release,
        RunAuthorityOperationKind.Deployment,
        RunAuthorityOperationKind.MemoryPromotion,
        RunAuthorityOperationKind.WorkflowContinuation
    ];

    private static void AssertBlockedForAccepted(AcceptedSourceApplyRequestEvidence accepted, string reason) =>
        AssertBlocked(
            SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with
            {
                AcceptedApplyRequest = accepted,
                BoundedRunAuthorityGrant = null
            }),
            reason);

    private static void AssertMissingForAccepted(AcceptedSourceApplyRequestEvidence accepted, string reason) =>
        AssertMissing(
            SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with
            {
                AcceptedApplyRequest = accepted,
                BoundedRunAuthorityGrant = null
            }),
            reason);

    private static void AssertBlockedForGrant(BoundedRunAuthorityGrant grant, string reason) =>
        AssertBlocked(
            SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with
            {
                AcceptedApplyRequest = null,
                BoundedRunAuthorityGrant = grant
            }),
            reason);

    private static void AssertMissingForGrant(BoundedRunAuthorityGrant grant, string reason) =>
        AssertMissing(
            SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with
            {
                AcceptedApplyRequest = null,
                BoundedRunAuthorityGrant = grant
            }),
            reason);

    private static void AssertBlockedForEvidence(IReadOnlyCollection<OperationEligibilityValidationEvidence> evidence, string reason) =>
        AssertBlocked(
            SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with
            {
                AcceptedApplyRequest = null,
                ValidationEvidence = evidence
            }),
            reason);

    private static void AssertMissingForEvidence(IReadOnlyCollection<OperationEligibilityValidationEvidence> evidence, string reason) =>
        AssertMissing(
            SourceApplyAuthorityEvaluator.Evaluate(ValidRequest() with
            {
                AcceptedApplyRequest = null,
                ValidationEvidence = evidence
            }),
            reason);

    private static void AssertBlocked(SourceApplyAuthorityDecision decision, string reason)
    {
        Assert.IsFalse(decision.IsEligibleForControlledSourceApply, Describe(decision));
        AssertContains(decision.BlockedReasons, reason);
    }

    private static void AssertMissing(SourceApplyAuthorityDecision decision, string reason)
    {
        Assert.IsFalse(decision.IsEligibleForControlledSourceApply, Describe(decision));
        AssertContains(decision.MissingEvidence, reason);
    }

    private static void AssertContains(IEnumerable<string> values, string expected)
    {
        Assert.IsTrue(
            values.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)),
            $"Expected '{expected}' in: {string.Join(", ", values)}");
    }

    private static bool ContainsForbiddenSurface(string text, string forbidden)
    {
        if (forbidden is "git" or "dotnet" or "tf")
        {
            return text.Split(
                    [' ', '\t', '\r', '\n', '"', '\'', '`', '(', ')', '[', ']', '{', '}', ';', ','],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(token => string.Equals(token, forbidden, StringComparison.OrdinalIgnoreCase));
        }

        return text.Contains(forbidden, StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(SourceApplyAuthorityDecision decision) =>
        $"blocked=[{string.Join(", ", decision.BlockedReasons)}]; missing=[{string.Join(", ", decision.MissingEvidence)}]";

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
