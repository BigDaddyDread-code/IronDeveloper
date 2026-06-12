using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Api.Controllers;

public sealed class SqlDogfoodLoopApiStore : IDogfoodLoopApiStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    private readonly IDogfoodReceiptStore _receiptStore;
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlDogfoodLoopApiStore(IDogfoodReceiptStore receiptStore, IDbConnectionFactory connectionFactory)
    {
        _receiptStore = receiptStore ?? throw new ArgumentNullException(nameof(receiptStore));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public DogfoodLoopApiStoreSaveResult Save(DogfoodLoopApiStoredReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var projectId = ProjectScopeId(receipt.TenantId, receipt.ProjectId);
        var durableReceiptId = DurableDogfoodReceiptId(receipt.TenantId, receipt.ProjectId, receipt.DogfoodLoopId);
        var existing = _receiptStore.GetAsync(durableReceiptId).GetAwaiter().GetResult();
        if (existing is not null)
            return new DogfoodLoopApiStoreSaveResult { Created = false };

        var payloadReceipt = receipt with
        {
            Durable = true,
            Warnings = BuildWarnings(receipt.Warnings)
        };

        var evidenceJson = JsonSerializer.Serialize(new DogfoodLoopEvidenceDocument
        {
            SchemaVersion = 1,
            Receipt = payloadReceipt,
            ApprovesRelease = false,
            GrantsApproval = false,
            GrantsExecution = false,
            SatisfiesPolicy = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            TransfersAuthority = false,
            ContainsRawPrivateReasoning = receipt.ContainsRawPrivateReasoning
        }, JsonOptions);

        _ = _receiptStore.RecordAsync(new DogfoodReceiptRecordRequest
        {
            DogfoodReceiptId = durableReceiptId,
            GovernanceEventId = GovernanceEventId(receipt.TenantId, receipt.ProjectId, receipt.DogfoodLoopId),
            ProjectId = projectId,
            ReceiptType = "dogfood_loop_api",
            SubjectType = "dogfood_loop",
            SubjectId = receipt.DogfoodLoopId,
            Outcome = nameof(DogfoodReceiptOutcome.Inconclusive),
            SummaryCode = "DOGFOOD_LOOP_API_RECEIPT_RECORDED",
            Summary = receipt.Summary,
            RecordedByActorType = "user",
            RecordedByActorId = receipt.CreatedByUserId,
            CorrelationId = CorrelationScopeId(receipt.CorrelationId),
            EvidenceVersion = 1,
            EvidenceJson = evidenceJson,
            CreatedUtc = receipt.CreatedAtUtc == default ? DateTimeOffset.UtcNow : receipt.CreatedAtUtc
        }).GetAwaiter().GetResult();

        return new DogfoodLoopApiStoreSaveResult { Created = true };
    }

    public DogfoodLoopApiStoredReceipt? Get(string tenantId, string projectId, string dogfoodLoopId)
    {
        var read = _receiptStore.GetAsync(DurableDogfoodReceiptId(tenantId, projectId, dogfoodLoopId)).GetAwaiter().GetResult();

        if (read is null)
            return null;

        try
        {
            var document = JsonSerializer.Deserialize<DogfoodLoopEvidenceDocument>(read.EvidenceJson, JsonOptions);
            if (document is null) return null;
            return document.Receipt with
            {
                Durable = true,
                CreatedAtUtc = read.CreatedUtc,
                Warnings = BuildWarnings(document.Receipt.Warnings)
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public int Count()
    {
        using var connection = _connectionFactory.CreateConnection();
        return connection.ExecuteScalar<int>(
            new CommandDefinition(
                """
                IF OBJECT_ID(N'governance.DogfoodReceipt', N'U') IS NULL
                    SELECT 0;
                ELSE
                    SELECT COUNT(*) FROM governance.DogfoodReceipt;
                """,
                commandType: CommandType.Text));
    }

    private static IReadOnlyList<string> BuildWarnings(IReadOnlyList<string> warnings)
    {
        var combined = new List<string>(warnings)
        {
            "Dogfood receipt is durable SQL-backed evidence, not release approval.",
            "Dogfood receipt does not execute tools, continue workflows, apply source, satisfy policy, or promote memory."
        };

        return combined.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static Guid DurableDogfoodReceiptId(string tenantId, string projectId, string dogfoodLoopId) =>
        StableGuid($"dogfood-receipt::{tenantId}::{projectId}::{dogfoodLoopId}");

    private static Guid GovernanceEventId(string tenantId, string projectId, string dogfoodLoopId) =>
        StableGuid($"dogfood-receipt-event::{tenantId}::{projectId}::{dogfoodLoopId}");

    private static Guid ProjectScopeId(string tenantId, string projectId) =>
        StableGuid($"project::{tenantId}::{projectId}");

    private static Guid? CorrelationScopeId(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Guid.TryParse(value, out var parsed) ? parsed : StableGuid($"correlation::{value}");

    private static Guid StableGuid(string seed)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, guidBytes.Length);
        return new Guid(guidBytes);
    }

    private sealed record DogfoodLoopEvidenceDocument
    {
        public string Schema { get; init; } = "dogfood.loop.api.receipt.v1";
        public int SchemaVersion { get; init; }
        public required DogfoodLoopApiStoredReceipt Receipt { get; init; }
        public bool ApprovesRelease { get; init; }
        public bool GrantsApproval { get; init; }
        public bool GrantsExecution { get; init; }
        public bool SatisfiesPolicy { get; init; }
        public bool MutatesSource { get; init; }
        public bool PromotesMemory { get; init; }
        public bool StartsWorkflow { get; init; }
        public bool TransfersAuthority { get; init; }
        public bool ContainsRawPrivateReasoning { get; init; }
    }
}
