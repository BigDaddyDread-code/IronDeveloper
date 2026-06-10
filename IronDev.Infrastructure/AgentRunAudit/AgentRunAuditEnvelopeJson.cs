using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;

namespace IronDev.Infrastructure.AgentRunAudit;

internal static class AgentRunAuditEnvelopeJson
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static string Serialize(AgentRunAuditEnvelope envelope) =>
        JsonSerializer.Serialize(envelope, JsonOptions);

    public static AgentRunAuditEnvelope? Deserialize(string json) =>
        JsonSerializer.Deserialize<AgentRunAuditEnvelope>(json, JsonOptions);

    public static string Sha256(string json)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            PropertyNamingPolicy = null,
            DictionaryKeyPolicy = null,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };
        options.Converters.Add(new AgentDefinitionJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class AgentDefinitionJsonConverter : JsonConverter<AgentDefinition>
    {
        public override AgentDefinition? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var dto = JsonSerializer.Deserialize<AgentDefinitionJson>(ref reader, options);
            if (dto is null)
                return null;

            return new AgentDefinition
            {
                AgentId = dto.AgentId,
                Kind = dto.Kind,
                ExecutionMode = dto.ExecutionMode,
                Persona = dto.Persona,
                Capabilities = dto.Capabilities ?? new HashSet<AgentCapability>(),
                ForbiddenCapabilities = dto.ForbiddenCapabilities ?? new HashSet<AgentCapability>(),
                Description = dto.Description,
                Owner = dto.Owner,
                IsEnabled = dto.IsEnabled,
                Name = dto.Name,
                Purpose = dto.Purpose,
                DefaultModelProfile = dto.DefaultModelProfile,
                Enabled = dto.Enabled,
                AllowedTools = dto.AllowedTools ?? []
            };
        }

        public override void Write(Utf8JsonWriter writer, AgentDefinition value, JsonSerializerOptions options)
        {
            var dto = new AgentDefinitionJson
            {
                AgentId = value.AgentId,
                Kind = value.Kind,
                ExecutionMode = value.ExecutionMode,
                Persona = value.Persona,
                Capabilities = value.Capabilities?.ToHashSet() ?? [],
                ForbiddenCapabilities = value.ForbiddenCapabilities?.ToHashSet() ?? [],
                Description = value.Description,
                Owner = value.Owner,
                IsEnabled = value.IsEnabled,
                Name = value.Name,
                Purpose = value.Purpose,
                DefaultModelProfile = value.DefaultModelProfile,
                Enabled = value.Enabled,
                AllowedTools = value.AllowedTools
            };

            JsonSerializer.Serialize(writer, dto, options);
        }
    }

    private sealed record AgentDefinitionJson
    {
        public string AgentId { get; init; } = string.Empty;
        public AgentKind Kind { get; init; }
        public AgentExecutionMode ExecutionMode { get; init; }
        public AgentPersona? Persona { get; init; }
        public HashSet<AgentCapability>? Capabilities { get; init; } = [];
        public HashSet<AgentCapability>? ForbiddenCapabilities { get; init; } = [];
        public string? Description { get; init; }
        public string? Owner { get; init; }
        public bool IsEnabled { get; init; } = true;
        public string Name { get; init; } = string.Empty;
        public string Purpose { get; init; } = string.Empty;
        public string DefaultModelProfile { get; init; } = string.Empty;
        public bool Enabled { get; init; } = true;
        public IReadOnlyList<string>? AllowedTools { get; init; } = [];
    }
}
