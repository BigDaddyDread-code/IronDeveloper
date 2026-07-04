using IronDev.Core.Builder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Builder;

[TestClass]
[TestCategory("TesterCoverage")]
[TestCategory("Contract")]
[TestCategory("Governance")]
[TestCategory("Boundary")]
public sealed class TesterCriterionCoverageContractTests
{
    [TestMethod]
    public void TesterCoveragePackage_WithCriterionToTestMatrix_PassesValidation()
    {
        var result = TesterCriterionCoverageValidator.Validate(ValidPackage());

        Assert.IsTrue(result.IsValid, Format(result));
    }

    [TestMethod]
    public void TesterCoveragePackage_CriterionWithoutTestOrGap_IsRejected()
    {
        var result = TesterCriterionCoverageValidator.Validate(ValidPackage() with
        {
            Coverage = [],
            UncoveredCriteria = []
        });

        AssertHasIssue(result, TesterCriterionCoverageValidator.CriterionMissingTrace);
    }

    [TestMethod]
    public void TesterCoveragePackage_TestWithoutCriterion_IsRejected()
    {
        var result = TesterCriterionCoverageValidator.Validate(ValidPackage() with
        {
            Tests =
            [
                TestCase("T-1", [])
            ]
        });

        AssertHasIssue(result, TesterCriterionCoverageValidator.TestMissingCriterion);
    }

    [TestMethod]
    public void TesterCoveragePackage_UnknownCriterionReference_IsRejected()
    {
        var fromTest = TesterCriterionCoverageValidator.Validate(ValidPackage() with
        {
            Tests = [TestCase("T-1", ["AC-MISSING"])]
        });
        AssertHasIssue(fromTest, TesterCriterionCoverageValidator.UnknownCriterion);

        var fromCoverage = TesterCriterionCoverageValidator.Validate(ValidPackage() with
        {
            Coverage = [CoverageRow("AC-MISSING", ["T-1"])]
        });
        AssertHasIssue(fromCoverage, TesterCriterionCoverageValidator.UnknownCriterion);

        var fromUncovered = TesterCriterionCoverageValidator.Validate(ValidPackage() with
        {
            UncoveredCriteria = [Uncovered("AC-MISSING")]
        });
        AssertHasIssue(fromUncovered, TesterCriterionCoverageValidator.UnknownCriterion);
    }

    [TestMethod]
    public void TesterCoveragePackage_UnknownTestReference_IsRejected()
    {
        var result = TesterCriterionCoverageValidator.Validate(ValidPackage() with
        {
            Coverage = [CoverageRow("AC-1", ["T-MISSING"])]
        });

        AssertHasIssue(result, TesterCriterionCoverageValidator.UnknownTest);
    }

    [TestMethod]
    public void TesterCoveragePackage_CriterionCannotBeBothCoveredAndUncovered()
    {
        var result = TesterCriterionCoverageValidator.Validate(ValidPackage() with
        {
            UncoveredCriteria = [Uncovered("AC-1")]
        });

        AssertHasIssue(result, TesterCriterionCoverageValidator.ConflictingStatus);
    }

    [TestMethod]
    public void TesterCoveragePackage_ExplicitUncoveredCriterion_IsValidButNamesHumanDecision()
    {
        var package = ValidPackage() with
        {
            Tests = [],
            Coverage = [],
            UncoveredCriteria =
            [
                new()
                {
                    CriterionId = "AC-1",
                    Reason = "Criterion requires external system not available in skeleton test lane.",
                    RequiredHumanDecision = "Human must accept or resolve this test coverage gap before gate."
                }
            ]
        };

        var result = TesterCriterionCoverageValidator.Validate(package);

        Assert.IsTrue(result.IsValid, Format(result));
        Assert.AreEqual("AC-1", package.UncoveredCriteria[0].CriterionId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(package.UncoveredCriteria[0].RequiredHumanDecision));
        StringAssert.Contains(package.UncoveredCriteria[0].Boundary, "not approval");
    }

    [TestMethod]
    [DataRow("skip")]
    [DataRow("later")]
    [DataRow("not needed")]
    [DataRow("too hard")]
    [DataRow("none")]
    public void TesterCoveragePackage_VagueUncoveredReason_IsRejected(string reason)
    {
        var result = TesterCriterionCoverageValidator.Validate(ValidPackage() with
        {
            Tests = [],
            Coverage = [],
            UncoveredCriteria =
            [
                new()
                {
                    CriterionId = "AC-1",
                    Reason = reason,
                    RequiredHumanDecision = "Human must resolve the coverage gap before gate."
                }
            ]
        });

        AssertHasIssue(result, TesterCriterionCoverageValidator.UncoveredReasonRequired);
    }

    [TestMethod]
    public void SkeletonTestAuthoringRequest_HasNoBuilderOutputChannel()
    {
        var properties = typeof(SkeletonTestAuthoringRequest)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[] { "AcceptanceCriteria", "Problem", "ProjectId", "TicketId", "TicketTitle" },
            properties);

        foreach (var forbidden in new[]
        {
            "Proposal",
            "Diff",
            "Patch",
            "Change",
            "Content",
            "File",
            "Builder",
            "Implementation",
            "FullContentAfter"
        })
        {
            Assert.IsFalse(properties.Any(property => property.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                $"SkeletonTestAuthoringRequest exposes forbidden Builder-output channel fragment: {forbidden}");
        }
    }

    [TestMethod]
    public void TesterCoveragePackage_DoesNotExposeAuthorityOrProofSurface()
    {
        var allowedFragments = new[] { "Coverage", "CoverageStatus" };
        var forbiddenFragments = new[]
        {
            "Approved",
            "Approval",
            "PolicySatisfied",
            "ReadyToApply",
            "ReadyToRelease",
            "ReadyToDeploy",
            "CriticSatisfied",
            "ContractSatisfied",
            "TestsPassed",
            "TestProof",
            "Authority",
            "Permission"
        };

        var names = new[]
        {
            typeof(TesterCriterionCoveragePackage),
            typeof(TesterCriterionCoverageMatrix),
            typeof(TesterAuthoredTestCase),
            typeof(TesterCriterionCoverageRow),
            typeof(TesterUncoveredCriterion)
        }
        .SelectMany(type => type.GetProperties().Select(property => $"{type.Name}.{property.Name}"))
        .ToArray();

        foreach (var name in names)
        {
            if (allowedFragments.Any(fragment => name.Contains(fragment, StringComparison.Ordinal)))
                continue;

            foreach (var forbidden in forbiddenFragments)
            {
                Assert.IsFalse(name.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"Forbidden authority/proof surface found: {name}");
            }
        }
    }

    [TestMethod]
    public void TesterCoveragePackage_BoundarySaysCoverageIsNotProofOrAuthority()
    {
        var boundary = TesterCriterionCoveragePackage.BoundaryText;
        var repositoryRoot = FindRepositoryRoot();
        var receipt = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Docs",
            "receipts",
            "P3_2_TESTER_CRITERION_TEST_COVERAGE_CONTRACT.md"));

        foreach (var fragment in new[]
        {
            "maps acceptance criteria to test intent",
            "not test execution",
            "not test proof",
            "not approval",
            "not critic review",
            "not policy satisfaction",
            "not workflow continuation",
            "not source apply permission",
            "not release readiness",
            "not deployment readiness"
        })
        {
            StringAssert.Contains(boundary, fragment);
            StringAssert.Contains(receipt, fragment);
        }

        AssertHasIssue(
            TesterCriterionCoverageValidator.Validate(ValidPackage() with { Boundary = "Coverage matrix." }),
            TesterCriterionCoverageValidator.BoundaryMissing);
    }

    [TestMethod]
    public void TesterCoveragePackage_RejectsAuthorityClaimTextAndBuilderVisibility()
    {
        var authority = TesterCriterionCoverageValidator.Validate(ValidPackage() with
        {
            Tests =
            [
                TestCase("T-1", ["AC-1"]) with
                {
                    Intent = "These tests passed, therefore the work is approved."
                }
            ]
        });
        AssertHasIssue(authority, TesterCriterionCoverageValidator.ForbiddenAuthorityClaim);

        var builderVisible = TesterCriterionCoverageValidator.Validate(ValidPackage() with
        {
            Tests =
            [
                TestCase("T-1", ["AC-1"]) with
                {
                    SawBuilderDiff = true,
                    SawBuilderPatch = true,
                    SawBuilderReasoning = true
                }
            ]
        });
        AssertHasIssue(builderVisible, TesterCriterionCoverageValidator.BuilderVisibilityForbidden);
    }

    [TestMethod]
    public void TesterCoveragePackage_SourceFilesIntroduceNoExecutionApplyApprovalCriticOrPersistenceSurface()
    {
        var repositoryRoot = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(repositoryRoot, "IronDev.Core", "Builder", "TesterCriterionCoverageModels.cs"),
            Path.Combine(repositoryRoot, "IronDev.Core", "Builder", "TesterCriterionCoverageValidator.cs")
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var forbidden in new[]
            {
                "ProcessStartInfo",
                "dotnet test",
                "RunTestsAsync",
                "ApplyAsync",
                "ContinueAsync",
                "TransitionAsync",
                "MutationLease",
                "AcceptedApproval",
                "SatisfyPolicy",
                "RunCritic",
                "RequestCriticReview",
                "CreateCriticReview",
                "File.WriteAllText",
                "SqlConnection",
                "ControllerBase"
            })
            {
                Assert.IsFalse(text.Contains(forbidden, StringComparison.Ordinal),
                    $"Forbidden runtime token '{forbidden}' found in {file}.");
            }
        }
    }

    private static TesterCriterionCoveragePackage ValidPackage() =>
        new()
        {
            PackageId = "tester-coverage:p3-2",
            TicketId = 320,
            ProjectId = 17,
            ContractId = "contract:p3-2",
            ContractHash = "sha256:p3-2-contract",
            TesterAgentId = "builtin.testing",
            TesterRunId = "tester-run:p3-2",
            Criteria =
            [
                new()
                {
                    CriterionId = "AC-1",
                    Description = "The coverage package maps every criterion to authored test intent.",
                    Measure = "AC-1 appears in a covered row with at least one authored test id."
                }
            ],
            Tests =
            [
                TestCase("T-1", ["AC-1"])
            ],
            Coverage =
            [
                CoverageRow("AC-1", ["T-1"])
            ],
            KnownRisks = ["Coverage intent still requires later execution evidence."],
            KnownGaps = ["No test execution occurs in this package."]
        };

    private static TesterAuthoredTestCase TestCase(string testId, IReadOnlyList<string> criteria) =>
        new()
        {
            TestId = testId,
            RelativePath = $"IronDev.Tests/{testId}.cs",
            TestName = $"{testId}_covers_criteria",
            Intent = "These tests intend to cover these criteria.",
            Content = "Assert.IsTrue(true);",
            CoveredCriterionIds = criteria
        };

    private static TesterCriterionCoverageRow CoverageRow(string criterionId, IReadOnlyList<string> testIds) =>
        new()
        {
            CriterionId = criterionId,
            TestIds = testIds,
            CoverageStatus = TesterCoverageStatuses.Covered,
            Notes = "Mapped from criterion id to test intent id."
        };

    private static TesterUncoveredCriterion Uncovered(string criterionId) =>
        new()
        {
            CriterionId = criterionId,
            Reason = "Criterion needs human clarification before test intent can be authored.",
            RequiredHumanDecision = "Human must clarify this criterion before gate."
        };

    private static void AssertHasIssue(TesterCriterionCoverageValidationResult result, string issue)
    {
        CollectionAssert.Contains(result.Issues, issue, Format(result));
    }

    private static string Format(TesterCriterionCoverageValidationResult result) =>
        string.Join(Environment.NewLine, result.Issues);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "IronDev.Core")) &&
                Directory.Exists(Path.Combine(directory.FullName, "IronDev.IntegrationTests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AIDeveloper repository root.");
    }
}
