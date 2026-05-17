using System;
using System.IO;
using System.Threading.Tasks;
using IronDev.Agent.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AppSettingsServiceTests
{
    [TestMethod]
    public async Task SaveAsync_ShouldPersistSettingsToDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), "IronDevSettingsTests", $"{Guid.NewGuid():N}.json");
        try
        {
            var service = new AppSettingsService(path);
            service.Current.SelectedModel = "gpt-4o-mini";
            service.Current.UseContextAgent = true;
            service.Current.IsLlmTracingEnabled = false;
            service.Current.RequireBuilderApplyApproval = false;

            await service.SaveAsync();

            var reloaded = new AppSettingsService(path);

            Assert.AreEqual("gpt-4o-mini", reloaded.Current.SelectedModel);
            Assert.IsTrue(reloaded.Current.UseContextAgent);
            Assert.IsFalse(reloaded.Current.IsLlmTracingEnabled);
            Assert.IsFalse(reloaded.Current.RequireBuilderApplyApproval);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
