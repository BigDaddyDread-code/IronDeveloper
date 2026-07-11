# V25-00 Work Item Contract Map Receipt

**Date:** 11 July 2026
**Status:** Planning contract added
**Branch:** `UX/v25-00-work-item-contract`

## Scope

This receipt records the v2.5 planning baseline and the Work Item ownership map that must precede durable Work Item schema work.

Changed artifacts:

- `Docs/product/IRONDEV_PRODUCT_UX_SPEC_V25.md`
- `Docs/product/V25_00_WORK_ITEM_CONTRACT_MAP.md`
- `Docs/product/README.md`

## Explicit Non-Changes

This slice does not add schema, migrations, endpoints, generated OpenAPI artifacts, TypeScript clients, UI routes, agent profile runtime behavior, AI connections, credentials, approval behavior, continuation behavior, or source apply behavior.

## Locked Decisions

- Work Item is not a cosmetic rename of `ProjectTicket`.
- Existing Work Item URLs and identifiers must keep working.
- Ticket becomes the versioned contract under a durable Work Item.
- Run becomes an attempt under a durable Work Item.
- Workshop evidence is provenance, not approval.
- Work Item state is navigation summary, not gate satisfaction.
- Approval and apply authority remain in governed backend stores.
- The client must use backend canonical identity and must not infer mappings.

## Next Slice

V25-01 may add durable Work Item storage only after answering the open implementation questions in `Docs/product/V25_00_WORK_ITEM_CONTRACT_MAP.md`.
