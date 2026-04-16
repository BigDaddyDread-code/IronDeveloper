using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.AI;
using IronDev.Services;

namespace IronDev.IntegrationTests;

[TestClass]
public class ContextInjectionTests : IntegrationTestBase
{
    private string _testDirPath = string.Empty;

    [TestInitialize]
    public async Task SetupTestDir()
    {
        await base.TestInitialize();
        
        _testDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "context_test_dir");
        if (Directory.Exists(_testDirPath))
            Directory.Delete(_testDirPath, true);
            
        Directory.CreateDirectory(_testDirPath);
        
        // Seed specific files for keyword tests
        await File.WriteAllTextAsync(Path.Combine(_testDirPath, "CodeWorkbenchWindow.xaml.cs"), "public partial class CodeWorkbenchWindow { }");
        await File.WriteAllTextAsync(Path.Combine(_testDirPath, "MainWindow.xaml.cs"), "public partial class MainWindow { }");
    }

    [TestMethod]
    public async Task BuildAsync_WhenPromptMentionsWorkbench_ShouldIncludeMatchingIndexedFiles()
    {
        using var scope = ServiceProvider.CreateScope();
        var indexService = scope.ServiceProvider.GetRequiredService<ICodeIndexService>();
        var promptBuilder = scope.ServiceProvider.GetRequiredService<IPromptContextBuilder>();
        var projectId = await SeedProjectAsync();

        // 1. Index the test directory
        await indexService.IndexDirectoryAsync(projectId, _testDirPath);

        // 2. Build prompt with a keyword that matches
        var prompt = await promptBuilder.BuildAsync(projectId, Guid.NewGuid(), "How does the CodeWorkbenchWindow work?");

        // 3. Verify the file content is in the prompt
        Assert.IsTrue(prompt.Contains("CodeWorkbenchWindow.xaml.cs"), "Prompt should contain the filename");
        Assert.IsTrue(prompt.Contains("public partial class CodeWorkbenchWindow"), "Prompt should contain the file content");
        Assert.IsTrue(prompt.Contains("## Relevant Code Context"), "Prompt should have the new context header");
    }

    [TestMethod]
    public async Task BuildAsync_WhenPromptIsGeneric_ShouldNotIncludeCodeContext()
    {
        using var scope = ServiceProvider.CreateScope();
        var promptBuilder = scope.ServiceProvider.GetRequiredService<IPromptContextBuilder>();
        var projectId = await SeedProjectAsync();

        var prompt = await promptBuilder.BuildAsync(projectId, Guid.NewGuid(), "Hello, how are you?");

        Assert.IsFalse(prompt.Contains("## Relevant Code Context"), "Generic prompt should not trigger code injection");
    }
}
