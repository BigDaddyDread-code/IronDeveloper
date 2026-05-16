using System;
using System.IO;
using System.Threading.Tasks;
using IronDev.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ProjectProfileDetectionServiceTests
{
    [TestMethod]
    public async Task DetectAsync_ForDotNetSolution_ShouldDetectProfileAndCommands()
    {
        var root = CreateTempProjectRoot("DetectSolution");
        try
        {
            var solutionPath = Path.Combine(root, "DetectSolution.slnx");
            await File.WriteAllTextAsync(solutionPath, "<Solution></Solution>");
            Directory.CreateDirectory(Path.Combine(root, "DetectSolution.Tests"));
            await File.WriteAllTextAsync(
                Path.Combine(root, "DetectSolution.Tests", "DetectSolution.Tests.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="MSTest" Version="4.0.1" />
                    <PackageReference Include="Dapper" Version="2.1.66" />
                    <PackageReference Include="Microsoft.Data.SqlClient" Version="6.1.1" />
                    <PackageReference Include="Serilog" Version="4.3.0" />
                  </ItemGroup>
                </Project>
                """);

            var service = new ProjectProfileDetectionService();
            var detected = await service.DetectAsync(root, projectId: 42);

            Assert.AreEqual(42, detected.Profile.ProjectId);
            Assert.AreEqual(solutionPath, detected.Profile.SolutionFile);
            Assert.AreEqual(root, detected.Profile.SafeWriteRoot);
            Assert.AreEqual("C#", detected.Profile.PrimaryLanguage);
            Assert.AreEqual(".NET 10", detected.Profile.Framework);
            Assert.AreEqual("net10.0", detected.Profile.RuntimeVersion);
            Assert.AreEqual("MSTest", detected.Profile.TestFramework);
            Assert.AreEqual("Dapper", detected.Profile.DataAccessStyle);
            Assert.AreEqual("SQL Server", detected.Profile.DatabaseEngine);
            StringAssert.Contains(detected.BuildCommand.CommandText, "dotnet build");
            StringAssert.Contains(detected.BuildCommand.CommandText, solutionPath);
            StringAssert.Contains(detected.TestCommand.CommandText, "dotnet test");
            Assert.IsTrue(detected.DetectedFacts.Exists(f => f.Contains("Serilog", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task DetectAsync_ForBookSeller_ShouldEnableSandboxDefaults()
    {
        var root = CreateTempProjectRoot("BookSeller");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "BookSeller.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            var service = new ProjectProfileDetectionService();
            var detected = await service.DetectAsync(root, projectId: 7);

            Assert.AreEqual("External Sandbox / Class Library", detected.Profile.ApplicationType);
            Assert.AreEqual(".NET 9", detected.Profile.Framework);
            Assert.IsTrue(detected.Profile.IsExternalProject);
            Assert.IsTrue(detected.Profile.AllowBuilderApply);
            Assert.IsFalse(detected.Profile.AllowWritesOutsideProjectRoot);
            Assert.AreEqual("None", detected.Profile.DatabaseEngine);
            Assert.AreEqual("None", detected.Profile.DataAccessStyle);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task DetectAsync_ForMissingPath_ShouldReturnSafeFallbackCommandsAndWarning()
    {
        var root = Path.Combine(Path.GetTempPath(), "IronDevMissingProject", Guid.NewGuid().ToString("N"));
        var service = new ProjectProfileDetectionService();

        var detected = await service.DetectAsync(root, projectId: 99);

        Assert.AreEqual(root, detected.Profile.SafeWriteRoot);
        Assert.AreEqual("dotnet build", detected.BuildCommand.CommandText);
        Assert.AreEqual("dotnet test", detected.TestCommand.CommandText);
        Assert.IsTrue(detected.Warnings.Count > 0);
    }

    private static string CreateTempProjectRoot(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "IronDevProfileDetection", $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
