using System.Text.Json;
using IronDev.Core.AiConnections;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

public sealed class FileSystemAiConnectionTestHealthStore : IAiConnectionTestHealthStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _root;

    public FileSystemAiConnectionTestHealthStore(IConfiguration configuration)
    {
        var configured = configuration["AiConnections:HealthStorePath"]?.Trim();
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IronDev", "ai-connection-health")
            : Path.GetFullPath(configured);
    }

    public async Task<AiConnectionTestHealth?> GetAsync(
        int tenantId,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        var path = PathFor(tenantId, connectionId);
        if (!File.Exists(path))
            return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AiConnectionTestHealth>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RecordAsync(
        int tenantId,
        string connectionId,
        bool succeeded,
        DateTimeOffset testedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var current = await GetAsync(tenantId, connectionId, cancellationToken).ConfigureAwait(false);
        var next = new AiConnectionTestHealth
        {
            LastSuccessfulTestUtc = succeeded ? testedAtUtc : current?.LastSuccessfulTestUtc,
            LastFailedTestUtc = succeeded ? current?.LastFailedTestUtc : testedAtUtc,
            LastStatus = succeeded ? "Passed" : "Failed"
        };
        var path = PathFor(tenantId, connectionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(next, JsonOptions), cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    private string PathFor(int tenantId, string connectionId) =>
        Path.Combine(_root, $"tenant-{tenantId}", $"{SafeFileName(connectionId)}.health.json");

    private static string SafeFileName(string value)
    {
        var cleaned = value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray();
        return cleaned.Length == 0 ? "connection" : new string(cleaned);
    }
}
