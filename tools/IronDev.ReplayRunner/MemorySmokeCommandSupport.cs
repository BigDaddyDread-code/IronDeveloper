using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.SemanticMemory;

internal static class MemorySmokeCommandSupport
{
    internal static async Task IndexDocumentVersionAsync(
        SemanticArtefactRepository artefactRepository,
        SemanticChunkRepository chunkRepository,
        int tenantId,
        int projectId,
        Guid artefactId,
        ProjectDocument document,
        ProjectDocumentVersion version,
        string authorityLevel,
        string content)
    {
        var hash = ComputeSha256(content);
        var artefact = new SemanticArtefactDraft
        {
            Id = artefactId,
            TenantId = tenantId,
            ProjectId = projectId,
            SourceEntityType = "ProjectDocument",
            SourceEntityId = document.Id.ToString(),
            SourceVersionId = version.Id.ToString(),
            ArtefactType = document.DocumentType,
            AuthorityLevel = authorityLevel,
            Title = document.Title,
            Summary = version.ChangeSummary,
            SearchableText = content,
            ContentHash = hash
        };

        await artefactRepository.UpsertArtefactAsync(artefact);
        await chunkRepository.ReplaceChunksAsync(artefactId, [
            new SemanticChunkDraft
            {
                Id = Guid.NewGuid(),
                ArtefactId = artefactId,
                ProjectId = projectId,
                ChunkIndex = 0,
                ChunkText = content,
                TokenEstimate = Math.Max(1, content.Length / 4),
                ContentHash = hash
            }
        ]);
    }

    internal static SemanticSearchCandidate BuildCandidate(
        int tenantId,
        int projectId,
        ProjectDocument document,
        ProjectDocumentVersion version,
        SemanticArtefact artefact,
        SemanticChunk chunk,
        string content,
        double vectorSimilarity,
        bool contentHashMismatch)
    {
        return new SemanticSearchCandidate
        {
            Document = new ProjectContextDocument
            {
                TenantId = tenantId,
                ProjectId = projectId,
                DocumentType = document.DocumentType,
                AuthorityLevel = artefact.AuthorityLevel,
                Status = version.Id == document.CurrentVersionId ? "Active" : "Superseded",
                Title = document.Title,
                Content = content,
                Summary = version.ChangeSummary,
                Source = $"ProjectDocumentVersion:{version.Id}",
                CreatedDate = version.CreatedAtUtc,
                UpdatedDate = version.CreatedAtUtc
            },
            Artefact = artefact,
            Chunk = chunk,
            VectorSimilarity = vectorSimilarity,
            ContentHashMismatch = contentHashMismatch
        };
    }

    internal static async Task EnsureWeaviateDogfoodCollectionAsync(HttpClient httpClient, string collectionName)
    {
        var existsResponse = await httpClient.GetAsync($"v1/schema/{collectionName}");
        if (existsResponse.IsSuccessStatusCode)
            return;

        var schema = new
        {
            @class = collectionName,
            vectorizer = "none",
            properties = new object[]
            {
                new { name = "chunkId", dataType = new[] { "text" } },
                new { name = "artefactId", dataType = new[] { "text" } },
                new { name = "tenantId", dataType = new[] { "int" } },
                new { name = "projectId", dataType = new[] { "int" } },
                new { name = "sourceEntityType", dataType = new[] { "text" } },
                new { name = "sourceEntityId", dataType = new[] { "text" } },
                new { name = "sourceVersionId", dataType = new[] { "text" } },
                new { name = "artefactType", dataType = new[] { "text" } },
                new { name = "authorityLevel", dataType = new[] { "text" } },
                new { name = "title", dataType = new[] { "text" } },
                new { name = "chunkText", dataType = new[] { "text" } },
                new { name = "chunkIndex", dataType = new[] { "int" } },
                new { name = "contentHash", dataType = new[] { "text" } },
                new { name = "isStale", dataType = new[] { "boolean" } }
            }
        };

        var response = await PostJsonAsync(httpClient, "v1/schema", schema);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity &&
                body.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            throw new InvalidOperationException($"Weaviate schema create failed for {collectionName}: {(int)response.StatusCode} {body}");
        }
    }

    internal static async Task UpsertWeaviateChunkAsync(
        HttpClient httpClient,
        string collectionName,
        Guid objectId,
        SemanticArtefact artefact,
        SemanticChunk chunk,
        int tenantId,
        bool isStale,
        IReadOnlyList<double> vector)
    {
        var payload = new
        {
            @class = collectionName,
            id = objectId.ToString(),
            properties = new
            {
                chunkId = chunk.Id.ToString(),
                artefactId = artefact.Id.ToString(),
                tenantId,
                projectId = artefact.ProjectId,
                sourceEntityType = artefact.SourceEntityType,
                sourceEntityId = artefact.SourceEntityId,
                sourceVersionId = artefact.SourceVersionId ?? string.Empty,
                artefactType = artefact.ArtefactType,
                authorityLevel = artefact.AuthorityLevel,
                title = artefact.Title,
                chunkText = chunk.ChunkText,
                chunkIndex = chunk.ChunkIndex,
                contentHash = chunk.ContentHash,
                isStale
            },
            vector
        };

        var response = await PostJsonAsync(httpClient, "v1/objects", payload);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity &&
                body.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                response = await PutJsonAsync(httpClient, $"v1/objects/{collectionName}/{objectId}", payload);
                if (response.IsSuccessStatusCode)
                    return;

                body = await response.Content.ReadAsStringAsync();
            }

            throw new InvalidOperationException($"Weaviate object upsert failed for {objectId}: {(int)response.StatusCode} {body}");
        }
    }

    internal static async Task<IReadOnlyList<WeaviateRawMatch>> QueryWeaviateDogfoodCollectionAsync(
        HttpClient httpClient,
        string collectionName,
        IReadOnlyList<double> queryVector,
        int limit)
    {
        var vectorText = string.Join(",", queryVector.Select(value => value.ToString("0.########", CultureInfo.InvariantCulture)));
        var graphQl = new
        {
            query = $$"""
            {
              Get {
                {{collectionName}}(
                  nearVector: { vector: [{{vectorText}}] }
                  limit: {{limit}}
                ) {
                  chunkId
                  artefactId
                  tenantId
                  projectId
                  sourceEntityType
                  sourceEntityId
                  sourceVersionId
                  title
                  isStale
                  _additional {
                    id
                    distance
                  }
                }
              }
            }
            """
        };

        var response = await PostJsonAsync(httpClient, "v1/graphql", graphQl);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Weaviate GraphQL query failed: {(int)response.StatusCode} {body}");

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("Get", out var get) ||
            !get.TryGetProperty(collectionName, out var objects) ||
            objects.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var matches = new List<WeaviateRawMatch>();
        var rank = 1;
        foreach (var item in objects.EnumerateArray())
        {
            var distance = item.TryGetProperty("_additional", out var additional) &&
                           additional.TryGetProperty("distance", out var distanceElement) &&
                           distanceElement.TryGetDouble(out var parsedDistance)
                ? parsedDistance
                : 1d;

            matches.Add(new WeaviateRawMatch
            {
                RawWeaviateRank = rank++,
                ChunkId = ParseGuidProperty(item, "chunkId"),
                ArtefactId = ParseGuidProperty(item, "artefactId"),
                TenantId = ReadIntProperty(item, "tenantId"),
                ProjectId = ReadIntProperty(item, "projectId"),
                SourceEntityType = ReadStringProperty(item, "sourceEntityType"),
                SourceEntityId = ReadStringProperty(item, "sourceEntityId"),
                SourceVersionId = ReadStringProperty(item, "sourceVersionId"),
                Title = ReadStringProperty(item, "title"),
                IsStale = item.TryGetProperty("isStale", out var staleElement) && staleElement.ValueKind == JsonValueKind.True,
                Distance = distance,
                VectorSimilarity = Math.Clamp(1d - distance, 0d, 1d)
            });
        }

        return matches;
    }

    internal static async Task<CliProjectContext?> ResolveProjectAsync(IDbConnectionFactory connectionFactory, string projectName)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<CliProjectContext?>(new CommandDefinition(
            """
            SELECT TOP (1)
                Id AS ProjectId,
                TenantId,
                Name AS ProjectName
            FROM dbo.Projects
            WHERE Name = @ProjectName OR Name = @FallbackProjectName
            ORDER BY CASE WHEN Name = @ProjectName THEN 0 ELSE 1 END, Id;
            """,
            new
            {
                ProjectName = projectName,
                FallbackProjectName = projectName == "IronDev" ? "IronDeveloper" : "IronDev"
            }));
    }

    internal static async Task ApplySqlScriptAsync(IDbConnectionFactory connectionFactory, string scriptPath)
    {
        if (!File.Exists(scriptPath))
            return;

        var script = await File.ReadAllTextAsync(scriptPath);
        var batches = script
            .Split(["\r\nGO\r\n", "\nGO\n", "\r\nGO\n", "\nGO\r\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(batch => batch.Trim())
            .Where(batch => !string.IsNullOrWhiteSpace(batch) && !batch.StartsWith("USE ", StringComparison.OrdinalIgnoreCase));

        using var connection = connectionFactory.CreateConnection();
        foreach (var batch in batches)
            await connection.ExecuteAsync(batch);
    }

    internal static string ResolveIronDevConnectionString(string[] args)
    {
        var explicitConnection = ReadOption(args, "--connection-string");
        if (!string.IsNullOrWhiteSpace(explicitConnection))
            return explicitConnection;

        var envConnection = Environment.GetEnvironmentVariable("IRONDEV_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(envConnection))
            return envConnection;

        var repoRoot = FindRepositoryRoot();
        foreach (var path in new[]
        {
            Path.Combine(repoRoot, "IronDeveloper", "appsettings.Development.json"),
            Path.Combine(repoRoot, "IronDeveloper", "appsettings.json")
        })
        {
            var connection = TryReadConnectionString(path, "IronDeveloperDb");
            if (!string.IsNullOrWhiteSpace(connection))
                return connection;
        }

        throw new InvalidOperationException("Could not resolve IronDeveloperDb connection string.");
    }

    internal static string ResolveWeaviateEndpoint()
    {
        var envEndpoint = Environment.GetEnvironmentVariable("IRONDEV_WEAVIATE_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(envEndpoint))
            return envEndpoint;

        var repoRoot = FindRepositoryRoot();
        foreach (var path in new[]
        {
            Path.Combine(repoRoot, "IronDeveloper", "appsettings.Development.json"),
            Path.Combine(repoRoot, "IronDeveloper", "appsettings.json")
        })
        {
            var endpoint = TryReadNestedString(path, "Weaviate", "Endpoint");
            if (!string.IsNullOrWhiteSpace(endpoint))
                return endpoint;
        }

        return "http://localhost:8080";
    }

    internal static string BuildWeaviateDogfoodCollectionName(string dogfoodRunId)
    {
        var hash = ComputeSha256(dogfoodRunId)[..12];
        return $"IronDevDogfoodMemoryChunks{hash}";
    }

    internal static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    internal static string? ReadOption(string[] args, string optionName)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return string.Empty;

            return args[i + 1];
        }

        return null;
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient httpClient, string requestUri, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await httpClient.PostAsync(requestUri, content);
    }

    private static async Task<HttpResponseMessage> PutJsonAsync(HttpClient httpClient, string requestUri, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await httpClient.PutAsync(requestUri, content);
    }

    private static Guid ParseGuidProperty(JsonElement item, string propertyName)
        => Guid.TryParse(ReadStringProperty(item, propertyName), out var value) ? value : Guid.Empty;

    private static int ReadIntProperty(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed))
            return parsed;

        return int.TryParse(value.ToString(), out parsed) ? parsed : 0;
    }

    private static string ReadStringProperty(JsonElement item, string propertyName)
        => item.TryGetProperty(propertyName, out var value) ? value.ToString() : string.Empty;

    private static string? TryReadConnectionString(string path, string name)
    {
        if (!File.Exists(path))
            return null;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings) &&
            connectionStrings.TryGetProperty(name, out var value))
        {
            return value.GetString();
        }

        return null;
    }

    private static string? TryReadNestedString(string path, string sectionName, string propertyName)
    {
        if (!File.Exists(path))
            return null;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.TryGetProperty(sectionName, out var section) &&
            section.TryGetProperty(propertyName, out var value))
        {
            return value.GetString();
        }

        return null;
    }

    private static string ComputeSha256(string text)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }
}
