---
id: bookseller-test-plan-current
project: BookSeller
title: BOOKSELLER_TEST_PLAN_CURRENT
document_type: TestPlan
authority: Accepted
source: BookSellerFixture020
created_utc: 2026-05-22T05:22:00Z
dogfood_run_id: fixture-020
---
# BookSeller Project Smoke Test Plan

The BookSeller 020 fixture proves IronDev can run a BookSeller-specific test plan through the CLI and TesterAgent.

The plan checks:

- BookSeller accepted architecture memory is retrievable.
- BOOK-001 is retrievable as a simple ticket fixture.
- Returned reports say project = BookSeller.
- IronDev memory does not appear as BookSeller authority.

This plan does not build the BookSeller app yet.
