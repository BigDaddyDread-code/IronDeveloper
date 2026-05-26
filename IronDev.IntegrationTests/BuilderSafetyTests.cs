using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.AI;
using IronDev.Services;

namespace IronDev.IntegrationTests;

[TestClass]
public class BuilderSafetyTests : IntegrationTestBase
{
    [TestMethod]
    [Description("Verify PromptContextBuilder includes target environment framing for IronDev (host).")]
    public async Task PromptContextBuilder_ShouldFrameIronDevAsHost()
    {
        using var scope = ServiceProvider.CreateScope();
        var promptBuilder = scope.ServiceProvider.GetRequiredService<IPromptContextBuilder>();
        var projectService = scope.ServiceProvider.GetRequiredService<IProjectService>();

        // Seed a project that looks like IronDev (contains 'AIDeveloper' in path)
        var projectId = await SeedProjectAsync();
        var project = await projectService.GetByIdAsync(projectId);
        project!.LocalPath = @"C:\Users\bob\source\repos\AIDeveloper";
        project.Name = "IronDev";
        // We'd need to update the DB if GetByIdAsync pulls from DB, but SeedProjectAsync might already be enough if we use the right path.
        // Actually, SeedProjectAsync probably inserts into DB.
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await Dapper.SqlMapper.ExecuteAsync(connection, "UPDATE dbo.Projects SET LocalPath = @Path, Name = @Name WHERE Id = @Id", 
            new { Path = project.LocalPath, Name = project.Name, Id = projectId });

        var prompt = await promptBuilder.BuildAsync(projectId, 0, "How do I fix the shell?");

        StringAssert.Contains(prompt, "Host Application: IronDev");
        StringAssert.Contains(prompt, "Active Target Project: IronDev");
        StringAssert.Contains(prompt, "Is External Project: False");
        StringAssert.Contains(prompt, "You are working on the IronDev HOST codebase itself.");
    }

    [TestMethod]
    [Description("Verify PromptContextBuilder includes target environment framing for external projects (e.g. BookSeller).")]
    public async Task PromptContextBuilder_ShouldFrameExternalProjectCorrectly()
    {
        using var scope = ServiceProvider.CreateScope();
        var promptBuilder = scope.ServiceProvider.GetRequiredService<IPromptContextBuilder>();
        var projectService = scope.ServiceProvider.GetRequiredService<IProjectService>();

        var projectId = await SeedProjectAsync();
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await Dapper.SqlMapper.ExecuteAsync(connection, "UPDATE dbo.Projects SET LocalPath = @Path, Name = @Name WHERE Id = @Id", 
            new { Path = @"C:\repo\BookSeller", Name = "BookSeller", Id = projectId });

        var prompt = await promptBuilder.BuildAsync(projectId, 0, "Implement book search.");

        StringAssert.Contains(prompt, "Host Application: IronDev");
        StringAssert.Contains(prompt, "Active Target Project: BookSeller");
        StringAssert.Contains(prompt, "Is External Project: True");
        StringAssert.Contains(prompt, "You are working on an EXTERNAL project.");
        StringAssert.Contains(prompt, "Do NOT apply IronDev-specific product assumptions");
    }

    [TestMethod]
    [Description("Verify CodePatchService blocks path traversal outside project root.")]
    public async Task CodePatchService_ShouldBlockPathTraversal()
    {
        using var scope = ServiceProvider.CreateScope();
        var patchService = scope.ServiceProvider.GetRequiredService<ICodePatchService>();
        var traceService = scope.ServiceProvider.GetRequiredService<ILlmTraceService>();
        traceService.Clear();

        var root = @"C:\repo\BookSeller";
        var changes = new List<FileChangeProposal>
        {
            new() { FilePath = @"..\AIDeveloper\IronDev.Api\Program.cs", BeforeSnippet = "old", AfterSnippet = "new" }
        };

        var result = await patchService.DryRunValidateAsync(root, changes);

        Assert.IsFalse(result.AllValid);
        StringAssert.Contains(result.FileResults[0].Message, "path traversal block");

        // Verify trace
        var traces = traceService.GetRecentTraces();
        var trace = System.Linq.Enumerable.First(traces, t => t.FeatureName == "Builder.WriteRootGuard");
        Assert.IsNotNull(trace);
        Assert.AreEqual(false, trace.WriteAllowed);
        Assert.AreEqual("BookSeller", trace.ActiveProjectName);
        StringAssert.Contains(trace.BlockReason, "path traversal block");
    }

    [TestMethod]
    [Description("Verify CodePatchService allows valid writes inside project root.")]
    public async Task CodePatchService_ShouldAllowValidWrites()
    {
        using var scope = ServiceProvider.CreateScope();
        var patchService = scope.ServiceProvider.GetRequiredService<ICodePatchService>();
        
        // We need a real file to exist for Rule 4 (file existence) to pass, 
        // but Rule 3 (path safety) is what we are testing here mainly.
        // Actually, DryRunValidateAsync checks File.Exists(resolved).
        
        var tempDir = Path.Combine(Path.GetTempPath(), "IronDevTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var testFile = Path.Combine(tempDir, "test.cs");
        File.WriteAllText(testFile, "public class Test { }");

        try
        {
            var changes = new List<FileChangeProposal>
            {
                new() { FilePath = "test.cs", BeforeSnippet = "Test", AfterSnippet = "ImprovedTest" }
            };

            var result = await patchService.DryRunValidateAsync(tempDir, changes);

            // It should pass Rule 3 (Path Safety)
            var fileResult = result.FileResults[0];
            Assert.IsTrue(fileResult.ResolvedPath.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase));
            
            // If it failed other rules (like existence if I didn't create it), 
            // result.AllValid might be false, but Rule 3 is what matters for safety.
            Assert.IsFalse(fileResult.Message.Contains("path traversal block"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
