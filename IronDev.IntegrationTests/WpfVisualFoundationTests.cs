using System;
using System.IO;

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
