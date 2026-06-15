# PR155 Approval Package Review UI Receipt

PR155 adds the Approval Package Review UI.

Approval Package Review UI is read-only.

## Boundary

Approval package is not accepted approval.
Approval package review is not approval.
Requested decision is not decision made.
Human approval note is not accepted approval record.
Approval requirement is not approval.
Policy evidence is not policy satisfaction.
Refresh is not retry.
Navigation is not workflow continuation.
Copy package id is not approval.
Copy correlation id is not workflow continuation.

This PR is not Block P approval authority.

The UI consumes existing GET-only governance trace APIs.
There is no dedicated approval package read endpoint in this PR, and this PR does not invent one.

## What this PR does

- Adds `/governance/approval-packages` as a read-only Approval Package Review route.
- Provides approval package filters over existing governance trace evidence.
- Shows approval package summaries, evidence references, boundary warnings, and related read-only navigation hints.
- Redacts unsafe approval payload, private reasoning, raw prompt, raw completion, raw tool output, patch payload, and secret-like markers.
- Adds Playwright coverage for read-only behavior and hidden payload exclusion.
- Adds static boundary tests for GET-only reads and forbidden control actions.

## What this PR does not do

- Does not approve or reject approval packages.
- Does not accept approvals.
- Does not create accepted approval records.
- Does not satisfy approval requirements.
- Does not satisfy policy.
- Does not transition workflow.
- Does not invoke tools.
- Does not dispatch agents.
- Does not apply source.
- Does not release software.
- Does not add API, SQL, CLI, runtime, scheduler, worker, or authority behavior.

## Review line

PR155 puts the approval package on the review table. It does not sign it.
