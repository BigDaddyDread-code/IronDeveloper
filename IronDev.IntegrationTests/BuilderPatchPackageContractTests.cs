using System.Reflection;
using IronDev.Core.Builder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("Builder")]
[TestCategory("Contract")]
[TestCategory("Governance")]
public sealed class BuilderPatchPackageContractTests
{
    [TestMethod]
    public void BuilderPatchPackage_WithContractTrace_PassesValidation()
    {
        var package = ValidPackage();

        var result = BuilderPatchPackageValidator.Validate(package);

        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues));
    }

    [TestMethod]
    public void BuilderPatchPackage_ChangeWithoutContractTrace_IsRejected()
    {
        var package = ValidPackage(ValidChange(acceptanceCriteria: [], scopeItems: [], supportReasons: []));

        var result = BuilderPatchPackageValidator.Validate(package);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Contains("src/SortService.cs", StringComparison.OrdinalIgnoreCase) &&
            issue.Contains("trace", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BuilderPatchPackage_VagueSupportReason_IsRejected()
    {
        foreach (var reason in new[] { "cleanup", "misc", "nice to have", "while I was here", "general improvement" })
        {
            var package = ValidPackage(ValidChange(acceptanceCriteria: [], scopeItems: [], supportReasons: [reason]));

            var result = BuilderPatchPackageValidator.Validate(package);

            Assert.IsFalse(result.IsValid, reason);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("too vague", StringComparison.OrdinalIgnoreCase)), reason);
        }
    }

    [TestMethod]
    public void BuilderPatchPackage_ProductionSupportReasonWithoutCriterionReference_IsRejected()
    {
        var invalid = ValidPackage(ValidChange(
            acceptanceCriteria: [],
            scopeItems: [],
            supportReasons: ["Refactor helper"]));

        var invalidResult = BuilderPatchPackageValidator.Validate(invalid);

        Assert.IsFalse(invalidResult.IsValid);
        Assert.IsTrue(invalidResult.Issues.Any(issue =>
            issue.Contains("Production code changes", StringComparison.OrdinalIgnoreCase) &&
            issue.Contains("criterion or scope", StringComparison.OrdinalIgnoreCase)));

        var valid = ValidPackage(ValidChange(
            acceptanceCriteria: [],
            scopeItems: [],
            supportReasons: ["Refactor helper to support AC-2 validation path"]));

        var validResult = BuilderPatchPackageValidator.Validate(valid);

        Assert.IsTrue(validResult.IsValid, string.Join(Environment.NewLine, validResult.Issues));
    }

    [TestMethod]
    public void BuilderPatchPackage_DoesNotExposeAuthorityOrReadinessSurface()
    {
        var allowedValidationNames = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(BuilderPatchPackage.ValidationIssues),
            nameof(BuilderPatchPackage.ValidationWarnings),
            nameof(BuilderPatchPackageChange.ValidationMessage),
            nameof(BuilderPatchPackageChange.IsValid)
        };
        var forbiddenFragments = new[]
        {
            "Approved",
            "Approval",
            "Satisfied",
            "PolicySatisfied",
            "ReadyToApply",
            "ReadyToRelease",
            "ReadyToDeploy",
            "CriticSatisfied",
            "TestsPassed",
            "Validated",
            "Authority",
            "Permission"
        };

        foreach (var property in typeof(BuilderPatchPackage).GetProperties().Concat(typeof(BuilderPatchPackageChange).GetProperties()))
        {
            if (allowedValidationNames.Contains(property.Name))
                continue;

            Assert.IsFalse(forbiddenFragments.Any(fragment => property.Name.Contains(fragment, StringComparison.Ordinal)),
                $"Builder package DTO property must not look like authority/readiness: {property.DeclaringType?.Name}.{property.Name}");
        }
    }

    [TestMethod]
    public void BuilderPatchPackage_PathSafetyValidation_IsPreserved()
    {
        AssertInvalidPath(@"C:\repo\src\SortService.cs", "Absolute");
        AssertInvalidPath(@"..\OtherRepo\Program.cs", "Path traversal");
        AssertInvalidPath(@"src/../../Program.cs", "Path traversal");
        AssertInvalidPath(@"src/SortService.exe", "not allowed");

        var deletion = ValidPackage(ValidChange(isDeletion: true));
        var deletionResult = BuilderPatchPackageValidator.Validate(deletion);

        Assert.IsFalse(deletionResult.IsValid);
        Assert.IsTrue(deletionResult.Issues.Any(issue => issue.Contains("deletions are not allowed", StringComparison.OrdinalIgnoreCase)));

        var emptyContent = ValidPackage(ValidChange(diff: string.Empty, fullContentAfter: string.Empty));
        var emptyContentResult = BuilderPatchPackageValidator.Validate(emptyContent);

        Assert.IsFalse(emptyContentResult.IsValid);
        Assert.IsTrue(emptyContentResult.Issues.Any(issue => issue.Contains("diff or full replacement content", StringComparison.OrdinalIgnoreCase)));

        var sample = ValidPackage(ValidChange(fullContentAfter: "namespace Sample; public sealed class Example { }"));
        var sampleResult = BuilderPatchPackageValidator.Validate(sample);

        Assert.IsFalse(sampleResult.IsValid);
        Assert.IsTrue(sampleResult.Issues.Any(issue => issue.Contains("generic sample code", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BuilderPatchPackage_BoundarySaysPatchIsNotAuthority()
    {
        var package = ValidPackage();
        var result = BuilderPatchPackageValidator.Validate(package);

        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues));
        StringAssert.Contains(package.Boundary, "implementation attempt");
        StringAssert.Contains(package.Boundary, "confirmed contract");
        StringAssert.Contains(package.Boundary, "not approval");
        StringAssert.Contains(package.Boundary, "not test proof");
        StringAssert.Contains(package.Boundary, "critic review");
        StringAssert.Contains(package.Boundary, "not policy satisfaction");
        StringAssert.Contains(package.Boundary, "source apply permission");
        StringAssert.Contains(package.Boundary, "release readiness");
        StringAssert.Contains(package.Boundary, "deployment readiness");
        StringAssert.Contains(package.Changes[0].Boundary, "trace to the confirmed contract");
        StringAssert.Contains(package.Changes[0].Boundary, "not proof");
    }

    [TestMethod]
    public void BuilderPatchPackage_RejectsAuthorityClaimText()
    {
        var package = ValidPackage(ValidChange(traceSummary: "tests passed therefore ready to apply"));

        var result = BuilderPatchPackageValidator.Validate(package);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => issue.Contains("authority claim marker", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BuilderPatchPackage_ProductionFilesIntroduceNoApplyOrCriticExecutionSurface()
    {
        var root = FindRepositoryRoot();
        var productionFiles = new[]
        {
            Path.Combine(root, "IronDev.Core", "Builder", "BuilderPatchPackageModels.cs"),
            Path.Combine(root, "IronDev.Core", "Builder", "BuilderPatchPackageValidator.cs")
        };
        var forbiddenMarkers = new[]
        {
            "ApplyAsync",
            "ContinueAsync",
            "TransitionAsync",
            "Acquire",
            "MutationLease",
            "GitCommit",
            "Push",
            "PullRequest",
            "AcceptedApproval",
            "SatisfyPolicy",
            "RunCritic",
            "RequestCriticReview",
            "CreateCriticReview",
            "TestsPassed",
            "ReadyToApply",
            "ReadyToRelease",
            "ReadyToDeploy"
        };

        foreach (var file in productionFiles)
        {
            var source = File.ReadAllText(file);
            foreach (var forbidden in forbiddenMarkers)
            {
                Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal),
                    $"{Path.GetFileName(file)} must not introduce source/apply/critic/authority surface marker {forbidden}.");
            }
        }
    }

    private static void AssertInvalidPath(string path, string expectedIssue)
    {
        var package = ValidPackage(ValidChange(filePath: path));

        var result = BuilderPatchPackageValidator.Validate(package);

        Assert.IsFalse(result.IsValid, path);
        Assert.IsTrue(result.Issues.Any(issue => issue.Contains(expectedIssue, StringComparison.OrdinalIgnoreCase)),
            string.Join(Environment.NewLine, result.Issues));
    }

    private static BuilderPatchPackage ValidPackage(BuilderPatchPackageChange? change = null) =>
        new()
        {
            PackageId = "builder-pkg-p3-3",
            TicketId = 42,
            ProjectId = 7,
            ContractId = "contract-1",
            ContractHash = new string('a', 64),
            ContractTitle = "Add sorting to book list",
            BuilderAgentId = "builder-agent-p3-3",
            BuilderRunId = "builder-run-p3-3",
            Summary = "Implement the confirmed sorting contract.",
            Rationale = "The implementation follows the accepted criterion trace.",
            Changes = [change ?? ValidChange()]
        };

    private static BuilderPatchPackageChange ValidChange(
        string filePath = "src/SortService.cs",
        string diff = "@@ add sorting",
        string? fullContentAfter = "namespace BookSeller.Core; public sealed class SortService { }",
        bool isDeletion = false,
        string traceSummary = "Covers AC-1.",
        IReadOnlyList<string>? acceptanceCriteria = null,
        IReadOnlyList<string>? scopeItems = null,
        IReadOnlyList<string>? supportReasons = null) =>
        new()
        {
            ChangeId = "change-1",
            FilePath = filePath,
            Description = "Implement sorting behavior.",
            Diff = diff,
            FullContentAfter = fullContentAfter,
            IsDeletion = isDeletion,
            CoveredAcceptanceCriterionIds = acceptanceCriteria ?? ["AC-1"],
            CoveredScopeItemIds = scopeItems ?? [],
            SupportReasons = supportReasons ?? [],
            TraceSummary = traceSummary,
            IsValid = true
        };

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
