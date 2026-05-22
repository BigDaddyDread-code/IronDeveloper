---
id: 20260522004256981-weaviate-local-setup
project: IronDev
title: weaviate-local-setup
document_type: Guide
authority: WorkingDraft
source: C:\Users\bob\source\repos\AIDeveloper\Docs\weaviate-local-setup.md
dogfood_run_id: DogfoodDocsSeed-20260522-012
created_utc: 2026-05-22T00:42:56.9955657+00:00
---

# Weaviate Local Setup

IronDev uses SQL Server as the canonical project-memory store. Weaviate is only the semantic retrieval index for searchable chunks and can be deleted and rebuilt from SQL.

The local development setup runs Weaviate in Docker with bring-your-own vectors. IronDev creates embeddings through `IEmbeddingProvider`; Weaviate stores and searches the vectors.

## Start Weaviate

From the repository root:

```powershell
.\Scripts\weaviate-dev.ps1 up
```

If PowerShell blocks local scripts, run the same command through a process-scoped bypass:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\weaviate-dev.ps1 up
```

This uses:

```text
docker-compose.weaviate.yml
Container: irondev-weaviate
HTTP:      http://localhost:8080
gRPC:      localhost:50051
Vectorizer: none
```

## Check Status

```powershell
.\Scripts\weaviate-dev.ps1 status
.\Scripts\weaviate-dev.ps1 ready
.\Scripts\weaviate-dev.ps1 meta
.\Scripts\weaviate-dev.ps1 schema
```

The ready check should return HTTP `200 OK`. An empty schema is normal before IronDev creates `IronDevContextChunks`.

## Smoke Test

Run:

```powershell
.\Scripts\weaviate-dev.ps1 smoke
```

Or, when script execution is blocked:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\weaviate-dev.ps1 smoke
```

The smoke test creates a temporary `IronDevMemorySmokeTest` collection with `vectorizer: none`, verifies it, then deletes it.

## Stop Weaviate

```powershell
.\Scripts\weaviate-dev.ps1 down
```

The Docker volume is preserved by default. To remove the volume manually, use Docker Desktop or:

```powershell
docker volume rm aideveloper_weaviate_data
```

Only do this if you want to wipe the local Weaviate index.

## IronDev Settings

Safe default in `IronDeveloper/appsettings.json`:

```json
"Weaviate": {
  "Enabled": false,
  "Endpoint": "http://localhost:8080",
  "GrpcPort": 50051,
  "CollectionPrefix": "IronDevContextChunks",
  "ApiKey": ""
}
```

Local development override in `IronDeveloper/appsettings.Development.json`:

```json
"Weaviate": {
  "Enabled": true,
  "Endpoint": "http://localhost:8080",
  "GrpcPort": 50051,
  "CollectionPrefix": "IronDevContextChunks",
  "ApiKey": ""
}
```

Keep `Embedding.Provider` as `Fake` for deterministic local smoke testing, or set it to `OpenAI` when testing real embeddings.

## App Dogfood Flow

1. Start Weaviate with `.\Scripts\weaviate-dev.ps1 up`.
2. Start IronDev with development settings.
3. Open the Knowledge Compiler workspace.
4. Use the Memory Health card to confirm provider/status.
5. Save or apply a project memory document.
6. Run `Rebuild Semantic Index`.
7. Use the semantic search panel to test retrieval.
8. Check the schema if needed:

```powershell
.\Scripts\weaviate-dev.ps1 schema
```

Expected collection:

```text
IronDevContextChunks
```

## Failure Mode

If Weaviate is disabled or unavailable, IronDev should still start and fall back to degraded semantic-memory status. The app default keeps `Weaviate.Enabled` false so Alpha startup is not blocked by Docker.