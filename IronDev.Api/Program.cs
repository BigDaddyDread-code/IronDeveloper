using System.Text;
using IronDev.Api.Auth;
using IronDev.Api.Middleware;
using IronDev.Core;
using IronDev.Core.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.Runs;
using IronDev.Core.RunReports;
using IronDev.Core.Workspaces;
using IronDev.Data;
using IronDev.Infrastructure.Builder;
using IronDev.Infrastructure.DependencyInjection;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.Runs;
using IronDev.Infrastructure.Services.RunReports;
using IronDev.Infrastructure.Services.Workspaces;
using IronDev.Infrastructure.Tracing;
using IronDev.Services;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;

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

var environmentInfo = CreateEnvironmentInfo(builder);
ValidateEnvironmentSafety(environmentInfo);
Log.Information(
    "IronDev.Api starting in {Environment} against database {Database}. WorkspaceRoot={WorkspaceRoot}; LogsRoot={LogsRoot}; DangerRealRepoWritesEnabled={DangerRealRepoWritesEnabled}",
    environmentInfo.Environment,
    environmentInfo.Database,
    environmentInfo.WorkspaceRoot,
    environmentInfo.LogsRoot,
    environmentInfo.DangerRealRepoWritesEnabled);

// ── Services ─────────────────────────────────────────────────────────────────

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();

// Infrastructure
builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IChatHistoryService, ChatHistoryService>();
builder.Services.AddScoped<IChatFeedbackService, ChatFeedbackService>();
builder.Services.AddScoped<IProjectMemoryService, ProjectMemoryService>();
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
builder.Services.AddScoped<IDotNetBuildService, DotNetRunnerService>();
builder.Services.AddScoped<IDotNetTestService, DotNetRunnerService>();
builder.Services.AddSingleton<IRunReportService, FileRunReportService>();
builder.Services.AddSingleton<IRunEvidenceService>(sp => (IRunEvidenceService)sp.GetRequiredService<IRunReportService>());
builder.Services.AddSingleton<IRunStore, SqlRunStore>();
builder.Services.AddSingleton<IRunEventStore, SqlRunEventStore>();
builder.Services.AddScoped<IDisposableWorkspaceExecutionService, DisposableWorkspaceExecutionService>();

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

// Tenant context — request-scoped, reads tenant_id from JWT claim.
builder.Services.AddScoped<ICurrentTenantContext, JwtTenantContext>();

// JWT token factory
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

// ── Authentication & Authorization ────────────────────────────────────────────

var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured in appsettings.");

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

// ── Build ─────────────────────────────────────────────────────────────────────

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
app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint (anonymous)
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .WithName("Health")
   .AllowAnonymous();

app.MapGet("/api/environment", () => Results.Ok(environmentInfo))
   .WithName("Environment")
   .AllowAnonymous();

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

static void ValidateEnvironmentSafety(EnvironmentInfoDto environmentInfo)
{
    if (!string.Equals(environmentInfo.Environment, "LocalTest", StringComparison.OrdinalIgnoreCase))
        return;

    if (string.IsNullOrWhiteSpace(environmentInfo.Database) ||
        !environmentInfo.Database.Contains("Test", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("LocalTest must use an isolated test database whose name contains 'Test'.");
    }

    if (string.IsNullOrWhiteSpace(environmentInfo.WorkspaceRoot) ||
        !environmentInfo.WorkspaceRoot.Contains("Test", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("LocalTest must use an isolated workspace root whose path contains 'Test'.");
    }

    if (string.IsNullOrWhiteSpace(environmentInfo.LogsRoot) ||
        !environmentInfo.LogsRoot.Contains("Test", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("LocalTest must use an isolated logs root whose path contains 'Test'.");
    }

    if (environmentInfo.DangerRealRepoWritesEnabled)
        throw new InvalidOperationException("LocalTest cannot enable dangerous real repo writes.");
}

// Expose Program for WebApplicationFactory in integration tests.
public partial class Program { }
