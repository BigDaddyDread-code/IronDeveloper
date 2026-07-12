# IronDev Documentation

**Status:** Canonical documentation entry point

Start with:

- [Current Product Capabilities](product/CURRENT_PRODUCT_CAPABILITIES.md) for reachable product truth.
- [Cleanup and Product Completion Plan](product/IRONDEV_CLEANUP_AND_PRODUCT_COMPLETION_PLAN.md) for cleanup-era product status.
- [Canonical Architecture Index](architecture/CANONICAL_ARCHITECTURE_INDEX.md) for architecture authority by domain.
- [Documentation Truth Inventory](cleanup/DOCUMENTATION_TRUTH_INVENTORY.md) for the status and owner of every document.
- [Terminology Deprecation Map](cleanup/TERMINOLOGY_DEPRECATION_MAP.md) for current product language and compatibility boundaries.

## Canonical Structure

| Directory | Owns |
| --- | --- |
| [`architecture/`](architecture/README.md) | Current architecture authority maps and bounded implementation architecture. |
| [`product/`](product/README.md) | Product truth, UX contracts, and product-surface specifications. |
| [`ux/`](ux/README.md) | Historical UX design input and implemented slice records. |
| [`api/`](api/README.md) | API contract documentation and checked-in API descriptions. |
| [`memory/`](memory/README.md) | Current memory boundaries and the future memory reality audit. |
| [`operations/`](operations/README.md) | Supported startup, configuration, diagnostics, retention, and support guidance. |
| [`testing/`](testing/README.md) | Test contracts, lane definitions, and validation guidance. |
| [`cleanup/`](cleanup/README.md) | Cleanup programme inventories, maps, and active remediation plans. |
| [`dogfood/`](dogfood/README.md) | Dogfood procedures and bounded operational evidence. |
| [`receipts/`](receipts/README.md) | Immutable historical evidence. |
| [`archive/`](archive/README.md) | Non-current documents moved only after reference proof. |

Legacy top-level documents and compatibility directories remain in place until a bounded move can update every live reference without rewriting receipts or corrupting generated knowledge metadata. New documentation belongs in its owning canonical directory unless it is one of the root entry points above.
