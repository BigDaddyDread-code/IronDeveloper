# C11 - Secret-Scanning Regression

## Summary

C11 adds a focused static regression guard for obvious committed secret material.

The guard scans committed text files for high-confidence secret regressions such as committed JWT signing keys, Weaviate API keys, provider tokens, bearer tokens, private-key blocks, and password-bearing connection strings.

## Boundary

Secret scanning is evidence hygiene only. A passing scan is not authentication, authorization, approval, policy satisfaction, execution permission, release readiness, deployment readiness, or workflow continuation.

The scanner is intentionally focused on high-confidence repository regressions. It is not a complete secret-detection product and must not be treated as proof that no secrets exist anywhere.

C11 does not change JWT behavior, Weaviate runtime behavior, CORS behavior, the environment endpoint, SQL, governance authority, memory promotion, source apply, commit, push, PR creation, merge, release, deploy, or workflow continuation.

## Scan Scope

The scanner covers active repository text files with these extensions:

- `.cs`
- `.json`
- `.md`
- `.ps1`
- `.yml`
- `.yaml`

The scanner excludes build, dependency, and generated output folders such as:

- `.git`
- `.vs`
- `.idea`
- `bin`
- `obj`
- `node_modules`
- `TestResults`
- `coverage`
- `.next`
- `dist`
- `build`
- `artifacts`
- temp/log folders

Package lock files are skipped to avoid noisy dependency snapshots.

## Forbidden Patterns

The scanner fails on:

- non-empty committed `Jwt:Key` values in appsettings/config files
- the old committed JWT placeholder outside the explicit rejection/test allowlist
- non-empty committed `Weaviate:ApiKey` values in appsettings/config files
- high-confidence OpenAI provider-token shapes
- high-confidence GitHub provider-token shapes
- bearer tokens with concrete token material
- private-key block headers
- password-bearing connection-string fragments
- concrete `ApiKey`, `Secret`, `ClientSecret`, and `SigningKey` assignments

## Allowlist Rules

Allowlisting is file-specific, rule-specific, and line-fragment-specific.

Allowed entries must be deterministic, fake, test-only or rejection-constant material, and documented with a reason.

The allowlist must not include real-looking provider tokens, real-looking JWT signing secrets, real-looking Weaviate API keys, broad regexes that hide leaks, whole active directories, or active production appsettings secret values.

If the scanner finds real secret-looking material, fix the leak. Do not expand the allowlist to hide it.

## Redaction Rules

Failure output must not print full candidate values.

Findings report:

- rule name
- file path
- line number
- redacted preview

The preview uses only redacted token/key markers, not the full candidate.

## CI Lane

C11 is wired into the existing governance-boundary CI script through an explicit `BlockC11SecretScanningRegressionTests` filter.

No new workflow, external scanner package, GitHub Advanced Security dependency, GitHub write permission, or artifact upload is added.

## Forbidden Mutation Paths

- no production code behavior change
- no JWT resolver/token behavior change
- no Weaviate runtime behavior change
- no CORS behavior change
- no environment endpoint behavior change
- no SQL migration
- no SQL store/procedure change
- no frontend/Tauri runtime change
- no OpenAPI/generated-client change
- no governance authority change
- no source apply
- no commit executor change
- no push executor change
- no PR executor change
- no merge
- no release
- no deployment
- no memory write or promotion
- no workflow continuation

## Validation

- Focused C11 tests: 9/9 passed.
- C06/C07/C08/C09/C10/C11 security boundary tests: 66/66 passed.
- Governance-boundary CI script: passed.
  - B-series profile boundary tests: 133/133 passed.
  - BQ-BU compatibility boundary tests: 80/80 passed.
  - Security boundary tests: 9/9 passed.
  - API boundary tests: 38/38 passed.
  - CLI boundary tests: 41/41 passed.
- Build: 0 errors / 4 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Review Traps

Reject C11 if:

- scanner prints full secret candidates
- scanner uses broad entropy-only rules with noisy false positives
- scanner allowlists whole active source/config directories
- scanner allowlists real-looking secrets
- scanner skips active appsettings files
- scanner skips active docs/receipts without reason
- scanner requires external services
- scanner needs GitHub write permissions
- scanner uploads artifacts containing findings
- scanner modifies production behavior
- scanner changes JWT, Weaviate, CORS, or environment endpoint behavior
- scanner changes SQL, governance, memory, source-apply, release, or deploy paths
- scanner is described as complete secret assurance
- a real finding is hidden by expanding the allowlist

## Killjoy

A secret scanner that is noisy will be bypassed. A secret scanner that is quiet and focused can guard the door.
