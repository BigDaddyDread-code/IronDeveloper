# PR156 Dogfood Receipt Viewer UI

PR156 adds the Dogfood Receipt Viewer UI.

Dogfood Receipt Viewer UI is read-only.

## Boundary

Read-only view.

Dogfood receipt is not release approval.

Dogfood pass is not release readiness.

Dogfood evidence is not policy satisfaction.

Receipt viewer is not dogfood execution.

The UI consumes existing GET-only dogfood receipt and governance trace APIs.

Copy receipt id is not release approval.

Copy correlation id is not workflow continuation.

Navigation is not workflow continuation.

This PR is not Block P release authority.

## No new authority

This UI cannot create dogfood receipts, mark dogfood passed, approve release, satisfy policy, transition workflow, invoke tools, dispatch agents, apply source, or release software.

PR156 does not add receipt creation, receipt outcome mutation, dogfood execution, release approval, policy satisfaction, workflow transition, tool invocation, agent dispatch, source apply, memory promotion, model calls, SQL schema, backend controller, CLI command, or runtime worker behavior.

## Evidence shape

The viewer reads safe dogfood receipt summaries, evidence references, gate/request references, warnings, known limitations, governance trace references, timeline links, correlation report links, tool gate ledger links, and approval package links.

Raw prompt, raw completion, raw tool output, dogfood payload JSON, validation output JSON, raw dogfood notes, source content, patch payload, private reasoning, hidden reasoning, chain-of-thought, scratchpad, and secret-like material are not viewer output.

## Review line

PR156 shows the dogfood receipt. It does not taste the food.
