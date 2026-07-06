using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using IronDev.Api.Controllers;
using IronDev.Core.Agents.Concrete;
using IronDev.Core.Builder;
using IronDev.Core.Governance;
using IronDev.Core.Models;
using IronDev.Core.Workflow;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api.Smoke;

/// <summary>
/// HERO-2 — the first fully REAL governed walk. No test-double registrations at
/// all: the live model writes the proposal, the real disposable workspace builds
/// and tests it, the live critic reviews the real package (ground-truth verified),
/// every real finding gets a human-shaped disposition through the product route,
/// and the hash-bound approval, continuation, and controlled apply are the same
/// governed acts every deterministic proof exercises.
///
/// Boundary — live output is proposed work, never authority: a model cannot
/// approve, disposition, continue, or apply anything here; the test performs
/// those explicit acts through the product API exactly as REL-3 does, and the
/// receipt names the model so nobody mistakes a live pass for model reliability.
/// This smoke never falls back to deterministic mode: without the explicit live
/// opt-in it is Inconclusive, and any model failure is a named failure.
/// </summary>
[TestClass]
[TestCategory("AlphaSmoke")]
[TestCategory("ReleaseReadiness")]
[TestCategory("RequiresExternalDependency")]
[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
public sealed class LiveModelHeroWalkTests : ApiTestBase
{
    private const string BulkDiscountKey = "bulk-discount";

    [TestMethod]
    public async Task Hero2_LiveModel_BulkDiscount_WalksRealLoopToApplied()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("IRONDEV_ALPHA_SMOKE_LIVE_MODEL"), "1", StringComparison.Ordinal))
        {
            Assert.Inconclusive("HERO-2 live walk requires IRONDEV_ALPHA_SMOKE_LIVE_MODEL=1. It never falls back to deterministic mode.");
        }

        var provider = RequiredEnv("IRONDEV_ALPHA_SMOKE_LIVE_PROVIDER");
        var model = RequiredEnv("IRONDEV_ALPHA_SMOKE_LIVE_MODEL_NAME");
        var baseUrl = Environment.GetEnvironmentVariable("IRONDEV_ALPHA_SMOKE_LIVE_BASE_URL");
        var apiKey = Environment.GetEnvironmentVariable("IRONDEV_ALPHA_SMOKE_LIVE_API_KEY")
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        var workspaceParent = TempDir("irondev-hero2-ws");
        var evidenceRoot = TempDir("irondev-hero2-ev");
        var sampleCopy = TempDir("irondev-hero2-src");
        var walkCompleted = false;

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
                // HERO-3: arm bounded repair. A live model sometimes writes code
                // that fails the real build (observed); with one repair attempt the
                // walk survives its own bad day — the Builder repairs from the real
                // compiler evidence, and the gate at the end is exactly the gate.
                builder.UseSetting("SkeletonRepair:MaxAttempts", "1");
                // ApiTestBase pins Ai:Provider=fake through an in-memory config
                // source; this LATER source overrides it with the live model.
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    var live = new Dictionary<string, string?>
                    {
                        ["Ai:Provider"] = provider,
                        ["Ai:Model"] = model
                    };
                    if (!string.IsNullOrWhiteSpace(baseUrl))
                        live["Ai:BaseUrl"] = baseUrl;
                    if (!string.IsNullOrWhiteSpace(apiKey))
                        live["Ai:ApiKey"] = apiKey;
                    configuration.AddInMemoryCollection(live);
                });
                // The ONLY service change is restoring the REAL stored-critic
                // executor that ApiTestBase stubs out — every role (Builder,
                // Tester, Critic) runs the real product service on the live model.
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IStoredManualIndependentCriticAgentService>();
                    services.AddScoped<IStoredManualIndependentCriticAgentService, StoredManualIndependentCriticAgentService>();
                });
            });

            using var client = factory.CreateClient();

            // Provision the critic's audit-envelope table through the app's OWN
            // connection factory — the same connection the store will write with —
            // so config-precedence surprises cannot leave the runtime database
            // unprovisioned while a differently-resolved test connection looks fine.
            var appConnectionFactory = factory.Services.GetRequiredService<IronDev.Data.IDbConnectionFactory>();
            using (var appConnection = appConnectionFactory.CreateConnection())
            {
                appConnection.Open();
                Console.WriteLine($"HERO2_RUNTIME_DB::{((SqlConnection)appConnection).Database}");
                var migration = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "Database", "migrate_agent_run_audit_envelope.sql"));
                appConnection.Execute(migration);
            }

            await AuthenticateAsync(client);

            // ── The REAL first-run journey: project → profile → index ─────────
            var project = await PostJsonAsync<Project>(client, "/api/projects", new Project
            {
                Name = "BookSeller HERO-2",
                Description = "HERO-2 live-model real-loop walk.",
                LocalPath = sampleCopy
            });

            var detection = await PostJsonAsync<ProjectProfileDetectionResult>(
                client,
                "/api/profile/detect",
                new ProfilesController.DetectProfileRequest(sampleCopy, project.Id));

            var profile = detection.Profile;
            profile.ProjectId = project.Id;
            profile.AllowBuilderApply = true;
            profile.DatabaseEngine = string.IsNullOrWhiteSpace(profile.DatabaseEngine) ? "None" : profile.DatabaseEngine;
            profile.DataAccessStyle = string.IsNullOrWhiteSpace(profile.DataAccessStyle) ? "None" : profile.DataAccessStyle;
            await PostOkAsync(client, $"/api/projects/{project.Id}/profile", profile);

            detection.BuildCommand.ProjectId = project.Id;
            detection.BuildCommand.IsDefault = true;
            await PostOkAsync(client, $"/api/projects/{project.Id}/profile/commands", detection.BuildCommand);
            detection.TestCommand.ProjectId = project.Id;
            detection.TestCommand.IsDefault = true;
            await PostOkAsync(client, $"/api/projects/{project.Id}/profile/commands", detection.TestCommand);

            var indexResult = await PostJsonAsync<JsonElement>(
                client,
                $"/api/projects/{project.Id}/code-index",
                new CodeIndexController.IndexRequest(sampleCopy));
            var indexedCount = await GetJsonAsync<int>(client, $"/api/projects/{project.Id}/code-index/file-count");
            Assert.IsTrue(indexedCount > 0, $"Real indexing must index the BookSeller source. Index response: {indexResult}");

            // ── Ticket and the live governed run ───────────────────────────────
            var ticket = await CreateFixtureTicketAsync(client, project.Id, BulkDiscountKey);

            // Link the target file the way a user does in the ticket editor.
            // Discovered building this walk: prose TechnicalNotes defeat the
            // context path-hint extraction SILENTLY, and the builder then invents
            // the file. LinkedFilePaths is the designed mechanism; the silent
            // context loss is recorded as a named gap in the HERO-2 receipt.
            ticket.LinkedFilePaths = "src/BookSeller.Domain/PricingService.cs";
            var patchResponse = await client.PatchAsync(
                $"/api/projects/{project.Id}/tickets/{ticket.Id}",
                JsonContent.Create(ticket));
            Assert.IsTrue(patchResponse.IsSuccessStatusCode,
                $"Ticket link update failed: {(int)patchResponse.StatusCode} {await patchResponse.Content.ReadAsStringAsync()}");

            var started = await PostJsonAsync<TicketBuildRunDto>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs",
                content: null);

            var haltedReport = await GetJsonAsync<SkeletonRunReport>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/report");
            Assert.AreEqual("PausedForApproval", started.Status,
                $"The live run must reach the human gate on real build/test evidence. Timeline: {Describe(haltedReport)}");
            Assert.IsNotNull(haltedReport.Proposal, "The live proposal trace must be recorded.");
            Assert.AreNotEqual("deterministic-fake", haltedReport.Proposal!.ModelProvider,
                "HERO-2 must run a real model — never the deterministic fake.");

            var packageHash = haltedReport.Approval!.TargetHash;
            Assert.AreEqual(packageHash, haltedReport.CriticPackage!.Sha256OnDisk);

            // ── The REAL critic reviews the real package ───────────────────────
            var criticReview = await PostJsonAsync<SkeletonCriticReviewOutcome>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/critic-review",
                content: null);
            Assert.IsTrue(criticReview.Succeeded, $"Live critic review failed: {criticReview.FailureReason}");

            // ── Every REAL finding gets an explicit disposition through the product route ──
            var dispositions = new List<(string FindingId, string Severity, string Title)>();
            foreach (var finding in criticReview.Findings)
            {
                var outcome = await PostJsonAsync<SkeletonFindingDispositionOutcome>(
                    client,
                    $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/findings/{finding.FindingId}/disposition",
                    new TicketsController.FindingDispositionBody(
                        "AcceptRisk",
                        $"HERO-2 live walk: operator running this opt-in smoke acknowledges '{finding.Title}' ({finding.Severity}). Accepted to complete the live proof; follow-up judged by the human reading this receipt."));
                Assert.IsTrue(outcome.Succeeded, $"Disposition of live finding '{finding.FindingId}' failed: {outcome.FailureReason}");
                dispositions.Add((finding.FindingId, finding.Severity, finding.Title));
            }

            // ── Hash-bound approval, continuation, controlled apply ────────────
            var approvalProjectId = TicketSkeletonRunService.ApprovalProjectGuid(project.Id);
            var approval = await CreateAcceptedApprovalAsync(client, approvalProjectId, started.RunId, packageHash);

            var continued = await PostJsonAsync<TicketBuildRunDto>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/continue",
                content: null);
            Assert.AreEqual("Completed", continued.Status);

            var applied = await PostJsonAsync<TicketBuildRunDto>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/apply",
                content: null);
            Assert.AreEqual("Applied", applied.Status);

            var finalReport = await GetJsonAsync<SkeletonRunReport>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/report");
            Assert.AreEqual("Applied", finalReport.Status);
            Assert.IsTrue(finalReport.LoopComplete, $"Final report gaps: {string.Join(" | ", finalReport.Gaps)}");

            var applyReceipt = finalReport.Apply!.Receipts.Single(receipt => receipt.Name == "apply-copy.json");
            var persistedApplyReceiptPath = CopySmokeArtifact(applyReceipt.Path, "hero2-apply-copy.json");

            var runState = await QueryRunStateAsync(project.Id, ticket.Id, started.RunId);
            Assert.AreEqual("Applied", runState);

            WriteReceipt(new
            {
                Ticket = BulkDiscountKey,
                Project = "BookSeller",
                ModelMode = "Live",
                RunUntil = "Applied",
                RunId = started.RunId,
                ApiPersisted = true,
                SqlPersisted = true,
                ProjectId = project.Id,
                TicketId = ticket.Id,
                GateState = "PausedForApproval",
                BuildAndTestSucceeded = true,
                CriticPackageSha256 = packageHash,
                ApprovalTargetHash = packageHash,
                Provider = provider,
                BuilderModel = $"{haltedReport.Proposal.ModelProvider}/{haltedReport.Proposal.ModelName}",
                CriticModel = $"{finalReport.CriticReviews.Single().ModelProvider}/{finalReport.CriticReviews.Single().ModelName}",
                ProposalFileChangeCount = haltedReport.Proposal.FileChangeCount,
                IndexedFileCount = indexedCount,
                CriticVerdict = criticReview.Verdict,
                FindingCount = criticReview.Findings.Count,
                Findings = dispositions.Select(d => new { d.FindingId, d.Severity, d.Title, Disposition = "AcceptRisk" }).ToArray(),
                CriticReviewRecorded = true,
                AcceptedApprovalCreated = true,
                AcceptedApprovalRecorded = true,
                AcceptedApprovalId = approval.AcceptedApprovalId.ToString("D"),
                ContinuationRequested = true,
                ApplyRequested = true,
                ApplyReceiptPath = persistedApplyReceiptPath,
                ApplyReceiptSha256 = ComputeSha256(await File.ReadAllBytesAsync(persistedApplyReceiptPath)),
                FinalState = "Applied",
                LoopComplete = finalReport.LoopComplete,
                ReportReconstructable = finalReport.Gaps.Count == 0,
                // HERO-3: the honest repair record. Zero attempts = the model got it
                // right first try; N attempts = the loop survived its own bad day,
                // repaired from real compiler evidence, and STILL waited at the gate.
                RepairBudget = 1,
                SelfRepairOccurred = finalReport.RepairAttempts.Count > 0,
                RepairAttempts = finalReport.RepairAttempts.Select(attempt => new
                {
                    attempt.AttemptNumber,
                    attempt.FailureKind,
                    attempt.FailedCommand,
                    RepairModel = $"{attempt.ModelProvider}/{attempt.ModelName}"
                }).ToArray(),
                InitialProposalId = finalReport.InitialProposal?.ProposalId ?? string.Empty,
                GateProposalId = finalReport.Proposal!.ProposalId,
                Proves = new[]
                {
                    "real profile detection, profile save, and code indexing through product routes",
                    "live model generated the bulk-discount proposal through the real Builder service",
                    "real disposable workspace build/test produced the evidence that reached the gate",
                    "live critic reviewed the real package with ground-truth verification",
                    "every live finding was dispositioned through the product route before continuation",
                    "hash-bound approval, continuation, and controlled apply reached Applied with a complete report",
                    "bounded repair was ARMED (budget 1): a live build failure self-repairs from real evidence and still answers to the same human gate"
                },
                DoesNotProveYet = new[]
                {
                    "model reliability across runs, tickets, or providers",
                    "UI click-path for the live walk",
                    "fresh-machine repeatability",
                    "release readiness of any kind"
                }
            });

            walkCompleted = true;
        }
        finally
        {
            // Preserve everything when the live walk fails — the workspace and
            // evidence roots ARE the diagnosis material for a live-model failure.
            if (walkCompleted)
            {
                TryDelete(sampleCopy);
                TryDelete(workspaceParent);
                TryDelete(evidenceRoot);
            }
            else
            {
                Console.WriteLine($"HERO2_PRESERVED_WORKSPACE::{workspaceParent}");
                Console.WriteLine($"HERO2_PRESERVED_EVIDENCE::{evidenceRoot}");
                Console.WriteLine($"HERO2_PRESERVED_SOURCE::{sampleCopy}");
            }
        }
    }

    private static string Describe(SkeletonRunReport report) =>
        string.Join(" -> ", report.Timeline.Select(entry => $"{entry.EventType}: {entry.Message}"))
        + (report.Gaps.Count > 0 ? $" | GAPS: {string.Join(" | ", report.Gaps)}" : string.Empty);

    private static async Task<string?> QueryRunStateAsync(int projectId, long ticketId, string runId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<string>(
            "SELECT State FROM dbo.Runs WHERE RunId = @RunId AND ProjectId = @ProjectId AND TicketId = @TicketId",
            new { RunId = runId, ProjectId = projectId, TicketId = ticketId });
    }

    private static async Task<ProjectTicket> CreateFixtureTicketAsync(HttpClient client, int projectId, string key)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "TestFixtures", "BookSeller", "tickets.json")));
        var fixture = document.RootElement.GetProperty("tickets")
            .EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == key);

        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/tickets", new CreateProjectTicketRequest
        {
            Title = fixture.GetProperty("title").GetString()!,
            Type = "Task",
            Priority = "Medium",
            Summary = fixture.GetProperty("summary").GetString()!,
            Problem = fixture.GetProperty("summary").GetString()!,
            ProposedChange = fixture.GetProperty("technicalNotes").GetString()!,
            AcceptanceCriteria = fixture.GetProperty("acceptanceCriteria").GetString()!
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray(),
            Provenance = new TicketProvenanceDto
            {
                Source = $"hero2-live:{key}",
                Notes = "HERO-2 live walk fixture ticket. It grants no authority."
            }
        });

        return await ReadSuccessAsync<ProjectTicket>(response);
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
            $"hero2:{runId}",
            $"critic-package:{runId}",
            [$"critic-package:{runId}", $"halt-package:{packageHash}"],
            [
                "Accepted approval record is input evidence only.",
                "Continuation and controlled apply remain separate governed requests."
            ],
            $"hero2-client:{runId}"));

        var envelope = await ReadSuccessAsync<AcceptedApprovalApiEnvelope<AcceptedApprovalReadModel>>(response);
        Assert.AreEqual("created", envelope.Status);
        Assert.IsNotNull(envelope.Data);
        return envelope.Data!;
    }

    private static async Task AuthenticateAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = AdminPassword });
        var loginJson = await ReadSuccessAsync<JsonElement>(login);
        var baseToken = loginJson.GetProperty("token").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tenants/select");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", baseToken);
        request.Content = JsonContent.Create(new { tenantId = AssignedTenantId });

        var select = await client.SendAsync(request);
        var selectJson = await ReadSuccessAsync<JsonElement>(select);
        var tenantToken = selectJson.GetProperty("token").GetString();

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tenantToken);
    }

    private static async Task<T> PostJsonAsync<T>(HttpClient client, string path, object? content)
    {
        var response = content is null
            ? await client.PostAsync(path, content: null)
            : await client.PostAsJsonAsync(path, content);
        return await ReadSuccessAsync<T>(response);
    }

    private static async Task PostOkAsync(HttpClient client, string path, object content)
    {
        var response = await client.PostAsJsonAsync(path, content);
        var text = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"POST {path}: {(int)response.StatusCode} {response.StatusCode}: {text}");
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

    private static string RequiredEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.IsFalse(string.IsNullOrWhiteSpace(value), $"{name} is required for the HERO-2 live walk.");
        return value!;
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
        RunTool(path, "git", "config user.email hero2@irondev.local");
        RunTool(path, "git", "config user.name Hero2LiveWalk");
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

    private static void WriteReceipt(object receipt)
    {
        var path = Environment.GetEnvironmentVariable("ALPHA_SMOKE_RECEIPT");
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Path.GetTempPath(), "IronDev", "alpha-smoke", "hero2-live-walk-receipt.json");

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
}
