# IronDev Ticket Source Of Truth

IronDev tickets are the canonical source of truth for product and build work inside IDA/IronDev.

GitHub issues are external references for coordination, PR discussion, and review evidence. They are not the canonical product backlog.

## Localhost Backend Contract

For local dogfooding, run the backend explicitly:

```powershell
dotnet run --project IronDev.Api
```

The CLI assumes the API is already running and writes through HTTP only:

```powershell
$env:IRONDEV_API_BASE_URL = "http://localhost:5000"
$env:IRONDEV_API_TOKEN = "<tenant-scoped-jwt>"

dotnet run --project .\tools\IronDev.Cli\IronDev.Cli.csproj -- ticket create `
  --project-id 1 `
  --file .\tools\dogfood\tickets\examples\source-of-truth-ticket.json `
  --json
```

The CLI checks `GET /health` before write commands. If the API is unavailable, it exits non-zero and prints:

```text
IronDev.Api is not reachable at http://localhost:5000.
Start it with:
dotnet run --project IronDev.Api
```

There is no direct SQL fallback, no Infrastructure service fallback, and no GitHub issue fallback.

## Configuration Order

API base URL resolution:

1. `--api-base-url`
2. `IRONDEV_API_BASE_URL`
3. `irondev.cli.json`
4. `http://localhost:5000`

Token resolution:

1. `--token`
2. `IRONDEV_API_TOKEN`
3. `irondev.cli.json`

Example config:

```json
{
  "IronDev": {
    "ApiBaseUrl": "http://localhost:5000",
    "ApiToken": "<tenant-scoped-jwt>"
  }
}
```

## Codex Ticket Rule

Create implementation tickets in IronDev/IDA, not as GitHub issues, unless explicitly asked to create a GitHub issue.

IronDev/IDA owns project tickets, implementation plans, decisions, discussion documents, build workflow state, traceability, and project memory.
