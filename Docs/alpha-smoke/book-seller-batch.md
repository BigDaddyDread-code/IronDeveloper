# BookSeller Batch Smoke

The BookSeller batch smoke is not implemented yet.

This document records the intended D-3 shape so a future PR can implement it without inventing batch authority.

## Fixture

Batch fixture:

```text
TestFixtures/BookSeller/tickets.json
```

Expected dependency waves:

```text
Wave 1:
  validate-book
  search-by-author

Wave 2:
  bulk-discount
```

`bulk-discount` depends on `validate-book`.

## Intended Command

```powershell
Scripts/smoke/alpha-smoke.ps1 `
  -Project BookSeller `
  -TicketBatch TestFixtures/BookSeller/tickets.json `
  -ModelMode Deterministic `
  -RunUntil BatchGate
```

This command is not currently supported. The current script supports only the single-ticket D-2a gate path.

## Required Future Boundary

- No batch-level approval may stand in for per-ticket approval.
- No batch-level continuation may stand in for per-ticket continuation.
- No batch-level apply may stand in for per-ticket apply.
- Wave 2 must wait for `validate-book` to apply.
- `search-by-author` applying must not satisfy `bulk-discount` unless explicitly modeled.

## Current Status

D-1 fixture wave proof exists. D-2a single-ticket deterministic gate exists. D-3 applied x3 remains future work.
