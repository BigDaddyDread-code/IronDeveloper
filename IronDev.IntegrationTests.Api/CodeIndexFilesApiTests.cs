using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

/// <summary>
/// Boundary tests for the code-index file listing that feeds the solution explorer.
///
/// Protected boundaries:
/// - tenant isolation: the list never crosses the caller's tenant;
/// - summary shape: the list never carries file content;
/// - deterministic paging ordered by path.
/// </summary>
[TestClass]
[TestCategory("CodeIndexFiles")]
public class CodeIndexFilesApiTests : ApiTestBase
{
    private const int ProjectId = 1;
    private const string ForeignTenantMarker = "zz-foreign-tenant-file.cs";

    [TestInitialize]
    public async Task SeedIndexedFilesAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("""
            IF NOT EXISTS (SELECT 1 FROM dbo.Projects WHERE Id = @ProjectId)
            BEGIN
                SET IDENTITY_INSERT dbo.Projects ON;
                INSERT INTO dbo.Projects (Id, TenantId, Name) VALUES (@ProjectId, 1, 'Explorer Test Project');
                SET IDENTITY_INSERT dbo.Projects OFF;
            END

            DELETE FROM dbo.ProjectFiles WHERE ProjectId = @ProjectId;

            INSERT INTO dbo.ProjectFiles (TenantId, ProjectId, FilePath, FileExtension, ContentHash, Content, LastIndexedDate)
            VALUES
                (1, @ProjectId, 'src/Catalog/Book.cs', '.cs', 'hash-a', 'class Book {}', SYSUTCDATETIME()),
                (1, @ProjectId, 'src/Catalog/CatalogService.cs', '.cs', 'hash-b', 'class CatalogService {}', SYSUTCDATETIME()),
                (1, @ProjectId, 'README.md', '.md', 'hash-c', '# readme', SYSUTCDATETIME()),
                (2, @ProjectId, @ForeignMarker, '.cs', 'hash-x', 'class Foreign {}', SYSUTCDATETIME());
            """,
            new { ProjectId, ForeignMarker = ForeignTenantMarker });
    }

    [TestMethod]
    public async Task ListFiles_WithoutToken_ShouldReturnUnauthorized()
    {
        var response = await Client.GetAsync($"/api/projects/{ProjectId}/code-index/files");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListFiles_ReturnsOnlyCurrentTenantFiles_OrderedByPath()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);

        var response = await client.GetAsync($"/api/projects/{ProjectId}/code-index/files");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var files = await response.Content.ReadFromJsonAsync<JsonElement>();
        var paths = new List<string>();
        foreach (var file in files.EnumerateArray())
        {
            paths.Add(file.GetProperty("filePath").GetString()!);
        }

        CollectionAssert.AreEqual(
            new[] { "README.md", "src/Catalog/Book.cs", "src/Catalog/CatalogService.cs" },
            paths,
            "Expected exactly the current tenant's files, ordered by path.");
        CollectionAssert.DoesNotContain(paths, ForeignTenantMarker,
            "A file indexed under another tenant must never appear.");
    }

    [TestMethod]
    public async Task ListFiles_NeverReturnsFileContent()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);

        var response = await client.GetAsync($"/api/projects/{ProjectId}/code-index/files");
        var files = await response.Content.ReadFromJsonAsync<JsonElement>();

        foreach (var file in files.EnumerateArray())
        {
            Assert.IsFalse(file.TryGetProperty("content", out _),
                "The file list is a summary surface — content must never ship in it.");
            Assert.IsFalse(file.TryGetProperty("contentHash", out _),
                "Content hashes stay out of the list summary.");
        }
    }

    [TestMethod]
    public async Task ListFiles_PagesDeterministically()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);

        var firstPage = await (await client.GetAsync($"/api/projects/{ProjectId}/code-index/files?skip=0&take=2"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var secondPage = await (await client.GetAsync($"/api/projects/{ProjectId}/code-index/files?skip=2&take=2"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.AreEqual(2, firstPage.GetArrayLength());
        Assert.AreEqual(1, secondPage.GetArrayLength());
        Assert.AreEqual("README.md", firstPage[0].GetProperty("filePath").GetString());
        Assert.AreEqual("src/Catalog/CatalogService.cs", secondPage[0].GetProperty("filePath").GetString());
    }
}
