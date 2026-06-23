# A09 — Frontend Readiness Freshness Marker Contract

## Review Line

Stale is not safe. Expired is not current.

## Purpose

Block A09 adds canonical frontend-readiness freshness markers for read-only backend truth:

- Current
- Stale
- Expired
- Unknown
- NotApplicable

The marker is carried on `FrontendReadinessReadState` and on the API envelope so frontend consumers can explain whether displayed backend truth is current, stale, expired, unknown, or not applicable.

## Boundary

A09 is read-only metadata. It does not refresh evidence, rerun validation, repair stale state, execute source apply, mutate source, commit, push, create PRs, mark ready, request reviewers, merge, release, deploy, promote memory, or continue workflow.

Freshness is not approval.
Freshness is not policy satisfaction.
Freshness is not source apply authority.
Freshness does not allow mutation or workflow continuation.

## Contract

`FrontendReadinessFreshnessClassifier` evaluates freshness from supplied timestamps and explicit stale evidence:

- missing freshness evidence becomes `Unknown`
- expired evidence becomes `Expired`
- explicit stale evidence becomes `Stale`
- fresh observed evidence becomes `Current`
- missing records use `NotApplicable`

Expired evidence takes precedence over stale evidence. Redacted and invalid read states keep their safety state instead of becoming current.

Freshness evaluation time is captured at the read/API boundary and passed through to the read-state classifier. The classifier does not read the system clock internally.

## Read-State Mapping

Frontend readiness read states now carry:

- `IsExpired`
- `Freshness`

The API envelope now carries:

- `Freshness`

Compact mode cannot hide freshness warnings.

## Validation

- Restore: `dotnet restore IronDev.slnx` passed with existing NU1510 warnings.
- Focused A09: `BlockA09FrontendReadinessFreshnessMarkerContractTests` passed 75/75.
- A08 compatibility: `BlockA08FrontendReadinessEmptyStateContractTests` passed 62/62.
- A01-A09 read adapter stack: passed 386/386.
- Frontend/readiness read-only lane plus A01-A09: passed 574/574.
- Build: `dotnet build IronDev.slnx --no-restore -v:minimal` passed with 0 errors / 4 warnings.
- `git diff --check`: passed with normal LF/CRLF warnings.

## Review Traps

Reject this slice if:

- stale evidence is presented as current
- expired evidence is presented as current
- unknown freshness is treated as safe
- freshness grants approval, policy satisfaction, source apply authority, mutation, or continuation
- the API envelope omits freshness
- the classifier reads the system clock internally instead of using supplied evaluation time
- A09 adds validation execution, refresh, repair, mutation, provider, UI, memory, or workflow continuation behavior

## Killjoy

Old truth is not fresh authority.
