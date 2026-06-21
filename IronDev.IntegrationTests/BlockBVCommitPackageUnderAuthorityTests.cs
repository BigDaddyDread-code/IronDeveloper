using System.Reflection;
using IronDev.Core.Governance;
using BvCommitMessageEvidence = IronDev.Core.Governance.Commit.CommitMessageEvidence;
using BvCommitPackageBuilder = IronDev.Core.Governance.Commit.CommitPackageBuilder;
using BvCommitPackageRequest = IronDev.Core.Governance.Commit.CommitPackageRequest;
using BvCommitPackageResult = IronDev.Core.Governance.Commit.CommitPackageResult;
using BvCommitValidationRequirementEvidence = IronDev.Core.Governance.Commit.CommitValidationRequirementEvidence;
using BvExpectedDiffEvidence = IronDev.Core.Governance.Commit.ExpectedDiffEvidence;
using BvSourceApplyReceiptEvidence = IronDev.Core.Governance.Commit.SourceApplyReceiptEvidence;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBVCommitPackageUnderAuthorityTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);
    private const string Repository = "BigDaddyDread-code/IronDeveloper";
    private const string Branch = "commit/commit-package-under-authority";
    private const string RunId = "run-bv-001";
    private const string PatchHash = "sha256:abcdef1234567890";
    private const string DiffHash = "sha256:fedcba0987654321";
    private const string FilePath = "IronDev.Core/Governance/Commit/CommitPackageBuilder.cs";

    [TestMethod]
    public void BlockBV_HappyPath_CreatesEligibleCommitPackage()
    {
        var result = BvCommitPackageBuilder.Build(ValidRequest());

        Assert.IsTrue(result.IsPackageCreated, Describe(result));
        Assert.IsNotNull(result.Manifest);
        Assert.AreEqual("commit-package-bv-001", result.Manifest.PackageId);
        Assert.AreEqual(GovernedOperationState.Eligible, result.OperationStatus.State);
        Assert.AreEqual(RunAuthorityOperationKind.Commit.ToString(), result.OperationStatus.OperationKind);
        Assert.AreEqual(0, result.OperationStatus.BlockedReasons.Count);
        Assert.AreEqual(0, result.OperationStatus.MissingEvidence.Count);
        AssertContains(result.OperationStatus.ForbiddenActions, "do not commit from package alone");
        AssertContains(result.OperationStatus.ForbiddenActions, "do not push from commit package");
        AssertContains(result.OperationStatus.ForbiddenActions, "executor must independently re-check source apply receipt, diff, commit authority, message, validation, branch, and worktree state");
        AssertContains(result.OperationStatus.NextSafeActions, "request controlled commit executor review for independent authority re-check");
        AssertValid(result);
    }

    [TestMethod]
    public void BlockBV_SourceApplyReceipt_IsRequiredButNotCommitAuthority()
    {
        AssertBlocked(ValidRequest() with { SourceApplyReceipt = null }, "SourceApplyReceiptRequired");
        AssertMissing(ValidRequest() with { SourceApplyReceipt = null }, "source-apply-receipt");
        AssertBlocked(ValidRequest() with { SourceApplyReceipt = ValidSourceApplyReceipt() with { ReceiptRef = "receipt:wrong" } }, "SourceApplyReceiptRefInvalid");
        AssertBlocked(ValidRequest() with { SourceApplyReceipt = ValidSourceApplyReceipt() with { Repository = "other/repo" } }, "SourceApplyReceiptRepositoryMismatch");
        AssertBlocked(ValidRequest() with { SourceApplyReceipt = ValidSourceApplyReceipt() with { Branch = "other-branch" } }, "SourceApplyReceiptBranchMismatch");
        AssertBlocked(ValidRequest() with { SourceApplyReceipt = ValidSourceApplyReceipt() with { RunId = "other-run" } }, "SourceApplyReceiptRunIdMismatch");
        AssertBlocked(ValidRequest() with { SourceApplyReceipt = ValidSourceApplyReceipt() with { PatchHash = "sha256:other" } }, "SourceApplyReceiptPatchHashMismatch");
        AssertBlocked(ValidRequest() with { SourceApplyReceipt = ValidSourceApplyReceipt() with { AppliedFilePaths = ["../outside.cs"] } }, "SourceApplyReceiptAppliedFilePathsUnsafe:../outside.cs");

        var result = BvCommitPackageBuilder.Build(ValidRequest() with { CommitEligibilityDecision = null });
        AssertBlocked(result, "CommitOperationAuthorityRequired");
        AssertContains(result.OperationStatus.ForbiddenActions, "do not treat source apply receipt as commit authority");
    }

    [TestMethod]
    public void BlockBV_ExpectedDiff_MustBeCleanAndMatchSourceApplyReceipt()
    {
        AssertBlocked(ValidRequest() with { ExpectedDiff = null }, "ExpectedDiffEvidenceRequired");
        AssertMissing(ValidRequest() with { ExpectedDiff = null }, "expected-diff");
        AssertBlocked(ValidRequest() with { ExpectedDiff = ValidExpectedDiff() with { EvidenceRef = "diff:wrong" } }, "ExpectedDiffEvidenceRefInvalid");
        AssertBlocked(ValidRequest() with { ExpectedDiff = ValidExpectedDiff() with { Repository = "other/repo" } }, "ExpectedDiffRepositoryMismatch");
        AssertBlocked(ValidRequest() with { ExpectedDiff = ValidExpectedDiff() with { Branch = "other-branch" } }, "ExpectedDiffBranchMismatch");
        AssertBlocked(ValidRequest() with { ExpectedDiff = ValidExpectedDiff() with { RunId = "other-run" } }, "ExpectedDiffRunIdMismatch");
        AssertBlocked(ValidRequest() with { ExpectedDiff = ValidExpectedDiff() with { PatchHash = "sha256:other" } }, "ExpectedDiffPatchHashMismatch");
        AssertMissing(ValidRequest() with { ExpectedDiff = ValidExpectedDiff() with { ExpectedDiffHash = "" } }, "expected-diff-hash");
        AssertBlocked(ValidRequest() with { ExpectedDiff = ValidExpectedDiff() with { ExpectedDiffHash = "latest" } }, "ExpectedDiffHashInvalid");
        AssertMissing(ValidRequest() with { ExpectedDiff = ValidExpectedDiff() with { ExpectedChangedFilePaths = [] } }, "ExpectedDiffChangedFilePathsRequired");
        AssertBlocked(ValidRequest() with { ExpectedDiff = ValidExpectedDiff() with { ExpectedChangedFilePaths = ["C:/outside.cs"] } }, "ExpectedDiffChangedFilePathsUnsafe:C:/outside.cs");
        AssertBlocked(ValidRequest() with { ExpectedDiff = ValidExpectedDiff() with { IsCleanExpectedDiff = false } }, "ExpectedDiffNotClean");
        AssertBlocked(ValidRequest() with { ExpectedDiff = ValidExpectedDiff() with { ExpectedChangedFilePaths = ["Docs/receipts/BV.md"] } }, "ExpectedDiffDoesNotMatchSourceApplyReceipt");
    }

    [TestMethod]
    public void BlockBV_CommitOperationAuthority_CannotBeSatisfiedByOtherOperations()
    {
        AssertBlocked(ValidRequest() with { CommitEligibilityDecision = null }, "CommitOperationAuthorityRequired");
        AssertMissing(ValidRequest() with { CommitEligibilityDecision = null }, "commit-operation-eligibility-decision");

        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.SourceApply,
            RunAuthorityOperationKind.PatchPackageWrite,
            RunAuthorityOperationKind.Push
        })
        {
            AssertBlocked(
                ValidRequest() with { CommitEligibilityDecision = EligibleDecision(operation) },
                "CommitOperationAuthorityRequired");
        }

        AssertBlocked(
            ValidRequest() with { CommitEligibilityDecision = EligibleDecision(RunAuthorityOperationKind.Commit) with { IsEligibleUnderProfileAndGrant = false } },
            "CommitEligibilityDecisionNotEligible");
        AssertBlocked(
            ValidRequest() with { CommitEligibilityDecision = EligibleDecision(RunAuthorityOperationKind.Commit) with { BlockedReasons = ["GrantExpired"] } },
            "CommitEligibilityDecisionBlocked");
        AssertBlocked(
            ValidRequest() with { CommitEligibilityDecision = EligibleDecision(RunAuthorityOperationKind.Commit) with { MissingEvidence = ["commit-approval"] } },
            "CommitEligibilityDecisionMissingEvidence");
    }

    [TestMethod]
    public void BlockBV_MessageEvidence_MustBeExplicitAndSafe()
    {
        AssertBlocked(ValidRequest() with { MessageEvidence = null }, "CommitMessageEvidenceRequired");
        AssertMissing(ValidRequest() with { MessageEvidence = null }, "commit-message");
        AssertMissing(ValidRequest() with { MessageEvidence = ValidMessage() with { EvidenceRef = "" } }, "commit-message");
        AssertBlocked(ValidRequest() with { MessageEvidence = ValidMessage() with { EvidenceRef = "message:wrong" } }, "CommitMessageEvidenceRefInvalid");
        AssertMissing(ValidRequest() with { MessageEvidence = ValidMessage() with { Subject = "" } }, "commit-message-subject");
        AssertBlocked(ValidRequest() with { MessageEvidence = ValidMessage() with { Subject = "bad\nsubject" } }, "CommitMessageSubjectUnsafe");

        foreach (var source in new[] { "Memory", "ModelImplied", "UiState", "OldReceipt", "Inferred", "Unknown", "Agent" })
        {
            AssertBlocked(
                ValidRequest() with { MessageEvidence = ValidMessage() with { MessageSource = source } },
                $"CommitMessageSourceForbidden:{source}");
        }

        var reviewedProposal = BvCommitPackageBuilder.Build(ValidRequest() with { MessageEvidence = ValidMessage() with { MessageSource = "ReviewedProposal" } });
        Assert.IsTrue(reviewedProposal.IsPackageCreated, Describe(reviewedProposal));
    }

    [TestMethod]
    public void BlockBV_ValidationRequirement_MustBeSatisfiedOrExplicitlyBlocked()
    {
        var satisfied = BvCommitPackageBuilder.Build(ValidRequest() with
        {
            ValidationRequirement = new BvCommitValidationRequirementEvidence
            {
                IsSatisfied = true,
                IsExplicitlyBlocked = false,
                ValidationEvidenceRefs = ["validation-result:focused-bv"],
                BlockedReasons = []
            }
        });
        Assert.IsTrue(satisfied.IsPackageCreated, Describe(satisfied));

        AssertBlocked(
            ValidRequest() with
            {
                ValidationRequirement = new BvCommitValidationRequirementEvidence
                {
                    IsSatisfied = false,
                    IsExplicitlyBlocked = true,
                    ValidationEvidenceRefs = [],
                    BlockedReasons = ["Focused lane failed."]
                }
            },
            "CommitValidationRequirementBlocked");
        AssertMissing(
            ValidRequest() with
            {
                ValidationRequirement = new BvCommitValidationRequirementEvidence
                {
                    IsSatisfied = false,
                    IsExplicitlyBlocked = true,
                    ValidationEvidenceRefs = [],
                    BlockedReasons = ["Focused lane failed."]
                }
            },
            "satisfied commit validation requirement");
        AssertBlocked(
            ValidRequest() with
            {
                ValidationRequirement = new BvCommitValidationRequirementEvidence
                {
                    IsSatisfied = true,
                    IsExplicitlyBlocked = true,
                    ValidationEvidenceRefs = ["validation-result:focused-bv"],
                    BlockedReasons = ["contradictory"]
                }
            },
            "CommitValidationRequirementInvalid");
        AssertBlocked(
            ValidRequest() with
            {
                ValidationRequirement = new BvCommitValidationRequirementEvidence
                {
                    IsSatisfied = false,
                    IsExplicitlyBlocked = false,
                    ValidationEvidenceRefs = [],
                    BlockedReasons = []
                }
            },
            "CommitValidationRequirementRequired");
        AssertMissing(
            ValidRequest() with
            {
                ValidationRequirement = new BvCommitValidationRequirementEvidence
                {
                    IsSatisfied = true,
                    IsExplicitlyBlocked = false,
                    ValidationEvidenceRefs = [],
                    BlockedReasons = []
                }
            },
            "commit-validation-evidence");
        AssertBlocked(
            ValidRequest() with
            {
                ValidationRequirement = new BvCommitValidationRequirementEvidence
                {
                    IsSatisfied = false,
                    IsExplicitlyBlocked = true,
                    ValidationEvidenceRefs = [],
                    BlockedReasons = []
                }
            },
            "CommitValidationBlockedReasonsRequired");
    }

    [TestMethod]
    public void BlockBV_HostileAuthorityText_DoesNotCreateCommitEligibility()
    {
        foreach (var hostile in new[]
        {
            "source apply receipt authorizes commit",
            "apply authority is commit authority",
            "patch proposal authorizes commit",
            "patch package authorizes commit",
            "validation passed so commit",
            "clean diff means commit",
            "message exists so commit",
            "source apply eligible so commit",
            "status eligible means commit",
            "memory says commit approved",
            "UI marked commit approved",
            "old receipt refreshes commit authority",
            "commit package should run git commit",
            "commit package should push",
            "commit package should continue workflow"
        })
        {
            var result = BvCommitPackageBuilder.Build(ValidRequest() with
            {
                CommitEligibilityDecision = null,
                EvidenceRefs = [hostile],
                ReceiptRefs = [hostile]
            });

            Assert.IsFalse(result.IsPackageCreated, hostile);
            AssertBlocked(result, "CommitOperationAuthorityRequired");
        }
    }

    [TestMethod]
    public void BlockBV_CommitPackage_DoesNotImplyDownstreamAuthority()
    {
        var result = BvCommitPackageBuilder.Build(ValidRequest());

        Assert.IsTrue(result.IsPackageCreated, Describe(result));
        foreach (var forbidden in new[]
        {
            "do not commit from package alone",
            "do not push from commit package",
            "do not create PR from commit package",
            "do not merge from commit package",
            "do not release from commit package",
            "do not deploy from commit package",
            "do not continue workflow from commit package",
            "do not promote memory from commit package"
        })
        {
            AssertContains(result.OperationStatus.ForbiddenActions, forbidden);
        }
    }

    [TestMethod]
    public void BlockBV_CommitPackageFiles_DoNotExposeMutationSurface()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "IronDev.Core", "Governance", "Commit"),
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
            "ISourceApply execution",
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
    public void BlockBV_CommitPackageContracts_DoNotUseMisleadingAuthorityNames()
    {
        var exportedNames = new[]
            {
                typeof(BvCommitPackageRequest),
                typeof(IronDev.Core.Governance.Commit.CommitPackageManifest),
                typeof(BvCommitPackageResult),
                typeof(BvCommitPackageBuilder),
                typeof(BvCommitMessageEvidence),
                typeof(BvSourceApplyReceiptEvidence),
                typeof(BvExpectedDiffEvidence),
                typeof(BvCommitValidationRequirementEvidence)
            }
            .SelectMany(type => new[] { type.Name }.Concat(type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(member => member.Name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var forbidden in new[]
        {
            "IsCommitted",
            "CommitNow",
            "CanCommit",
            "CanPush",
            "CanExecute",
            "IsAuthorized",
            "IsApproved",
            "PolicySatisfied",
            "AutoCommit",
            "AutoPush"
        })
        {
            Assert.IsFalse(exportedNames.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)), forbidden);
        }
    }

    [TestMethod]
    public void BlockBV_Receipt_RecordsCommitPackageAuthorityBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "BV_COMMIT_PACKAGE_UNDER_AUTHORITY.md"));

        StringAssert.Contains(doc, "This PR adds a commit package under authority only.");
        StringAssert.Contains(doc, "It does not create a commit.");
        StringAssert.Contains(doc, "Source apply receipt is required but is not commit authority.");
        StringAssert.Contains(doc, "Commit operation authority is required separately.");
        StringAssert.Contains(doc, "Apply authority is not commit authority.");
    }

    private static BvCommitPackageRequest ValidRequest() =>
        new()
        {
            PackageId = "commit-package-bv-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            SourceApplyReceipt = ValidSourceApplyReceipt(),
            ExpectedDiff = ValidExpectedDiff(),
            CommitEligibilityDecision = EligibleDecision(RunAuthorityOperationKind.Commit),
            MessageEvidence = ValidMessage(),
            ValidationRequirement = ValidValidationRequirement(),
            ObservedAtUtc = ObservedAtUtc,
            EvidenceRefs =
            [
                "operation-eligibility-decision:commit-bv-001",
                "commit-package-request:bv-001"
            ],
            ReceiptRefs = []
        };

    private static BvSourceApplyReceiptEvidence ValidSourceApplyReceipt() =>
        new()
        {
            ReceiptRef = "source-apply-receipt:source-apply-bv-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            AppliedFilePaths = [FilePath],
            AppliedAtUtc = ObservedAtUtc.AddMinutes(-20),
            AppliedByAuthorityPath = "BoundedRunAuthority"
        };

    private static BvExpectedDiffEvidence ValidExpectedDiff() =>
        new()
        {
            EvidenceRef = "expected-diff:diff-bv-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            ExpectedDiffHash = DiffHash,
            ExpectedChangedFilePaths = [FilePath],
            IsCleanExpectedDiff = true
        };

    private static BvCommitMessageEvidence ValidMessage() =>
        new()
        {
            EvidenceRef = "commit-message:message-bv-001",
            Subject = "docs/test(governance): add commit package authority boundary",
            Body = "Commit package evidence only.",
            MessageSource = "HumanProvided"
        };

    private static BvCommitValidationRequirementEvidence ValidValidationRequirement() =>
        new()
        {
            IsSatisfied = true,
            IsExplicitlyBlocked = false,
            ValidationEvidenceRefs = ["validation-result:focused-bv"],
            BlockedReasons = []
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

    private static void AssertBlocked(BvCommitPackageRequest request, string reason) =>
        AssertBlocked(BvCommitPackageBuilder.Build(request), reason);

    private static void AssertMissing(BvCommitPackageRequest request, string missing) =>
        AssertMissing(BvCommitPackageBuilder.Build(request), missing);

    private static void AssertBlocked(BvCommitPackageResult result, string reason)
    {
        Assert.IsFalse(result.IsPackageCreated, Describe(result));
        Assert.AreEqual(GovernedOperationState.Blocked, result.OperationStatus.State);
        AssertContains(result.OperationStatus.BlockedReasons, reason);
        AssertValid(result);
    }

    private static void AssertMissing(BvCommitPackageResult result, string missing)
    {
        Assert.IsFalse(result.IsPackageCreated, Describe(result));
        Assert.AreEqual(GovernedOperationState.Blocked, result.OperationStatus.State);
        AssertContains(result.OperationStatus.MissingEvidence, missing);
        AssertValid(result);
    }

    private static void AssertValid(BvCommitPackageResult result)
    {
        Assert.IsTrue(result.StatusValidation.IsValid, string.Join(", ", result.StatusValidation.Issues.Concat(result.StatusValidation.RedFlags)));
        Assert.AreEqual(0, result.StatusValidation.Boundary.CanCommit ? 1 : 0);
        Assert.AreEqual(0, result.StatusValidation.Boundary.CanPush ? 1 : 0);
        Assert.AreEqual(0, result.StatusValidation.Boundary.CanContinueWorkflow ? 1 : 0);
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

    private static string Describe(BvCommitPackageResult result) =>
        $"blocked=[{string.Join(", ", result.OperationStatus.BlockedReasons)}]; missing=[{string.Join(", ", result.OperationStatus.MissingEvidence)}]; issues=[{string.Join(", ", result.Issues)}]";

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
