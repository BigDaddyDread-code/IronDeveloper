# Local Development Setup

This guide helps you set up a local development environment for IronDev.

## Prerequisites

1.  **SQL Server**: LocalDB, SQL Express, or a full SQL Server instance.
2.  **AI Provider**: Access to OpenAI, a local OpenAI-compatible API (e.g., vLLM, LMStudio), or Ollama.
3.  **Docker Desktop**: Optional, only needed for local Weaviate semantic-memory dogfooding.

---

## Quick Bootstrap

Start with the developer environment doctor from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\local\doctor-local.ps1
```

The doctor is diagnostic only. It reports repository, toolchain, frontend, local override, SQL, Weaviate, LocalTest, API/UI, root-safety, smoke-path, and next-safe-action status where possible. It does not create local files, start services, create or rebuild SQL, ensure or rebuild Weaviate, reset LocalTest data, run smoke, write evidence, approve anything, mutate source, or claim alpha/release readiness.

Use JSON output when another local script needs a stable diagnostic model:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\local\doctor-local.ps1 -Json
```

After the doctor has reduced the problem to one next safe action, use the safe local bootstrap check when the next step is local setup:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\local\bootstrap-local.ps1 -CheckOnly
```

The default mode is check-only. It verifies the repository shape, tool presence, local override status, J08 config-summary contract availability, and J10 root-safety availability without creating files, restoring packages, installing frontend packages, starting services, touching SQL, touching Weaviate, writing evidence, or running governed product flows.

To prepare only the developer-local override template:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\local\bootstrap-local.ps1 -Prepare -CreateLocalOverride
```

To explicitly restore .NET packages:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\local\bootstrap-local.ps1 -Prepare -RestoreDotNet
```

To explicitly install frontend packages without starting the UI:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\local\bootstrap-local.ps1 -Prepare -InstallFrontend
```

Supported J04 switches:

```text
-CheckOnly
-Prepare
-CreateLocalOverride
-RestoreDotNet
-InstallFrontend
-NonInteractive
-Verbose
```

The local bootstrap script prepares local convenience. It is not evidence, approval, root safety proof, policy satisfaction, or permission to mutate source, SQL, Weaviate, evidence, or sandbox repositories.

The older `Scripts/setup-local-dev.ps1` remains a higher-power local setup helper for developers who intentionally want restore/build/smoke behavior. Prefer the J04 script first when you want a non-destructive setup check.

Database setup remains separate so a bootstrap command cannot accidentally reseed an existing database. After the J04 check, use the guarded J05 command explicitly when you want to create or rebuild a developer-local SQL database:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\local\sql-local.ps1 -CheckOnly
```

Create a local database if missing:

```text
powershell -ExecutionPolicy Bypass -File .\Scripts\local\sql-local.ps1 `
  -Create `
  -ServerInstance "(localdb)\MSSQLLocalDB" `
  -DatabaseName "IronDeveloper_Local"
```

Create and apply the local setup script:

```text
powershell -ExecutionPolicy Bypass -File .\Scripts\local\sql-local.ps1 `
  -Create `
  -ApplyLocalDevSetup `
  -ServerInstance "(localdb)\MSSQLLocalDB" `
  -DatabaseName "IronDeveloper_Local"
```

Rebuild requires an exact confirmation phrase:

```text
powershell -ExecutionPolicy Bypass -File .\Scripts\local\sql-local.ps1 `
  -Rebuild `
  -ApplyLocalDevSetup `
  -ServerInstance "(localdb)\MSSQLLocalDB" `
  -DatabaseName "IronDeveloper_Local" `
  -ConfirmRebuild "REBUILD IronDeveloper_Local"
```

Local Weaviate setup is also separate. J06 checks or prepares only developer-local Weaviate collections and never starts Docker, starts Weaviate, loads demo vectors, runs alpha smoke, writes evidence, or claims readiness:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\local\weaviate-local.ps1 -CheckOnly
```

Ensure the local schema against an already-running loopback Weaviate instance:

```text
powershell -ExecutionPolicy Bypass -File .\Scripts\local\weaviate-local.ps1 `
  -EnsureSchema `
  -Endpoint http://localhost:8080 `
  -CollectionName IronDeveloper_Local
```

Rebuild requires an exact collection confirmation phrase:

```text
powershell -ExecutionPolicy Bypass -File .\Scripts\local\weaviate-local.ps1 `
  -Rebuild `
  -Endpoint http://localhost:8080 `
  -CollectionName IronDeveloper_Local `
  -ConfirmRebuild "REBUILD IronDeveloper_Local"
```

Local Weaviate state is a disposable derived index. Rebuilding it is setup convenience, not authority, approval, evidence, alpha readiness, release readiness, or product truth.

---

## 1. Local Database Setup

1.  **Create Database**: Create a new database named `IronDeveloper`.
2.  **Run Setup Script**: Execute `Database/local_dev_setup.sql` against the `IronDeveloper` database.
    *   This script creates all required tables and seeds a default local tenant, user, and project.
3.  **Connection String**: The committed development and test settings use a generic LocalDB example. Do not commit machine-specific SQL Server names, usernames, passwords, or local paths. If your SQL Server is different, set a local environment variable, API user secret, or ignored API local override instead:

```json
{
  "ConnectionStrings": {
    "IronDeveloperDb": "Server=(localdb)\\MSSQLLocalDB;Database=IronDeveloper;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"
  }
}
```

PowerShell environment override:

```powershell
$env:ConnectionStrings__IronDeveloperDb = "Server=(localdb)\MSSQLLocalDB;Database=IronDeveloper;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"
```

API user secret override:

```powershell
dotnet user-secrets set "ConnectionStrings:IronDeveloperDb" "Server=(localdb)\MSSQLLocalDB;Database=IronDeveloper;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;" --project .\IronDev.Api\IronDev.Api.csproj
```

---

## 2. Development Local Override

The API can load an ignored developer-only override file when the host environment is `Development`:

```text
IronDev.Api/appsettings.Development.Local.json
```

This file is optional. If it is absent, startup behavior is unchanged. It is ignored by git and must stay untracked.

Use it for machine-specific development settings such as local SQL, AI provider, Weaviate endpoints, workspace roots, and evidence roots. Do not use it for CI, production, LocalTest, Test, shared defaults, bootstrap, schema changes, evidence, approval, source apply, release, deployment, or workflow behavior.

Configuration precedence for the API host is:

1. `appsettings.json`
2. `appsettings.Development.json`
3. `appsettings.Development.Local.json`
4. API user secrets, when available
5. environment variables
6. command-line arguments

That means environment variables and CI/runtime secrets still override the local file.

Start from the placeholder-only example if you want a template:

```powershell
Copy-Item .\IronDev.Api\appsettings.Development.Local.example.json .\IronDev.Api\appsettings.Development.Local.json
```

Example shape:

```json
{
  "ConnectionStrings": {
    "IronDeveloperDb": ""
  },
  "Ai": {
    "Provider": "",
    "Model": "",
    "ApiKey": ""
  },
  "Weaviate": {
    "Enabled": false,
    "HttpEndpoint": "",
    "GrpcEndpoint": ""
  }
}
```

Keep real values on your own machine only. A local override file is convenience, not shared configuration, not evidence, not authority, and not a runtime contract.

---

## 3. Redacted Configuration Summary

J08 adds a Core-only redacted configuration summary contract for local/development diagnostics. The summary can report safe derived metadata such as:

- which configuration sources are present or loaded
- whether SQL configuration is present
- SQL provider shape, database name, server kind, authentication mode, and whether a credential key exists
- AI provider/model status and base URL host/port only
- Weaviate enabled/auth status and endpoint host/port only
- local root configuration status with redacted user paths
- feature flag state as status only

The summary never prints raw connection strings, API keys, JWT keys, token values, authorization headers, local override contents, or full user-local paths. For example, user-profile roots such as a Windows `.irondev\workspaces` folder or a Linux `.irondev/evidence` folder are summarized with the user segment redacted.

Because J10 root-safety validation is not in this slice, root entries report `NotEvaluated` unless a future root-safety validator result is supplied. The summary does not validate roots by itself.

Boundary: a config summary is diagnostic evidence for a human. It is not approval, authority, policy satisfaction, root safety proof, or permission to mutate anything.

If redaction is uncertain, the value is redacted. Debug convenience loses to secret safety.

---

## 4. Configure AI Provider

IronDev supports multiple AI providers. Configure your choice in `IronDev.Api/appsettings.Development.Local.json`, API user secrets, or environment variables. Keep committed `appsettings.Development.json` generic.

### Option A: OpenAI (Default)
```json
"Ai": {
  "Provider": "",
  "Model": "",
  "ApiKey": ""
}
```

### Option B: Local OpenAI-Compatible (vLLM, LMStudio, Ollama v1)
```json
"Ai": {
  "Provider": "",
  "Model": "",
  "BaseUrl": "",
  "ApiKey": ""
}
```

### Option C: Ollama (Native API)
```json
"Ai": {
  "Provider": "",
  "Model": "",
  "BaseUrl": ""
}
```

---

## 5. Optional Weaviate Semantic Memory

Weaviate is optional for local development. IronDev defaults to safe startup with Weaviate disabled; use `IronDev.Api/appsettings.Development.Local.json`, API user secrets, or environment variables when you want to enable it on your machine.

Start the local container:

```powershell
.\Scripts\weaviate-dev.ps1 up
```

If local script execution is blocked:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\weaviate-dev.ps1 up
```

Run a smoke test:

```powershell
.\Scripts\weaviate-dev.ps1 smoke
```

Expected local endpoints:

```text
HTTP: http://localhost:8080
gRPC: localhost:50051
```

For full setup and settings, see [Docs/weaviate-local-setup.md](weaviate-local-setup.md).

---

## 6. Build & Launch

### Main Application
Build the solution:
```powershell
dotnet build IronDev.slnx
```

### Launch
Run the API from the repo root:
```powershell
dotnet run --project IronDev.Api --urls http://localhost:5000
```

Run the Tauri shell in a second terminal:
```powershell
cd IronDev.TauriShell
npm install
npm run dev
```

---

## 7. Login & First Steps

1.  **Login**:
    *   **Email**: `bob@irondev.local`
    *   **Password**: `change-me-local-only`
2.  **Open Project**: Select the seeded IronDev project.
3.  **Index Project**: Use API-backed indexing/product commands before asking code-aware questions.

---

## 8. Running Tests

### Integration Tests
SQL-backed integration tests use the generic LocalDB test default in `appsettings.Test.json`. Use `ConnectionStrings__IronDeveloperDb` for machine-specific overrides instead of committing local config edits.
```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --logger "console;verbosity=minimal"
```

### API Tests
```powershell
dotnet test IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj --logger "console;verbosity=minimal"
```
