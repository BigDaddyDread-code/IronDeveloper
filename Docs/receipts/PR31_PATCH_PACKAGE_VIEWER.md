# PR31 Patch Package Viewer

## Purpose

PR31 adds a read-only frontend viewer for patch packages.

The viewer consumes the frontend readiness API and renders:

- patch package identity and metadata
- patch diff text
- review summary
- validation and test summary
- known risks
- proposed files
- artifact refs
- evidence refs
- receipt refs
- authority warnings
- read-only boundary flags

## Review line

Reviewable work must be easy to inspect before it is easy to mutate.

## Killjoy

Reading the patch is not permission to apply it.

## Boundary

The patch package viewer is read-only.

Patch package evidence is not source apply authority.

Validation evidence is not approval.

Receipt refs are not workflow continuation.

UI text cannot approve, execute, mutate source, or continue workflow.

The viewer does not:

- apply source
- execute rollback
- run validation
- create approval
- accept approval
- satisfy policy
- commit
- push
- create or update PRs
- mark ready for review
- request reviewers
- merge
- release
- deploy
- promote memory
- continue workflow

## Proof

- The API exposes patch package artifacts through `GET /api/frontend-readiness/patch-packages/{packageId}/artifacts`.
- The artifacts endpoint uses the same read-only envelope and boundary as the frontend readiness API.
- Patch diff text renders as inspection text only.
- Review summary renders as inspection text only.
- Validation summary renders as evidence text only.
- Known risks render as evidence text only.
- Evidence refs and receipt refs stay visible as references.
- Authority warnings stay visible.
- Compact mode does not hide authority-critical fields.
- Hostile patch, review, validation, risk, or receipt text does not create controls.
- The viewer registers no workspace commands.
- The viewer renders no action buttons or links.
- The viewer cannot apply, approve, satisfy policy, rollback, commit, push, create PRs, mark ready, merge, release, deploy, promote memory, or continue workflow.

## Validation

- Frontend PR31 Playwright: 41/41
- Focused PR31: 58/58
- Frontend PR30+PR31 Playwright lane: 74/74
- Frontend PR30+PR31 integration lane: 104/104
- BJ through PR31 authority corridor: 971/971
- Frontend build: passed
- .NET build: 0 errors / 4 warnings
- `git diff --check`: passed with normal LF/CRLF warnings
- `git diff --cached --check`: passed
- commit-range diff check: passed
