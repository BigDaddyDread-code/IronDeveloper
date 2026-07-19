using IronDev.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class WorkbenchReleaseInfoTests
{
    [TestMethod]
    public void Defaults_AreVersionedV1Boundary()
    {
        var info = WorkbenchReleaseInfoFactory.Create(
            new ConfigurationBuilder().Build(),
            new StubHostEnvironment("Test"));

        Assert.AreEqual("0.1.0-preview.4", info.Version);
        Assert.AreEqual("V1", info.Mode);
        Assert.AreEqual("default", info.PreviewId);
        Assert.IsFalse(info.V2Enabled);
        Assert.IsTrue(info.V1FallbackEnabled);
        Assert.IsFalse(info.ResetSupported);
        Assert.IsFalse(string.IsNullOrWhiteSpace(info.ApiCommit));
    }

    [TestMethod]
    public void LocalTestPreview_ReportsV2VersionAndResetBoundary()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkbenchV2:Version"] = "0.1.0-preview.4",
                ["WorkbenchV2:Enabled"] = "true",
                ["WorkbenchV2:V1FallbackEnabled"] = "true",
                ["WorkbenchV2:PreviewId"] = "workbench-pr00a",
                ["LocalTest:ResetAllowed"] = "true"
            })
            .Build();

        var info = WorkbenchReleaseInfoFactory.Create(configuration, new StubHostEnvironment("LocalTest"));

        Assert.AreEqual("V2", info.Mode);
        Assert.AreEqual("workbench-pr00a", info.PreviewId);
        Assert.IsTrue(info.V2Enabled);
        Assert.IsTrue(info.V1FallbackEnabled);
        Assert.IsTrue(info.ResetSupported);
    }

    [TestMethod]
    public void InvalidPreviewId_IsRejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkbenchV2:PreviewId"] = "../../production"
            })
            .Build();

        Assert.ThrowsException<InvalidOperationException>(() =>
            WorkbenchReleaseInfoFactory.Create(configuration, new StubHostEnvironment("LocalTest")));
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "IronDev.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(AppContext.BaseDirectory);
    }
}
