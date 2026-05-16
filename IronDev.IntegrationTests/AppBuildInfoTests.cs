using IronDev.Agent.ViewModels.Workspaces;
using IronDev.Core.Models;
using IronDev.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public class AppBuildInfoTests
{
    [TestMethod]
    public void AppBuildInfo_ShouldExposeAlphaVersion()
    {
        StringAssert.Contains(AppBuildInfo.DisplayName, "IronDev");
        StringAssert.Contains(AppBuildInfo.DisplayName, "Alpha 0.1");
        StringAssert.Contains(AppBuildInfo.WindowTitle, "0.1");
        StringAssert.Contains(AppBuildInfo.WindowTitle, "Project-Aware Ticket and Proposal Workflow");
    }

    [TestMethod]
    public void SettingsWorkspaceViewModel_ShouldExposeHelpAboutText()
    {
        var vm = new SettingsWorkspaceViewModel(new LlmTraceService());

        Assert.AreEqual(AppBuildInfo.DisplayName, vm.ProductDisplayName);
        Assert.AreEqual(AppBuildInfo.Version, vm.ProductVersion);
        Assert.AreEqual(AppBuildInfo.WorkflowName, vm.ProductWorkflowName);
        StringAssert.Contains(vm.ProductSafetyModel, "Proposal first");
        StringAssert.Contains(vm.AlphaTesterPrompt, "small repo");
    }
}
