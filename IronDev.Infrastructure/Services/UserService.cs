using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core.Auth;
using IronDev.Data;
using IronDev.Data.Models;

namespace IronDev.Services;

// ── Contracts ────────────────────────────────────────────────────────────────

public interface IUserService
{
    /// <summary>
    /// Returns the User if credentials are valid, null otherwise.
    /// </summary>
    Task<User?> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default);

    Task<User?> GetByIdAsync(int userId, CancellationToken ct = default);

    Task<IReadOnlyList<TenantDto>> GetUserTenantsAsync(int userId, CancellationToken ct = default);

    Task<IReadOnlyList<TenantDto>> GetAllActiveTenantsAsync(CancellationToken ct = default);

    Task<bool> IsMemberOfTenantAsync(int userId, int tenantId, CancellationToken ct = default);
}

// ── Domain entity (not in Core/DataModels — stays internal to Infrastructure) ─

public sealed class User
{
    public int Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? PasswordHash { get; init; }
    public bool IsActive { get; init; }
}

// ── Implementation ────────────────────────────────────────────────────────────

public sealed class UserService : IUserService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public UserService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<User?> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default)
    {
        const string sql = """
            SELECT Id, Email, DisplayName, PasswordHash, IsActive
            FROM dbo.Users
            WHERE Email = @Email AND IsActive = 1;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var user = await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
            sql, new { Email = email }, cancellationToken: ct));

        if (user is null) return null;
        if (string.IsNullOrEmpty(user.PasswordHash)) return null;

        // BCrypt verification — safe against timing attacks.
        var valid = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
        return valid ? user : null;
    }

    public async Task<User?> GetByIdAsync(int userId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT Id, Email, DisplayName, PasswordHash, IsActive
            FROM dbo.Users
            WHERE Id = @UserId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
            sql, new { UserId = userId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<TenantDto>> GetAllActiveTenantsAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT Id, Name, Slug
            FROM dbo.Tenants
            WHERE IsActive = 1;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<TenantDto>(new CommandDefinition(
            sql, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<TenantDto>> GetUserTenantsAsync(int userId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT t.Id, t.Name, t.Slug
            FROM dbo.Tenants t
            INNER JOIN dbo.TenantUsers tu ON tu.TenantId = t.Id
            WHERE tu.UserId = @UserId
              AND t.IsActive = 1;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<TenantDto>(new CommandDefinition(
            sql, new { UserId = userId }, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<bool> IsMemberOfTenantAsync(int userId, int tenantId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.TenantUsers tu
            INNER JOIN dbo.Tenants t ON t.Id = tu.TenantId
            WHERE tu.UserId = @UserId
              AND tu.TenantId = @TenantId
              AND t.IsActive = 1;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql, new { UserId = userId, TenantId = tenantId }, cancellationToken: ct));

        return count > 0;
    }
}
