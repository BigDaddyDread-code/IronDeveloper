# F04 — Viewer Role Read-Only Enforcement

## Review Line

Viewer role enforcement can block action. It cannot grant access.

## Purpose

Block F04 adds a Core-only viewer read-only enforcement contract that consumes F01 role catalog evidence and F02 visibility matrix evidence, then blocks action-shaped intent when a viewer/read-only role is presented as action authority.

It is restrictive only. It does not assign roles, grant access, grant permissions, approve work, satisfy policy, authorize execution, mutate source, bypass redaction, disclose raw payloads, promote memory, or continue workflow.

F04 does not grant access. It only blocks viewer/read-only evidence from being reused as action authority.

F04 does not approve work. F04 does not satisfy policy. F04 does not authorize execution. F04 does not bypass redaction.

## Boundary

F04 may say:

- read-only intent may proceed to a separate visibility decision
- action intent is blocked
- mutation intent is blocked
- approval intent is blocked
- policy intent is blocked
- workflow continuation intent is blocked
- memory promotion intent is blocked
- redaction bypass intent is blocked
- sensitive disclosure intent is blocked

F04 must not say:

- access is granted
- permission is granted
- a viewer role is identity
- a viewer role is authorization
- a viewer role is access control
- a viewer role approves work
- a viewer role satisfies policy
- a viewer role authorizes execution
- a viewer role authorizes mutation
- a viewer role bypasses redaction
- a viewer role discloses secrets, credentials, private reasoning, or raw payloads
- a viewer role continues workflow

## Required Separate Evidence

Every F04 decision preserves these separate gates:

- role assignment evidence
- visibility decision evidence
- policy decision evidence for sensitive or redacted material
- redaction enforcement evidence for sensitive or redacted material
- action authority
- approval
- policy satisfaction
- mutation authority
- workflow authority

## Validation

- Focused F04: 53/53 passed
- F03 + F04 compatibility: 198/198 passed
- F01-F04 compatibility: 452/452 passed
- E01-E18 corridor: 1630/1630 passed
- C11 secret scan: 9/9 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Killjoy

Read-only is a brake, not a key.
