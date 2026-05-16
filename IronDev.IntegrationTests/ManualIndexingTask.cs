using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.Services;
using IronDev.Core.Auth;
using IronDev.Data;
using Microsoft.Extensions.Configuration;

namespace IronDev.IntegrationTests;

[TestClass]
public class ManualIndexingTask
{
    private class SimpleTenantContext : ICurrentTenantContext
    {
        public int TenantId { get; set; } = 3;
    }

    [TestMethod]
    public async Task ReindexIronDevRepo()
    {
        var projectId = 2; // Real ID
        var path = @"C:\Users\bob\source\repos\AIDeveloper";

        // Setup services manually to point to the REAL database
        var connString = "Server=(localdb)\\MSSQLLocalDB;Database=IronDeveloper;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";
        
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:IronDeveloperDb"] = connString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
        services.AddSingleton<ICurrentTenantContext>(new SimpleTenantContext());
        services.AddScoped<ICodeIndexService, SqlCodeIndexService>();

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var indexService = scope.ServiceProvider.GetRequiredService<ICodeIndexService>();
        
        Console.WriteLine($"Starting manual index of {path} for Project {projectId}...");
        var result = await indexService.IndexDirectoryAsync(projectId, path);
        
        Console.WriteLine($"Indexing complete.");
        Console.WriteLine($"Files Scanned: {result.FilesScanned}");
        Console.WriteLine($"Files Added: {result.FilesAdded}");
        Console.WriteLine($"Files Updated: {result.FilesUpdated}");
        Console.WriteLine($"Files Unchanged: {result.FilesUnchanged}");
        Console.WriteLine($"Stored File Count: {result.StoredFileCount}");
        
        Assert.IsTrue(result.StoredFileCount > 0, "Should have indexed some files.");
    }
}
