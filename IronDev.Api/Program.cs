using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using IronDev.Api.Controllers;
using IronDev.AI;
using IronDev.Api.Auth;
using IronDev.Api.Middleware;
using IronDev.Core.Builder;
using IronDev.Core.Chat;
using IronDev.Core;
using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using IronDev.Core.Agents.Concrete;
using IronDev.Core.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.Promotion;
using IronDev.Core.Runs;
using IronDev.Core.RunReports;
using IronDev.Core.Workspaces;
using IronDev.Core.Governance;
using IronDev.Core.Operations;
using IronDev.Core.Workflow;
using IronDev.Infrastructure.AgentRunAudit;
using IronDev.Data;
using IronDev.Infrastructure.Builder;
using IronDev.Infrastructure.DependencyInjection;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.Runs;
using IronDev.Infrastructure.Services.RunReports;
using IronDev.Infrastructure.Services.Promotion;
using IronDev.Infrastructure.Services.Workspaces;
using IronDev.Infrastructure.Security;
using IronDev.Infrastructure.Tracing;
using IronDev.Infrastructure.Agents;
using IronDev.Infrastructure.Governance;
using IronDev.Infrastructure.Operations;
using IronDev.Infrastructure.Workflow;
using IronDev.Services;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;

const string CorsPolicyName = "IronDevCors";
const string AuthLoginRateLimitPolicyName = "AuthLoginPolicy";
const string SensitiveApiRateLimitPolicyName = "SensitiveApiPolicy";

var logDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "IronDev",
    "api-logs");

Directory.CreateDirectory(logDirectory);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.File(
        Path.Combine(logDirectory, "irondev-api-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

var allowedCorsOrigins = ResolveAllowedCorsOrigins(builder.Configuration, builder.Environment);
StartupEnvironmentSafety.Current = CreateEnvironmentSafetyContext(builder);
var environmentInfo = CreateEnvironmentInfo(builder);
ValidateEnvironmentSafety(environmentInfo);
Log.Information(
    "IronDev.Api starting in {Environment} against database {Database}. WorkspaceRoot={WorkspaceRoot}; LogsRoot={LogsRoot}; DangerRealRepoWritesEnabled={DangerRealRepoWritesEnabled}",
    environmentInfo.Environment,
    environmentInfo.Database,
    environmentInfo.WorkspaceRoot,
    environmentInfo.LogsRoot,
    environmentInfo.DangerRealRepoWritesEnabled);

// ???????????????? Services ????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        var origins = allowedCorsOrigins.Length == 0
            ? ["https://irondev-cors-disabled.invalid"]
            : allowedCorsOrigins;

        policy
            .WithOrigins(origins)
            .WithHeaders("Authorization", "Content-Type")
            .WithMethods("GET", "POST", "PUT", "DELETE");
    });
});

var authLoginPermitLimit = ResolveRateLimitPermitLimit(
    builder.Configuration,
    "RateLimiting:AuthLogin:PermitLimit",
    5);
var sensitiveApiPermitLimit = ResolveRateLimitPermitLimit(
    builder.Configuration,
    "RateLimiting:SensitiveApi:PermitLimit",
    60);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthLoginRateLimitPolicyName, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            ResolveLoginRateLimitPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authLoginPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            }));
    options.AddPolicy(SensitiveApiRateLimitPolicyName, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            ResolveSensitiveApiRateLimitPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = sensitiveApiPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            }));
});

// Infrastructure
builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IChatHistoryService, ChatHistoryService>();
builder.Services.AddScoped<IChatFeedbackService, ChatFeedbackService>();
builder.Services.AddScoped<IPromptContextBuilder, PromptContextBuilder>();
builder.Services.AddScoped<IContextAgentRouteJudge, ContextAgentRouteJudgeService>();
builder.Services.AddScoped<IContextAgentService, ContextAgentService>();
builder.Services.AddScoped<IChatModeClassifier, LlmChatModeClassifier>();
builder.Services.AddScoped<IChatClarificationClassifier, LlmChatClarificationClassifier>();
builder.Services.AddScoped<IChatTurnPersistenceService, ChatTurnPersistenceService>();
builder.Services.AddScoped<IChatPromptTemplateProvider, FileSystemChatPromptTemplateProvider>();
builder.Services.AddScoped<ProjectChatContextPipeline>();
builder.Services.AddScoped<ProjectChatContextStateCompiler>();
builder.Services.AddScoped<ProjectChatResponseComposer>();
builder.Services.AddSingleton<ProjectChatResponseMetadataBuilder>();
builder.Services.AddScoped<IProjectChatResponseService, ProjectChatResponseService>();
builder.Services.AddScoped<IProjectStateReviewService, ProjectStateReviewService>();
builder.Services.AddScoped<IProjectMemoryService, ProjectMemoryService>();
builder.Services.AddScoped<IProjectMemoryMapService, ProjectMemoryMapService>();
builder.Services.AddScoped<IArtifactSourceReferenceService, ArtifactSourceReferenceService>();
builder.Services.AddScoped<IProjectProfileDetectionService, ProjectProfileDetectionService>();
builder.Services.AddScoped<IProjectProfileService, ProjectProfileService>();
builder.Services.AddScoped<IProjectDocumentService, ProjectDocumentService>();
builder.Services.AddScoped<IProjectContextExportService, ProjectContextExportService>();
builder.Services.AddScoped<ITicketService, TicketService>();
builder.Services.AddScoped<SqlCodeIndexService>();
builder.Services.AddScoped<ICodeIndexService, TracingCodeIndexServiceDecorator>();
builder.Services.AddScoped<global::IronDev.Infrastructure.Services.IDeepCodeLookupService, global::IronDev.Infrastructure.Services.DeepCodeLookupService>();
builder.Services.AddSingleton<ILlmTraceService, LlmTraceService>();
builder.Services.AddSingleton<IMarkdownRenderService, MarkdownRenderService>();
builder.Services.AddCodeIntelligenceServices();
builder.Services.AddGovernedTools();
builder.Services.AddScoped<IBuilderContextService, BuilderContextService>();
builder.Services.AddScoped<ICodeChangeProposalService, CodeChangeProposalService>();
builder.Services.AddScoped<ICodePatchService, CodePatchService>();
builder.Services.AddScoped<TicketBuildOrchestrator>();
builder.Services.AddScoped<ITicketBuildOrchestrator, TracingTicketBuildOrchestratorDecorator>();
builder.Services.AddScoped<IDraftTicketService, DraftTicketService>();
builder.Services.AddScoped<IBuilderProposalService, BuilderProposalService>();
builder.Services.AddScoped<ICodebaseTicketGeneratorService, CodebaseTicketGeneratorService>();
builder.Services.AddScoped<IBuildErrorClassifierService, BuildErrorClassifierService>();
builder.Services.AddScoped<IBuilderReadinessService, BuilderReadinessService>();
builder.Services.AddScoped<ITicketEvidenceSummaryService, TicketEvidenceSummaryService>();
builder.Services.AddScoped<ITicketRunReviewService, TicketRunReviewService>();
builder.Services.AddScoped<ITicketBuildRunService, TicketBuildRunService>();
builder.Services.AddScoped<ITicketSkeletonRunService, TicketSkeletonRunService>();
builder.Services.AddScoped<ISkeletonTestAuthoringService, SkeletonTestAuthoringService>();
builder.Services.AddScoped<ISkeletonCriticReviewService, SkeletonCriticReviewService>();
builder.Services.AddScoped<ISkeletonCriticGroundTruthVerifier, SkeletonCriticGroundTruthVerifier>();
builder.Services.AddScoped<ISkeletonFindingDispositionService, SkeletonFindingDispositionService>();
builder.Services.AddScoped<ISkeletonCriticCanaryRunner, SkeletonCriticCanaryRunner>();
builder.Services.AddScoped<ISkeletonCanaryMeasurementService, SkeletonCanaryMeasurementService>();
builder.Services.AddSingleton<IApprovalSatisfactionEvaluator, ApprovalSatisfactionEvaluator>();
builder.Services.AddSingleton<IWorkflowApprovalHaltEvaluator, WorkflowApprovalHaltEvaluator>();
builder.Services.AddScoped<IDiscussionDocumentService, DiscussionDocumentService>();
builder.Services.AddSingleton<DiscussionCodeScenarioCatalog>();
builder.Services.AddSingleton<IBuildScenarioCatalog>(sp => sp.GetRequiredService<DiscussionCodeScenarioCatalog>());
builder.Services.AddSingleton<ICodeRunProfileCatalog, CodeRunProfileCatalog>();
builder.Services.AddScoped<ICodeProposalValidator, CodeProposalValidator>();
builder.Services.AddScoped<ITicketFromDocumentService, TicketFromDocumentService>();
builder.Services.AddScoped<ITicketReviewService, TicketReviewService>();
builder.Services.AddScoped<ICodeProposalGenerator>(sp =>
{
    var mode = builder.Configuration["CodeProposal:Mode"];
    return string.Equals(mode, "ModelAssisted", StringComparison.OrdinalIgnoreCase)
        ? ActivatorUtilities.CreateInstance<ModelAssistedCodeProposalGenerator>(sp)
        : ActivatorUtilities.CreateInstance<DeterministicCodeProposalGenerator>(sp);
});
builder.Services.AddScoped<IDisposableCodeRunService, DisposableCodeRunService>();
builder.Services.AddScoped<IRunReviewPackageService, RunReviewPackageService>();
builder.Services.AddScoped<IDotNetBuildService, DotNetRunnerService>();
builder.Services.AddScoped<IDotNetTestService, DotNetRunnerService>();
builder.Services.AddSingleton<IRunReportService>(_ => CreateRunReportService(builder.Configuration));
builder.Services.AddSingleton<IRunEvidenceService>(sp => (IRunEvidenceService)sp.GetRequiredService<IRunReportService>());
builder.Services.AddSingleton<IRunStore, SqlRunStore>();
builder.Services.AddSingleton<IRunEventStore, SqlRunEventStore>();
builder.Services.AddScoped<IDisposableWorkspaceExecutionService, DisposableWorkspaceExecutionService>();
builder.Services.AddSingleton<ILanguageRuntimeRegistry, LanguageRuntimeRegistry>();
builder.Services.AddScoped<IPatchProposalService, PatchProposalService>();
builder.Services.AddScoped<IPromotionPackageService, PromotionPackageService>();
builder.Services.AddScoped<IControlledWriteApprovalService, ControlledWriteApprovalService>();
builder.Services.AddScoped<IControlledWorktreeApplyService, ControlledWorktreeApplyService>();
builder.Services.AddScoped<IAgentRunAuditEnvelopeStore, SqlAgentRunAuditEnvelopeStore>();
builder.Services.AddScoped<IAgentRunAuditEnvelopeReadRepository, SqlAgentRunAuditEnvelopeReadRepository>();
builder.Services.AddScoped<IAgentRunAuditQueryService, AgentRunAuditQueryService>();
builder.Services.AddScoped<IManualMemoryImprovementAgentService, ManualMemoryImprovementAgentService>();
builder.Services.AddScoped<ManualAgentExecutionStoreValidator>();
builder.Services.AddScoped<IStoredManualIndependentCriticAgentService, StoredManualIndependentCriticAgentService>();
builder.Services.AddScoped<IStoredManualMemoryImprovementAgentService, StoredManualMemoryImprovementAgentService>();
builder.Services.AddSingleton<AgentToolRequestValidator>();
builder.Services.AddScoped<IToolRequestStore, SqlToolRequestStore>();
builder.Services.AddScoped<IToolRequestApiStore, SqlToolRequestApiStore>();
builder.Services.AddSingleton<IAgentToolExecutionGate, AgentToolExecutionGate>();
builder.Services.AddScoped<IToolGateDecisionStore, SqlToolGateDecisionStore>();
builder.Services.AddScoped<IToolGateApiStore, SqlToolGateApiStore>();
builder.Services.AddScoped<IDogfoodReceiptStore, SqlDogfoodReceiptStore>();
builder.Services.AddScoped<IDogfoodLoopApiStore, SqlDogfoodLoopApiStore>();
builder.Services.AddScoped<IWorkflowRunStore, SqlWorkflowRunStore>();
builder.Services.AddScoped<IWorkflowStepStore, SqlWorkflowStepStore>();
builder.Services.AddScoped<IWorkflowCheckpointStore, SqlWorkflowCheckpointStore>();
builder.Services.AddScoped<IWorkflowTransitionRecordStore, SqlWorkflowTransitionRecordStore>();
builder.Services.AddScoped<IWorkflowTransitionRecordQueryService, WorkflowTransitionRecordQueryService>();
builder.Services.AddScoped<IReleaseReadinessDecisionRecordStore, SqlReleaseReadinessDecisionRecordStore>();
builder.Services.AddScoped<IReleaseReadinessDecisionRecordQueryService, ReleaseReadinessDecisionRecordQueryService>();
builder.Services.AddSingleton<ReleaseReadinessGateEvaluator>();
builder.Services.AddScoped<IGovernedReleaseGateService, GovernedReleaseGateService>();
builder.Services.AddSingleton<IWorkflowContinuationGateEvaluator, WorkflowContinuationGateEvaluator>();
builder.Services.AddScoped<IControlledWorkflowStateTransitionStore, SqlControlledWorkflowStateTransitionStore>();
builder.Services.AddScoped<IGovernedWorkflowContinuationService, GovernedWorkflowContinuationService>();
builder.Services.AddScoped<IApplyDryRunStore, SqlApplyDryRunStore>();
builder.Services.AddScoped<IApplyPreviewService, ApplyPreviewService>();
builder.Services.AddScoped<IAcceptedApprovalStore, SqlAcceptedApprovalStore>();
builder.Services.AddScoped<IAcceptedApprovalQueryService, AcceptedApprovalQueryService>();
builder.Services.AddScoped<IAcceptedApprovalCreateService, AcceptedApprovalCreateService>();
builder.Services.AddSingleton<IPolicyRequirementSatisfactionEvaluator, PolicyRequirementSatisfactionEvaluator>();
builder.Services.AddScoped<IPolicySatisfactionStore, SqlPolicySatisfactionStore>();
builder.Services.AddScoped<IPolicySatisfactionQueryService, PolicySatisfactionQueryService>();
builder.Services.AddScoped<IPolicySatisfactionCreateService, PolicySatisfactionCreateService>();
builder.Services.AddScoped<IPatchArtifactStore, SqlPatchArtifactStore>();
builder.Services.AddScoped<IPatchArtifactQueryService, PatchArtifactQueryService>();
builder.Services.AddScoped<ISourceApplyDryRunReceiptStore, SqlSourceApplyDryRunReceiptStore>();
builder.Services.AddScoped<ISourceApplyDryRunReceiptQueryService, SourceApplyDryRunReceiptQueryService>();
builder.Services.AddScoped<IRollbackSupportReceiptStore, SqlRollbackSupportReceiptStore>();
builder.Services.AddScoped<IRollbackSupportReceiptQueryService, RollbackSupportReceiptQueryService>();
builder.Services.AddScoped<IGovernanceTraceExplorerService, GovernanceTraceExplorerService>();
builder.Services.AddScoped<IFailedWorkflowDiagnosisReportService, FailedWorkflowDiagnosisReportService>();
builder.Services.AddScoped<IApprovalGateDogfoodCorrelationReportService, ApprovalGateDogfoodCorrelationReportService>();
builder.Services.AddScoped<IAgentRunHealthSummaryService, AgentRunHealthSummaryService>();
builder.Services.AddScoped<IBackendOperationalHealthService, BackendOperationalHealthService>();
builder.Services.AddSingleton<IGovernedOperationStatusReadRepository, GovernedOperationStatusReadRepository>();
builder.Services.AddSingleton<IEvidenceMetadataReadRepository, EvidenceMetadataReadRepository>();
builder.Services.AddSingleton<IReceiptMetadataReadRepository, ReceiptMetadataReadRepository>();
builder.Services.AddSingleton<IOperationTimelineReadRepository, OperationTimelineReadRepository>();
builder.Services.AddSingleton<IPatchPackageMetadataReadRepository, PatchPackageMetadataReadRepository>();
builder.Services.AddSingleton<IValidationResultMetadataReadRepository, ValidationResultMetadataReadRepository>();
builder.Services.AddScoped<IFrontendReadinessBackendTruthSource, OperationStatusFrontendReadinessBackendTruthSource>();
builder.Services.AddScoped<IFrontendReadinessBackendTruthSource, EvidenceMetadataFrontendReadinessBackendTruthSource>();
builder.Services.AddScoped<IFrontendReadinessBackendTruthSource, ReceiptMetadataFrontendReadinessBackendTruthSource>();
builder.Services.AddScoped<IFrontendReadinessBackendTruthSource, OperationTimelineFrontendReadinessBackendTruthSource>();
builder.Services.AddScoped<IFrontendReadinessBackendTruthSource, PatchPackageMetadataFrontendReadinessBackendTruthSource>();
builder.Services.AddScoped<IFrontendReadinessBackendTruthSource, ValidationResultMetadataFrontendReadinessBackendTruthSource>();
builder.Services.AddScoped<IFrontendReadinessBackendTruthSource, RunReportFrontendReadinessBackendTruthSource>();
builder.Services.AddScoped<IFrontendReadinessReadApi, BackendFrontendReadinessReadApi>();
builder.Services.AddSingleton<IFrontendControlledActionRequestService, FrontendControlledActionRequestService>();
builder.Services.AddSingleton<ISecurityAuditLog, SecurityAuditLog>();

var aiOptions = builder.Configuration.GetSection("Ai").Get<LlmOptions>() ?? new LlmOptions();
if (string.IsNullOrWhiteSpace(aiOptions.ApiKey))
    aiOptions.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

builder.Services.AddScoped<ILLMService>(_ =>
{
    var provider = aiOptions.Provider?.ToLowerInvariant() ?? "openai";
    return provider switch
    {
        "openai" => new OpenAiLlmService(aiOptions),
        "localopenai" => new LocalOpenAiCompatibleLlmService(aiOptions),
        "ollama" => new OllamaLlmService(aiOptions),
        "custom" => new LocalOpenAiCompatibleLlmService(aiOptions),
        _ => new FakeLlmService()
    };
});

// Tenant context ???????? request-scoped, reads tenant_id from JWT claim.
builder.Services.AddScoped<ICurrentTenantContext, JwtTenantContext>();

// JWT token factory
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

// ???????????????? Authentication & Authorization ????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????

var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtStartupValidation = JwtStartupConfigurationValidator.Validate(builder.Configuration);
Log.Information(
    JwtStartupConfigurationValidator.StartupValidationPassedLogMessage,
    jwtStartupValidation.Source);
var jwtKey = JwtSigningKeyResolver.Resolve(builder.Configuration);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

// ???????????????? Build ????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Test"))
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "IronDev API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseMiddleware<RequestTracingMiddleware>();
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors(CorsPolicyName);
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// Health check endpoint (anonymous)
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .WithName("Health")
   .AllowAnonymous();

var environmentEndpoint = app.MapGet("/api/environment", () => Results.Ok(environmentInfo))
   .WithName("Environment")
   .RequireAuthorization();
environmentEndpoint.RequireRateLimiting(SensitiveApiRateLimitPolicyName);

app.MapControllers();

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

static EnvironmentInfoDto CreateEnvironmentInfo(WebApplicationBuilder builder)
{
    var environmentName = builder.Environment.EnvironmentName;
    var connectionString = builder.Configuration.GetConnectionString("IronDeveloperDb") ?? string.Empty;
    var database = ResolveDatabaseName(connectionString);
    var localTest = builder.Configuration.GetSection("LocalTest");

    return new EnvironmentInfoDto
    {
        Environment = environmentName,
        Database = database,
        WeaviatePrefix = localTest["WeaviatePrefix"] ?? string.Empty,
        IsTestEnvironment =
            builder.Environment.IsEnvironment("LocalTest") ||
            builder.Environment.IsEnvironment("Test"),
        WorkspaceRoot = localTest["WorkspaceRoot"] ?? string.Empty,
        LogsRoot = localTest["LogsRoot"] ?? string.Empty,
        DangerRealRepoWritesEnabled = bool.TryParse(localTest["DangerRealRepoWritesEnabled"], out var enabled) && enabled
    };
}

static string ResolveDatabaseName(string connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
        return string.Empty;

    try
    {
        return new SqlConnectionStringBuilder(connectionString).InitialCatalog;
    }
    catch
    {
        return string.Empty;
    }
}

static StartupEnvironmentSafetyContext CreateEnvironmentSafetyContext(WebApplicationBuilder builder)
{
    var connectionString = builder.Configuration.GetConnectionString("IronDeveloperDb") ?? string.Empty;
    var parsed = TryParseConnectionString(connectionString);

    return new StartupEnvironmentSafetyContext(
        connectionString,
        parsed.Database,
        parsed.DataSource,
        parsed.ContainsPassword,
        builder.Configuration["LocalTest:WorkspaceRoot"] ?? string.Empty,
        builder.Configuration["LocalTest:LogsRoot"] ?? string.Empty,
        builder.Configuration["DisposableBuild:WorkspaceRoot"] ?? string.Empty,
        builder.Configuration["DisposableBuild:EvidenceRoot"] ?? string.Empty);
}

static void ValidateEnvironmentSafety(EnvironmentInfoDto environmentInfo)
{
    if (string.Equals(environmentInfo.Environment, "LocalTest", StringComparison.OrdinalIgnoreCase))
    {
        ValidateLocalTestEnvironmentSafety(environmentInfo);
        return;
    }

    if (IsProductionLikeEnvironment(environmentInfo.Environment))
        ValidateProductionLikeEnvironmentSafety(environmentInfo, StartupEnvironmentSafety.Current);
}

static void ValidateLocalTestEnvironmentSafety(EnvironmentInfoDto environmentInfo)
{
    if (!IsSafeLocalTestDatabaseName(environmentInfo.Database))
    {
        throw new InvalidOperationException("LocalTest must use an isolated test database with a clear test marker.");
    }

    if (!IsSafeLocalTestPath(environmentInfo.WorkspaceRoot, "workspace"))
    {
        throw new InvalidOperationException("LocalTest must use an isolated workspace root with a clear test marker.");
    }

    if (!IsSafeLocalTestPath(environmentInfo.LogsRoot, "logs"))
    {
        throw new InvalidOperationException("LocalTest must use an isolated logs root with a clear test marker.");
    }

    if (environmentInfo.DangerRealRepoWritesEnabled)
        throw new InvalidOperationException("LocalTest cannot enable dangerous real repo writes.");
}

static void ValidateProductionLikeEnvironmentSafety(
    EnvironmentInfoDto environmentInfo,
    StartupEnvironmentSafetyContext? safetyContext)
{
    safetyContext ??= StartupEnvironmentSafetyContext.Empty;

    if (string.IsNullOrWhiteSpace(safetyContext.ConnectionString))
        throw new InvalidOperationException("Production-like environment must configure a database connection string.");

    if (ContainsPlaceholderDatabaseConfiguration(safetyContext.ConnectionString))
        throw new InvalidOperationException("Production-like environment must not use placeholder database server configuration.");

    if (string.IsNullOrWhiteSpace(safetyContext.Database))
        throw new InvalidOperationException("Production-like environment must configure a database name.");

    if (HasUnsafeProductionLikeDatabaseMarker(safetyContext.Database))
        throw new InvalidOperationException("Production-like environment must not use test-like database names.");

    if (IsLocalDatabaseServer(safetyContext.DataSource))
        throw new InvalidOperationException("Production-like environment must not use a local database server.");

    if (safetyContext.ContainsPassword)
        throw new InvalidOperationException("Production-like environment must not use password-bearing database configuration.");

    if (environmentInfo.DangerRealRepoWritesEnabled)
        throw new InvalidOperationException("Production-like environment must not enable dangerous real repo writes.");

    if (IsUnsafeProductionLikeRoot(safetyContext.LocalTestWorkspaceRoot))
        throw new InvalidOperationException("Production-like environment must not use local or test workspace roots.");

    if (IsUnsafeProductionLikeRoot(safetyContext.LocalTestLogsRoot))
        throw new InvalidOperationException("Production-like environment must not use local or test logs roots.");

    if (IsUnsafeProductionLikeRoot(safetyContext.DisposableWorkspaceRoot))
        throw new InvalidOperationException("Production-like environment must not use local or test disposable workspace roots.");

    if (IsUnsafeProductionLikeRoot(safetyContext.DisposableEvidenceRoot))
        throw new InvalidOperationException("Production-like environment must not use local or test disposable evidence roots.");
}

static bool IsProductionLikeEnvironment(string environmentName) =>
    !string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(environmentName, "LocalTest", StringComparison.OrdinalIgnoreCase);

static (string Database, string DataSource, bool ContainsPassword) TryParseConnectionString(string connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
        return (string.Empty, string.Empty, false);

    try
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        return (
            builder.InitialCatalog ?? string.Empty,
            builder.DataSource ?? string.Empty,
            !string.IsNullOrWhiteSpace(builder.Password));
    }
    catch
    {
        return (string.Empty, string.Empty, ContainsPasswordKey(connectionString));
    }
}

static bool ContainsPasswordKey(string connectionString) =>
    connectionString
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Any(part =>
            part.StartsWith("Password=", StringComparison.OrdinalIgnoreCase) ||
            part.StartsWith("Pwd=", StringComparison.OrdinalIgnoreCase));

static bool ContainsPlaceholderDatabaseConfiguration(string connectionString) =>
    connectionString.Contains("YOUR_SERVER", StringComparison.OrdinalIgnoreCase);

static bool HasUnsafeProductionLikeDatabaseMarker(string database)
{
    var segments = SplitLocalTestSafetySegments(database);
    return segments.Any(segment =>
        segment.Equals("Test", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("LocalTest", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Dev", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Development", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Local", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Scratch", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Temp", StringComparison.OrdinalIgnoreCase));
}

static bool IsLocalDatabaseServer(string dataSource)
{
    if (string.IsNullOrWhiteSpace(dataSource))
        return true;

    var normalized = dataSource.Trim();
    var host = normalized;
    if (host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        host = host[4..];

    var slashIndex = host.IndexOf('\\', StringComparison.Ordinal);
    if (slashIndex >= 0)
        host = host[..slashIndex];

    var commaIndex = host.IndexOf(',', StringComparison.Ordinal);
    if (commaIndex >= 0)
        host = host[..commaIndex];

    host = host.Trim();

    return host.Equals(".", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("(local)", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("(localdb)", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
        host.StartsWith("DESKTOP-", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("(localdb)", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("SQLEXPRESS", StringComparison.OrdinalIgnoreCase);
}

static bool IsUnsafeProductionLikeRoot(string root)
{
    if (string.IsNullOrWhiteSpace(root))
        return false;

    var normalized = root.Replace('\\', '/').Trim();
    var lower = normalized.ToLowerInvariant();
    var tempRoot = Path.GetTempPath().Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
    var userRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        .Replace('\\', '/')
        .TrimEnd('/')
        .ToLowerInvariant();

    if (!string.IsNullOrWhiteSpace(tempRoot) &&
        (lower.Equals(tempRoot, StringComparison.Ordinal) || lower.StartsWith(tempRoot + "/", StringComparison.Ordinal)))
    {
        return true;
    }

    if (!string.IsNullOrWhiteSpace(userRoot) &&
        (lower.Equals(userRoot, StringComparison.Ordinal) || lower.StartsWith(userRoot + "/", StringComparison.Ordinal)))
    {
        return true;
    }

    if (lower.Contains("/source/repos/", StringComparison.Ordinal))
    {
        return true;
    }

    var segments = SplitLocalTestSafetySegments(root);
    return segments.Any(segment =>
        segment.Equals("Test", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("LocalTest", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Dev", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Development", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Local", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Scratch", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Temp", StringComparison.OrdinalIgnoreCase));
}

static bool IsSafeLocalTestDatabaseName(string database)
{
    var segments = SplitLocalTestSafetySegments(database);
    return HasExplicitTestSegment(segments) && !HasProductionLikeSegment(segments);
}

static bool IsSafeLocalTestPath(string path, string expectedLabel)
{
    var segments = SplitLocalTestSafetySegments(path);
    if (segments.Length == 0 || HasProductionLikeSegment(segments))
        return false;

    if (HasExplicitTestSegment(segments))
        return true;

    return expectedLabel.Equals("workspace", StringComparison.OrdinalIgnoreCase)
        ? segments.Any(segment =>
            segment.Equals("IronDevTestWorkspaces", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("TestWorkspace", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("TestWorkspaces", StringComparison.OrdinalIgnoreCase))
        : segments.Any(segment =>
            segment.Equals("IronDevTestLogs", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("TestLog", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("TestLogs", StringComparison.OrdinalIgnoreCase));
}

static bool HasExplicitTestSegment(IReadOnlyCollection<string> segments) =>
    segments.Any(segment => segment.Equals("Test", StringComparison.OrdinalIgnoreCase));

static bool HasProductionLikeSegment(IReadOnlyCollection<string> segments) =>
    segments.Any(segment =>
        segment.Contains("Prod", StringComparison.OrdinalIgnoreCase) ||
        segment.Contains("Production", StringComparison.OrdinalIgnoreCase) ||
        segment.Contains("Live", StringComparison.OrdinalIgnoreCase) ||
        segment.Contains("Accept", StringComparison.OrdinalIgnoreCase));

static string[] SplitLocalTestSafetySegments(string value) =>
    string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(
            ['\\', '/', '_', '-', '.', ':', ' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

static string[] ResolveAllowedCorsOrigins(IConfiguration configuration, IWebHostEnvironment environment)
{
    var configuredOrigins = configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>() ?? [];

    if (configuredOrigins.Length == 0)
        return [];

    var normalizedOrigins = new List<string>();
    var seenOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var allowLocalhost =
        environment.IsDevelopment() ||
        environment.IsEnvironment("Test") ||
        environment.IsEnvironment("LocalTest");

    foreach (var configuredOrigin in configuredOrigins)
    {
        var normalizedOrigin = NormalizeCorsOrigin(configuredOrigin);
        if (!seenOrigins.Add(normalizedOrigin))
            throw new InvalidOperationException($"Duplicate CORS origin configured: {normalizedOrigin}");

        if (!allowLocalhost && IsLocalhostOrigin(normalizedOrigin))
            throw new InvalidOperationException("Production CORS configuration cannot include localhost origins.");

        normalizedOrigins.Add(normalizedOrigin);
    }

    return normalizedOrigins.ToArray();
}

static string NormalizeCorsOrigin(string? configuredOrigin)
{
    if (string.IsNullOrWhiteSpace(configuredOrigin))
        throw new InvalidOperationException("Cors:AllowedOrigins cannot contain blank origins.");

    var origin = configuredOrigin.Trim();
    if (origin == "*" || origin.Contains('*'))
        throw new InvalidOperationException("Cors:AllowedOrigins cannot contain wildcard origins.");

    if (origin.EndsWith("/", StringComparison.Ordinal))
        throw new InvalidOperationException("Cors:AllowedOrigins must not include trailing slashes.");

    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        throw new InvalidOperationException($"Invalid CORS origin configured: {origin}");

    if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"CORS origin must use http or https: {origin}");
    }

    if (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/")
        throw new InvalidOperationException($"CORS origin must not include a path: {origin}");

    if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
        throw new InvalidOperationException($"CORS origin must not include query, fragment, or user info: {origin}");

    return uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped);
}

static bool IsLocalhostOrigin(string origin)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        return false;

    return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(uri.Host, "[::1]", StringComparison.OrdinalIgnoreCase);
}

static int ResolveRateLimitPermitLimit(IConfiguration configuration, string key, int fallback)
{
    var configured = configuration[key];
    if (int.TryParse(configured, out var value) && value > 0)
        return value;

    return fallback;
}

static string ResolveLoginRateLimitPartitionKey(HttpContext context) =>
    $"ip:{NormalizeRateLimitKey(context.Connection.RemoteIpAddress?.ToString())}";

static string ResolveSensitiveApiRateLimitPartitionKey(HttpContext context)
{
    var userId =
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
        context.User.FindFirst("sub")?.Value;
    var tenantId = context.User.FindFirst("tenant_id")?.Value;

    if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(tenantId))
        return $"user-tenant:{NormalizeRateLimitKey(userId)}:{NormalizeRateLimitKey(tenantId)}";

    return ResolveLoginRateLimitPartitionKey(context);
}

static string NormalizeRateLimitKey(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return "unknown";

    var safe = new string(value
        .Trim()
        .Where(character => char.IsLetterOrDigit(character) || character is '.' or ':' or '-' or '_')
        .Take(128)
        .ToArray());

    return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
}

static IRunReportService CreateRunReportService(IConfiguration configuration)
{
    var configuredRoot =
        configuration["DisposableBuild:EvidenceRoot"] ??
        configuration["LocalTest:LogsRoot"];

    return string.IsNullOrWhiteSpace(configuredRoot)
        ? new FileRunReportService()
        : new FileRunReportService(Path.Combine(configuredRoot, "runs"));
}

// Expose Program for WebApplicationFactory in integration tests.
public partial class Program { }

internal sealed record StartupEnvironmentSafetyContext(
    string ConnectionString,
    string Database,
    string DataSource,
    bool ContainsPassword,
    string LocalTestWorkspaceRoot,
    string LocalTestLogsRoot,
    string DisposableWorkspaceRoot,
    string DisposableEvidenceRoot)
{
    public static StartupEnvironmentSafetyContext Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);
}

internal static class StartupEnvironmentSafety
{
    public static StartupEnvironmentSafetyContext? Current { get; set; }
}
