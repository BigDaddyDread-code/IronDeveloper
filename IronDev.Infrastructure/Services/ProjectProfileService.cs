using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core.Auth;
using IronDev.Core.Interfaces;
using IronDev.Data;
using IronDev.Data.Models;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectProfileService : IProjectProfileService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentTenantContext _tenant;

    public ProjectProfileService(IDbConnectionFactory connectionFactory, ICurrentTenantContext tenant)
    {
        _connectionFactory = connectionFactory;
        _tenant = tenant;
    }

    public async Task<ProjectProfile?> GetProjectProfileAsync(int projectId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT * FROM dbo.ProjectProfiles
            WHERE ProjectId = @ProjectId AND TenantId = @TenantId
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ProjectProfile>(new CommandDefinition(
            sql, new { ProjectId = projectId, TenantId = _tenant.TenantId }, cancellationToken: ct));
    }

    public async Task SaveProjectProfileAsync(ProjectProfile profile, CancellationToken ct = default)
    {
        const string sql = """
            IF EXISTS (SELECT 1 FROM dbo.ProjectProfiles WHERE ProjectId = @ProjectId AND TenantId = @TenantId)
            BEGIN
                UPDATE dbo.ProjectProfiles
                SET IsExternalProject = @IsExternalProject,
                    ApplicationType = @ApplicationType,
                    PrimaryLanguage = @PrimaryLanguage,
                    Framework = @Framework,
                    RuntimeVersion = @RuntimeVersion,
                    DatabaseEngine = @DatabaseEngine,
                    DataAccessStyle = @DataAccessStyle,
                    TestFramework = @TestFramework,
                    SolutionFile = @SolutionFile,
                    SafeWriteRoot = @SafeWriteRoot,
                    AllowBuilderApply = @AllowBuilderApply,
                    AllowWritesOutsideProjectRoot = @AllowWritesOutsideProjectRoot,
                    ProfileNotes = @ProfileNotes,
                    UpdatedUtc = sysutcdatetime()
                WHERE ProjectId = @ProjectId AND TenantId = @TenantId
            END
            ELSE
            BEGIN
                INSERT INTO dbo.ProjectProfiles (
                    TenantId, ProjectId, IsExternalProject, ApplicationType, PrimaryLanguage,
                    Framework, RuntimeVersion, DatabaseEngine, DataAccessStyle, TestFramework,
                    SolutionFile, SafeWriteRoot, AllowBuilderApply, AllowWritesOutsideProjectRoot, ProfileNotes
                )
                VALUES (
                    @TenantId, @ProjectId, @IsExternalProject, @ApplicationType, @PrimaryLanguage,
                    @Framework, @RuntimeVersion, @DatabaseEngine, @DataAccessStyle, @TestFramework,
                    @SolutionFile, @SafeWriteRoot, @AllowBuilderApply, @AllowWritesOutsideProjectRoot, @ProfileNotes
                )
            END
            """;

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new {
            _tenant.TenantId,
            profile.ProjectId,
            profile.IsExternalProject,
            profile.ApplicationType,
            profile.PrimaryLanguage,
            profile.Framework,
            profile.RuntimeVersion,
            profile.DatabaseEngine,
            profile.DataAccessStyle,
            profile.TestFramework,
            profile.SolutionFile,
            profile.SafeWriteRoot,
            profile.AllowBuilderApply,
            profile.AllowWritesOutsideProjectRoot,
            profile.ProfileNotes
        }, cancellationToken: ct));
    }

    public async Task<List<ProjectCommand>> GetProjectCommandsAsync(int projectId, CancellationToken ct = default)
    {
        const string sql = "SELECT * FROM dbo.ProjectCommands WHERE ProjectId = @ProjectId AND TenantId = @TenantId";
        using var connection = _connectionFactory.CreateConnection();
        var result = await connection.QueryAsync<ProjectCommand>(new CommandDefinition(sql, new { ProjectId = projectId, TenantId = _tenant.TenantId }, cancellationToken: ct));
        return result.ToList();
    }

    public async Task SaveProjectCommandAsync(ProjectCommand command, CancellationToken ct = default)
    {
        const string sql = """
            IF EXISTS (SELECT 1 FROM dbo.ProjectCommands WHERE ProjectCommandId = @ProjectCommandId AND TenantId = @TenantId)
            BEGIN
                UPDATE dbo.ProjectCommands
                SET CommandType = @CommandType,
                    CommandText = @CommandText,
                    WorkingDirectory = @WorkingDirectory,
                    TimeoutSeconds = @TimeoutSeconds,
                    IsDefault = @IsDefault,
                    IsEnabled = @IsEnabled,
                    UpdatedUtc = sysutcdatetime()
                WHERE ProjectCommandId = @ProjectCommandId AND TenantId = @TenantId
            END
            ELSE
            BEGIN
                INSERT INTO dbo.ProjectCommands (
                    TenantId, ProjectId, CommandType, CommandText, WorkingDirectory, TimeoutSeconds, IsDefault, IsEnabled
                )
                VALUES (
                    @TenantId, @ProjectId, @CommandType, @CommandText, @WorkingDirectory, @TimeoutSeconds, @IsDefault, @IsEnabled
                )
            END
            """;

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new {
            _tenant.TenantId,
            command.ProjectCommandId,
            command.ProjectId,
            command.CommandType,
            command.CommandText,
            command.WorkingDirectory,
            command.TimeoutSeconds,
            command.IsDefault,
            command.IsEnabled
        }, cancellationToken: ct));
    }

    public async Task<ProjectCommand?> GetDefaultCommandAsync(int projectId, string commandType, CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP 1 * FROM dbo.ProjectCommands
            WHERE ProjectId = @ProjectId AND TenantId = @TenantId AND CommandType = @CommandType AND IsDefault = 1 AND IsEnabled = 1
            """;
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ProjectCommand>(new CommandDefinition(sql, new { ProjectId = projectId, TenantId = _tenant.TenantId, CommandType = commandType }, cancellationToken: ct));
    }

    public async Task<List<ProjectProfileOption>> GetOptionsByCategoryAsync(string category, CancellationToken ct = default)
    {
        const string sql = "SELECT * FROM dbo.ProjectProfileOptions WHERE Category = @Category AND IsActive = 1 ORDER BY SortOrder";
        using var connection = _connectionFactory.CreateConnection();
        var result = await connection.QueryAsync<ProjectProfileOption>(new CommandDefinition(sql, new { Category = category }, cancellationToken: ct));
        return result.ToList();
    }
}
