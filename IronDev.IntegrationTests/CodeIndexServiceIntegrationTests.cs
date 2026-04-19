using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.Data;
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
    public async Task IndexDirectoryAsync_ShouldPersistSupportedFiles()
    {
        using var scope = ServiceProvider.CreateScope();
        var indexService = scope.ServiceProvider.GetRequiredService<ICodeIndexService>();
        var projectId = await SeedProjectAsync();

        var result = await indexService.IndexDirectoryAsync(projectId, _testDirPath);
        Assert.AreEqual(3, result.FilesAdded, "Should add file1.cs, file2.cs and readme.md");
    }

    [TestMethod]
    public async Task IndexDirectoryAsync_ReindexWithoutChanges_ShouldNotDuplicateRows()
    {
        using var scope = ServiceProvider.CreateScope();
        var indexService = scope.ServiceProvider.GetRequiredService<ICodeIndexService>();
        var projectId = await SeedProjectAsync();

        await indexService.IndexDirectoryAsync(projectId, _testDirPath);
        var result = await indexService.IndexDirectoryAsync(projectId, _testDirPath);
        
        Assert.AreEqual(0, result.FilesAdded);
        Assert.AreEqual(3, result.FilesUnchanged);
    }

    [TestMethod]
    public async Task IndexDirectoryAsync_WhenFileChanges_ShouldUpdateContentAndHash()
    {
        using var scope = ServiceProvider.CreateScope();
        var indexService = scope.ServiceProvider.GetRequiredService<ICodeIndexService>();
        var projectId = await SeedProjectAsync();

        await indexService.IndexDirectoryAsync(projectId, _testDirPath);
        
        await File.WriteAllTextAsync(Path.Combine(_testDirPath, "file1.cs"), "public class TestOneModified { }");
        var result = await indexService.IndexDirectoryAsync(projectId, _testDirPath);
        
        Assert.AreEqual(1, result.FilesUpdated);
        
        var search = await indexService.SearchFilesAsync(projectId, "TestOneModified");
        Assert.AreEqual(1, search.Count);
    }

    [TestMethod]
    public async Task SearchFilesAsync_ByFileName_ShouldReturnMatchingFile()
    {
        using var scope = ServiceProvider.CreateScope();
        var indexService = scope.ServiceProvider.GetRequiredService<ICodeIndexService>();
        var projectId = await SeedProjectAsync();

        await indexService.IndexDirectoryAsync(projectId, _testDirPath);
        
        var results = await indexService.SearchFilesAsync(projectId, "file2.cs");
        Assert.IsTrue(results.Any(f => f.FilePath.EndsWith("file2.cs")));
    }

    [TestMethod]
    public async Task SearchFilesAsync_ByContentKeyword_ShouldReturnMatchingFile()
    {
        using var scope = ServiceProvider.CreateScope();
        var indexService = scope.ServiceProvider.GetRequiredService<ICodeIndexService>();
        var projectId = await SeedProjectAsync();

        await indexService.IndexDirectoryAsync(projectId, _testDirPath);
        
        var results = await indexService.SearchFilesAsync(projectId, "TestTwo");
        Assert.IsTrue(results.Any(f => f.FilePath.EndsWith("file2.cs")), "Should find the file containing TestTwo symbol");
    }

    [TestMethod]
    public async Task SearchFilesAsync_ShouldReturnOnlyFilesForCurrentTenant()
    {
        using var scope = ServiceProvider.CreateScope();
        var indexService = scope.ServiceProvider.GetRequiredService<ICodeIndexService>();
        
        // Tenant 1
        var proj1 = await SeedProjectAsync(1);
        var t1Path = Path.Combine(_testDirPath, "tenant1");
        Directory.CreateDirectory(t1Path);
        await File.WriteAllTextAsync(Path.Combine(t1Path, "t1.cs"), "secret1");
        await indexService.IndexDirectoryAsync(proj1, t1Path);

        // Switch to Tenant 2
        var tenant2Context = new TestTenantContext { TenantId = 2 };
        var indexServiceT2 = new SqlCodeIndexService(
            scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>(),
            tenant2Context);
        
        var proj2 = await SeedProjectAsync(2);
        var t2Path = Path.Combine(_testDirPath, "tenant2");
        Directory.CreateDirectory(t2Path);
        await File.WriteAllTextAsync(Path.Combine(t2Path, "t2.cs"), "secret2");
        await indexServiceT2.IndexDirectoryAsync(proj2, t2Path);

        // Search T2 for T1 secret
        var results = await indexServiceT2.SearchFilesAsync(proj2, "secret1");
        Assert.AreEqual(0, results.Count, "Tenant 2 should not see Tenant 1 files");
    }
}
