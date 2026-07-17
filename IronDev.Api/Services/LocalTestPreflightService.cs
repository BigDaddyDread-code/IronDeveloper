using System.Reflection;
using System.Text.Json;
using Dapper;
using IronDev.Data;
using Microsoft.Data.SqlClient;

namespace IronDev.Api.Services;

public static class LocalTestPreflightStates
{
    public const string ApiOffline = "ApiOffline";
    public const string ApiConnected = "ApiConnected";
    public const string WrongEnvironment = "WrongEnvironment";
    public const string WrongDatabase = "WrongDatabase";
    public const string SeedUserMissing = "SeedUserMissing";
    public const string SeedCredentialInvalid = "SeedCredentialInvalid";
    public const string SeedMembershipMissing = "SeedMembershipMissing";
    public const string ApiIdentityMismatch = "ApiIdentityMismatch";
    public const string SessionCapabilityMismatch = "SessionCapabilityMismatch";
    public const string DatabaseUnavailable = "DatabaseUnavailable";
    public const string LocalTestReady = "LocalTestReady";
}

public sealed record LocalTestPreflightResponse(
    string State,
    string Environment,
    string? Database,
    string ApiBuildIdentity,
    string ApiBuildCommit,
    string? LauncherRepositoryCommit,
    string? SessionId,
    string? ApiBaseUrl,
    int ApiPid,
    int? SeedContractVersion,
    string SeededLoginCheckResult,
    string NextSafeAction,
    string? ResetCommand,
    string Detail)
{
    public string WorkbenchVersion { get; init; } = WorkbenchReleaseInfoFactory.DefaultVersion;
    public string WorkbenchMode { get; init; } = "V1";
    public string PreviewId { get; init; } = WorkbenchReleaseInfoFactory.DefaultPreviewId;
    public string SessionMode { get; init; } = string.Empty;
    public bool SandboxApplyRequested { get; init; }
    public bool SandboxApplyEnabled { get; init; }
    public string? SandboxApplyRoot { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public string SandboxApplyRestartCommand { get; init; } = LocalTestPreflightService.SandboxApplyRestartCommand;
}

public interface ILocalTestPreflightService
{
    Task<LocalTestPreflightResponse> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed class LocalTestPreflightService : ILocalTestPreflightService
{
    public const string ResetCommand =
        ".\\tools\\localtest\\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset";
    public const string SandboxApplyRestartCommand =
        ".\\tools\\localtest\\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset -EnableSandboxApply";

    private const string ContractFileName = "localtest-seed-contract.json";
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IConfiguration _configuration;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<LocalTestPreflightService> _logger;

    public LocalTestPreflightService(
        IHostEnvironment hostEnvironment,
        IConfiguration configuration,
        IDbConnectionFactory connectionFactory,
        ILogger<LocalTestPreflightService> logger)
    {
        _hostEnvironment = hostEnvironment;
        _configuration = configuration;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<LocalTestPreflightResponse> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_hostEnvironment.IsEnvironment("LocalTest"))
        {
            return new LocalTestPreflightResponse(
                LocalTestPreflightStates.WrongEnvironment,
                _hostEnvironment.EnvironmentName,
                Database: null,
                ApiBuildIdentity: "Not disclosed outside LocalTest",
                ApiBuildCommit: string.Empty,
                LauncherRepositoryCommit: null,
                SessionId: null,
                ApiBaseUrl: null,
                ApiPid: 0,
                SeedContractVersion: null,
                SeededLoginCheckResult: "NotChecked",
                NextSafeAction: "Stop. Start the supported LocalTest launcher against the intended LocalTest API.",
                ResetCommand: null,
                Detail: $"Connected API environment is '{_hostEnvironment.EnvironmentName}', not LocalTest.");
        }

        var identity = BuildIdentity();
        var contract = LoadContract(identity.PreviewId);
        var configuredDatabase = ResolveDatabaseName(_configuration.GetConnectionString("IronDeveloperDb"));

        if (!string.Equals(configuredDatabase, contract.Database.Name, StringComparison.Ordinal))
        {
            return CreateResponse(
                LocalTestPreflightStates.WrongDatabase,
                configuredDatabase,
                identity,
                contract.SchemaVersion,
                "NotChecked",
                $"Stop. Configure the LocalTest database as '{contract.Database.Name}' before resetting LocalTest data.",
                ResetCommand,
                $"Configured database '{configuredDatabase}' does not match the seed contract database '{contract.Database.Name}'.");
        }

        if (string.IsNullOrWhiteSpace(identity.SessionId) ||
            string.IsNullOrWhiteSpace(identity.LauncherRepositoryCommit) ||
            !string.Equals(identity.ApiBuildCommit, identity.LauncherRepositoryCommit, StringComparison.OrdinalIgnoreCase))
        {
            return CreateResponse(
                LocalTestPreflightStates.ApiIdentityMismatch,
                configuredDatabase,
                identity,
                contract.SchemaVersion,
                "NotChecked",
                "Stop. Restart LocalTest through the supported launcher so the API and browser share one session identity.",
                ResetCommand,
                "The API build commit, launcher repository commit, or LocalTest session identifier is missing or mismatched.");
        }

        if (!identity.HasConsistentCapabilities)
        {
            return CreateResponse(
                LocalTestPreflightStates.SessionCapabilityMismatch,
                configuredDatabase,
                identity,
                contract.SchemaVersion,
                "NotChecked",
                "Session capability mismatch. Restart through the supported project-work launcher.",
                SandboxApplyRestartCommand,
                "The API session mode, sandbox-apply request, enabled state, root, or declared capabilities do not agree.");
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            var user = await connection.QuerySingleOrDefaultAsync<SeedUserProbe>(new CommandDefinition(
                """
                SELECT Id, Email, PasswordHash, IsActive
                FROM dbo.Users
                WHERE Email = @Email;
                """,
                new { contract.Credentials.Email },
                cancellationToken: cancellationToken));

            if (user is null || !user.IsActive)
            {
                return CreateResponse(
                    LocalTestPreflightStates.SeedUserMissing,
                    configuredDatabase,
                    identity,
                    contract.SchemaVersion,
                    "SeedUserMissing",
                    "Reset the explicit LocalTest data, then restart the supported LocalTest session.",
                    ResetCommand,
                    "The contracted active LocalTest user does not exist.");
            }

            if (!HasValidPassword(contract.Credentials.Password, user.PasswordHash))
            {
                return CreateResponse(
                    LocalTestPreflightStates.SeedCredentialInvalid,
                    configuredDatabase,
                    identity,
                    contract.SchemaVersion,
                    "SeedCredentialInvalid",
                    "Reset the explicit LocalTest data, then restart the supported LocalTest session.",
                    ResetCommand,
                    "The contracted LocalTest password does not match the stored BCrypt hash.");
            }

            var membershipCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(1)
                FROM dbo.TenantUsers
                WHERE TenantId = @TenantId
                  AND UserId = @UserId
                  AND Role = @Role;
                """,
                new
                {
                    TenantId = contract.Tenant.Id,
                    UserId = user.Id,
                    Role = contract.Users.Single(candidate =>
                        string.Equals(candidate.Email, contract.Credentials.Email, StringComparison.OrdinalIgnoreCase)).TenantRole
                },
                cancellationToken: cancellationToken));

            if (membershipCount != 1)
            {
                return CreateResponse(
                    LocalTestPreflightStates.SeedMembershipMissing,
                    configuredDatabase,
                    identity,
                    contract.SchemaVersion,
                    "SeedMembershipMissing",
                    "Reset the explicit LocalTest data, then restart the supported LocalTest session.",
                    ResetCommand,
                    "The contracted LocalTest tenant membership is missing or does not have the expected role.");
            }

            return CreateResponse(
                LocalTestPreflightStates.LocalTestReady,
                configuredDatabase,
                identity,
                contract.SchemaVersion,
                "Passed",
                "Sign in with the documented LocalTest credentials.",
                resetCommand: null,
                detail: "The API, database, seed credential, tenant membership, and launcher identity checks passed.");
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException)
        {
            _logger.LogError(exception, "LocalTest preflight could not query the configured database {Database}", configuredDatabase);
            return CreateResponse(
                LocalTestPreflightStates.DatabaseUnavailable,
                configuredDatabase,
                identity,
                contract.SchemaVersion,
                "DatabaseUnavailable",
                "Stop. Inspect the session API log, then explicitly reset and restart LocalTest.",
                ResetCommand,
                "The configured LocalTest database could not be queried. Inspect the retained session API log for the SQL error.");
        }
    }

    private LocalTestPreflightResponse CreateResponse(
        string state,
        string? database,
        LocalTestIdentity identity,
        int? contractVersion,
        string seededLoginCheckResult,
        string nextSafeAction,
        string? resetCommand,
        string detail)
    {
        var scopedResetCommand = ScopeCommand(resetCommand, identity);

        return new LocalTestPreflightResponse(
            state,
            _hostEnvironment.EnvironmentName,
            database,
            identity.ApiBuildIdentity,
            identity.ApiBuildCommit,
            identity.LauncherRepositoryCommit,
            identity.SessionId,
            identity.ApiBaseUrl,
            Environment.ProcessId,
            contractVersion,
            seededLoginCheckResult,
            nextSafeAction,
            scopedResetCommand,
            detail)
        {
            WorkbenchVersion = identity.WorkbenchVersion,
            WorkbenchMode = identity.WorkbenchV2Enabled ? "V2" : "V1",
            PreviewId = identity.PreviewId,
            SessionMode = identity.SessionMode,
            SandboxApplyRequested = identity.SandboxApplyRequested,
            SandboxApplyEnabled = identity.SandboxApplyEnabled,
            SandboxApplyRoot = identity.SandboxApplyRoot,
            Capabilities = identity.Capabilities,
            SandboxApplyRestartCommand = ScopeCommand(SandboxApplyRestartCommand, identity)!
        };
    }

    private static bool HasValidPassword(string password, string? passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    private static LocalTestSeedContract LoadContract(string previewId)
    {
        var path = Path.Combine(AppContext.BaseDirectory, ContractFileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"LocalTest seed contract was not copied to '{path}'.");
        }

        var contract = JsonSerializer.Deserialize<LocalTestSeedContract>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (contract is null || contract.SchemaVersion <= 0 ||
            !string.Equals(contract.Environment, "LocalTest", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("LocalTest seed contract is missing or invalid.");
        }

        if (!string.Equals(previewId, WorkbenchReleaseInfoFactory.DefaultPreviewId, StringComparison.Ordinal))
        {
            contract.Database.Name = $"IronDeveloper_Test_{previewId.Replace('-', '_')}";
        }

        return contract;
    }

    private static string? ScopeCommand(string? command, LocalTestIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return command;
        }

        if (!string.Equals(identity.PreviewId, WorkbenchReleaseInfoFactory.DefaultPreviewId, StringComparison.Ordinal))
        {
            command += $" -PreviewId {identity.PreviewId}";
        }

        if (!identity.WorkbenchV2Enabled)
        {
            command += " -UseV1";
        }

        return command;
    }

    private static string ResolveDatabaseName(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        return new SqlConnectionStringBuilder(connectionString).InitialCatalog;
    }

    private LocalTestIdentity BuildIdentity()
    {
        var informationalVersion = typeof(LocalTestPreflightService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";
        var separator = informationalVersion.IndexOf('+');
        var buildCommit = separator >= 0 && separator < informationalVersion.Length - 1
            ? informationalVersion[(separator + 1)..]
            : informationalVersion;

        var workbench = WorkbenchReleaseInfoFactory.Create(
            _configuration,
            _hostEnvironment,
            typeof(LocalTestPreflightService).Assembly);

        return new LocalTestIdentity(
            informationalVersion,
            buildCommit,
            workbench.Version,
            workbench.V2Enabled,
            workbench.PreviewId,
            Environment.GetEnvironmentVariable("IRONDEV_LOCALTEST_REPOSITORY_COMMIT"),
            Environment.GetEnvironmentVariable("IRONDEV_LOCALTEST_SESSION_ID"),
            Environment.GetEnvironmentVariable("IRONDEV_LOCALTEST_API_BASE_URL"),
            Environment.GetEnvironmentVariable("IRONDEV_LOCALTEST_SESSION_MODE") ?? string.Empty,
            IsTrue(Environment.GetEnvironmentVariable("IRONDEV_LOCALTEST_SANDBOX_APPLY_REQUESTED")),
            IsTrue(Environment.GetEnvironmentVariable("IRONDEV_LOCALTEST_SANDBOX_APPLY_ENABLED")),
            Environment.GetEnvironmentVariable("IRONDEV_LOCALTEST_SANDBOX_APPLY_ROOT"),
            (Environment.GetEnvironmentVariable("IRONDEV_LOCALTEST_CAPABILITIES") ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            IsTrue(_configuration["SkeletonApply:Enabled"]),
            _configuration["SkeletonApply:SandboxRoot"],
            IsTrue(_configuration["SkeletonApply:LauncherCapabilityDeclared"]),
            _configuration["SkeletonApply:LauncherSessionId"]);
    }

    private static bool IsTrue(string? value) =>
        value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

    private sealed record LocalTestIdentity(
        string ApiBuildIdentity,
        string ApiBuildCommit,
        string WorkbenchVersion,
        bool WorkbenchV2Enabled,
        string PreviewId,
        string? LauncherRepositoryCommit,
        string? SessionId,
        string? ApiBaseUrl,
        string SessionMode,
        bool SandboxApplyRequested,
        bool SandboxApplyEnabled,
        string? SandboxApplyRoot,
        IReadOnlyList<string> Capabilities,
        bool ApiApplyEnabled,
        string? ApiSandboxRoot,
        bool ApiLauncherCapabilityDeclared,
        string? ApiLauncherSessionId)
    {
        public bool HasConsistentCapabilities
        {
            get
            {
                if (SandboxApplyRequested)
                {
                    return SandboxApplyEnabled &&
                           ApiApplyEnabled &&
                           ApiLauncherCapabilityDeclared &&
                           SessionMode == "ProjectFeatureWork" &&
                           !string.IsNullOrWhiteSpace(SandboxApplyRoot) &&
                           string.Equals(SandboxApplyRoot, ApiSandboxRoot, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(SessionId, ApiLauncherSessionId, StringComparison.Ordinal) &&
                           Capabilities.Contains("ProjectFeatureWork", StringComparer.Ordinal) &&
                           Capabilities.Contains("ControlledSandboxApply", StringComparer.Ordinal);
                }

                return !SandboxApplyEnabled && !ApiApplyEnabled && !ApiLauncherCapabilityDeclared &&
                       string.IsNullOrWhiteSpace(SandboxApplyRoot) &&
                       string.IsNullOrWhiteSpace(ApiSandboxRoot) &&
                       string.Equals(SessionId, ApiLauncherSessionId, StringComparison.Ordinal) &&
                       SessionMode == "SmokeSimulation" &&
                       !Capabilities.Contains("ControlledSandboxApply", StringComparer.Ordinal);
            }
        }
    }

    private sealed class SeedUserProbe
    {
        public int Id { get; init; }
        public string Email { get; init; } = string.Empty;
        public string? PasswordHash { get; init; }
        public bool IsActive { get; init; }
    }

    private sealed class LocalTestSeedContract
    {
        public int SchemaVersion { get; init; }
        public string Environment { get; init; } = string.Empty;
        public LocalTestDatabaseContract Database { get; init; } = new();
        public LocalTestCredentialContract Credentials { get; init; } = new();
        public LocalTestTenantContract Tenant { get; init; } = new();
        public IReadOnlyList<LocalTestUserContract> Users { get; init; } = [];
    }

    private sealed class LocalTestDatabaseContract
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class LocalTestCredentialContract
    {
        public string Email { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
    }

    private sealed class LocalTestTenantContract
    {
        public int Id { get; init; }
    }

    private sealed class LocalTestUserContract
    {
        public string Email { get; init; } = string.Empty;
        public string TenantRole { get; init; } = string.Empty;
    }
}
