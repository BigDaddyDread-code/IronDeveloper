---
id: bookseller-architecture-current
project: BookSeller
title: BOOKSELLER_ARCHITECTURE_CURRENT
document_type: Architecture
authority: Accepted
source: BookSellerFixture020
created_utc: 2026-05-22T05:20:00Z
dogfood_run_id: fixture-020
---
# BookSeller Architecture Current

BookSeller is a project-scoped dogfood fixture for IronDev.

The current architecture direction is a small inventory and sales application backed by SQL Server. The persistence layer should be explicit and traceable. Dapper is acceptable for the first fixture path because the goal is to keep the generated app simple while IronDev proves project-scoped memory, tickets, builder previews, and test execution.

Current project memory rules:

- SQL Server is the source of truth for books, authors, stock counts, storage locations, and sales history.
- Weaviate is retrieval/index support only.
- BookSeller memory must not be mixed with IronDev product memory.
- Fixture tests should report project = BookSeller.
- Builder work must remain preview-first until a later proof slice allows writes.
