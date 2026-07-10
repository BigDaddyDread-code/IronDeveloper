# IronDev Product and UX Specification v2

**Date:** 10 July 2026  
**Status:** Product and implementation contract  
**Product:** IronDev desktop client (Tauri and React)

This is the repository index for the complete IronDev product and UX specification. The specification is split into reviewable modules while the formatted PDF/DOCX deliverables remain one continuous document.

## Specification modules

1. [Foundations, product decisions, tenancy, information architecture, routes, and entry journey](IRONDEV_PRODUCT_UX_SPEC_V2_FOUNDATIONS.md)
2. [Board, Chat, Work Item, Library, Documents, Tools, and Members](IRONDEV_PRODUCT_UX_SPEC_V2_SURFACES.md)
3. [System states, components, writing, responsive behavior, accessibility, and visual system](IRONDEV_PRODUCT_UX_SPEC_V2_SYSTEM.md)
4. [Developer handoff, implementation order, acceptance test, and review checklist](IRONDEV_PRODUCT_UX_SPEC_V2_HANDOFF.md)

Detailed supporting contracts:

- [Multi-User Chat and Collaboration](IRONDEV_MULTI_USER_CHAT_SPEC.md)
- [Project Documents and Tools](IRONDEV_DOCUMENTS_AND_TOOLS_SPEC.md)
- [Chat, Discussion, Ticket, and Build boundaries](CHAT_DISCUSSION_TICKET_BUILD_BOUNDARIES.md)

## Locked product model

```text
Board | Chat | Work Item | Library
```

- Chat is a first-class, project-scoped collaboration and work-formation surface.
- Tenant selection is shown only when the user has more than one accessible tenant.
- Documents and Tools have dedicated Library routes and honest lifecycle states.
- Multi-user attribution, membership, mentions, unread state, assignment, concurrency, and eligibility are foundational.
- Chat conversation is not approval, execution, continuation, tool authority, or source apply.
- Working functions receive real routes. Defined but unbuilt destinations return **Not implemented**. Temporary failures are **Unavailable**.
- The product does not use demo, alpha, beta, preview, experimental, coming-soon, or similar labels to disguise missing behavior.

## Governance boundary

The client displays backend truth and requests actions. The backend remains authoritative for readiness, membership, visibility, ticket creation, tool use, findings, approval, continuation, and source mutation.

The target experience remains:

> Simple on the surface, rigorous underneath.
