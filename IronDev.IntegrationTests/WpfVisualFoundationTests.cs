using System;
using System.IO;
using IronDev.Core.Time;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class WpfVisualFoundationTests
{
    [TestMethod]
    public void AppVisualFoundation_DefinesRequiredTokenAndComponentKeys()
    {
        var root = FindRepositoryRoot();
        var tokens = File.ReadAllText(Path.Combine(root, "IronDeveloper", "Themes", "IronDevVisualTokens.xaml"));
        var components = File.ReadAllText(Path.Combine(root, "IronDeveloper", "Themes", "IronDevComponentStyles.xaml"));
        var appStyles = File.ReadAllText(Path.Combine(root, "IronDeveloper", "Themes", "AppStyles.xaml"));

        foreach (var key in new[]
        {
            "IronDev.Color.Ink.980",
            "IronDev.Brush.Background.App",
            "IronDev.Font.Size.Display",
            "IronDev.Space.4",
            "IronDev.Radius.Medium",
            "IronDev.Border.Thin",
            "IronDev.Elevation.PanelBlur",
            "IronDev.State.Opacity.Disabled",
            "IronDev.Icon.Size.Default"
        })
        {
            StringAssert.Contains(tokens, key);
        }

        foreach (var key in new[]
        {
            "IronDev.SurfacePanel.Base",
            "IronDev.CommandButton.Primary",
            "IronDev.CommandButton.Secondary",
            "IronDev.CommandButton.Ghost",
            "IronDev.Input.TextBox",
            "IronDev.Input.ComboBox",
            "IronDev.StatusBadge.Host",
            "IronDev.EmptyState.Text",
            "IronDev.Loading.SkeletonBlock"
        })
        {
            StringAssert.Contains(components, key);
        }

        StringAssert.Contains(appStyles, "IronDevVisualTokens.xaml");
        StringAssert.Contains(appStyles, "IronDevComponentStyles.xaml");
    }

    [TestMethod]
    public void TicketsShowcaseSurface_ExposesStableAutomationIds()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "IronDeveloper", "Views", "Workspaces", "TicketsWorkspaceView.xaml"));

        foreach (var automationId in new[]
        {
            "tickets.workspace",
            "tickets.header",
            "ticket.commandBar.primary",
            "ticket.generateFromCodebase",
            "ticket.review.open",
            "ticket.list",
            "ticket.row",
            "ticket.detail",
            "ticket.emptyState",
            "ticket.preflight",
            "ticket.preflight.indexProject",
            "ticket.preflight.continueWithoutIndex",
            "ticket.preflight.cancel",
            "ticket.review.queue",
            "ticket.review.list",
            "ticket.review.importSelected",
            "ticket.review.clear",
            "ticket.editor",
            "ticket.editor.title",
            "ticket.editor.status",
            "ticket.editor.priority",
            "ticket.editor.type",
            "ticket.editor.summary",
            "ticket.editor.acceptanceCriteria",
            "ticket.detail.brief",
            "ticket.detail.plan",
            "ticket.detail.context",
            "ticket.detail.tests",
            "ticket.detail.build",
            "ticket.inspector",
            "ticket.inspector.evidence",
            "ticket.inspector.linkedDocuments",
            "ticket.inspector.decisions",
            "ticket.inspector.affectedFiles",
            "ticket.inspector.buildReadiness",
            "ticket.editor.commandBar",
            "ticket.command.generatePlan",
            "ticket.command.buildTicket",
            "ticket.command.reviewContext",
            "ticket.command.save",
            "ticket.command.cancel",
            "ticket.command.archive",
            "ticket.draft.commandBar",
            "ticket.draft.save",
            "ticket.draft.saveWithPlan"
        })
        {
            StringAssert.Contains(xaml, $"AutomationProperties.AutomationId=\"{automationId}\"");
        }
    }

    [TestMethod]
    public void DocumentsAndMemorySurface_ExposesStableAutomationIds()
    {
        var root = FindRepositoryRoot();
        var documentsXaml = File.ReadAllText(Path.Combine(root, "IronDeveloper", "Views", "Workspaces", "DocumentsWorkspaceView.xaml"));
        var memoryXaml = File.ReadAllText(Path.Combine(root, "IronDeveloper", "Views", "Workspaces", "KnowledgeCompilerView.xaml"));

        foreach (var automationId in new[]
        {
            "documents.workspace",
            "documents.header",
            "document.list",
            "document.row",
            "document.detail",
            "document.detail.brief",
            "document.detail.content",
            "document.detail.versions",
            "document.detail.decisions",
            "document.detail.tickets",
            "document.detail.evidence",
            "document.inspector",
            "document.inspector.provenance",
            "document.inspector.sourceLinks",
            "document.inspector.versions",
            "document.inspector.relatedDecisions",
            "document.inspector.linkedTickets",
            "document.inspector.memoryEvidence",
            "document.inspector.traceLinks",
            "document.command.resolveDiscussion",
            "document.command.createTickets",
            "document.command.saveDecision",
            "document.command.searchRelatedMemory",
            "document.command.reloadIntoChat",
            "document.command.save",
            "document.command.cancel",
            "document.command.archive"
        })
        {
            StringAssert.Contains(documentsXaml, $"AutomationProperties.AutomationId=\"{automationId}\"");
        }

        foreach (var automationId in new[]
        {
            "memory.workspace",
            "memory.search",
            "memory.resultList",
            "memory.resultRow",
            "memory.result.detail",
            "memory.inspector",
            "memory.command.search",
            "memory.command.createTicketFromResult"
        })
        {
            StringAssert.Contains(memoryXaml, $"AutomationProperties.AutomationId=\"{automationId}\"");
        }
    }

    [TestMethod]
    public void DocumentsAndMemorySurface_UsesUtcTimestampDisplayContract()
    {
        var root = FindRepositoryRoot();
        var documentsViewModel = File.ReadAllText(Path.Combine(root, "IronDeveloper", "ViewModels", "Workspaces", "DocumentsWorkspaceViewModel.cs"));
        var documentsXaml = File.ReadAllText(Path.Combine(root, "IronDeveloper", "Views", "Workspaces", "DocumentsWorkspaceView.xaml"));
        var memoryViewModel = File.ReadAllText(Path.Combine(root, "IronDeveloper", "ViewModels", "Workspaces", "KnowledgeCompilerViewModel.cs"));
        var memoryXaml = File.ReadAllText(Path.Combine(root, "IronDeveloper", "Views", "Workspaces", "KnowledgeCompilerView.xaml"));
        var semanticModels = File.ReadAllText(Path.Combine(root, "IronDev.Core", "KnowledgeCompiler", "SemanticMemoryModels.cs"));
        var architecture = File.ReadAllText(Path.Combine(root, "Docs", "ARCHITECTURE.md"));

        foreach (var source in new[] { documentsViewModel, memoryViewModel })
        {
            var forbiddenLocalClock = "DateTime" + ".Now";
            Assert.IsFalse(source.Contains(forbiddenLocalClock, StringComparison.Ordinal), "Product UI timestamp code must not use the local system clock.");
            StringAssert.Contains(source, "DateTimeDisplay.");
        }

        StringAssert.Contains(documentsXaml, "LastUpdatedUtcTooltip");
        StringAssert.Contains(documentsXaml, "VersionCreatedUtcMetadata");
        StringAssert.Contains(documentsXaml, "VersionCreatedUtcTooltip");
        StringAssert.Contains(memoryXaml, "IndexedUtcLabel");
        StringAssert.Contains(memoryXaml, "IndexedUtcTooltip");
        StringAssert.Contains(semanticModels, "IndexedUtc");
        StringAssert.Contains(architecture, "UTC Timestamp Contract");

        var timestamp = new DateTimeOffset(2026, 5, 25, 2, 32, 0, TimeSpan.Zero);
        Assert.AreEqual("2026-05-25 02:32 UTC", DateTimeDisplay.ToUtcMetadata(timestamp));
        Assert.AreEqual("2026-05-25T02:32:00Z UTC", DateTimeDisplay.ToUtcTooltip(timestamp));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "IronDeveloper")) &&
                Directory.Exists(Path.Combine(directory.FullName, "IronDev.Core")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AIDeveloper repository root.");
    }
}
