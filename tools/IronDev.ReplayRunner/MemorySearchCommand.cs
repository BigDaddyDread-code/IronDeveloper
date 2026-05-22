using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.SemanticMemory;

public static class MemorySearchCommand
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var query = ReadOption(args, "--query") ?? ReadPositionalText(args, 2);
        if (string.IsNullOrWhiteSpace(query))
        {
            Console.Error.WriteLine("Usage: IronDev.ReplayRunner memory search <query> [--project IronDev] [--take 5] [--json]");
            return 2;
        }

        var projectName = ReadOption(args, "--project") ?? "IronDev";
        var take = int.TryParse(ReadOption(args, "--take"), out var parsedTake) ? parsedTake : 5;
        var dogfoodRunId = ReadOption(args, "--dogfood-run-id") ?? $"memory-search-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var docsRoot = EnsureKnowledgeDocsRoot(GetDogfoodKnowledgeStore(args), projectName);
        var documents = await LoadDocumentsAsync(projectName, docsRoot);
        var endpoint = ReadOption(args, "--weaviate-endpoint") ?? ResolveWeaviateEndpoint();

        using var httpClient = new HttpClient { BaseAddress = new Uri(endpoint.TrimEnd('/') + "/") };
        var collectionName = BuildWeaviateDogfoodCollectionName($"codex-memory-{Slugify(projectName)}-{Slugify(query)}-{dogfoodRunId}");
        await EnsureWeaviateDogfoodCollectionAsync(httpClient, collectionName);
        await UpsertDocumentsAsync(httpClient, collectionName, projectName, documents);

        var relevantRawMatches = await SearchRawMatchesAsync(httpClient, collectionName, projectName, query, take, documents);
        var candidates = BuildCandidates(projectName, query, documents, relevantRawMatches);
        var traceId = Guid.NewGuid();
        var ranked = RankCandidates(projectName, query, take, documents, candidates);
        var result = BuildResult(query, projectName, endpoint, collectionName, dogfoodRunId, traceId, ranked, relevantRawMatches, documents);

        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return 0;
    }

    private static async Task<Dictionary<string, (KnowledgeDocument Document, string Body)>> LoadDocumentsAsync(
        string projectName,
        string docsRoot)
    {
        var index = await BuildKnowledgeIndexAsync(projectName, docsRoot);
        var documents = new Dictionary<string, (KnowledgeDocument Document, string Body)>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in index.Where(document => string.Equals(document.Project, projectName, StringComparison.OrdinalIgnoreCase)))
        {
            var text = await File.ReadAllTextAsync(document.Path);
            documents[document.Id] = (document, StripFrontmatter(text));
        }

        return documents;
    }

    private static async Task UpsertDocumentsAsync(
        HttpClient httpClient,
        string collectionName,
        string projectName,
        IReadOnlyDictionary<string, (KnowledgeDocument Document, string Body)> documents)
    {
        foreach (var item in documents.Values)
        {
            var doc = item.Document;
            var artefact = BuildKnowledgeArtefact(projectName, doc, item.Body);
            var chunk = BuildKnowledgeChunk(artefact, item.Body);
            await UpsertWeaviateChunkAsync(
                httpClient,
                collectionName,
                DeterministicGuid($"memory-search:{doc.Id}"),
                artefact,
                chunk,
                tenantId: 0,
                isStale: IsKnowledgeDocumentStale(doc),
                vector: BuildLexicalVector($"{doc.Title} {doc.DocumentType} {doc.Authority} {item.Body}"));
        }
    }

    private static async Task<List<WeaviateRawMatch>> SearchRawMatchesAsync(
        HttpClient httpClient,
        string collectionName,
        string projectName,
        string query,
        int take,
        IReadOnlyDictionary<string, (KnowledgeDocument Document, string Body)> documents)
    {
        var rawMatches = await QueryWeaviateDogfoodCollectionAsync(
            httpClient,
            collectionName,
            BuildLexicalVector(query),
            limit: Math.Max(take * 3, take));

        return rawMatches
            .Where(match => match.ProjectId == StableProjectId(projectName))
            .Where(match => documents.ContainsKey(match.SourceEntityId))
            .ToList();
    }

    private static List<SemanticSearchCandidate> BuildCandidates(
        string projectName,
        string query,
        IReadOnlyDictionary<string, (KnowledgeDocument Document, string Body)> documents,
        IReadOnlyList<WeaviateRawMatch> relevantRawMatches)
    {
        return relevantRawMatches.Select(match =>
        {
            var (document, body) = documents[match.SourceEntityId];
            var artefact = BuildKnowledgeArtefact(projectName, document, body);
            var chunk = BuildKnowledgeChunk(artefact, body, match.ChunkId);

            return new SemanticSearchCandidate
            {
                Document = new ProjectContextDocument
                {
                    Id = StableLongId(document.Id),
                    TenantId = 0,
                    ProjectId = StableProjectId(projectName),
                    DocumentType = document.DocumentType,
                    AuthorityLevel = document.Authority,
                    Title = document.Title,
                    Content = body,
                    Summary = $"Dogfood knowledge imported from {document.Source}",
                    Source = document.Source,
                    CreatedDate = document.CreatedUtc.UtcDateTime,
                    UpdatedDate = document.CreatedUtc.UtcDateTime
                },
                Artefact = artefact,
                Chunk = chunk,
                VectorSimilarity = Math.Min(1.0, match.VectorSimilarity + GetTitleOverlapBoost(document.Title, query)),
                ContentHashMismatch = false
            };
        }).ToList();
    }

    private static IReadOnlyList<SemanticSearchResult> RankCandidates(
        string projectName,
        string query,
        int take,
        IReadOnlyDictionary<string, (KnowledgeDocument Document, string Body)> documents,
        IReadOnlyList<SemanticSearchCandidate> candidates)
    {
        var boostedArtefactIds = documents.Values
            .Where(item => HasStrongTitleMatch(item.Document.Title, query))
            .Select(item => BuildKnowledgeArtefact(projectName, item.Document, item.Body).Id)
            .ToArray();

        return new SemanticRankingService().Rank(new SemanticSearchQuery
        {
            ProjectId = StableProjectId(projectName),
            QueryText = query,
            Limit = take,
            IncludeStale = false,
            Consumer = "CodexMemorySearch",
            BoostedArtefactIds = boostedArtefactIds
        }, candidates);
    }

    private static CodexMemorySearchResult BuildResult(
        string query,
        string projectName,
        string endpoint,
        string collectionName,
        string dogfoodRunId,
        Guid traceId,
        IReadOnlyList<SemanticSearchResult> ranked,
        IReadOnlyList<WeaviateRawMatch> relevantRawMatches,
        IReadOnlyDictionary<string, (KnowledgeDocument Document, string Body)> documents)
    {
        return new CodexMemorySearchResult
        {
            Query = query,
            Project = new CodexMemorySearchProject
            {
                Id = StableProjectId(projectName),
                Name = projectName
            },
            WeaviateEndpoint = endpoint,
            WeaviateCollection = collectionName,
            SemanticTraceId = traceId.ToString(),
            DogfoodRunId = dogfoodRunId,
            Matches = ranked.Select((match, index) =>
            {
                var raw = relevantRawMatches.FirstOrDefault(raw => raw.SourceEntityId == match.SourceEntityId);
                var document = documents[match.SourceEntityId].Document;
                return new CodexMemorySearchMatch
                {
                    DocumentTitle = match.Title,
                    DocumentId = document.Id,
                    DocumentVersionId = document.Id,
                    SourceEntityType = "DogfoodKnowledgeDocument",
                    SourceEntityId = document.Id,
                    RawWeaviateRank = raw?.RawWeaviateRank,
                    RawVectorScore = raw?.VectorSimilarity ?? match.VectorSimilarity,
                    FinalIronDevRank = index + 1,
                    FinalAuthorityScore = match.FinalScore,
                    AuthorityLevel = document.Authority,
                    CurrentStatus = match.IsStale ? "Stale" : "Current",
                    SourceLinks = [document.Source, document.Path],
                    Excerpt = match.Snippet,
                    SemanticTraceId = traceId.ToString(),
                    MatchReason = match.MatchReason
                };
            }).ToArray()
        };
    }

    private static async Task EnsureWeaviateDogfoodCollectionAsync(HttpClient httpClient, string collectionName)
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

    private static async Task UpsertWeaviateChunkAsync(
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

    private static async Task<IReadOnlyList<WeaviateRawMatch>> QueryWeaviateDogfoodCollectionAsync(
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

    private static string BuildWeaviateDogfoodCollectionName(string dogfoodRunId)
    {
        var hash = ComputeSha256(dogfoodRunId)[..12];
        return $"IronDevDogfoodMemoryChunks{hash}";
    }

    private static string ResolveWeaviateEndpoint()
    {
        var envEndpoint = Environment.GetEnvironmentVariable("IRONDEV_WEAVIATE_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(envEndpoint))
            return envEndpoint;

        foreach (var path in new[]
        {
            Path.Combine(FindRepositoryRoot(), "IronDeveloper", "appsettings.Development.json"),
            Path.Combine(FindRepositoryRoot(), "IronDeveloper", "appsettings.json")
        })
        {
            var endpoint = TryReadNestedString(path, "SemanticMemory", "WeaviateEndpoint") ??
                           TryReadNestedString(path, "Weaviate", "Endpoint");
            if (!string.IsNullOrWhiteSpace(endpoint))
                return endpoint;
        }

        return "http://localhost:8080";
    }

    private static string GetDogfoodKnowledgeStore(string[] args)
        => Path.GetFullPath(ReadOption(args, "--store-root") ?? Path.Combine("tools", "dogfood", "knowledge"));

    private static string EnsureKnowledgeDocsRoot(string storeRoot, string project)
    {
        var docsRoot = Path.Combine(storeRoot, Slugify(project), "docs");
        Directory.CreateDirectory(docsRoot);
        return docsRoot;
    }

    private static SemanticArtefact BuildKnowledgeArtefact(string projectName, KnowledgeDocument document, string body)
    {
        var id = DeterministicGuid($"artefact:{document.Id}");
        return new SemanticArtefact
        {
            Id = id,
            TenantId = 0,
            ProjectId = StableProjectId(projectName),
            SourceEntityType = "DogfoodKnowledgeDocument",
            SourceEntityId = document.Id,
            SourceVersionId = document.Id,
            ArtefactType = document.DocumentType,
            AuthorityLevel = MapKnowledgeAuthorityLevel(document),
            Title = document.Title,
            Summary = $"Dogfood knowledge imported from {document.Source}",
            ContentHash = ComputeSha256(body),
            IsStale = IsKnowledgeDocumentStale(document),
            CreatedUtc = document.CreatedUtc.UtcDateTime,
            UpdatedUtc = document.CreatedUtc.UtcDateTime
        };
    }

    private static SemanticChunk BuildKnowledgeChunk(SemanticArtefact artefact, string body, Guid? chunkId = null)
        => new()
        {
            Id = chunkId ?? DeterministicGuid($"chunk:{artefact.SourceEntityId}"),
            ArtefactId = artefact.Id,
            ProjectId = artefact.ProjectId,
            ChunkIndex = 0,
            ChunkText = body,
            TokenEstimate = Math.Max(1, body.Length / 4),
            ContentHash = artefact.ContentHash,
            IsStale = artefact.IsStale
        };

    private static bool IsKnowledgeDocumentStale(KnowledgeDocument document)
        => document.Authority.Contains("Superseded", StringComparison.OrdinalIgnoreCase) ||
           document.Authority.Contains("Stale", StringComparison.OrdinalIgnoreCase) ||
           document.Title.Contains("superseded", StringComparison.OrdinalIgnoreCase);

    private static string MapKnowledgeAuthorityLevel(KnowledgeDocument document)
    {
        if (document.Authority.Contains("Accepted", StringComparison.OrdinalIgnoreCase) &&
            document.DocumentType.Contains("Architecture", StringComparison.OrdinalIgnoreCase))
            return "AcceptedArchitecture";

        if (document.Authority.Contains("Accepted", StringComparison.OrdinalIgnoreCase))
            return "AcceptedRequirement";

        if (document.Authority.Contains("Resolved", StringComparison.OrdinalIgnoreCase))
            return "ResolvedKnowledge";

        if (document.Authority.Contains("Draft", StringComparison.OrdinalIgnoreCase) ||
            document.Authority.Contains("Working", StringComparison.OrdinalIgnoreCase))
            return "ChatSummary";

        return document.Authority;
    }

    private static bool HasTitleTermOverlap(string title, string query)
    {
        var terms = query
            .Split([' ', '\t', '\r', '\n', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length >= 4)
            .ToArray();

        return terms.Any(term => title.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasStrongTitleMatch(string title, string query)
    {
        var normalizedTitle = NormalizeTitleForMatch(title);
        var normalizedQuery = NormalizeTitleForMatch(query);
        if (string.IsNullOrWhiteSpace(normalizedTitle) || string.IsNullOrWhiteSpace(normalizedQuery))
            return false;

        if (normalizedQuery.Contains(normalizedTitle, StringComparison.OrdinalIgnoreCase) ||
            normalizedTitle.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var titleTerms = normalizedTitle
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return titleTerms.Length > 0 &&
               titleTerms.All(term => normalizedQuery.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static double GetTitleOverlapBoost(string title, string query)
    {
        var terms = query
            .Split([' ', '\t', '\r', '\n', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var hits = terms.Count(term => title.Contains(term, StringComparison.OrdinalIgnoreCase));
        return Math.Min(0.60, hits * 0.25);
    }

    private static string NormalizeTitleForMatch(string value)
    {
        var chars = value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray();

        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static IReadOnlyList<double> BuildLexicalVector(string text, int dimensions = 32)
    {
        var vector = new double[dimensions];
        foreach (var rawTerm in text.Split([' ', '\t', '\r', '\n', '.', ',', ';', ':', '/', '\\', '|', '-', '_', '`', '"', '\'', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var hash = StablePositiveHash(rawTerm.ToLowerInvariant());
            vector[hash % dimensions] += 1.0;
        }

        var magnitude = Math.Sqrt(vector.Sum(value => value * value));
        if (magnitude <= 0)
            return vector;

        for (var i = 0; i < vector.Length; i++)
            vector[i] /= magnitude;

        return vector;
    }

    private static Guid DeterministicGuid(string value)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes);
    }

    private static int StableProjectId(string projectName)
        => StablePositiveHash(projectName) % 100_000 + 1;

    private static long StableLongId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Math.Abs(BitConverter.ToInt64(bytes, 0));
    }

    private static int StablePositiveHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToInt32(bytes, 0) & int.MaxValue;
    }

    private static async Task<List<KnowledgeDocument>> BuildKnowledgeIndexAsync(string project, string docsRoot)
    {
        var documents = new List<KnowledgeDocument>();
        if (!Directory.Exists(docsRoot))
            return documents;

        foreach (var file in Directory.GetFiles(docsRoot, "*.md", SearchOption.TopDirectoryOnly))
        {
            var text = await File.ReadAllTextAsync(file);
            var frontmatter = ParseFrontmatter(text);
            var created = DateTimeOffset.TryParse(GetMeta(frontmatter, "created_utc"), out var parsedCreated)
                ? parsedCreated
                : File.GetCreationTimeUtc(file);

            documents.Add(new KnowledgeDocument
            {
                Id = GetMeta(frontmatter, "id", Path.GetFileNameWithoutExtension(file)),
                Project = GetMeta(frontmatter, "project", project),
                Title = GetMeta(frontmatter, "title", Path.GetFileNameWithoutExtension(file)),
                DocumentType = GetMeta(frontmatter, "document_type", "Discussion"),
                Authority = GetMeta(frontmatter, "authority", "Draft"),
                Source = GetMeta(frontmatter, "source", "Unknown"),
                DogfoodRunId = GetMeta(frontmatter, "dogfood_run_id", string.Empty),
                Path = Path.GetFullPath(file),
                CreatedUtc = created
            });
        }

        return documents
            .OrderByDescending(document => document.CreatedUtc)
            .ThenBy(document => document.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string> ParseFrontmatter(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(text);
        if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
            return result;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(line, "---", StringComparison.Ordinal))
                break;

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
                continue;

            result[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return result;
    }

    private static string StripFrontmatter(string text)
    {
        using var reader = new StringReader(text);
        if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
            return text;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(line, "---", StringComparison.Ordinal))
                return reader.ReadToEnd().Trim();
        }

        return text;
    }

    private static string GetMeta(IReadOnlyDictionary<string, string> metadata, string key, string fallback = "")
        => metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static string Slugify(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var compact = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(compact) ? "document" : compact[..Math.Min(compact.Length, 72)];
    }

    private static string? ReadOption(string[] args, string optionName)
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

    private static string ReadPositionalText(string[] args, int startIndex)
    {
        var parts = new List<string>();
        for (var i = startIndex; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
                break;

            parts.Add(args[i]);
        }

        return string.Join(' ', parts).Trim();
    }

    private static string? TryReadNestedString(string path, string sectionName, string propertyName)
    {
        if (!File.Exists(path))
            return null;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty(sectionName, out var section) &&
               section.TryGetProperty(propertyName, out var value)
            ? value.GetString()
            : null;
    }

    private static string FindRepositoryRoot()
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

    private static string ComputeSha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }
}
