# Database Schema Ownership

**Status:** Canonical cleanup contract

**Programme slice:** CLN-19

## Ownership Rule

Core SQL schema is owned by the ordered migration manifest. API controllers and runtime services may query and mutate migrated records; they must not create or alter core tables during startup or a user request.

The controlled migration `Database/migrate_runtime_schema_ownership.sql` now owns schema previously created by runtime services:

- Runs and run events;
- project context documents and artifact source references;
- ticket compatibility columns;
- semantic artefacts, chunks, embedding jobs, search traces, embeddings, and index-run metadata.

The migration is idempotent and is applied by `Database/apply-migrations.ps1`. It preserves the existing service contract. Semantic search traces are derived diagnostics; only the unsupported pre-GUID trace shape is rebuilt.

## Runtime Failure

A missing table or column is a migration defect. Runtime code must surface the database failure or bounded readiness state; it must not repair production schema silently.

Weaviate collection initialization remains derived-index lifecycle, not SQL core-schema ownership. It grants no authority and SQL remains the source of truth.

## Enforcement

The StaticBoundary CI category scans production API and Infrastructure C# sources and rejects `CREATE TABLE` or `ALTER TABLE`. SQL inventory checks require the migration to be manifest-owned and verifier-owned.

## Authority

Applying a migration changes storage shape only. It grants no tenant access, project visibility, workflow authority, approval, continuation, apply, memory promotion, or release permission.
