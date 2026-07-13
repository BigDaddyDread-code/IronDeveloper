# Bounded Operational Diagnostics

**Status:** Canonical operations contract

`GET /api/v1/operations/health` and its existing backend/dependency views return safe status signals for the API process, database, model provider, vector provider, workspace root, Git, disk capacity, migration state, and background/reindex state.

Diagnostics expose status and safe summaries only. They do not expose connection strings, credentials, provider keys, absolute workspace paths, exact disk figures, job payloads, prompts, model responses, or command output. Provider configuration is not provider reachability. Until live migration-state and background-job evidence authorities exist, missing state reports `NotConfigured` and configuration-declared state reports `Degraded`; neither is promoted to available evidence. A configured migration state does not prove, run, or approve a migration.

The endpoint is read-only. Healthy or degraded status does not create approval, policy satisfaction, release readiness, repair authority, restart authority, migration authority, cleanup authority, or workflow authority.
