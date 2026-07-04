# Local Development Setup

This guide helps you set up a local development environment for IronDev.

## Prerequisites

1.  **SQL Server**: LocalDB, SQL Express, or a full SQL Server instance.
2.  **AI Provider**: Access to OpenAI, a local OpenAI-compatible API (e.g., vLLM, LMStudio), or Ollama.
3.  **Docker Desktop**: Optional, only needed for local Weaviate semantic-memory dogfooding.

---

## Quick Bootstrap

For a local machine that already has .NET and Docker Desktop installed, run this from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\setup-local-dev.ps1
```

This restores packages, starts/smoke-tests Weaviate, builds the API/product CLI, and runs focused boundary smoke tests.

Database setup is opt-in so the script does not accidentally reseed an existing database:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\setup-local-dev.ps1 -RunDatabaseSetup
```

Useful switches:

```text
-SkipWeaviate
-SkipBuild
-SkipTests
-RunDatabaseSetup
-SqlServerInstance "(localdb)\MSSQLLocalDB"
-DatabaseName "IronDeveloper"
```

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

## 3. Configure AI Provider

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

## 4. Optional Weaviate Semantic Memory

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

## 5. Build & Launch

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

## 6. Login & First Steps

1.  **Login**:
    *   **Email**: `bob@irondev.local`
    *   **Password**: `change-me-local-only`
2.  **Open Project**: Select the seeded IronDev project.
3.  **Index Project**: Use API-backed indexing/product commands before asking code-aware questions.

---

## 7. Running Tests

### Integration Tests
SQL-backed integration tests use the generic LocalDB test default in `appsettings.Test.json`. Use `ConnectionStrings__IronDeveloperDb` for machine-specific overrides instead of committing local config edits.
```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --logger "console;verbosity=minimal"
```

### API Tests
```powershell
dotnet test IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj --logger "console;verbosity=minimal"
```
