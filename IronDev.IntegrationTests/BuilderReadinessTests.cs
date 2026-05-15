using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BuilderReadinessTests : IntegrationTestBase
{
    private IBuilderReadinessService _readinessService = null!;
    private IBuilderProposalService _proposalService = null!;
    private int _projectId;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        _readinessService = ServiceProvider.GetRequiredService<IBuilderReadinessService>();
        _proposalService = ServiceProvider.GetRequiredService<IBuilderProposalService>();
        
        var tempPath = Path.Combine(Path.GetTempPath(), "IronDev_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        
        // Seed a project
        _projectId = await SeedProjectAsync(1, "BookSeller", tempPath);
    }

    [TestMethod]
    public async Task EvaluateReadiness_MissingProfile_ReturnsNeedsUpdate()
    {
        // Act
        var result = await _readinessService.EvaluateReadinessAsync(_projectId, 1);

        // Assert
        Assert.AreEqual(BuildReadinessStatus.NeedsProjectProfileUpdate, result.Status);
        Assert.IsFalse(result.IsReady);
        StringAssert.Contains(result.Message, "Complete Project Profile");
    }

    [TestMethod]
    public async Task EvaluateReadiness_CompleteProfile_ReturnsReadyToBuild()
    {
        // Arrange
        await SeedProjectProfileAsync(_projectId, testFramework: "xUnit", allowBuilderApply: true);
        await SeedProjectCommandAsync(_projectId, "Build", "dotnet build");

        // Act
        var result = await _readinessService.EvaluateReadinessAsync(_projectId, 1);

        // Assert
        Assert.AreEqual(BuildReadinessStatus.ReadyToBuild, result.Status);
        Assert.IsTrue(result.IsReady);
    }

    [TestMethod]
    public async Task EvaluateReadiness_AllowBuilderApplyFalse_ReturnsBlockedByConflict()
    {
        // Arrange
        await SeedProjectProfileAsync(_projectId, testFramework: "xUnit", allowBuilderApply: false);
        await SeedProjectCommandAsync(_projectId, "Build", "dotnet build");

        // Act
        var result = await _readinessService.EvaluateReadinessAsync(_projectId, 1);

        // Assert
        Assert.AreEqual(BuildReadinessStatus.BlockedByConflict, result.Status);
        Assert.IsFalse(result.IsReady);
        StringAssert.Contains(result.Message, "disabled");
    }

    [TestMethod]
    public async Task ValidateProposalArchitecture_XunitMismatch_ReturnsNeedsUpdate()
    {
        // Arrange
        var projectPath = Path.Combine(Path.GetTempPath(), "BookSeller_ReadinessTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectPath);
        var testProjPath = Path.Combine(projectPath, "BookSeller.Tests.csproj");
        await File.WriteAllTextAsync(testProjPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup></ItemGroup></Project>");

        var proposal = new BuilderProposal
        {
            ProjectId = _projectId,
            ProjectRoot = projectPath,
            Changes = new List<ProposedFileChange>
            {
                new ProposedFileChange
                {
                    FilePath = "BookSeller.Tests/BookServiceTests.cs",
                    FullContentAfter = "using Xunit; [Fact] public void Test() {}"
                }
            }
        };

        // Act
        var result = await _readinessService.ValidateProposalArchitectureAsync(proposal);

        // Assert
        Assert.AreEqual(BuildReadinessStatus.NeedsProjectProfileUpdate, result.Status);
        Assert.IsFalse(result.IsReady);
        StringAssert.Contains(result.Message, "does not reference xUnit");

        // Cleanup
        Directory.Delete(projectPath, true);
    }

    [TestMethod]
    public async Task ValidateProposalArchitecture_XunitMatch_ReturnsReady()
    {
        // Arrange
        var projectPath = Path.Combine(Path.GetTempPath(), "BookSeller_ReadinessTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectPath);
        var testProjPath = Path.Combine(projectPath, "BookSeller.Tests.csproj");
        await File.WriteAllTextAsync(testProjPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"xunit\" Version=\"2.4.2\" /></ItemGroup></Project>");

        var proposal = new BuilderProposal
        {
            ProjectId = _projectId,
            ProjectRoot = projectPath,
            Changes = new List<ProposedFileChange>
            {
                new ProposedFileChange
                {
                    FilePath = "BookSeller.Tests/BookServiceTests.cs",
                    FullContentAfter = "using Xunit; [Fact] public void Test() {}"
                }
            }
        };

        // Act
        var result = await _readinessService.ValidateProposalArchitectureAsync(proposal);

        // Assert
        Assert.IsTrue(result.IsReady);

        // Cleanup
        Directory.Delete(projectPath, true);
    }
}
