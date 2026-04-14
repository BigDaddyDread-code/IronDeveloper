using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.Services;

namespace IronDev.IntegrationTests;

[TestClass]
public class CodeIndexServiceIntegrationTests : IntegrationTestBase
{
    private string _testDirPath = string.Empty;

    [TestInitialize]
    public async Task SetupTestDir()
    {
        await base.TestInitialize();
        
        // Ensure test directory exists
        _testDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_code_dir");
        if (Directory.Exists(_testDirPath))
            Directory.Delete(_testDirPath, true);
            
        Directory.CreateDirectory(_testDirPath);
        await File.WriteAllTextAsync(Path.Combine(_testDirPath, "file1.cs"), "public class TestOne { }");
        await File.WriteAllTextAsync(Path.Combine(_testDirPath, "file2.cs"), "public class TestTwo { }");
        await File.WriteAllTextAsync(Path.Combine(_testDirPath, "readme.md"), "# IronDev Test");
    }

    [TestMethod]
    public async Task IndexDirectoryAsync_ShouldInsertFilesAndSkipUnchanged()
    {
        using var scope = ServiceProvider.CreateScope();
        var indexService = scope.ServiceProvider.GetRequiredService<ICodeIndexService>();
        var projectId = await SeedProjectAsync();

        // 1. Initial index
        var result1 = await indexService.IndexDirectoryAsync(projectId, _testDirPath);
        Assert.AreEqual(3, result1.FilesAdded);
        Assert.AreEqual(0, result1.FilesUpdated);
        Assert.AreEqual(0, result1.FilesUnchanged);

        // 2. Search confirms insertion
        var searchResults = await indexService.SearchFilesAsync(projectId, "TestOne");
        Assert.HasCount(1, searchResults);
        Assert.AreEqual("file1.cs", searchResults[0].FilePath);

        // 3. Second index with unchanged files
        var result2 = await indexService.IndexDirectoryAsync(projectId, _testDirPath);
        Assert.AreEqual(0, result2.FilesAdded);
        Assert.AreEqual(3, result2.FilesUnchanged);

        // 4. Modify one file and re-index
        await File.WriteAllTextAsync(Path.Combine(_testDirPath, "file1.cs"), "public class TestOneModified { }");
        var result3 = await indexService.IndexDirectoryAsync(projectId, _testDirPath);
        Assert.AreEqual(0, result3.FilesAdded);
        Assert.AreEqual(1, result3.FilesUpdated);
        Assert.AreEqual(2, result3.FilesUnchanged);
    }
}
