---
id: 20260521215528072-irondev-alpha-stabilisation-principles
project: IronDev
title: IronDev Alpha Stabilisation Principles
document_type: Architecture
authority: Accepted
source: SeedBaseline
dogfood_run_id:
created_utc: 2026-05-21T21:55:28.0738589+00:00
---

# IronDev Alpha Stabilisation Principles

SQL Server remains the canonical source of truth. Weaviate and local dogfood stores are retrieval/index layers only.

The stabilisation branch should prefer bug fixes, traceability, and dogfood loops over broad new product surface.
Unsafe writes, code changes, and destructive actions must pause at review or approval gates.
