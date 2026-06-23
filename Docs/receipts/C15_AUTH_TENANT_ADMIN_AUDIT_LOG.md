# C15 - Auth / Tenant / Admin Security Audit Log

## Review Line

Audit records explain security-sensitive changes. They do not authenticate users, authorize tenants, grant admin authority, approve execution, satisfy policy, create release readiness, create deployment readiness, or continue workflow.

## Purpose

C15 adds append-only security audit evidence for auth and tenant-sensitive posture changes.

It records:

- `AuthLoginSucceeded`
- `AuthLoginFailed`
- `AuthLogoutRequested`
- `TenantSelectionSucceeded`
- `TenantSelectionDenied`

The model also reserves future admin-security event names, but this slice does not add admin endpoints.

## Boundary

An audit record is evidence that a decision or attempt occurred. It is not authority for future decisions.

C15 does not:

- authenticate users
- authorize tenants
- grant admin authority
- create admin endpoints
- change JWT claims or signing behavior
- change rate limiting
- change CORS
- change environment safety
- change Weaviate behavior
- approve execution
- satisfy policy
- mutate source
- commit
- push
- create pull requests
- merge
- release
- deploy
- promote memory
- continue workflow

## Implementation

- `SecurityAuditEvent` defines the event shape and hashes email, remote IP, and user-agent values.
- `ISecurityAuditLog` defines an append-only security audit log contract.
- `SecurityAuditLog` appends events in memory and rejects unsafe raw material.
- `AuthController` records successful login, failed login, and logout attempts.
- `TenantController` records successful and denied tenant selection.
- `Program.cs` registers the audit log.

Successful auth and tenant transitions fail closed if the audit append fails. No token is issued for a successful login or tenant selection when the required audit record cannot be written.

## Redaction

Audit records may contain:

- user id when known
- tenant id when known
- target user id
- target tenant id
- reason code
- correlation id
- hashed email
- hashed remote IP
- hashed user-agent
- request path

Audit records must not contain:

- passwords
- JWT tokens
- bearer tokens
- raw authorization headers
- JWT signing keys
- Weaviate keys
- OpenAI/API keys
- connection strings
- raw request bodies
- raw credential objects
- raw private reasoning
- stack traces

## Review Traps

Reject this PR if:

- auth/tenant success paths fail closed when audit append fails is no longer true
- raw credentials, bearer tokens, authorization headers, request bodies, connection strings, stack traces, and provider keys enter audit records
- successful login can issue a token when audit append fails
- successful tenant selection can issue a token when audit append fails
- audit records are treated as authentication, authorization, approval, policy satisfaction, or admin authority
- admin endpoints are added without audited security-change events
- audit logging changes C06-C14 security behavior
- audit logging changes frontend, SQL schema, source apply, commit, push, PR, merge, release, deploy, memory, or workflow continuation behavior

## Validation

- Focused C15 API behavior: 10/10 passed
- Focused C15 static boundary: 12/12 passed
- C06-C15 security boundary lane: 109/109 passed
- Governance boundary CI script: passed
  - B-series profile boundary tests: 133/133 passed
  - BQ-BU compatibility boundary tests: 80/80 passed
  - Security boundary tests: 52/52 passed
  - API boundary tests: 38/38 passed
  - CLI boundary tests: 41/41 passed
- Build: 0 errors / 2 warnings
- `git diff --check`: passed with normal LF/CRLF warnings
- `git diff --cached --check`: passed

## Killjoy

An audit log is a witness, not a badge. If it starts granting access, it has stopped being an audit log.
