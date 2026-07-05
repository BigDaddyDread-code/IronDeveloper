# REL-6 - Fresh-Machine Setup and Release Doctor

## Purpose

Prove that a developer starting from a clean checkout can understand and prepare the v0.1 Local Alpha path without author-only knowledge.

REL-6 does not invent a second setup stack. It composes and hardens the existing J-block machinery:

- `Scripts/local/bootstrap-local.ps1`
- `Scripts/local/sql-local.ps1`
- `Scripts/local/weaviate-local.ps1`
- `Scripts/local/doctor-local.ps1`
- `tools/localtest/start-alpha-localtest.ps1`
- `tools/localtest/Invoke-LocalTestSmoke.ps1`
- `Scripts/smoke/alpha-smoke.ps1`

The release doctor must tell a new developer what is missing, why it matters, and the next safe command to run. It must not silently prepare, repair, approve, apply, release, or mutate anything unless the user requested that specific setup command.

## Review Line

Fresh-machine setup is a map and a checklist. It is not readiness, approval, or permission to mutate.

## Killjoy Line

A release path that only the author can run is still a private ritual.

## Suggested PR

Title:

```text
release(alpha): add fresh-machine setup and release doctor proof
```

Branch:

```text
release/rel6-fresh-machine-setup-doctor
```

## Required User Flow

```text
Developer clones the repo.
Developer runs the release doctor in check-only mode.
Doctor reports missing tools/config/services with named reason codes.
Doctor gives exactly one primary next safe action.
Developer creates the local override from the documented example.
Developer restores/builds frontend and backend dependencies.
Developer prepares local SQL or sees a named SQL blocker.
Developer prepares local Weaviate or sees that it is optional/not required for the alpha path.
Developer starts LocalTest API/UI from documented commands.
Developer runs the deterministic alpha smoke check.
Developer reaches either a clean release preflight or a named blocker with no secrets printed.
```

## Required Command Shape

REL-6 should expose one release-facing doctor entrypoint, even if it delegates to existing local scripts:

```powershell
Scripts/release/doctor-alpha-local.ps1 -CheckOnly
Scripts/release/doctor-alpha-local.ps1 -Markdown
Scripts/release/doctor-alpha-local.ps1 -Json
```

The command may be a thin wrapper around `Scripts/local/doctor-local.ps1`, but the output must be release-facing and must name the v0.1 Local Alpha path.

Allowed optional commands:

```powershell
Scripts/release/prepare-alpha-local.ps1 -CheckOnly
Scripts/release/prepare-alpha-local.ps1 -CreateLocalOverride
Scripts/release/prepare-alpha-local.ps1 -RestoreDotNet
Scripts/release/prepare-alpha-local.ps1 -InstallFrontend
Scripts/release/prepare-alpha-local.ps1 -PrepareSql
Scripts/release/prepare-alpha-local.ps1 -PrepareWeaviate
```

If REL-6 reuses the existing local scripts directly instead of adding wrappers, the release runbook must clearly say so and must not leave developers guessing which J-block script to run first.

## Required Doctor Checks

The release doctor must report:

- Repository shape and solution file
- .NET SDK
- Git
- Node/npm
- Frontend package dependencies
- Local override presence, ignored status, and tracked-file rejection
- SQL target classification and database-name safety
- Weaviate endpoint classification and optional/required status
- API loopback health
- UI loopback health
- Root safety contract availability
- Redacted config summary availability
- LocalTest isolation shape
- Alpha smoke command availability
- Primary next safe action

## Required Reason Codes

These are rendered, not invented ad hoc:

```text
RepositoryShapeMissing
DotNetMissing
GitMissing
NodeMissing
NpmMissing
FrontendDependenciesMissing
LocalOverrideMissing
TrackedLocalOverrideRejected
SqlcmdMissing
SqlTargetRemoteRejected
SqlTargetCredentialedRejected
DatabaseNameProductionLikeRejected
WeaviateEndpointRemoteRejected
WeaviateEndpointCredentialRejected
ApiUnavailable
UiUnavailable
RootSafetyNotEvaluated
RootSafetyBlocked
ConfigSummaryUnavailable
LocalTestIsolationBlocked
AlphaSmokeUnavailable
AlphaSmokeBlocked
```

Existing J-block reason codes may be reused where they already match this meaning. The release doctor should not create synonyms when a current reason code is already precise.

## Required Output Rules

- Markdown output must be readable as a checklist.
- JSON output must be machine-readable and stable enough for contract tests.
- Secret-like config values must be redacted.
- User-local paths must be redacted or shortened when printed.
- Connection strings must not be printed raw.
- The doctor must mark every check as diagnostic evidence only.
- The doctor must show next safe actions as commands, not prose guesses.
- The doctor must not run long or destructive setup in check-only mode.

## Forbidden Behavior

- No automatic approval, continuation, apply, release, or deploy.
- No automatic SQL rebuild unless the user explicitly invokes a prepare/rebuild command.
- No automatic Weaviate rebuild unless the user explicitly invokes a prepare/rebuild command.
- No automatic source mutation.
- No writing raw config, connection strings, tokens, API keys, or user-local paths into reports.
- No remote SQL targets accepted as local setup.
- No cloud Weaviate endpoint accepted as local setup.
- No hidden fallback from missing dependencies to fake success.

## Tests Required

Contract tests:

```text
ReleaseDoctor_CheckOnly_RendersNamedBlockersAndNextSafeAction
ReleaseDoctor_JsonOutput_DoesNotPrintSecretsOrFullUserPaths
ReleaseDoctor_RejectsRemoteSqlTarget
ReleaseDoctor_RejectsTrackedLocalOverride
ReleaseDoctor_RejectsCloudWeaviateEndpoint
ReleaseDoctor_CheckOnly_DoesNotMutateSqlWeaviateSourceOrConfig
ReleaseDoctor_ReusesExistingJBlockScriptsOrDocumentsDirectUse
ReleaseDoctor_ReasonCodesAreFromKnownCatalog
ReleaseDoctor_PrimaryNextSafeActionIsSingleCommand
```

Smoke proof:

```text
FreshMachineAlphaDoctorTranscript_RecordsNamedBlockersOrReadyState
```

The transcript may be produced on a developer machine, but must be honest about what was run and what was only checked.

## Receipt

Path:

```text
Docs/receipts/REL6_FRESH_MACHINE_SETUP_DOCTOR.md
```

The receipt must include:

- Commit SHA
- Machine class, with user/machine identifiers redacted
- Doctor command used
- Doctor result
- Named blockers and warnings
- Primary next safe action
- Local override status
- SQL target classification
- Weaviate status and whether it was required
- Root safety result or explicit not-evaluated status
- Alpha smoke command availability
- Any setup commands actually run
- Boundary statement

## Acceptance Criteria

- A new developer can run one documented doctor command and know the next safe action.
- The doctor never prints secrets, raw connection strings, or full user-local paths.
- Check-only mode is non-mutating.
- Release setup does not require author-only script knowledge.
- Local SQL and Weaviate setup are either prepared safely or produce named blockers.
- The final state is either `ReadyForAlphaSmoke` or a named blocker, never vague failure.

## Review Traps

Block if:

- The doctor performs hidden setup in check-only mode.
- Output leaks secrets or full user-local paths.
- Remote/prod SQL targets are accepted.
- Cloud Weaviate endpoints are accepted as local setup.
- A missing prerequisite becomes green because the script skipped it.
- The next safe action is vague prose rather than an explicit command.
- The release doctor forks the J-block setup logic instead of composing it.

## Out Of Scope

- Product UI journey to `Applied`; that is REL-5.5.
- Release packaging; that is REL-7.
- Real-repo import.
- Live model setup beyond reporting whether live-model config is present.
- Installer UX.
- Auto-repair of developer machines.

## Next PR

REL-7 release docs and packaging.
