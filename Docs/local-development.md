# Local Development Setup

This guide helps you set up a local development environment for IronDev.

## Prerequisites

1.  **SQL Server**: LocalDB, SQL Express, or a full SQL Server instance.
2.  **AI Provider**: Access to OpenAI, a local OpenAI-compatible API (e.g., vLLM, LMStudio), or Ollama.
3.  **IronDeveloper.Controls**: The UI controls library must be cloned locally (currently source-referenced).

---

## 1. Local Database Setup

1.  **Create Database**: Create a new database named `IronDeveloper`.
2.  **Run Setup Script**: Execute `Database/local_dev_setup.sql` against the `IronDeveloper` database.
    *   This script creates all required tables and seeds a default local tenant, user, and project.
3.  **Connection String**: Update `IronDeveloper/appsettings.Development.json` with your connection string:

```json
{
  "ConnectionStrings": {
    "IronDeveloperDb": "Server=(localdb)\\MSSQLLocalDB;Database=IronDeveloper;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"
  }
}
```

---

## 2. Configure AI Provider

IronDev supports multiple AI providers. Configure your choice in `appsettings.Development.json`.

### Option A: OpenAI (Default)
```json
"Ai": {
  "Provider": "OpenAI",
  "Model": "gpt-4o",
  "ApiKey": "your-api-key-here"
}
```

### Option B: Local OpenAI-Compatible (vLLM, LMStudio, Ollama v1)
```json
"Ai": {
  "Provider": "LocalOpenAI",
  "Model": "qwen2.5-coder:latest",
  "BaseUrl": "http://localhost:11434/v1",
  "ApiKey": "local-dev-key"
}
```

### Option C: Ollama (Native API)
```json
"Ai": {
  "Provider": "Ollama",
  "Model": "qwen2.5-coder:latest",
  "BaseUrl": "http://localhost:11434"
}
```

---

## 3. Optional Weaviate Semantic Memory

Weaviate is optional for local development. IronDev defaults to safe startup with Weaviate disabled, while `IronDeveloper/appsettings.Development.json` can enable it when Docker is running.

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

## 4. Build & Launch

### Controls Library
The controls are currently source-referenced. Ensure the repo is cloned at the expected path (or update the `.slnx` if moved):
`C:\Users\bob\source\repos\IronDeveloper.Controls`

Build the controls:
```powershell
dotnet build ..\IronDeveloper.Controls\IronDeveloperControls\IronDeveloperControls.csproj
```

### Main Application
Build the solution:
```powershell
dotnet build IronDev.slnx
```

### Launch
Run the application from Visual Studio or via CLI:
```powershell
cd IronDeveloper
dotnet run
```

---

## 5. Login & First Steps

1.  **Login**:
    *   **Email**: `bob@irondev.local`
    *   **Password**: `change-me-local-only`
2.  **Open Project**: The `IronDeveloper` project is seeded automatically.
3.  **Index Project**: Click **Index Project** in the Hub or Ticket workspace to enable code-aware AI features.

---

## 6. Running Tests

### Integration Tests
Requires a local SQL Server instance (configured in `appsettings.Test.json`).
```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --logger "console;verbosity=minimal"
```

### API Tests
```powershell
dotnet test IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj --logger "console;verbosity=minimal"
```
