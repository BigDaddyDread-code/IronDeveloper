using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronDev.Data;

namespace IronDev.Services;

/// <summary>
/// Simple lookup item returned from lookup tables.
/// </summary>
public sealed class LookupItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Provides access to lookup data (decision categories, decision statuses, etc.)
/// </summary>
public interface ILookupService
{
    Task<IReadOnlyList<LookupItem>> GetDecisionCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LookupItem>> GetDecisionStatusesAsync(CancellationToken cancellationToken = default);
}

public sealed class LookupService : ILookupService
{
    private readonly IDbConnectionFactory _connectionFactory;

    // Simple in-memory cache — lookup data rarely changes
    private IReadOnlyList<LookupItem>? _categoriesCache;
    private IReadOnlyList<LookupItem>? _statusesCache;

    public LookupService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<LookupItem>> GetDecisionCategoriesAsync(CancellationToken cancellationToken = default)
    {
        if (_categoriesCache != null) return _categoriesCache;

        const string sql = """
            SELECT Id, Name, SortOrder, IsActive
            FROM dbo.DecisionCategories
            WHERE IsActive = 1
            ORDER BY SortOrder;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<LookupItem>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        _categoriesCache = rows.ToList();
        return _categoriesCache;
    }

    public async Task<IReadOnlyList<LookupItem>> GetDecisionStatusesAsync(CancellationToken cancellationToken = default)
    {
        if (_statusesCache != null) return _statusesCache;

        const string sql = """
            SELECT Id, Name, SortOrder, IsActive
            FROM dbo.DecisionStatuses
            WHERE IsActive = 1
            ORDER BY SortOrder;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<LookupItem>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        _statusesCache = rows.ToList();
        return _statusesCache;
    }
}
