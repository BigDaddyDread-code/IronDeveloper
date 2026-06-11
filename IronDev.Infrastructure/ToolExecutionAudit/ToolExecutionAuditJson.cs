using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronDev.Infrastructure.ToolExecutionAudit;

internal static class ToolExecutionAuditJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    static ToolExecutionAuditJson()
    {
        Options.Converters.Add(new JsonStringEnumConverter());
    }

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Options);

    public static IReadOnlyList<string> DeserializeEvidenceRefs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        return JsonSerializer.Deserialize<IReadOnlyList<string>>(json, Options) ?? [];
    }
}
