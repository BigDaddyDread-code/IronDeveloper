# PR30 Thin Operation Status Viewer

## Purpose

PR30 adds the first thin frontend surface for governed operation status.

The viewer consumes the PR29 read-only frontend readiness API and renders one operation status:

- operation id, kind, subject, state, observed timestamp, and expiry timestamp
- blocked reasons
- missing evidence
- next safe action as guidance only
- forbidden actions
- evidence refs
- receipt refs
- backend authority warnings
- read-only boundary flags

## Review line

The first frontend is a window, not a cockpit.

## Killjoy

Looking at the lock is not touching the key.

## Boundary

The operation status viewer is read-only.

It does not:

- execute operations
- request or accept authority
- create approval
- satisfy policy
- apply source
- execute rollback
- commit
- push
- create or update PRs
- mark ready for review
- merge
- release
- deploy
- promote memory
- continue workflow

## Proof

- Operation state renders as backend state, including `Blocked`, `Eligible`, `Completed`, `Failed`, and `Expired`.
- `Eligible` is not labeled as ready to execute.
- Blocked reasons render under `Blocked reasons`.
- Missing evidence renders under `Missing evidence`.
- Next safe action renders under `Next safe action - guidance only`.
- Forbidden actions render under `Forbidden actions`.
- Evidence refs render as references only with `Evidence refs are not approval.`
- Receipt refs render as references only with `Receipt refs are not authority.`
- Backend authority warnings remain visible.
- The viewer renders the read-only boundary and tests every authority flag false.
- The viewer registers no workspace commands and renders no action buttons or links.
- Hostile backend text remains display text only and does not create mutation controls.
- Compact mode does not hide forbidden actions or missing evidence.
- Next safe action is guidance only.

## Validation

- Frontend PR30 Playwright: 33/33
- Focused PR30: 46/46
- PR29 through CA focused lane: 487/487
- BJ through PR30 authority corridor: 913/913
- Frontend build: passed
- .NET build: 0 errors / 4 warnings
- `git diff --check`: passed with normal LF/CRLF warnings
- `git diff --cached --check`: passed
- commit-range diff check: passed
