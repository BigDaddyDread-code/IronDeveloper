using System.Linq;
using IronDev.Core.Builder;
using IronDev.Core.Models;
using IronDev.Infrastructure.Services.CodeIntelligence;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CodexTicketGroundingTests
{
    [TestMethod]
    public void ContextQualityScorer_WhenSymbolsAndFilesExist_ReturnsHighScore()
    {
        var snapshot = new CodexProjectSnapshot
        {
            SolutionPath = @"C:\repo\IronDev\IronDev.sln",
            Files =
            [
                new IndexedFileSummary { FilePath = "IronDev.Core/Services/Tickets/ITicketService.cs" }
            ],
            Symbols =
            [
                new SemanticSymbolInfo
                {
                    Name = "ITicketService",
                    FullyQualifiedName = "IronDev.Core.Services.Tickets.ITicketService"
                }
            ],
            Decisions =
            [
                new ProjectDecisionSummary { Title = "Use semantic grounding" }
            ],
            ExistingTickets =
            [
                new ProjectTicketSummary { Title = "Add ticket generation" }
            ]
        };

        var result = new CodexContextQualityScorer().Score(snapshot);

        Assert.AreEqual(100, result.Score);
        Assert.AreEqual(0, result.MissingContextReasons.Count);
    }

    [TestMethod]
    public void ContextQualityScorer_WhenNoSymbols_ReturnsMissingReason()
    {
        var snapshot = new CodexProjectSnapshot
        {
            SolutionPath = @"C:\repo\IronDev\IronDev.sln",
            Files =
            [
                new IndexedFileSummary { FilePath = "IronDev.Core/Services/Tickets/ITicketService.cs" }
            ]
        };

        var result = new CodexContextQualityScorer().Score(snapshot);

        Assert.IsTrue(result.Score < 70);
        Assert.IsTrue(result.MissingContextReasons.Any(reason =>
            reason.Contains("No Roslyn semantic symbols", System.StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void GroundingValidator_WhenTicketReferencesKnownFileAndSymbol_ReturnsHighConfidence()
    {
        var snapshot = CreateGroundedSnapshot();
        var ticket = new CodebaseTicketDraft
        {
            Title = "Update ticket service",
            AffectedFiles = ["IronDev.Core/Services/Tickets/ITicketService.cs"],
            AffectedSymbols = ["IronDev.Core.Services.Tickets.ITicketService"]
        };

        var result = new CodexTicketGroundingValidator().ValidateAndScore([ticket], snapshot);
        var grounded = result.Single();

        Assert.IsTrue(grounded.ConfidenceScore >= 80);
        Assert.AreEqual(0, grounded.GroundingWarnings.Count);
    }

    [TestMethod]
    public void GroundingValidator_WhenTicketInventsFile_AddsWarning()
    {
        var snapshot = CreateGroundedSnapshot();
        var ticket = new CodebaseTicketDraft
        {
            Title = "Update invented file",
            AffectedFiles = ["IronDev.Core/Fake/FakeTicketMagicService.cs"],
            AffectedSymbols = ["ITicketService"]
        };

        var result = new CodexTicketGroundingValidator().ValidateAndScore([ticket], snapshot);

        Assert.IsTrue(result.Single().GroundingWarnings.Any(warning =>
            warning.Contains("FakeTicketMagicService.cs", System.StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ValidateAndScore_WhenTicketReferencesUnknownSymbol_AddsGroundingWarning()
    {
        var snapshot = CreateGroundedSnapshot();
        var ticket = new CodebaseTicketDraft
        {
            Title = "Update fake service",
            AffectedFiles = ["IronDev.Core/Services/Tickets/ITicketService.cs"],
            AffectedSymbols = ["FakeTicketMagicService"]
        };

        var result = new CodexTicketGroundingValidator().ValidateAndScore([ticket], snapshot);

        Assert.IsTrue(result.Single().GroundingWarnings.Any(warning =>
            warning.Contains("FakeTicketMagicService", System.StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void GroundingValidator_WhenTicketHasNoAffectedSymbols_AddsWarning()
    {
        var snapshot = CreateGroundedSnapshot();
        var ticket = new CodebaseTicketDraft
        {
            Title = "Update ticket service",
            AffectedFiles = ["IronDev.Core/Services/Tickets/ITicketService.cs"]
        };

        var result = new CodexTicketGroundingValidator().ValidateAndScore([ticket], snapshot);

        Assert.IsTrue(result.Single().GroundingWarnings.Any(warning =>
            warning.Contains("does not reference any affected symbols", System.StringComparison.OrdinalIgnoreCase)));
    }

    private static CodexProjectSnapshot CreateGroundedSnapshot()
        => new()
        {
            ContextQualityScore = 100,
            Files =
            [
                new IndexedFileSummary { FilePath = "IronDev.Core/Services/Tickets/ITicketService.cs" }
            ],
            Symbols =
            [
                new SemanticSymbolInfo
                {
                    Name = "ITicketService",
                    FullyQualifiedName = "IronDev.Core.Services.Tickets.ITicketService",
                    Signature = "public interface ITicketService",
                    FilePath = "IronDev.Core/Services/Tickets/ITicketService.cs"
                }
            ]
        };
}
