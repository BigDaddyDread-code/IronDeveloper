using System;
using System.Threading.Tasks;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Builder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public class BuildErrorClassifierServiceTests
{
    private BuildErrorClassifierService _service;

    [TestInitialize]
    public void Setup()
    {
        _service = new BuildErrorClassifierService();
    }

    [TestMethod]
    public async Task ClassifyBuildFailureAsync_XunitMissing_ReturnsTestFrameworkMismatch()
    {
        var buildResult = new DotNetBuildResult
        {
            Succeeded = false,
            StandardError = "error CS0246: The type or namespace name 'Xunit' could not be found"
        };
        var profile = new ProjectProfile { TestFramework = "Unknown" };

        var result = await _service.ClassifyBuildFailureAsync(buildResult, profile, "C:\\repo");

        Assert.IsNotNull(result);
        Assert.AreEqual("TestFrameworkMismatch", result.FailureCategory);
        Assert.IsTrue(result.RequiresUserApproval);
        Assert.IsTrue(result.Questions.Count > 0);
    }

    [TestMethod]
    public async Task ClassifyBuildFailureAsync_FactAttributeMissing_ReturnsTestFrameworkMismatch()
    {
        var buildResult = new DotNetBuildResult
        {
            Succeeded = false,
            StandardError = "error CS0246: The type or namespace name 'FactAttribute' could not be found"
        };
        var profile = new ProjectProfile { TestFramework = "NUnit" };

        var result = await _service.ClassifyBuildFailureAsync(buildResult, profile, "C:\\repo");

        Assert.IsNotNull(result);
        Assert.AreEqual("TestFrameworkMismatch", result.FailureCategory);
        Assert.AreEqual("TestFramework: NUnit", result.CurrentProfileValue);
    }
}
