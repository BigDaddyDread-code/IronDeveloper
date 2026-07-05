using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IronDev.Api.Controllers;
using Dapper;
using IronDev.Core.Builder;
using IronDev.Core.Governance;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Core.Workflow;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
[TestCategory("AlphaSmoke")]
[TestCategory("ReleaseReadiness")]
[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
public sealed class AlphaSmokeApiPersistenceTests : ApiTestBase
{
    private const string TicketKey = "validate-book";

    private const string ValidatedBook =
        """
        namespace BookSeller.Domain;

        public sealed class Book
        {
            public Book(string isbn, string title, string author, decimal price)
            {
                if (string.IsNullOrWhiteSpace(isbn))
                    throw new System.ArgumentException("ISBN is required.", nameof(isbn));
                if (string.IsNullOrWhiteSpace(title))
                    throw new System.ArgumentException("Title is required.", nameof(title));
                if (price < 0)
                    throw new System.ArgumentOutOfRangeException(nameof(price), "Price cannot be negative.");

                Isbn = isbn;
                Title = title;
                Author = author;
                Price = price;
            }

            public string Isbn { get; }
            public string Title { get; }
            public string Author { get; }
            public decimal Price { get; }
        }
        """;

    [TestMethod]
    public async Task Rel3_OneTicket_ReachesApplied_ThroughSqlBackedApi()
    {
        var workspaceParent = TempDir("irondev-rel3-ws");
        var evidenceRoot = TempDir("irondev-rel3-ev");
        var sampleCopy = TempDir("irondev-rel3-src");

        try
        {
            using var noNodeReuse = DisableMsBuildNodeReuse();
            CopySample(SampleRoot(), sampleCopy);
            PrepareRestoredBookSellerSource(sampleCopy);
            GitInit(sampleCopy);

            using var factory = Factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.UseSetting("DisposableBuild:EvidenceRoot", evidenceRoot);
                builder.UseSetting("DisposableBuild:WorkspaceRoot", workspaceParent);
                builder.UseSetting("DisposableBuild:BuildTimeoutSeconds", "300");
                builder.UseSetting("DisposableBuild:TestTimeoutSeconds", "300");
                builder.UseSetting("SkeletonApply:Enabled", "true");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IBuilderProposalService>();
                    services.AddScoped<IBuilderProposalService, DeterministicRel3Builder>();
                    services.RemoveAll<ISkeletonTestAuthoringService>();
                    services.AddScoped<ISkeletonTestAuthoringService, EmptyTestAuthoring>();
                    services.RemoveAll<ISkeletonCriticReviewService>();
                    services.AddScoped<ISkeletonCriticReviewService, DeterministicCleanCriticReviewService>();
                });
            });

            using var client = factory.CreateClient();
            await AuthenticateAsync(client);

            var project = await CreateProjectAsync(client, sampleCopy);
            var ticket = await CreateTicketAsync(client, project.Id);

            var started = await PostJsonAsync<TicketBuildRunDto>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs",
                content: null);
            Assert.AreEqual("PausedForApproval", started.Status);
            Assert.IsTrue(started.RequiresHumanApproval);

            var haltedReport = await GetJsonAsync<SkeletonRunReport>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/report");
            Assert.AreEqual("PausedForApproval", haltedReport.Status);
            Assert.IsNotNull(haltedReport.CriticPackage);
            Assert.IsTrue(haltedReport.CriticPackage!.HashVerified);
            Assert.IsNotNull(haltedReport.Approval);
            Assert.IsTrue(haltedReport.Approval!.HaltObserved);
            Assert.IsFalse(haltedReport.Approval.ContinuationUnblocked);

            var packageHash = haltedReport.Approval.TargetHash;
            Assert.IsFalse(string.IsNullOrWhiteSpace(packageHash));
            Assert.AreEqual(packageHash, haltedReport.CriticPackage.Sha256OnDisk);

            var criticReview = await PostJsonAsync<SkeletonCriticReviewOutcome>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/critic-review",
                content: null);
            Assert.IsTrue(criticReview.Succeeded);
            Assert.AreEqual("NoFindings", criticReview.Verdict);

            var approvalProjectId = TicketSkeletonRunService.ApprovalProjectGuid(project.Id);
            var approval = await CreateAcceptedApprovalAsync(
                client,
                approvalProjectId,
                started.RunId,
                packageHash);
            Assert.AreEqual(started.RunId, approval.ApprovalTargetId);
            Assert.AreEqual(packageHash, approval.ApprovalTargetHash);

            var approvalReadback = await GetJsonAsync<AcceptedApprovalApiEnvelope<IReadOnlyList<AcceptedApprovalReadModel>>>(
                client,
                $"/api/v1/projects/{approvalProjectId:D}/accepted-approvals/by-target/{TicketSkeletonRunService.ApprovalTargetKind}/{started.RunId}");
            Assert.AreEqual("found", approvalReadback.Status);
            Assert.IsTrue(approvalReadback.Data!.Any(record => record.AcceptedApprovalId == approval.AcceptedApprovalId));

            var continued = await PostJsonAsync<TicketBuildRunDto>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/continue",
                content: null);
            Assert.AreEqual("Completed", continued.Status);
            Assert.IsFalse(continued.RequiresHumanApproval);

            var applied = await PostJsonAsync<TicketBuildRunDto>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/apply",
                content: null);
            Assert.AreEqual("Applied", applied.Status);

            var finalStatus = await GetJsonAsync<RunStatusDto>(client, $"/api/runs/{started.RunId}");
            Assert.AreEqual("Applied", finalStatus.Status);

            var finalReport = await GetJsonAsync<SkeletonRunReport>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/report");
            Assert.AreEqual("Applied", finalReport.Status);
            Assert.IsTrue(finalReport.LoopComplete, $"Final report gaps: {string.Join(" | ", finalReport.Gaps)}");
            Assert.AreEqual(approval.AcceptedApprovalId.ToString("D"), finalReport.Approval!.AcceptedApprovalId);
            Assert.IsTrue(finalReport.Apply!.Applied);
            Assert.IsTrue(finalReport.Apply.Receipts.All(receipt => receipt.ExistsOnDisk));

            await AssertSqlPersistenceAsync(started.RunId, project.Id, ticket.Id, approval.AcceptedApprovalId, packageHash);

            var applyReceipt = finalReport.Apply.Receipts.Single(receipt => receipt.Name == "apply-copy.json");
            var persistedApplyReceiptPath = CopySmokeArtifact(applyReceipt.Path, "rel3-apply-copy.json");
            var applyReceiptHash = ComputeSha256(await File.ReadAllBytesAsync(persistedApplyReceiptPath));

            var landedPath = Path.Combine(sampleCopy, "src", "BookSeller.Domain", "Book.cs");
            Assert.AreEqual(
                ValidatedBook.Replace("\r\n", "\n", StringComparison.Ordinal),
                (await File.ReadAllTextAsync(landedPath)).Replace("\r\n", "\n", StringComparison.Ordinal),
                "The API-persisted applied path must land the approved Book change in the disposable source copy.");
            Assert.IsFalse(Directory.Exists(Path.Combine(sampleCopy, ".irondev")),
                "Apply evidence must stay in the workspace, not the source repo.");

            WriteReceipt(new Rel3AlphaSmokeReceipt(
                Ticket: TicketKey,
                Project: "BookSeller",
                ModelMode: "Deterministic",
                RunUntil: "Applied",
                RunId: started.RunId,
                ApiPersisted: true,
                SqlPersisted: true,
                ProjectId: project.Id,
                TicketId: ticket.Id,
                GateState: "PausedForApproval",
                BuildAndTestSucceeded: true,
                CriticPackageSha256: packageHash,
                ApprovalTargetHash: packageHash,
                BuilderModel: "deterministic-fake",
                AcceptedApprovalCreated: true,
                AcceptedApprovalRecorded: true,
                ContinuationRequested: true,
                ApplyRequested: true,
                CriticReviewRecorded: true,
                AcceptedApprovalId: approval.AcceptedApprovalId.ToString("D"),
                ApplyReceiptPath: persistedApplyReceiptPath,
                ApplyReceiptSha256: applyReceiptHash,
                FinalState: "Applied",
                LoopComplete: finalReport.LoopComplete,
                ReportReconstructable: finalReport.Gaps.Count == 0,
                Proves:
                [
                    "authenticated API creates project and ticket",
                    "authenticated API starts the skeleton run and reconstructs the halt report",
                    "authenticated API records critic review evidence",
                    "accepted approval is created through the accepted-approval API and read back from SQL",
                    "continuation consumes live SQL-backed accepted approval evidence",
                    "controlled apply reaches Applied through the API",
                    "SQL contains the run, event trail, and accepted approval rows"
                ],
                DoesNotProveYet:
                [
                    "a live model",
                    "product UI approval recording",
                    "fresh-machine dogfood from clone",
                    "commit, push, release, or deployment"
                ]));
        }
        finally
        {
            TryDelete(sampleCopy);
            TryDelete(workspaceParent);
            TryDelete(evidenceRoot);
        }
    }

    private static async Task<Project> CreateProjectAsync(HttpClient client, string localPath)
    {
        var response = await client.PostAsJsonAsync("/api/projects", new Project
        {
            Name = "BookSeller REL-3",
            Description = "REL-3 SQL/API persisted alpha smoke fixture.",
            LocalPath = localPath
        });

        return await ReadSuccessAsync<Project>(response);
    }

    private static async Task<ProjectTicket> CreateTicketAsync(HttpClient client, int projectId)
    {
        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/tickets", new CreateProjectTicketRequest
        {
            Title = "Reject invalid books at the door",
            Type = "Task",
            Priority = "High",
            Summary = "Validate a Book at construction.",
            Problem = "Invalid book data can currently enter the domain model.",
            ProposedChange = "Reject empty ISBN, empty title, and negative price in the Book constructor.",
            AcceptanceCriteria =
            [
                "Empty ISBN is rejected.",
                "Empty title is rejected.",
                "Negative price is rejected.",
                "Zero price is valid."
            ],
            Provenance = new TicketProvenanceDto
            {
                Source = "rel3-alpha-smoke",
                Notes = "Fixture-backed release smoke ticket."
            }
        });

        return await ReadSuccessAsync<ProjectTicket>(response);
    }

    private static async Task<T> PostJsonAsync<T>(HttpClient client, string path, object? content)
    {
        var response = content is null
            ? await client.PostAsync(path, content: null)
            : await client.PostAsJsonAsync(path, content);

        return await ReadSuccessAsync<T>(response);
    }

    private static async Task<T> GetJsonAsync<T>(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        return await ReadSuccessAsync<T>(response);
    }

    private static async Task<T> ReadSuccessAsync<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.StatusCode}: {text}");
        var result = JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.IsNotNull(result, $"Response JSON could not deserialize to {typeof(T).Name}: {text}");
        return result!;
    }

    private static async Task<AcceptedApprovalReadModel> CreateAcceptedApprovalAsync(
        HttpClient client,
        Guid approvalProjectId,
        string runId,
        string packageHash)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/projects/{approvalProjectId:D}/accepted-approvals", new CreateAcceptedApprovalRequest(
            TicketSkeletonRunService.ApprovalTargetKind,
            runId,
            packageHash,
            TicketSkeletonRunService.ContinueCapabilityCode,
            AcceptedApprovalPurposes.WorkflowContinuationInput,
            DateTimeOffset.UtcNow.AddHours(1),
            $"rel3:{runId}",
            $"critic-package:{runId}",
            [$"critic-package:{runId}", $"halt-package:{packageHash}"],
            [
                "Accepted approval record is input evidence only.",
                "Continuation and controlled apply remain separate governed requests."
            ],
            $"rel3-client:{runId}"));

        var envelope = await ReadSuccessAsync<AcceptedApprovalApiEnvelope<AcceptedApprovalReadModel>>(response);
        Assert.AreEqual("created", envelope.Status);
        Assert.IsTrue(envelope.MutationOccurred);
        Assert.IsNotNull(envelope.Data);
        AssertAcceptedApprovalCreateBoundary(envelope.Boundary);
        return envelope.Data!;
    }

    private static void AssertAcceptedApprovalCreateBoundary(object boundary)
    {
        var json = JsonSerializer.SerializeToElement(boundary, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.IsFalse(json.GetProperty("acceptedApprovalCreateContinuesWorkflow").GetBoolean());
        Assert.IsFalse(json.GetProperty("acceptedApprovalCreateAppliesSource").GetBoolean());
        Assert.IsFalse(json.GetProperty("acceptedApprovalCreateApprovesRelease").GetBoolean());
        Assert.IsFalse(json.GetProperty("acceptedApprovalCreateAuthorizesExecution").GetBoolean());
    }

    private static async Task AuthenticateAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = AdminPassword });
        var loginJson = await ReadSuccessAsync<JsonElement>(login);
        var baseToken = loginJson.GetProperty("token").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(baseToken));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tenants/select");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", baseToken);
        request.Content = JsonContent.Create(new { tenantId = AssignedTenantId });

        var select = await client.SendAsync(request);
        var selectJson = await ReadSuccessAsync<JsonElement>(select);
        var tenantToken = selectJson.GetProperty("token").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(tenantToken));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
    }

    private static async Task AssertSqlPersistenceAsync(
        string runId,
        int projectId,
        long ticketId,
        Guid acceptedApprovalId,
        string packageHash)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var run = await connection.QuerySingleAsync<PersistedRunRow>(
            "SELECT RunId, ProjectId, TicketId, State FROM dbo.Runs WHERE RunId = @RunId",
            new { RunId = runId });
        Assert.AreEqual(projectId, run.ProjectId);
        Assert.AreEqual(ticketId, run.TicketId);
        Assert.AreEqual("Applied", run.State);

        var eventTypes = (await connection.QueryAsync<string>(
            "SELECT EventType FROM dbo.RunEvents WHERE RunId = @RunId ORDER BY TimestampUtc, Id",
            new { RunId = runId })).ToArray();
        foreach (var required in new[]
                 {
                     "RunStarted",
                     "ProposalGenerated",
                     "SkeletonEvidencePackaged",
                     "CriticReviewPackageReady",
                     "ApprovalRequiredHalt",
                     "SkeletonCriticReviewRecorded",
                     "SkeletonContinuationUnblocked",
                     "SkeletonApplyStarted",
                     "SkeletonApplyPromoted",
                     "SkeletonApplied"
                 })
        {
            CollectionAssert.Contains(eventTypes, required, $"Missing persisted event: {required}");
        }

        var approvalCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM governance.AcceptedApproval
            WHERE AcceptedApprovalId = @AcceptedApprovalId
              AND ApprovalTargetId = @RunId
              AND ApprovalTargetHash = @PackageHash;
            """,
            new { AcceptedApprovalId = acceptedApprovalId, RunId = runId, PackageHash = packageHash });
        Assert.AreEqual(1, approvalCount, "Accepted approval must be persisted and bound to the run/package hash.");
    }

    private sealed class DeterministicRel3Builder : IBuilderProposalService
    {
        public Task<BuilderProposal> GenerateProposalAsync(long ticketId, CancellationToken ct = default) =>
            Task.FromResult(new BuilderProposal
            {
                TicketId = ticketId,
                ProjectId = 0,
                Summary = "Validate Book at construction.",
                Rationale = "Reject empty ISBN/title and negative price so downstream code can trust a Book.",
                ModelProvider = "deterministic-fake",
                ModelName = "rel3-validate-book-fixed",
                Changes =
                [
                    new ProposedFileChange
                    {
                        FilePath = "src/BookSeller.Domain/Book.cs",
                        Description = "Add constructor validation.",
                        IsValid = true,
                        FullContentAfter = ValidatedBook
                    }
                ]
            });

        public Task<BuilderProposal> GenerateProposalFromRequestAsync(int projectId, string request, CancellationToken ct = default) =>
            GenerateProposalAsync(0, ct);

        public Task ApplyProposalAsync(BuilderProposal proposal, CancellationToken ct = default) =>
            throw new NotSupportedException("REL-3 applies through the skeleton-run API, not direct builder writes.");
    }

    private sealed class EmptyTestAuthoring : ISkeletonTestAuthoringService
    {
        public Task<SkeletonTestAuthoringResult> AuthorTestsAsync(SkeletonTestAuthoringRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SkeletonTestAuthoringResult
            {
                Succeeded = true,
                Tests = [],
                ModelProvider = "deterministic-fake",
                ModelName = "rel3-no-authored-tests"
            });
    }

    private sealed class DeterministicCleanCriticReviewService(IRunEventStore events) : ISkeletonCriticReviewService
    {
        public async Task<SkeletonCriticReviewOutcome?> ReviewAsync(SkeletonCriticReviewRequest request, CancellationToken cancellationToken = default)
        {
            var runEvents = await events.GetEventsAsync(request.RunId, cancellationToken);
            var package = runEvents.LastOrDefault(e => e.EventType == "CriticReviewPackageReady");
            if (package is null || !package.Payload.TryGetValue("packageSha256", out var packageHash))
            {
                return new SkeletonCriticReviewOutcome
                {
                    Succeeded = false,
                    FailureReason = "Critic package evidence is missing."
                };
            }

            var reviewId = $"review-{request.RunId}";
            await events.PublishAsync(new RunEventDto
            {
                RunId = request.RunId,
                EventType = "SkeletonCriticReviewRecorded",
                Message = "Deterministic REL-3 critic review recorded no findings. A critic review is not approval.",
                Payload = new Dictionary<string, string>
                {
                    ["criticAgentRunId"] = $"critic-{request.RunId}",
                    ["reviewId"] = reviewId,
                    ["verdict"] = "NoFindings",
                    ["findingCount"] = "0",
                    ["blockingFindingCount"] = "0",
                    ["findingIds"] = string.Empty,
                    ["packageSha256"] = packageHash,
                    ["groundTruthCheckCount"] = "1",
                    ["groundTruthMismatchCount"] = "0",
                    ["modelProvider"] = "deterministic-fake",
                    ["modelName"] = "rel3-critic-clean-fixed",
                    ["requestedByUserId"] = request.RequestedByUserId
                }
            }, cancellationToken);

            return new SkeletonCriticReviewOutcome
            {
                Succeeded = true,
                CriticAgentRunId = $"critic-{request.RunId}",
                ReviewId = reviewId,
                Verdict = "NoFindings",
                GroundTruth = new SkeletonGroundTruthVerification
                {
                    Checks =
                    [
                        new SkeletonGroundTruthCheck
                        {
                            CheckName = SkeletonGroundTruthCheckNames.PackageHash,
                            Passed = true,
                            Expected = packageHash,
                            Actual = packageHash,
                            Detail = "Deterministic REL-3 review accepted the persisted package hash.",
                            BlocksMerge = false
                        }
                    ]
                },
                Findings = []
            };
        }
    }

    private static string SampleRoot() => Path.Combine(RepositoryRoot(), "Samples", "BookSeller");

    private static void CopySample(string source, string destination)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            if (IsIgnored(dir))
                continue;
            Directory.CreateDirectory(dir.Replace(source, destination, StringComparison.Ordinal));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (IsIgnored(file))
                continue;
            var target = file.Replace(source, destination, StringComparison.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool IsIgnored(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase));

    private static void PrepareRestoredBookSellerSource(string path)
    {
        File.WriteAllText(Path.Combine(path, "Directory.Build.props"),
            """
            <Project>
              <PropertyGroup>
                <MSBuildProjectExtensionsPath>.assets/$(MSBuildProjectName)/</MSBuildProjectExtensionsPath>
              </PropertyGroup>
            </Project>
            """);
        RunTool(path, "dotnet", "restore BookSeller.slnx --nologo");
    }

    private static void GitInit(string path)
    {
        RunTool(path, "git", "init");
        RunTool(path, "git", "config user.email alpha-smoke@irondev.local");
        RunTool(path, "git", "config user.name AlphaSmoke");
        RunTool(path, "git", "add .");
        RunTool(path, "git", "commit -m baseline");
    }

    private static void RunTool(string workingDirectory, string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        })!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        Assert.AreEqual(0, process.ExitCode, $"{fileName} {arguments} failed: {stderr.Result}{stdout.Result}");
    }

    private static IDisposable DisableMsBuildNodeReuse()
    {
        var previous = Environment.GetEnvironmentVariable("MSBUILDDISABLENODEREUSE");
        Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1");
        return new EnvironmentVariableScope("MSBUILDDISABLENODEREUSE", previous);
    }

    private static string TempDir(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), "IronDev", "Test", $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup for local smoke temp folders.
        }
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static void WriteReceipt(Rel3AlphaSmokeReceipt receipt)
    {
        var path = Environment.GetEnvironmentVariable("ALPHA_SMOKE_RECEIPT");
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Path.GetTempPath(), "IronDev", "alpha-smoke", "rel3-run-receipt.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"ALPHA_SMOKE_RECEIPT_PATH::{path}");
    }

    private static string CopySmokeArtifact(string sourcePath, string fileName)
    {
        var receiptPath = Environment.GetEnvironmentVariable("ALPHA_SMOKE_RECEIPT");
        var outputDirectory = string.IsNullOrWhiteSpace(receiptPath)
            ? Path.Combine(Path.GetTempPath(), "IronDev", "alpha-smoke")
            : Path.GetDirectoryName(receiptPath)!;

        Directory.CreateDirectory(outputDirectory);
        var targetPath = Path.Combine(outputDirectory, fileName);
        File.Copy(sourcePath, targetPath, overwrite: true);
        return targetPath;
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class EnvironmentVariableScope(string name, string? previousValue) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, previousValue);
    }

    private sealed record PersistedRunRow(string RunId, int ProjectId, long TicketId, string State);

    private sealed record Rel3AlphaSmokeReceipt(
        string Ticket,
        string Project,
        string ModelMode,
        string RunUntil,
        string RunId,
        bool ApiPersisted,
        bool SqlPersisted,
        int ProjectId,
        long TicketId,
        string GateState,
        bool BuildAndTestSucceeded,
        string CriticPackageSha256,
        string ApprovalTargetHash,
        string BuilderModel,
        bool AcceptedApprovalCreated,
        bool AcceptedApprovalRecorded,
        bool ContinuationRequested,
        bool ApplyRequested,
        bool CriticReviewRecorded,
        string AcceptedApprovalId,
        string ApplyReceiptPath,
        string ApplyReceiptSha256,
        string FinalState,
        bool LoopComplete,
        bool ReportReconstructable,
        string[] Proves,
        string[] DoesNotProveYet);
}
