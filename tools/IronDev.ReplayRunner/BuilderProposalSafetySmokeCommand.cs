using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using IronDev.Core.Auth;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data;
using IronDev.Data.Models;
using IronDev.Infrastructure.Builder;
using IronDev.Infrastructure.Services;
using IronDev.Services;

public static class BuilderProposalSafetySmokeCommand
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var requestedProjectName = ReadOption(args, "--project") ?? "IronDev";
        var dogfoodRunId = ReadOption(args, "--dogfood-run-id") ?? $"builder-proposal-safety-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var repoRoot = FindRepositoryRoot();
        var fixture = await CreateFixtureAsync(repoRoot, dogfoodRunId);
        var connectionFactory = new CliConnectionFactory(ResolveIronDevConnectionString(args, repoRoot));

        await ApplySqlScriptAsync(
            connectionFactory,
            Path.Combine(repoRoot, "Database", "migrate_project_documents.sql"));

        var baseProject = await ResolveProjectAsync(connectionFactory, requestedProjectName);
        if (baseProject is null)
        {
            Console.Error.WriteLine($"Project not found: {requestedProjectName}");
            return 1;
        }

        var services = CreateSmokeServices(connectionFactory, baseProject.TenantId);
        var project = await CreateDisposableProjectAsync(services.ProjectService, baseProject, dogfoodRunId, fixture.RunRoot);
        var source = await CreateSourceDocumentAsync(services.DocumentService, project.ProjectId);
        var ticketId = await CreateTicketAsync(services.TicketService, services.DocumentService, baseProject, project.ProjectId, source.Version.Id, fixture.TargetFileName);
        var result = await RunSafetyChecksAsync(services.ContextService, project, source, ticketId, fixture, dogfoodRunId, baseProject.TenantId);

        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return result.Passed ? 0 : 1;
    }

    private static async Task<BuilderSafetyFixture> CreateFixtureAsync(string repoRoot, string dogfoodRunId)
    {
        var runRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", dogfoodRunId, "builder-safety-target");
        Directory.CreateDirectory(runRoot);

        const string targetFileName = "BuilderSafetyTarget.txt";
        const string beforeContent = "original builder safety fixture";
        const string afterContent = "changed builder safety fixture";
        var targetFilePath = Path.Combine(runRoot, targetFileName);
        await File.WriteAllTextAsync(targetFilePath, beforeContent);

        return new BuilderSafetyFixture(
            runRoot,
            targetFileName,
            targetFilePath,
            beforeContent,
            afterContent,
            ComputeFileSha256(targetFilePath));
    }

    private static BuilderSafetyServices CreateSmokeServices(IDbConnectionFactory connectionFactory, int tenantId)
    {
        var tenant = new CliTenantContext(tenantId);
        var sourceReferenceService = new ArtifactSourceReferenceService(connectionFactory);
        var documentService = new ProjectDocumentService(connectionFactory, tenant);
        var ticketService = new TicketService(connectionFactory, tenant, sourceReferenceService);
        var projectService = new ProjectService(connectionFactory, tenant);
        var memoryService = new ProjectMemoryService(connectionFactory, tenant, sourceReferenceService);
        var profileService = new ProjectProfileService(connectionFactory, tenant);
        var contextService = new BuilderContextService(
            ticketService,
            projectService,
            memoryService,
            profileService,
            documentService);

        return new BuilderSafetyServices(projectService, documentService, ticketService, contextService);
    }

    private static async Task<BuilderSafetyProject> CreateDisposableProjectAsync(
        ProjectService projectService,
        CliProjectContext baseProject,
        string dogfoodRunId,
        string runRoot)
    {
        var stamp = dogfoodRunId.Replace(':', '-').Replace('\\', '-').Replace('/', '-');
        var name = $"IronDevBuilderProposalSafety016_{stamp}";
        var projectId = await projectService.CreateProjectAsync(new Project
        {
            TenantId = baseProject.TenantId,
            Name = name[..Math.Min(120, name.Length)],
            Description = "Disposable project for Memory Spine 016 builder proposal safety smoke.",
            LocalPath = runRoot
        });

        return new BuilderSafetyProject(projectId, name);
    }

    private static async Task<BuilderSafetySourceDocument> CreateSourceDocumentAsync(
        ProjectDocumentService documentService,
        int projectId)
    {
        var title = $"BUILDER_PROPOSAL_SAFETY_016_{Guid.NewGuid():N}";
        var document = await documentService.CreateDocumentAsync(new CreateProjectDocumentRequest
        {
            ProjectId = projectId,
            Title = title[..Math.Min(120, title.Length)],
            DocumentType = "Architecture",
            ContentMarkdown = """
                # Builder Proposal Safety

                Builder must generate reviewable proposals and stop before writes.
                Dry-run validation may inspect target files, but proposal generation must not modify them.
                Applying patches requires explicit approval and is outside this smoke.
                """,
            ChangeSummary = "Builder proposal safety source document",
            CreatedBy = "TestAgent",
            SourceEntityType = "Discussion",
            SourceEntityId = 16016
        });

        var version = await documentService.GetCurrentVersionAsync(document.Id)
            ?? throw new InvalidOperationException("Builder proposal safety source document version was not created.");

        return new BuilderSafetySourceDocument(document, version);
    }

    private static async Task<long> CreateTicketAsync(
        TicketService ticketService,
        ProjectDocumentService documentService,
        CliProjectContext baseProject,
        int projectId,
        long sourceVersionId,
        string targetFileName)
    {
        var ticket = new ProjectTicket
        {
            TenantId = baseProject.TenantId,
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            Title = "Prove builder proposal remains approval-first",
            TicketType = "Test",
            Priority = "High",
            Summary = "Generate a deterministic builder proposal and prove no target files are changed before approval.",
            Problem = "Builder workflows are dangerous if proposal generation writes files or bypasses approval.",
            AcceptanceCriteria = "- Proposal is generated.\n- Dry-run validation runs.\n- Target file hash is unchanged.\n- Apply without implemented approval path changes no files.",
            Status = "Draft",
            Content = "Memory Spine 016 smoke ticket.",
            ContextSummary = $"Source ProjectDocumentVersion:{sourceVersionId}",
            LinkedFilePaths = targetFileName,
            IsGenerated = true,
            GenerationNote = "Memory Spine 016 builder proposal safety smoke",
            SourceDocumentVersionId = sourceVersionId
        };

        var ticketId = await ticketService.SaveTicketAsync(ticket);
        await documentService.LinkVersionAsync(new LinkProjectDocumentVersionRequest
        {
            DocumentVersionId = sourceVersionId,
            LinkedEntityType = "Ticket",
            LinkedEntityId = ticketId,
            LinkType = "GeneratedTicket",
            CreatedBy = "TestAgent"
        });

        return ticketId;
    }

    private static async Task<BuilderProposalSafetySmokeResult> RunSafetyChecksAsync(
        BuilderContextService contextService,
        BuilderSafetyProject project,
        BuilderSafetySourceDocument source,
        long ticketId,
        BuilderSafetyFixture fixture,
        string dogfoodRunId,
        int tenantId)
    {
        var patchService = new CodePatchService();
        var orchestrator = new TicketBuildOrchestrator(
            contextService,
            new DeterministicCodeChangeProposalService(fixture.TargetFileName, fixture.BeforeContent, fixture.AfterContent),
            patchService);

        var preview = await orchestrator.CreateBuildPreviewAsync(project.ProjectId, ticketId);
        var afterPreviewHash = ComputeFileSha256(fixture.TargetFilePath);
        var approvalResult = await TryApplyAndBuildAsync(orchestrator, project, ticketId, fixture, preview.Proposal);
        var directPatchResult = await patchService.ApplyPatchesAsync(fixture.RunRoot, preview.Proposal.FileChanges);
        var afterDirectApplyAttemptHash = ComputeFileSha256(fixture.TargetFilePath);

        return BuildResult(
            dogfoodRunId,
            tenantId,
            project,
            source,
            ticketId,
            fixture,
            preview,
            approvalResult,
            directPatchResult,
            afterPreviewHash,
            afterDirectApplyAttemptHash);
    }

    private static async Task<(TicketBuildResult Result, string Hash)> TryApplyAndBuildAsync(
        TicketBuildOrchestrator orchestrator,
        BuilderSafetyProject project,
        long ticketId,
        BuilderSafetyFixture fixture,
        CodeChangeProposal proposal)
    {
        var result = await orchestrator.ApplyAndBuildAsync(new TicketBuildApproval
        {
            ProjectId = project.ProjectId,
            TicketId = ticketId,
            ProjectPath = fixture.RunRoot,
            ApprovedProposal = proposal
        });

        return (result, ComputeFileSha256(fixture.TargetFilePath));
    }

    private static BuilderProposalSafetySmokeResult BuildResult(
        string dogfoodRunId,
        int tenantId,
        BuilderSafetyProject project,
        BuilderSafetySourceDocument source,
        long ticketId,
        BuilderSafetyFixture fixture,
        TicketBuildPreview preview,
        (TicketBuildResult Result, string Hash) approval,
        PatchApplyResult directPatch,
        string afterPreviewHash,
        string afterDirectApplyAttemptHash)
    {
        var fileUnchangedAfterPreview = fixture.BeforeHash == afterPreviewHash;
        var fileUnchangedAfterApplyAttempt = fixture.BeforeHash == approval.Hash;
        var fileUnchangedAfterDirectPatchAttempt = fixture.BeforeHash == afterDirectApplyAttemptHash;
        var dryRunValidated = preview.ValidationResult.FileResults.Count > 0;
        var approvalBlocked = !approval.Result.PatchSucceeded &&
                              approval.Result.FilesChanged.Count == 0 &&
                              approval.Result.ErrorMessage.Contains("not implemented", StringComparison.OrdinalIgnoreCase);
        var directPatchBlocked = !directPatch.Succeeded && directPatch.FilesWritten.Count == 0;
        var sourceContextIncluded = preview.ContextSummary.Contains("Prove builder proposal remains approval-first", StringComparison.OrdinalIgnoreCase);

        var passed = preview.Proposal.FileChanges.Count > 0 &&
                     dryRunValidated &&
                     preview.ValidationResult.AllValid &&
                     fileUnchangedAfterPreview &&
                     fileUnchangedAfterApplyAttempt &&
                     fileUnchangedAfterDirectPatchAttempt &&
                     approvalBlocked &&
                     directPatchBlocked &&
                     sourceContextIncluded;

        return new BuilderProposalSafetySmokeResult
        {
            Goal = "builder-proposal-safety-016",
            DogfoodRunId = dogfoodRunId,
            Passed = passed,
            TenantId = tenantId,
            ProjectId = project.ProjectId,
            ProjectName = project.ProjectName,
            TicketId = ticketId,
            TicketTitle = "Prove builder proposal remains approval-first",
            SourceDocumentId = source.Document.Id,
            SourceDocumentVersionId = source.Version.Id,
            TargetFile = fixture.TargetFilePath,
            Proposal = BuildProposalEvidence(preview.Proposal),
            Safety = new BuilderProposalSafetyFlags
            {
                DryRunValidationRan = dryRunValidated,
                DryRunValidationPassed = preview.ValidationResult.AllValid,
                FileUnchangedAfterPreview = fileUnchangedAfterPreview,
                ApprovalGateBlockedApply = approvalBlocked,
                FileUnchangedAfterApplyAttempt = fileUnchangedAfterApplyAttempt,
                DirectPatchApplyBlocked = directPatchBlocked,
                FileUnchangedAfterDirectPatchAttempt = fileUnchangedAfterDirectPatchAttempt,
                SourceContextIncluded = sourceContextIncluded
            },
            Evidence = new BuilderProposalSafetyEvidence
            {
                BeforeHash = fixture.BeforeHash,
                AfterPreviewHash = afterPreviewHash,
                AfterApplyAttemptHash = approval.Hash,
                AfterDirectPatchAttemptHash = afterDirectApplyAttemptHash,
                ValidationSummary = preview.ValidationResult.Summary,
                ValidationMessages = preview.ValidationResult.FileResults.Select(result => result.Message).ToArray(),
                ApplyErrorMessage = approval.Result.ErrorMessage,
                DirectPatchErrorMessage = directPatch.ErrorMessage,
                ContextSummary = preview.ContextSummary
            },
            Boundary = "This proves builder proposal safety only: context plus deterministic proposal plus dry-run validation. It does not apply patches, run builds, or prove LLM proposal quality."
        };
    }

    private static BuilderProposalSafetyProposalEvidence BuildProposalEvidence(CodeChangeProposal proposal)
    {
        return new BuilderProposalSafetyProposalEvidence
        {
            ProposalGenerated = proposal.FileChanges.Count > 0,
            ProposedFileCount = proposal.FileChanges.Count,
            ProposedFiles = proposal.FileChanges.Select(change => change.FilePath).ToArray(),
            Summary = proposal.Summary,
            Rationale = proposal.Rationale,
            RiskNotes = proposal.RiskNotes,
            TestPlan = proposal.TestPlan
        };
    }

    private static async Task<CliProjectContext?> ResolveProjectAsync(IDbConnectionFactory connectionFactory, string projectName)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<CliProjectContext?>(new CommandDefinition(
            """
            SELECT TOP (1)
                Id AS ProjectId,
                TenantId,
                Name AS ProjectName
            FROM dbo.Projects
            WHERE Name = @ProjectName OR Name = @FallbackProjectName
            ORDER BY CASE WHEN Name = @ProjectName THEN 0 ELSE 1 END, Id;
            """,
            new
            {
                ProjectName = projectName,
                FallbackProjectName = projectName == "IronDev" ? "IronDeveloper" : "IronDev"
            }));
    }

    private static async Task ApplySqlScriptAsync(IDbConnectionFactory connectionFactory, string scriptPath)
    {
        if (!File.Exists(scriptPath))
            return;

        var script = await File.ReadAllTextAsync(scriptPath);
        var batches = script
            .Split(["\r\nGO\r\n", "\nGO\n", "\r\nGO\n", "\nGO\r\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(batch => batch.Trim())
            .Where(batch => !string.IsNullOrWhiteSpace(batch) && !batch.StartsWith("USE ", StringComparison.OrdinalIgnoreCase));

        using var connection = connectionFactory.CreateConnection();
        foreach (var batch in batches)
            await connection.ExecuteAsync(batch);
    }

    private static string ResolveIronDevConnectionString(string[] args, string repoRoot)
    {
        var explicitConnection = ReadOption(args, "--connection-string");
        if (!string.IsNullOrWhiteSpace(explicitConnection))
            return explicitConnection;

        var envConnection = Environment.GetEnvironmentVariable("IRONDEV_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(envConnection))
            return envConnection;

        foreach (var path in new[]
        {
            Path.Combine(repoRoot, "IronDeveloper", "appsettings.Development.json"),
            Path.Combine(repoRoot, "IronDeveloper", "appsettings.json")
        })
        {
            var connection = TryReadConnectionString(path, "IronDeveloperDb");
            if (!string.IsNullOrWhiteSpace(connection))
                return connection;
        }

        throw new InvalidOperationException("Could not resolve IronDeveloperDb connection string.");
    }

    private static string? TryReadConnectionString(string path, string name)
    {
        if (!File.Exists(path))
            return null;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings) &&
               connectionStrings.TryGetProperty(name, out var value)
            ? value.GetString()
            : null;
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}

public sealed record BuilderSafetyFixture(
    string RunRoot,
    string TargetFileName,
    string TargetFilePath,
    string BeforeContent,
    string AfterContent,
    string BeforeHash);

public sealed record BuilderSafetyServices(
    ProjectService ProjectService,
    ProjectDocumentService DocumentService,
    TicketService TicketService,
    BuilderContextService ContextService);

public sealed record BuilderSafetyProject(int ProjectId, string ProjectName);

public sealed record BuilderSafetySourceDocument(
    ProjectDocument Document,
    ProjectDocumentVersion Version);
