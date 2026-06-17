# PR209 - Rollback Failure Regression Tests

PR209 adds rollback failure regression tests only.

PR209 teaches the emergency brake how to fail loudly. It does not fix the crash.

## Boundary

PR209 does not add rollback behaviour.
PR209 does not add rollback action kinds.
PR209 does not add source mutation capability.
PR209 does not add SQL.
PR209 does not add API.
PR209 does not add CLI.
PR209 does not add UI.
PR209 does not add runtime execution.
PR209 does not continue workflow.
PR209 does not approve release.
PR209 does not infer release readiness.
PR209 does not declare rollback cleanup.
PR209 does not expand source apply.
PR209 does not call agents, models, or tools.
PR209 does not promote memory.
PR209 does not activate retrieval.
PR209 does not git commit.
PR209 does not git push.
PR209 does not merge.

Rollback failure evidence is not rollback success.
Rollback failure evidence is not workflow permission.
Rollback failure evidence is not release readiness.
Rollback failure evidence is not proof that the crash is cleaned up.
EvidenceConsistent is not RollbackSucceeded.
RollbackSucceeded is not ReleaseReady.
Human review remains required.

## Coverage

The PR209 regression pack covers preflight rejection without mutation or receipt persistence, partial rollback failure persistence, rollback execution audit truth-table checks, loud receipt-store failure after mutation, direct SQL unsafe-material rejection, persisted receipt content minimisation, and static no-new-surface checks.

## Review line

PR209 teaches the emergency brake how to fail loudly. It does not fix the crash.