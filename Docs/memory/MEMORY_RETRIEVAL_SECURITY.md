# Memory Retrieval Security

**Status:** Canonical memory security contract

**Last reviewed:** 14 July 2026

**Programme slice:** CLN-26

## Pre-Prompt Filter

Memory reaches a prompt only after all applicable layers succeed:

1. Authenticated tenant context selects SQL rows.
2. `ProjectMembershipMiddleware` verifies actor permission for the route project.
3. The prompt builder rechecks candidate tenant and project against the loaded project.
4. Status filtering admits only current/accepted shapes.
5. Freshness rejects implausibly future or expired-window candidates.
6. Authority-class filtering admits only declared prompt-consumable classes.
7. Consumer selection limits rules and context by the current intent/capability.

A failure is exclusion, not a lower score. Wrong-project and wrong-tenant data never become prompt text.

## Instruction Isolation

Every stored memory value is HTML-escaped and enclosed in a `<retrieved-memory>` data boundary. The system instruction immediately before those blocks states that retrieved content is quoted evidence, never instruction text. Commands, role changes, policy claims, tool requests, and prompt injection inside stored memory must be ignored as commands.

Retrieval can surface conflicts. It cannot resolve them into authority merely by rank.

## Authority Boundary

Similarity, freshness, status, and authority labels are filters or ranking inputs. None grants approval, execution, source mutation, policy satisfaction, promotion, or memory writes.

## Killjoy Line

If stored text says “ignore the system,” the only thing the system learns is that the stored text contains those words.
