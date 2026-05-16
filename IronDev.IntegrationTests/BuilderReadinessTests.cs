using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BuilderReadinessTests : IntegrationTestBase
{
    private IBuilderReadinessService _readinessService = null!;
    private IBuilderProposalService _proposalService = null!;
    private int _projectId;
    private long _ticketId;

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
        _ticketId = await SeedTicketAsync(_projectId);
    }

    [TestMethod]
    public async Task EvaluateReadiness_MissingProfile_ReturnsNeedsUpdate()
    {
        // Act
        var result = await _readinessService.EvaluateReadinessAsync(_projectId, _ticketId);

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
        await SeedProjectIndexAsync(_projectId);

        // Act
        var result = await _readinessService.EvaluateReadinessAsync(_projectId, _ticketId);

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
        await SeedProjectIndexAsync(_projectId);

        // Act
        var result = await _readinessService.EvaluateReadinessAsync(_projectId, _ticketId);

        // Assert
        Assert.AreEqual(BuildReadinessStatus.BlockedByConflict, result.Status);
        Assert.IsFalse(result.IsReady);
        StringAssert.Contains(result.Message, "disabled");
    }

    [TestMethod]
    public async Task EvaluateReadiness_NotIndexed_ReturnsNeedsReindex()
    {
        // Arrange
        await SeedProjectProfileAsync(_projectId, testFramework: "xUnit", allowBuilderApply: true);
        await SeedProjectCommandAsync(_projectId, "Build", "dotnet build");

        // Act
        var result = await _readinessService.EvaluateReadinessAsync(_projectId, _ticketId);

        // Assert
        Assert.AreEqual(BuildReadinessStatus.NeedsReindex, result.Status);
        Assert.IsFalse(result.IsReady);
        StringAssert.Contains(result.Message, "Index this project");
        CollectionAssert.Contains(result.BlockingIssues, "Project has not been indexed.");
    }

    [TestMethod]
    public async Task EvaluateReadiness_IndexFailed_ReturnsNeedsReindex()
    {
        // Arrange
        await SeedProjectProfileAsync(_projectId, testFramework: "xUnit", allowBuilderApply: true);
        await SeedProjectCommandAsync(_projectId, "Build", "dotnet build");
        await SeedProjectIndexAsync(_projectId, status: "Index failed: invalid column", indexedFileCount: 0);

        // Act
        var result = await _readinessService.EvaluateReadinessAsync(_projectId, _ticketId);

        // Assert
        Assert.AreEqual(BuildReadinessStatus.NeedsReindex, result.Status);
        Assert.IsFalse(result.IsReady);
        StringAssert.Contains(result.Message, "not ready");
    }

    [TestMethod]
    public async Task EvaluateReadiness_StaleIndex_ReturnsNeedsReindex()
    {
        // Arrange
        await SeedProjectProfileAsync(_projectId, testFramework: "xUnit", allowBuilderApply: true);
        await SeedProjectCommandAsync(_projectId, "Build", "dotnet build");
        await SeedProjectIndexAsync(_projectId, status: "Stale Index", indexedFileCount: 7);

        // Act
        var result = await _readinessService.EvaluateReadinessAsync(_projectId, _ticketId);

        // Assert
        Assert.AreEqual(BuildReadinessStatus.NeedsReindex, result.Status);
        Assert.IsFalse(result.IsReady);
        StringAssert.Contains(result.Message, "stale");
        CollectionAssert.Contains(result.BlockingIssues, "Project index is stale after file changes.");
    }

    [TestMethod]
    public async Task EvaluateReadiness_ReadyIndexWithZeroFiles_ReturnsNeedsReindex()
    {
        // Arrange
        await SeedProjectProfileAsync(_projectId, testFramework: "xUnit", allowBuilderApply: true);
        await SeedProjectCommandAsync(_projectId, "Build", "dotnet build");
        await SeedProjectIndexAsync(_projectId, indexedFileCount: 0);

        // Act
        var result = await _readinessService.EvaluateReadinessAsync(_projectId, _ticketId);

        // Assert
        Assert.AreEqual(BuildReadinessStatus.NeedsReindex, result.Status);
        Assert.IsFalse(result.IsReady);
        StringAssert.Contains(result.Message, "contains no files");
    }

    [TestMethod]
    public async Task EvaluateReadiness_UnclearTicket_ReturnsNeedsClarification()
    {
        // Arrange
        var unclearTicketId = await SeedTicketAsync(_projectId, title: "Fix it", summary: null, problem: null, acceptanceCriteria: null);
        await SeedProjectProfileAsync(_projectId, testFramework: "xUnit", allowBuilderApply: true);
        await SeedProjectCommandAsync(_projectId, "Build", "dotnet build");
        await SeedProjectIndexAsync(_projectId);

        // Act
        var result = await _readinessService.EvaluateReadinessAsync(_projectId, unclearTicketId);

        // Assert
        Assert.AreEqual(BuildReadinessStatus.NeedsClarification, result.Status);
        Assert.IsFalse(result.IsReady);
        StringAssert.Contains(result.Message, "scope is unclear");
    }

    [TestMethod]
    public async Task EvaluateReadiness_PersistenceTicketWithoutArchitecture_ReturnsNeedsArchitectureDecision()
    {
        // Arrange
        var persistenceTicketId = await SeedTicketAsync(
            _projectId,
            title: "Persist books",
            summary: "Persist books to a database.",
            problem: "BookSeller needs database persistence.",
            acceptanceCriteria: "Books survive application restart.");
        await SeedProjectProfileAsync(_projectId, testFramework: "xUnit", allowBuilderApply: true);
        await SeedProjectCommandAsync(_projectId, "Build", "dotnet build");
        await SeedProjectIndexAsync(_projectId);

        // Act
        var result = await _readinessService.EvaluateReadinessAsync(_projectId, persistenceTicketId);

        // Assert
        Assert.AreEqual(BuildReadinessStatus.NeedsArchitectureDecision, result.Status);
        Assert.IsFalse(result.IsReady);
        StringAssert.Contains(result.Message, "Persistence architecture");
    }

    [TestMethod]
    public async Task EvaluateReadiness_RelevantOpenQuestion_ReturnsNeedsArchitectureDecision()
    {
        // Arrange
        await SeedProjectProfileAsync(_projectId, testFramework: "xUnit", allowBuilderApply: true);
        await SeedProjectCommandAsync(_projectId, "Build", "dotnet build");
        await SeedProjectIndexAsync(_projectId);
        await SeedOpenQuestionAsync(_projectId, "Which sorting API should BookSeller expose?", appliesToArea: "sorting");

        // Act
        var result = await _readinessService.EvaluateReadinessAsync(_projectId, _ticketId);

        // Assert
        Assert.AreEqual(BuildReadinessStatus.NeedsArchitectureDecision, result.Status);
        Assert.IsFalse(result.IsReady);
        Assert.IsTrue(result.BlockingIssues.Any(issue => issue.Contains("Open question", StringComparison.OrdinalIgnoreCase)));
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

    private async Task SeedProjectIndexAsync(int projectId, string status = "Ready", int indexedFileCount = 7)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            UPDATE dbo.Projects
            SET IndexingStatus = @Status,
                IndexedFileCount = @IndexedFileCount,
                LastIndexedUtc = SYSUTCDATETIME()
            WHERE Id = @ProjectId;
            """;

        await connection.ExecuteAsync(sql, new { ProjectId = projectId, Status = status, IndexedFileCount = indexedFileCount });
    }

    private async Task<long> SeedTicketAsync(
        int projectId,
        string title = "Add book sorting",
        string? summary = "Sort books by title.",
        string? problem = "Users need a predictable book ordering workflow.",
        string? acceptanceCriteria = "Books can be sorted by title.")
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            INSERT INTO dbo.ProjectTickets
                (TenantId, ProjectId, SessionId, Title, TicketType, Priority,
                 Summary, Problem, AcceptanceCriteria, Status, Content, IsGenerated)
            OUTPUT inserted.Id
            VALUES
                (1, @ProjectId, NEWID(), @Title, 'Feature', 'Medium',
                 @Summary, @Problem, @AcceptanceCriteria, 'Draft', COALESCE(@Summary, ''), 0);
            """;

        return await connection.QuerySingleAsync<long>(sql, new
        {
            ProjectId = projectId,
            Title = title,
            Summary = summary,
            Problem = problem,
            AcceptanceCriteria = acceptanceCriteria
        });
    }

    private async Task SeedOpenQuestionAsync(int projectId, string title, string appliesToArea)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            INSERT INTO dbo.ProjectContextDocuments
                (TenantId, ProjectId, DocumentType, AuthorityLevel, Status, Title, Content, AppliesToArea)
            VALUES
                (1, @ProjectId, 'OpenQuestion', 'Pending', 'Active', @Title, @Title, @AppliesToArea);
            """;

        await connection.ExecuteAsync(sql, new { ProjectId = projectId, Title = title, AppliesToArea = appliesToArea });
    }
}
