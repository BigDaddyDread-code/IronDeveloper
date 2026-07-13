# Sensitive Data Sweep

**Status:** Canonical cleanup contract

**Programme slice:** CLN-18

## Client Sessions

Bearer tokens are session state, not durable browser preferences. The client stores the active token in `sessionStorage`; tenant and selected-project identifiers may remain in `localStorage`. On first startup after this change, a legacy `irondev.token` value is moved into session storage and deleted from local storage. Sign-out, rejected-session cleanup, and LocalTest `FreshSession` clear both locations.

This reduces persistence after the browser or desktop process closes. It does not replace backend token validation, expiry, tenant selection, or authorization.

## API And Export Boundaries

Project context exports use the shared sensitive-data redactor before emitting project paths or user-authored fields. It removes common credential assignments, bearer values, provider-key shapes, JWT-shaped values, private-key blocks, and local absolute paths.

Run-report HTTP responses preserve backend-owned absolute paths internally but expose only:

- a workspace directory label;
- the report file name; and
- evidence references relative to the validated run directory, falling back to a file name for an external or malformed reference.

Run-event payloads redact prompt/completion/request-body fields, credential-shaped values, and absolute path values before server-sent events leave the API. Relative evidence references remain inert and still require project-artifact authorization plus bounded evidence-file resolution.

## CI Evidence

SQL CI starts its container after generating a random per-job password. The workflow masks the password before the container command and exports it only through the job environment. Passwords are no longer hard-coded or derived from public run identifiers.

The shared evidence scanner rejects additional credential assignments and JWT-shaped values. It runs before CI artifacts are uploaded. Frontend test fixtures use unmistakably synthetic values that do not resemble real provider credentials, so an unrelated screenshot failure does not manufacture false secret evidence.

## Scope Boundary

This contract applies to current client session storage, current API run-report/event responses, current project context exports, and newly produced CI evidence. Historical receipts remain immutable. Backend-only execution artifacts may retain full paths where filesystem access is required; they must be shaped before crossing a user-facing or retained-evidence boundary.

Redaction is defense in depth. It grants no authority and cannot make an incorrectly scoped response safe.
