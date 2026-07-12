# CLN-09 Brittle Test Cleanup Receipt

**Date:** 13 July 2026

**Branch:** `cleanup/cln-09-brittle-test-cleanup`

## Scope

Removed the three observed current-product Playwright timing races recorded by CLN-08. Production behavior and backend contracts are unchanged.

## Changes

- Replaced the Workshop sending test's fixed 900 ms delay with an explicit completion request gate.
- Held the shared-channel read response until both the request and pre-response unread state were observed.
- Held the project-list response until the chooser request was observed, then asserted the durable tile grid.
- Added one shared typed deferred helper for test-controlled asynchronous boundaries.
- Kept all boundary assertions. Added no retries, timeout increases, skips, or test deletion.

## Evidence

```text
npx playwright test tests/chat-conversation-first.spec.ts tests/chat-session-navigation.spec.ts tests/project-entry.spec.ts --grep "sending keeps|shared-channel URLs|projects render" --repeat-each=10 --workers=4 --reporter=line
30 passed
```

The stress run also exposed that React development mode may issue two identical project-list reads. The test synchronizes on one or more observed reads and validates the final UI state instead of asserting an accidental request count.

## Result

`TEST-REL-03` is closed with deterministic request/response synchronization.
