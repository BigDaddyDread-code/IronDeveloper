# C16 - CI Validation Artifact Retention

## Summary

C16 retains bounded CI validation evidence artifacts for the governance-boundary, SQL integration, and frontend contract lanes.

Artifacts preserve evidence. They do not approve the work.

## Boundary

CI artifacts preserve validation evidence. They do not grant authority, approval, policy satisfaction, execution permission, merge readiness, release readiness, deployment readiness, or workflow continuation.

A retained artifact is a receipt of what CI attempted or observed. It is not a decision to merge, release, deploy, apply source, or continue workflow.

C16 does not:

- change production runtime behavior
- change API behavior
- change SQL schema or stores
- change frontend runtime behavior
- change JWT/CORS/Weaviate/environment safety behavior
- change LocalTest or production safety behavior
- change auth/tenant/admin audit behavior
- change governance authority code
- approve work
- satisfy policy
- apply source
- execute rollback
- commit
- push
- create pull requests
- merge
- release
- deploy
- promote memory
- continue workflow

## Artifact Scope

Artifacts are bounded under:

- `artifacts/ci/governance-boundary`
- `artifacts/ci/sql-integration`
- `artifacts/ci/frontend-contract`

The workflows do not upload the repository root, `.git`, `bin`, `obj`, `node_modules`, whole workspaces, raw environment dumps, generated clients, temporary OpenAPI files, generated appsettings files, or SQL connection-string material.

## Retention Policy

Artifact uploads use:

- `actions/upload-artifact@v4`
- `if: always()` as part of the upload condition
- `retention-days: 14`
- `if-no-files-found: error`

The artifact names are stable and scoped by workflow, run id, and run attempt.

## Sanitization Rules

Each lane runs `Scripts/ci/test-ci-evidence-artifact-safety.ps1` before upload. The scan requires artifact paths under `artifacts/ci` and rejects forbidden directories and common secret markers.

Rejected markers include:

- `Password` followed by `=`
- `Pwd` followed by `=`
- `Bearer `
- `Authorization:`
- `Jwt:Key`
- `Weaviate:ApiKey`
- `OPENAI_API_KEY=`
- `IRONDEV_JWT_KEY=`
- `IRONDEV_WEAVIATE_API_KEY=`
- `sk-`
- `ghp_`
- `github_pat_`
- `BEGIN` followed by `PRIVATE KEY`

## Workflow Coverage

- Governance-boundary CI writes `evidence-summary.md` and `.trx` files under `test-results`.
- SQL integration CI writes `evidence-summary.md` and `.trx` files under `test-results`.
- Frontend contract CI writes `evidence-summary.md`, `frontend-contract-output.txt`, and `openapi-drift-summary.txt`.

## Permissions

The workflows keep:

```yaml
permissions:
  contents: read
```

C16 does not add `actions: write`, `contents: write`, `pull-requests: write`, `issues: write`, `deployments: write`, or `id-token: write`.

## Forbidden Upload Paths

Reject uploads of:

- repository root
- `.git`
- `bin`
- `obj`
- `node_modules`
- full workspace folders
- user profile folders
- temp dumps
- raw environment dumps
- generated appsettings files
- SQL connection string material
- generated frontend clients
- temporary OpenAPI output

## CI Lane

C16 adds artifact capture without broadening the existing CI filters.

The security lane still includes:

- C11 secret scanning
- C12 LocalTest safety
- C13 production environment safety
- C14 sensitive API rate-limit/auth boundary
- C15 auth/tenant/admin audit-log boundary

## Validation

- Focused C16 static artifact-retention tests: 14/14 passed.
- C06-C16 security boundary lane: 123/123 passed.
- Governance-boundary CI script: passed locally.
  - B-series profile boundary tests: 133/133 passed.
  - BQ-BU compatibility boundary tests: 80/80 passed.
  - Security boundary tests: 66/66 passed.
  - API boundary tests: 38/38 passed.
  - CLI boundary tests: 41/41 passed.
  - Local artifact safety scan passed for `artifacts/ci/governance-boundary`.
- SQL integration CI script: not run locally; requires a live CI SQL Server service and CI-scoped database environment.
- Frontend contract CI script: passed locally.
  - Local artifact safety scan passed for `artifacts/ci/frontend-contract`.
  - `npm audit` reported dependency findings; this did not fail the C16 artifact-retention lane.
- Build: `dotnet build IronDev.slnx --no-restore -v:minimal` passed with 0 errors and 4 existing warnings.
- `git diff --check`: pending final diff check.
- `git diff --cached --check`: pending final staged diff check.

GitHub artifact upload verification is pending until the C16 workflow runs on GitHub.

## Review Traps

Reject C16 if:

- artifacts upload the repository root
- artifacts upload `.git`
- artifacts upload `bin`, `obj`, `node_modules`, or whole workspace folders
- artifacts include raw environment dumps
- artifacts include SQL passwords or password-bearing connection strings
- artifacts include bearer tokens, JWT keys, API keys, private keys, or authorization headers
- workflow permissions are widened beyond read-only
- artifact upload runs only on success
- artifact upload silently succeeds with no files
- artifacts are retained indefinitely
- artifacts are described as approval, readiness, authority, or permission
- CI filters are widened
- SQL/frontend/governance behavior changes beyond evidence capture
- production runtime code changes
- source-apply/commit/push/release/deploy/memory/governance authority paths are touched

## Killjoy

A retained artifact is a receipt, not a permission slip.
