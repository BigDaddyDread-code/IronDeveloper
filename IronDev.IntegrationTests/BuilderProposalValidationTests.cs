using System;
using System.IO;
using System.Linq;
using IronDev.Core.Models;
using IronDev.Infrastructure.Builder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BuilderProposalValidationTests
{
    [TestMethod]
    public void Validate_EmptyProposal_AddsBlockingIssue()
    {
        var proposal = MakeProposal();

        BuilderProposalValidator.Validate(proposal);

        Assert.IsFalse(proposal.IsAllValid);
        Assert.IsTrue(proposal.ValidationIssues.Any(issue => issue.Contains("any file changes", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Validate_PathTraversal_BlocksFileChange()
    {
        var proposal = MakeProposal(new ProposedFileChange
        {
            FilePath = @"..\IronDeveloper\App.xaml.cs",
            Description = "Unsafe host edit",
            Diff = "@@"
        });

        BuilderProposalValidator.Validate(proposal);

        Assert.IsFalse(proposal.IsAllValid);
        Assert.IsFalse(proposal.Changes[0].IsValid);
        StringAssert.Contains(proposal.Changes[0].ValidationMessage, "Path traversal");
    }

    [TestMethod]
    public void Validate_PersistenceTicketWithoutDataAccessFiles_AddsBlockingIssue()
    {
        var proposal = MakeProposal(new ProposedFileChange
        {
            FilePath = @"BookSeller.Core\Services\BookService.cs",
            Description = "Add persistence",
            Diff = "@@",
            FullContentAfter = """
                namespace BookSeller.Core.Services;
                public class BookService : IBookService
                {
                }
                """
        });
        proposal.OriginalRequest = "Persist books to a SQL database using Dapper.";

        BuilderProposalValidator.Validate(proposal);

        Assert.IsFalse(proposal.IsAllValid);
        Assert.IsTrue(proposal.ValidationIssues.Any(issue => issue.Contains("data access", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Validate_TestsRequestedWithoutTestFiles_AddsBlockingIssue()
    {
        var proposal = MakeProposal(new ProposedFileChange
        {
            FilePath = @"BookSeller.Core\Services\BookService.cs",
            Description = "Add sorting",
            Diff = "@@",
            FullContentAfter = """
                namespace BookSeller.Core.Services;
                public class BookService : IBookService
                {
                }
                """
        });
        proposal.OriginalRequest = "Add sorting and unit tests.";

        BuilderProposalValidator.Validate(proposal);

        Assert.IsFalse(proposal.IsAllValid);
        Assert.IsTrue(proposal.ValidationIssues.Any(issue => issue.Contains("tests", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Validate_ProductionCodeWithoutTests_AddsWarningOnly()
    {
        var proposal = MakeProposal(new ProposedFileChange
        {
            FilePath = @"BookSeller.Core\Services\BookService.cs",
            Description = "Add sorting",
            Diff = "@@",
            FullContentAfter = """
                namespace BookSeller.Core.Services;
                public class BookService : IBookService
                {
                }
                """
        });
        proposal.OriginalRequest = "Add sorting.";

        BuilderProposalValidator.Validate(proposal);

        Assert.IsTrue(proposal.IsAllValid);
        Assert.IsTrue(proposal.HasValidationWarnings);
        Assert.IsTrue(proposal.ValidationWarnings.Any(warning => warning.Contains("without adding or updating tests", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Validate_CompleteSortingProposal_Passes()
    {
        var proposal = MakeProposal(
            new ProposedFileChange
            {
                FilePath = @"BookSeller.Core\Services\BookService.cs",
                Description = "Add sorting",
                Diff = "@@",
                FullContentAfter = """
                    namespace BookSeller.Core.Services;
                    public class BookService : IBookService
                    {
                    }
                    """
            },
            new ProposedFileChange
            {
                FilePath = @"BookSeller.Tests\BookServiceTests.cs",
                Description = "Cover sorting",
                Diff = "@@",
                FullContentAfter = """
                    using Xunit;
                    namespace BookSeller.Tests;
                    public class BookServiceTests
                    {
                    }
                    """
            });
        proposal.OriginalRequest = "Add sorting and unit tests.";

        BuilderProposalValidator.Validate(proposal);

        Assert.IsTrue(proposal.IsAllValid);
        Assert.IsFalse(proposal.HasValidationIssues);
    }

    private static BuilderProposal MakeProposal(params ProposedFileChange[] changes)
    {
        return new BuilderProposal
        {
            ProjectId = 1,
            ProjectName = "BookSeller",
            ProjectRoot = Path.Combine(Path.GetTempPath(), "BookSeller"),
            OriginalRequest = "Add sorting.",
            Summary = "Add sorting.",
            Changes = changes.ToList()
        };
    }
}
