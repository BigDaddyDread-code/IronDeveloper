using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.CodeIntelligence;
using IronDev.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace IronDev.IntegrationTests;

/// <summary>
/// Failure-path unit tests for the refactored code-intelligence pipeline.
/// These run in isolation using mocks — no database required.
/// </summary>
[TestClass]
public class CodebaseTicketGeneratorFailureTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static CodexProjectSnapshot EmptySnapshot(int projectId = 1) => new()
    {
        ProjectId             = projectId,
        ProjectName           = "TestProject",
        SolutionPath          = "C:/test/TestProject.sln",
        ContextQualityScore   = 75,
        Files                 = [],
        Symbols               = [],
        ExistingTickets       = [],
        Decisions             = [],
        LanguageQuality       = [],
        MissingContextReasons = [],
        SemanticWarnings      = []
    };

    private static (Mock<IProjectMemoryService> mock, Mock<ILlmTraceService> trace) CreateMocks()
    {
        var mem = new Mock<IProjectMemoryService>();
        mem.Setup(m => m.GetLatestSummaryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((ProjectSummary?)null);
        mem.Setup(m => m.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new List<ProjectDecision>());
        mem.Setup(m => m.GetProjectRulesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new List<ProjectRule>());
        return (mem, new Mock<ILlmTraceService>());
    }

    private static CodebaseTicketGeneratorService BuildSut(
        Mock<ILLMService> llm,
        Mock<ICodexSnapshotBuilder> snapshot,
        Mock<IProjectMemoryService> memory,
        Mock<ILlmTraceService> trace,
        ICodexTicketGroundingValidator? grounding = null)
    {
        return new CodebaseTicketGeneratorService(
            llm.Object,
            memory.Object,
            trace.Object,
            snapshot.Object,
            grounding ?? new Mock<ICodexTicketGroundingValidator>().Object,
            new CodebaseTicketPromptBuilder(),
            new CodebaseTicketResponseParser());
    }

    // ── Test 1: invalid JSON from LLM ────────────────────────────────────────

    [TestMethod]
    public async Task GenerateTicketsAsync_WhenLlmReturnsInvalidJson_ReturnsFailureResult()
    {
        const string garbageResponse = "Sorry, I cannot generate JSON right now. Here is some prose instead.";

        var llm = new Mock<ILLMService>();
        llm.Setup(l => l.GetResponseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(garbageResponse);

        var snap = new Mock<ICodexSnapshotBuilder>();
        snap.Setup(s => s.BuildSnapshotAsync(It.IsAny<CodexSnapshotBuildRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptySnapshot());

        var (mem, trace) = CreateMocks();
        var sut = BuildSut(llm, snap, mem, trace);

        var result = await sut.GenerateTicketsAsync(projectId: 1);

        Assert.IsFalse(result.Success, "Invalid JSON should produce a failure result.");
        StringAssert.Contains(result.ErrorMessage, "Draft generation failed");
        trace.Verify(t => t.AddTrace(It.IsAny<LlmTraceEntry>()), Times.Once,
            "Trace should be recorded even on parse failure.");
    }

    // ── Test 2: unknown file / symbol grounding ───────────────────────────────

    [TestMethod]
    public async Task GenerateTicketsAsync_WhenLlmInventsFilesAndSymbols_GroundingWarningsAreAdded()
    {
        const string inventedJson = """
            {
              "drafts": [{
                "title":              "Invented refactor",
                "category":           "TechDebt",
                "summary":            "Hallucinated task",
                "problem":            "Problem",
                "proposedChange":     "Change",
                "whyNow":             "Now",
                "background":         "BG",
                "acceptanceCriteria": "AC",
                "priority":           "Medium",
                "ticketType":         "Task",
                "affectedFiles":      ["src/DoesNotExist.cs"],
                "affectedSymbols":    ["FakeClass.FakeMethod"],
                "dependencies":       [],
                "suggestedBuildOrder":1,
                "riskLevel":          "Low",
                "confidenceScore":    80,
                "groundingWarnings":  [],
                "testSuggestions":    []
              }]
            }
            """;

        var llm = new Mock<ILLMService>();
        llm.Setup(l => l.GetResponseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(inventedJson);

        var snap = new Mock<ICodexSnapshotBuilder>();
        snap.Setup(s => s.BuildSnapshotAsync(It.IsAny<CodexSnapshotBuildRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptySnapshot());

        var (mem, trace) = CreateMocks();

        // Use real grounding validator — it flags invented paths/symbols
        var sut = BuildSut(llm, snap, mem, trace, grounding: new CodexTicketGroundingValidator());

        var result = await sut.GenerateTicketsAsync(projectId: 1);

        Assert.IsTrue(result.Success, "Generation should succeed even when grounding warnings exist.");
        Assert.AreEqual(1, result.Drafts.Count);
        Assert.IsTrue(result.Drafts[0].GroundingWarnings.Count > 0,
            "Invented files and symbols should produce grounding warnings.");
    }

    // ── Test 3: Roslyn/MSBuild failure returns warning result ────────────────

    [TestMethod]
    public async Task GenerateTicketsAsync_WhenSnapshotBuilderThrows_ReturnsFailureResult()
    {
        var snap = new Mock<ICodexSnapshotBuilder>();
        snap.Setup(s => s.BuildSnapshotAsync(It.IsAny<CodexSnapshotBuildRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("MSBuild workspace failed: solution file not found."));

        var llm = new Mock<ILLMService>();
        var (mem, trace) = CreateMocks();
        var sut = BuildSut(llm, snap, mem, trace);

        var result = await sut.GenerateTicketsAsync(projectId: 1);

        Assert.IsFalse(result.Success, "MSBuild failure should produce a non-throwing failure result.");
        StringAssert.Contains(result.ErrorMessage, "Generation failed");
        Assert.AreEqual(0, result.Drafts.Count, "No drafts on snapshot build failure.");

        // LLM must NOT have been called
        llm.Verify(l => l.GetResponseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Test 4: ResponseParser handles markdown fence variants ───────────────

    [TestMethod]
    public void ResponseParser_Parse_HandlesMarkdownFences()
    {
        var parser = new CodebaseTicketResponseParser();

        // Wrapped in ```json fences
        var fenced = "```json\n{\"drafts\":[]}\n```";
        var result = parser.Parse(fenced);
        Assert.AreEqual(0, result.Count);

        // Wrapped in plain ``` fences
        var plain = "```\n{\"drafts\":[]}\n```";
        result = parser.Parse(plain);
        Assert.AreEqual(0, result.Count);

        // No fences
        var bare = "{\"drafts\":[]}";
        result = parser.Parse(bare);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ResponseParser_Parse_ThrowsOnGarbage()
    {
        var parser = new CodebaseTicketResponseParser();
        var ex = false;
        try { parser.Parse("this is not json at all"); }
        catch (JsonException) { ex = true; }
        Assert.IsTrue(ex, "ResponseParser.Parse should throw JsonException on garbage input.");
    }
}
