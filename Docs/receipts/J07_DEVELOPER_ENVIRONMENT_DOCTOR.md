# J07 Developer Environment Doctor Receipt

## Purpose

J07 adds a local developer environment doctor command:

- `Scripts/local/doctor-local.ps1`

The doctor inspects whether a machine appears capable of running the IronDev alpha/local loop and reports blockers, warnings, and one primary next safe action.

The developer doctor reports local readiness blockers and next safe actions. It does not create readiness, evidence, approval, authority, service state, SQL state, Weaviate state, smoke proof, source mutation, or release permission.

The developer doctor is diagnostic only. It reports local readiness blockers and next safe actions; it does not create readiness, evidence, approval, authority, or permission to run/mutate/apply/release.

Review line: J07 tells the developer what is safe or blocked on this machine. It does not make the machine safe.

Killjoy: A green doctor report is a map, not permission.

## Command Shape

Default invocation:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\local\doctor-local.ps1
```

The default invocation behaves as a diagnostic check-only run.

Supported switches:

- `-CheckOnly`
- `-Json`
- `-Markdown`
- `-Strict`
- `-NonInteractive`
- `-Verbose`
- `-ApiBaseUrl`
- `-UiBaseUrl`
- `-SqlServer`
- `-DatabaseName`
- `-LocalTestDatabaseName`
- `-WeaviateEndpoint`

Forbidden switches are rejected as unsafe options before child checks run:

- fix / prepare / create / rebuild modes
- service start modes
- Docker / Weaviate start modes
- seed / demo / smoke / alpha / Playwright modes
- evidence, approval, continuation, and apply modes

Unsafe requested options return exit code `3`.

## Checks Performed

J07 reports these diagnostic categories:

- Repository shape
- Toolchain command availability
- Frontend package shape and dependency folder presence
- .NET restore/build readiness as not evaluated unless separately run
- Development local override presence, ignored/untracked status, and example presence
- J08 redacted config summary contract availability
- J10 root-safety availability
- SQL local readiness through J05 check-only delegation or equivalent safety classification
- Weaviate local readiness through J06 check-only delegation or equivalent safety classification
- LocalTest file presence and config-shape safety
- API and UI loopback GET-only probes
- Smoke path availability
- Single primary next safe action

Each diagnostic row has:

- name
- status
- severity
- detail
- next safe action
- evidence kind
- authority boundary

The boundary values include:

- `DiagnosticOnly`
- `NotAuthority`
- `NotEvidence`
- `NotApproval`
- `NotReadiness`

## Checks Deliberately Not Performed

J07 does not:

- restore packages
- install frontend dependencies
- build the solution
- build the frontend
- create local override files
- print local override contents
- start API, UI, Tauri, Docker, or Weaviate
- reset LocalTest data
- run LocalTest smoke
- run Playwright
- create SQL databases
- rebuild SQL databases
- apply SQL setup scripts
- ensure Weaviate schema
- rebuild Weaviate schema
- seed demo or BookSeller data
- write evidence
- write reports
- approve anything
- satisfy policy
- request workflow continuation
- request source apply
- mutate source
- claim alpha or release readiness

## J04 / J05 / J06 / J10 Relationship

J04 remains the safe local bootstrap helper for explicit local override creation, .NET restore, and frontend install.

J05 remains the guarded SQL create/rebuild command. J07 may delegate to J05 and J06 only in check-only mode.

J06 remains the guarded local Weaviate schema command. J07 does not ensure or rebuild Weaviate.

J10 root safety is treated as a required external-alpha precondition when present. If the J10 root-safety validator is absent, J07 reports root safety as `NotEvaluated` and blocks external alpha. Missing J10 is never treated as safe.

## Output Contract

J07 supports text, JSON, and Markdown output over the same internal model.

Top-level fields:

- `DoctorStatus`: `Ready`, `Blocked`, `Warning`, or `Unknown`
- `Mode`: `CheckOnly`
- `BoundaryStatement`
- `Checks`
- `Blockers`
- `Warnings`
- `NextSafeAction`

Only one primary next safe action is selected.

## Exit Codes

- `0`: doctor completed and no blockers were found
- `1`: doctor command failed unexpectedly
- `2`: doctor completed and blockers were found
- `3`: unsafe requested options were rejected

A blocked machine is a successful diagnosis, not a command crash.

## Tests Added

J07 adds `BlockJ07DeveloperEnvironmentDoctorTests`.

The tests cover:

- command existence and documentation
- default diagnostic-only behavior
- no evidence/report/artifact/log mutation
- unsafe switch rejection
- JSON output parsing
- Markdown output shape
- J05 check-only delegation only
- SQL unsafe target blocking before J05 delegation
- SQL unsafe database name blocking before J05 delegation
- J06 check-only delegation only
- Weaviate unsafe endpoint blocking before J06 delegation
- local override missing/tracked/ignored behavior
- local override contents never printed
- LocalTest safety checks
- output redaction for secrets and user-local paths
- API/UI GET-only and loopback-only probe posture
- toolchain blocker vocabulary
- no runtime/authority surface

## Validation Run

- J07 focused tests: passed, 20/20.
- J04/J05/J06/J08 local-tool compatibility: passed, 100/100.
- Integration category and slow/quarantine category contracts: passed, 17/17.
- C11 secret scan: passed, 9/9.
- Direct doctor JSON invocation in the original J07 checkout: completed with exit code 2 because the machine was diagnostically blocked by missing frontend dependencies and missing J10 root safety; this is a blocked diagnosis, not a script crash.
- `dotnet restore IronDev.slnx`: passed with existing package warnings.
- `dotnet build IronDev.slnx --no-restore`: passed with 0 errors / 7 existing warnings.
- `git diff --check`: passed; git reported line-ending normalization warnings for docs only.
- `git diff --cached --check`: passed; git reported line-ending normalization warnings only.

## Known Limitations

J07 does not prove a real SQL database exists.

J07 does not prove Weaviate content is correct.

J07 does not run smoke.

J07 does not produce evidence.

J07 does not validate real root safety by itself. When a J10 validator exists, J07 reports contract presence but root safety remains a separate evaluation.

J07 does not replace explicit J04/J05/J06 setup commands.

J07 does not claim alpha readiness, release readiness, merge readiness, deployment readiness, source apply safety, or workflow continuation safety.

## Next Intended Slice

J07a - Doctor output wired into LocalTest preflight docs.

Purpose: make the local setup path boring for a developer by documenting the exact sequence:

```text
doctor -> one next safe action -> LocalTest browser start -> LocalTest smoke proof
```

Boundary: documentation can guide a human. It does not create readiness.
