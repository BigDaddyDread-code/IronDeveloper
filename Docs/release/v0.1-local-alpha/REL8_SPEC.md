# REL-8 - DOGFOOD-ALPHA-LOCAL-001 Release Gate

## Purpose

Run and record the first explicit local-alpha dogfood gate: `DOGFOOD-ALPHA-LOCAL-001`.

REL-8 is not another abstract governance slice. It is the release-gate evidence packet that says whether v0.1 Local Alpha is actually repeatable by a developer following the docs and scripts, with no author-only shortcuts.

The gate must consume the current release path:

- Fresh-machine/release-doctor setup proof from REL-6, if available.
- Release runbook and package truth from REL-7, if available.
- Deterministic BookSeller single-ticket path through SQL/API state.
- Governed run path to `Applied` where available.
- Honest limitations where UI or fresh-machine proof is still incomplete.

## Review Line

A dogfood gate is evidence with a verdict. It does not grant release authority by itself.

## Killjoy Line

If the release gate cannot be repeated from the runbook, it is not a gate. It is a diary entry.

## Suggested PR

Title:

```text
release(alpha): add DOGFOOD-ALPHA-LOCAL-001 release gate
```

Branch:

```text
release/rel8-dogfood-alpha-local-001
```

## Required Gate Inputs

The release gate must name:

- Repository commit SHA
- Branch
- Machine class with user/machine identity redacted
- Setup path used
- Runbook version/path
- Doctor command and result
- Smoke command and result
- SQL/API state proof, if used
- UI journey proof, if used
- Package manifest/doc proof, if used
- Known limitations carried into the verdict

## Required Gate Flow

```text
Start from a clean checkout or explicitly documented clean local state.
Run the release doctor.
Resolve blockers or record them as gate blockers.
Run setup commands exactly as documented.
Run deterministic BookSeller single-ticket alpha smoke.
Verify report/receipt/evidence paths.
Verify final state and reason codes from backend truth.
Record whether the release path is repeatable.
Record a go/no-go verdict.
```

If REL-5.5 is available, the gate must include the UI journey. If REL-5.5 is not available, the gate must say that the UI journey is not part of `DOGFOOD-ALPHA-LOCAL-001` proof.

## Required Verdict Vocabulary

Use one of:

```text
GoForLocalAlphaPreview
NoGoBlocked
ConditionalGoWithNamedLimitations
EvidenceIncomplete
```

Do not use vague verdicts such as:

```text
LooksGood
ProbablyReady
ShipIt
MostlyWorks
```

## Required Evidence Artifacts

Suggested paths:

```text
Docs/release/v0.1-local-alpha/DOGFOOD-ALPHA-LOCAL-001.md
Docs/release/v0.1-local-alpha/DOGFOOD-ALPHA-LOCAL-001.json
Docs/receipts/REL8_DOGFOOD_ALPHA_LOCAL_001_RELEASE_GATE.md
```

The markdown transcript must be human-readable. The JSON artifact must be stable enough for a contract test.

## Required Transcript Sections

- Executive verdict
- Commit and environment summary
- Commands run, in order
- Doctor result
- Setup actions taken
- Smoke/run result
- Evidence and receipt paths
- Backend state checked
- UI state checked, if applicable
- Known limitations
- Gate blockers
- Next safe action
- Boundary statement

## Required JSON Fields

```text
gateId
commitSha
branch
startedAtUtc
completedAtUtc
verdict
doctorStatus
alphaSmokeStatus
finalRunState
backendEvidenceRefs
receiptRefs
knownLimitations
gateBlockers
nextSafeAction
boundary
```

The JSON must not contain secrets, raw connection strings, raw user-local paths, private tokens, or unredacted machine identifiers.

## Required Tests

Contract tests:

```text
DogfoodAlphaLocal001_TranscriptExists
DogfoodAlphaLocal001_JsonHasStableGateFields
DogfoodAlphaLocal001_UsesAllowedVerdictVocabulary
DogfoodAlphaLocal001_ListsCommandsInOrder
DogfoodAlphaLocal001_NamesDoctorSmokeAndFinalState
DogfoodAlphaLocal001_SeparatesEvidenceFromAuthority
DogfoodAlphaLocal001_DoesNotLeakSecretsOrUserLocalPaths
DogfoodAlphaLocal001_DoesNotClaimUiJourneyIfRel55Missing
DogfoodAlphaLocal001_KnownLimitationsAreExplicit
```

If the gate produces a `GoForLocalAlphaPreview` verdict, add:

```text
DogfoodAlphaLocal001_GoVerdictRequiresNoGateBlockers
DogfoodAlphaLocal001_GoVerdictRequiresRepeatableRunbook
DogfoodAlphaLocal001_GoVerdictRequiresAppliedOrExplicitlyDescopedUiApplyPath
```

## Forbidden Behavior

- No invented release readiness.
- No vague go/no-go language.
- No hidden setup commands omitted from the transcript.
- No hand-carried IDs treated as product-path proof.
- No screenshots or reports that do not name the backend state they came from.
- No raw secrets, connection strings, tokens, or user-local paths.
- No release, tag, deploy, publish, or upload action.
- No changing product behavior to make the gate pass.

## Acceptance Criteria

- `DOGFOOD-ALPHA-LOCAL-001` exists as markdown and JSON.
- The gate records exact commands and exact evidence.
- The verdict is one of the allowed values.
- A no-go or conditional-go verdict names blockers/limitations.
- A go verdict is backed by repeatable runbook proof.
- The gate clearly states what is evidence and what is not authority.

## Review Traps

Block if:

- The transcript is written after the fact without command/evidence detail.
- The JSON omits final state or known limitations.
- The gate claims UI proof without walking the UI path.
- A successful smoke is presented as release authority.
- The gate passes while known blockers are hidden in prose.
- Any output leaks secrets or full user-local paths.

## Out Of Scope

- Three-ticket batch; that is REL-9 and only after the single-ticket path is boring.
- Public release.
- Deployment.
- Installer.
- Live model release proof beyond previously scoped live-model draft smoke.
- Real-repo import.

## Next PR

REL-9 optional BookSeller three-ticket batch after the single-ticket release path is boring.
