using Dapper;
using IronDev.Core.Audit;
using IronDev.Data;

namespace IronDev.Infrastructure.Audit;

public sealed class SqlUserMutationAttributionStore : IUserMutationAttributionStore
{
    private readonly IDbConnectionFactory _connections;

    public SqlUserMutationAttributionStore(IDbConnectionFactory connections) => _connections = connections;

    public async Task AppendAsync(UserMutationAttributionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        Validate(record);

        using var connection = _connections.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.UserMutationAttribution
            (
                ActorUserId, TenantId, ProjectId, CorrelationId, CausationId,
                TimestampUtc, SourceSurface, SourceClient, Method, Route, Phase, StatusCode
            )
            VALUES
            (
                @ActorUserId, @TenantId, @ProjectId, @CorrelationId, @CausationId,
                @TimestampUtc, @SourceSurface, @SourceClient, @Method, @Route, @Phase, @StatusCode
            );
            """,
            new
            {
                record.ActorUserId,
                record.TenantId,
                ProjectId = NormalizeNullable(record.ProjectId, 128),
                CorrelationId = Normalize(record.CorrelationId, 128),
                CausationId = NormalizeNullable(record.CausationId, 128),
                TimestampUtc = record.TimestampUtc.UtcDateTime,
                SourceSurface = Normalize(record.SourceSurface, 80),
                SourceClient = Normalize(record.SourceClient, 120),
                Method = Normalize(record.Method, 12),
                Route = Normalize(record.Route, 500),
                Phase = Normalize(record.Phase, 24),
                record.StatusCode
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static void Validate(UserMutationAttributionRecord record)
    {
        if (record.ActorUserId <= 0)
            throw new ArgumentOutOfRangeException(nameof(record), "ActorUserId must be positive.");

        _ = Normalize(record.CorrelationId, 128);
        _ = Normalize(record.SourceSurface, 80);
        _ = Normalize(record.SourceClient, 120);
        _ = Normalize(record.Method, 12);
        _ = Normalize(record.Route, 500);
        _ = Normalize(record.Phase, 24);
    }

    private static string Normalize(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Attribution text values must not be blank.");

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? NormalizeNullable(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Normalize(value, maxLength);
}
