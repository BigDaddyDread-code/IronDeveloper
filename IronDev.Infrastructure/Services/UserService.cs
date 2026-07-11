using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
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

    /// <summary>Returns the caller's membership role for the tenant, or null when not a member.</summary>
    Task<string?> GetTenantRoleAsync(int userId, int tenantId, CancellationToken ct = default);

    /// <summary>Returns all users with a membership in the tenant, including their tenant role.</summary>
    Task<IReadOnlyList<TenantUserRecord>> GetTenantUsersAsync(int tenantId, CancellationToken ct = default);

    /// <summary>
    /// Creates the user when the email is new (BCrypt-hashed password required) and adds a tenant
    /// membership with the given role. Adding an existing user to the tenant reuses the account and
    /// never touches its password.
    /// </summary>
    Task<CreateTenantUserResult> CreateTenantUserAsync(
        int tenantId,
        string email,
        string displayName,
        string? password,
        string role,
        CancellationToken ct = default);

    /// <summary>Updates the membership role. Refuses to demote the tenant's last owner.</summary>
    Task<TenantUserMutationResult> SetTenantUserRoleAsync(int tenantId, int userId, string role, CancellationToken ct = default);

    /// <summary>Removes the tenant membership only — the user account survives. Refuses to remove the last owner.</summary>
    Task<TenantUserMutationResult> RemoveTenantUserAsync(int tenantId, int userId, CancellationToken ct = default);
}

/// <summary>
/// Canonical tenant role vocabulary. Role decides visibility; it never grants mutation authority —
/// backend authority gates remain the only authority.
/// </summary>
public static class TenantUserRoles
{
    public const string Owner = "Owner";
    public const string TenantAdmin = "TenantAdmin";
    public const string Approver = "Approver";
    public const string Reviewer = "Reviewer";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";
    public const string Member = "Member";

    public static readonly IReadOnlyList<string> All =
        [Owner, TenantAdmin, Approver, Reviewer, Operator, Viewer, Member];

    public static bool IsKnown(string? role) =>
        role is not null && All.Contains(role, StringComparer.OrdinalIgnoreCase);

    public static bool CanAdministerUsers(string? role) =>
        string.Equals(role, Owner, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, TenantAdmin, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string role) =>
        All.First(known => string.Equals(known, role, StringComparison.OrdinalIgnoreCase));
}

public sealed class TenantUserRecord
{
    public int Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public string Role { get; init; } = TenantUserRoles.Member;
}

public enum TenantUserMutationStatus
{
    Succeeded = 0,
    NotFound = 1,
    LastOwnerProtected = 2,
    AlreadyMember = 3,
    PasswordRequired = 4
}

public sealed record TenantUserMutationResult(TenantUserMutationStatus Status)
{
    public static readonly TenantUserMutationResult Succeeded = new(TenantUserMutationStatus.Succeeded);
    public static readonly TenantUserMutationResult NotFound = new(TenantUserMutationStatus.NotFound);
    public static readonly TenantUserMutationResult LastOwnerProtected = new(TenantUserMutationStatus.LastOwnerProtected);
}

public sealed record CreateTenantUserResult(TenantUserMutationStatus Status, TenantUserRecord? User);

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

    public async Task<string?> GetTenantRoleAsync(int userId, int tenantId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT tu.Role
            FROM dbo.TenantUsers tu
            INNER JOIN dbo.Tenants t ON t.Id = tu.TenantId
            WHERE tu.UserId = @UserId
              AND tu.TenantId = @TenantId
              AND t.IsActive = 1;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            sql, new { UserId = userId, TenantId = tenantId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<TenantUserRecord>> GetTenantUsersAsync(int tenantId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT u.Id, u.Email, u.DisplayName, u.IsActive, tu.Role
            FROM dbo.Users u
            INNER JOIN dbo.TenantUsers tu ON tu.UserId = u.Id
            WHERE tu.TenantId = @TenantId
            ORDER BY u.DisplayName;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<TenantUserRecord>(new CommandDefinition(
            sql, new { TenantId = tenantId }, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<CreateTenantUserResult> CreateTenantUserAsync(
        int tenantId,
        string email,
        string displayName,
        string? password,
        string role,
        CancellationToken ct = default)
    {
        var normalizedRole = TenantUserRoles.Normalize(role);

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            var existing = await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
                "SELECT Id, Email, DisplayName, PasswordHash, IsActive FROM dbo.Users WHERE Email = @Email;",
                new { Email = email }, transaction, cancellationToken: ct));

            int userId;
            if (existing is null)
            {
                if (string.IsNullOrWhiteSpace(password))
                    return new CreateTenantUserResult(TenantUserMutationStatus.PasswordRequired, null);

                var passwordHash = BCrypt.Net.BCrypt.HashPassword(password);
                userId = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    """
                    INSERT INTO dbo.Users (Email, DisplayName, PasswordHash, IsActive)
                    OUTPUT INSERTED.Id
                    VALUES (@Email, @DisplayName, @PasswordHash, 1);
                    """,
                    new { Email = email, DisplayName = displayName, PasswordHash = passwordHash },
                    transaction, cancellationToken: ct));
            }
            else
            {
                userId = existing.Id;
                var isMember = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(1) FROM dbo.TenantUsers WHERE TenantId = @TenantId AND UserId = @UserId;",
                    new { TenantId = tenantId, UserId = userId }, transaction, cancellationToken: ct));
                if (isMember > 0)
                    return new CreateTenantUserResult(TenantUserMutationStatus.AlreadyMember, null);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO dbo.TenantUsers (TenantId, UserId, Role) VALUES (@TenantId, @UserId, @Role);",
                new { TenantId = tenantId, UserId = userId, Role = normalizedRole },
                transaction, cancellationToken: ct));

            var record = await connection.QuerySingleAsync<TenantUserRecord>(new CommandDefinition(
                """
                SELECT u.Id, u.Email, u.DisplayName, u.IsActive, tu.Role
                FROM dbo.Users u
                INNER JOIN dbo.TenantUsers tu ON tu.UserId = u.Id
                WHERE tu.TenantId = @TenantId AND u.Id = @UserId;
                """,
                new { TenantId = tenantId, UserId = userId }, transaction, cancellationToken: ct));

            transaction.Commit();
            return new CreateTenantUserResult(TenantUserMutationStatus.Succeeded, record);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<TenantUserMutationResult> SetTenantUserRoleAsync(int tenantId, int userId, string role, CancellationToken ct = default)
    {
        var normalizedRole = TenantUserRoles.Normalize(role);

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            var currentRole = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT Role FROM dbo.TenantUsers WHERE TenantId = @TenantId AND UserId = @UserId;",
                new { TenantId = tenantId, UserId = userId }, transaction, cancellationToken: ct));

            if (currentRole is null)
                return TenantUserMutationResult.NotFound;

            if (string.Equals(currentRole, TenantUserRoles.Owner, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalizedRole, TenantUserRoles.Owner, StringComparison.OrdinalIgnoreCase))
            {
                var ownerCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(1) FROM dbo.TenantUsers WHERE TenantId = @TenantId AND Role = @Owner;",
                    new { TenantId = tenantId, Owner = TenantUserRoles.Owner }, transaction, cancellationToken: ct));
                if (ownerCount <= 1)
                    return TenantUserMutationResult.LastOwnerProtected;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.TenantUsers SET Role = @Role WHERE TenantId = @TenantId AND UserId = @UserId;",
                new { TenantId = tenantId, UserId = userId, Role = normalizedRole },
                transaction, cancellationToken: ct));

            transaction.Commit();
            return TenantUserMutationResult.Succeeded;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<TenantUserMutationResult> RemoveTenantUserAsync(int tenantId, int userId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            var currentRole = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT Role FROM dbo.TenantUsers WHERE TenantId = @TenantId AND UserId = @UserId;",
                new { TenantId = tenantId, UserId = userId }, transaction, cancellationToken: ct));

            if (currentRole is null)
                return TenantUserMutationResult.NotFound;

            if (string.Equals(currentRole, TenantUserRoles.Owner, StringComparison.OrdinalIgnoreCase))
            {
                var ownerCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(1) FROM dbo.TenantUsers WHERE TenantId = @TenantId AND Role = @Owner;",
                    new { TenantId = tenantId, Owner = TenantUserRoles.Owner }, transaction, cancellationToken: ct));
                if (ownerCount <= 1)
                    return TenantUserMutationResult.LastOwnerProtected;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                IF OBJECT_ID(N'dbo.ProjectChannelMembers', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.ProjectChannelMembers
                    SET Status = 'Removed', RemovedUtc = SYSUTCDATETIME()
                    WHERE TenantId = @TenantId AND UserId = @UserId AND Status = 'Active';
                END;
                IF OBJECT_ID(N'dbo.ProjectMembers', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.ProjectMembers
                    SET Status = 'Removed', RemovedUtc = SYSUTCDATETIME()
                    WHERE TenantId = @TenantId AND UserId = @UserId AND Status = 'Active';
                END;
                """,
                new { TenantId = tenantId, UserId = userId }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.TenantUsers WHERE TenantId = @TenantId AND UserId = @UserId;",
                new { TenantId = tenantId, UserId = userId }, transaction, cancellationToken: ct));

            transaction.Commit();
            return TenantUserMutationResult.Succeeded;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
