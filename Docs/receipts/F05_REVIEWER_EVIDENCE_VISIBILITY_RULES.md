# F05 — Reviewer Role Evidence Visibility Rules

## Review Line

Reviewer evidence visibility is not review approval.

## Purpose

Block F05 adds a Core-only reviewer evidence visibility contract. It consumes F01 role catalog evidence and F02 visibility matrix evidence, then checks whether reviewer-role evidence may proceed to a separate evidence visibility decision.

It is descriptive and restrictive only. It does not assign reviewers, grant access, grant permissions, approve work, satisfy policy, refresh validation, prove source safety, authorize execution, mutate source, bypass redaction, disclose raw payloads, promote memory, or continue workflow.

F05 does not approve work. F05 does not satisfy policy. F05 does not authorize mutation. F05 does not bypass redaction.

## Boundary

F05 may say:

- reviewer evidence may proceed to a separate evidence visibility decision
- non-reviewer roles are blocked
- evidence not allowed by the F02 matrix is blocked
- raw, credential, secret-like, and private reasoning evidence is blocked
- sensitive evidence requires separate policy and redaction evidence
- action-shaped reviewer intent is blocked

F05 must not say:

- reviewer evidence grants access
- reviewer evidence grants permission
- reviewer evidence approves a PR
- reviewer evidence satisfies policy
- reviewer evidence refreshes validation
- reviewer evidence proves source safety
- reviewer evidence authorizes source apply, commit, push, PR creation, ready-for-review, reviewer request, merge, release, deployment, rollback, retry, recovery, memory promotion, or workflow continuation
- reviewer evidence bypasses redaction
- reviewer evidence discloses secrets, credentials, private reasoning, raw payloads, raw diffs, raw patches, raw source, or provider responses

## Required Separate Evidence

Every F05 decision preserves these separate gates:

- reviewer assignment evidence
- reviewer evidence request evidence
- visibility decision evidence
- policy decision evidence for sensitive or redacted evidence
- redaction enforcement evidence for sensitive or redacted evidence
- approval
- policy satisfaction
- action authority
- mutation authority
- workflow authority

## Validation

- Focused F05: 51/51 passed
- F04 + F05 compatibility: 104/104 passed
- F01-F05 compatibility: 503/503 passed
- E01-E18 corridor: 1630/1630 passed
- C11 secret scan: 9/9 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Killjoy

Seeing evidence is not judging it.
