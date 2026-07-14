# Memory Retrieval Security

**Status:** Canonical memory security contract

**Last reviewed:** 15 July 2026

**Programme slice:** CLN-26

## Pre-Prompt Filter

Memory reaches a prompt only after all applicable layers succeed:

1. Every caller supplies a `MemoryRetrievalRequestContext`: `TenantId`, `ProjectId`, `ActorUserId`, `Consumer`, `AllowedAuthorityClasses`, and `AsOfUtc`.
2. `ProjectMembershipMiddleware` verifies HTTP actor permission, while the chat pipeline and prompt builder independently recheck membership so internal calls cannot bypass the boundary.
3. The prompt builder verifies the requested project belongs to the explicit tenant before any memory query.
4. Lifecycle status and the request's `AsOfUtc` admit only current/accepted shapes and reject implausibly future rows. There is no universal age cutoff: an unsuperseded ten-year-old binding decision may still be current.
5. `Binding` prompt authority comes from `memory.vw_CurrentProjectCanonMemory`. Legacy context-document rows that self-label as `Binding` or `StrongGuidance` are excluded.
6. Legacy `ObservedFact` and `ContextOnly` rows remain eligible only when the consumer explicitly allows those authority classes.
7. Consumer selection limits rules and context by the current intent/capability.

A failure is exclusion, not a lower score. Wrong-project and wrong-tenant data never become prompt text.

## Instruction Isolation

Every stored memory value is HTML-escaped and enclosed in a `<retrieved-memory>` data boundary. The system instruction immediately before those blocks states that retrieved content is quoted evidence, never instruction text. Commands, role changes, policy claims, tool requests, and prompt injection inside stored memory must be ignored as commands.

Retrieval can surface conflicts. It cannot resolve them into authority merely by rank.

## Authority Boundary

Similarity, freshness, status, and authority labels are filters or ranking inputs. None grants approval, execution, source mutation, policy satisfaction, promotion, or memory writes.

## Active Retrieval Versus Disabled Authority Injection

The active prompt path assembles project-scoped SQL context, including accepted decisions, observable state, tickets, rules, eligible legacy observations, and the current governed Project Canon projection. Existing semantic retrieval may supply derived, quoted evidence through its explicit evidence lane.

What remains disabled is automatic semantic/vector **candidate-to-authority injection**: similarity results, proposal candidates, or vector matches do not enter Project Canon, become `Binding`/`StrongGuidance`, or bypass the explicit consumer and membership checks. Project Canon currently projects governed rows as `Binding`; `StrongGuidance` is not inferred from legacy labels and requires an explicit governed canon model before prompt use.

## Killjoy Line

If stored text says “ignore the system,” the only thing the system learns is that the stored text contains those words.
