# CLN-42 Clean-Clone Qualification Receipt

## Result

**CLN-42 qualification:** PASS at commit `47851dea1acab33c77f8c7b611c3b90f14b8aec5`.

**Cleanup exit:** NO-GO. CLN-43 non-author qualification and the final combined-head review remain separate required gates.

## Repository Gate

The full qualification harness ran without `-SkipFrontend` from a new retained clone. The exact evidence is in `Docs/receipts/CLN_42_CLEAN_CLONE_REPOSITORY_EVIDENCE.json`.

- result: `RepositoryQualificationPassed`
- checked-out commit: `47851dea1acab33c77f8c7b611c3b90f14b8aec5`
- .NET restore, vulnerability audit, and full solution build: PASS
- documentation contract: PASS, 686 documents
- locked npm install, vulnerability audit, and frontend build: PASS, zero npm vulnerabilities
- Cargo check: PASS
- completed: `2026-07-15T06:31:19.1941928+00:00`

The solution build completed with 1,817 existing compiler/analyzer warnings and zero errors. This receipt does not relabel warning debt as clean output.

## Live LocalTest Gate

The live evidence is in `Docs/receipts/CLN_42_LIVE_QUALIFICATION_EVIDENCE.json`.

- fresh LocalTest reset, migrations, seed, and seed-contract validation: PASS
- visible sign-in and tenant/project selection: PASS
- Board and Work Item journey: PASS
- live Playwright/API/SQL governed smoke: PASS
- disposable SQL evidence: one run, one `ToolCallCompleted`, and one workspace-preparation `StepCompleted`
- Governance inspection: PASS
- Audit inspection and bounded audit export: PASS
- support bundle export: PASS

A separate visible attempt to start a new governed run failed closed with `ProposalGenerationFailed` because the Fake provider is disabled and no real LLM provider was configured. That refusal is expected configuration enforcement; it is recorded rather than hidden or called a successful execution.

## CLN-41 Operational Qualification

The merged reset/support surface was exercised through `Invoke-LocalTestResetAndSupport.ps1`:

- `ResetTestTenantProject`: PASS against `IronDeveloper_Test`
- `ResetDisposableWorkspace`: PASS; the intended child was recreated empty and its sibling hash was unchanged
- workspace root, repository root, drive root, UNC path, junction target, and `Production_test_Archive`: all refused
- `ExportSupportBundle`: PASS; one synthetic log was included, its correlation ID remained, redaction markers were present, and none of six secret sentinels leaked

## Scope Disclosure

Qualification exposed two dependency advisories and a stale LocalTest smoke vocabulary. This PR therefore also:

- updates ASP.NET authentication/OpenAPI and Swashbuckle package pins to remove the transitive `Microsoft.OpenApi` advisory;
- refreshes the frontend lockfile to remove five npm advisories, including the direct Vite advisory;
- makes both vulnerability audits fail the qualification harness;
- aligns disposable-run SQL proof with current `ToolCallCompleted` and `StepCompleted` events.

## Boundary

CLN-42 evidence is qualification evidence only. It is not release approval, does not close CLN-43, and does not authorize cleanup exit or new memory work.
