using System.Text;
using IronDev.Api.Auth;
using IronDev.Core.Auth;
using IronDev.Data;
using IronDev.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── Services ─────────────────────────────────────────────────────────────────

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();

// Infrastructure
builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IChatHistoryService, ChatHistoryService>();
builder.Services.AddScoped<IProjectMemoryService, ProjectMemoryService>();
builder.Services.AddScoped<ITicketService, TicketService>();
builder.Services.AddScoped<ICodeIndexService, SqlCodeIndexService>();

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

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint (anonymous)
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .WithName("Health")
   .AllowAnonymous();

app.MapControllers();

app.Run();

// Expose Program for WebApplicationFactory in integration tests.
public partial class Program { }
