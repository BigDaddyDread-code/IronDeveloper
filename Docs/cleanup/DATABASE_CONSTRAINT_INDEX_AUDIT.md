# Database Constraint and Index Audit

**Status:** Canonical cleanup contract

**Last reviewed:** 14 July 2026

**Programme slice:** CLN-22

## Scope

This audit covers referential integrity, allowed-value constraints, retrieval and retention indexes, default lookup rows, and audit/memory indexes in the manifest-owned SQL schema. The standard is catalog evidence on a clean migrated database, not the presence of plausible SQL text.

## Foreign Keys

CLN-22 adds checked relationships for product and tenant scope, run-to-ticket scope, semantic project ownership, typed ticket sources, and agent handoff supersession. It also adds the composite uniqueness required to bind a run ticket to the same project. Before checked typed-source constraints are added, orphaned optional ticket source pointers are cleared to `NULL`; valid source provenance is preserved and covered by the supported-upgrade proof.

Project identifiers in governance, agent-audit, memory-proposal, and A2A evidence ledgers are deliberately not foreign keys to `dbo.Projects`: those identifiers are GUID or textual evidence scope, while the product project key is an integer. Evidence must remain append-only and intelligible after product-record lifecycle changes. Their scope is enforced at the governed store and dispatch gates.

Polymorphic source identifiers remain deliberately unconstrained where a row can name more than one source kind. Typed source columns receive foreign keys where their SQL types and lifecycle match.

## Check Constraints

Existing authority, isolation, append-only, chronology, and safety constraints remain verified by the migration verifier. CLN-22 adds `CK_Runs_TicketRequiresProject` so a ticket reference cannot exist without its project scope.

The following flexible status vocabularies are classified as **P2 contract debt**, not silently constrained: `EmbeddingJobs.Status`, `ProjectContextDocuments.Status`, `ProjectDecisions.Status`, `ProjectDocumentVersions.Status`, `ProjectImplementationPlans.Status`, `Projects.IndexingStatus`, `ProjectTickets.Status`, `Runs.State`, and `SemanticIndexRuns.Status`. Database and product-contract owners must first align API, client, seed, and compatibility values; adding a narrow database enum before that contract exists would create upgrade breakage. No P0 or P1 authority/isolation defect was found in this category.

## Indexes

CLN-22 adds indexes for command lookup, run state/retention, ticket status and typed sources, semantic ingestion and retrieval, document/chat source tracing, Work Item source/supersession, and agent-handoff supersession. Existing user-mutation attribution, agent-run audit, governance evidence, and memory-proposal indexes remain part of verifier coverage.

## Default Rows

Clean migration verification requires the ten canonical decision categories in sort order 1-10 and the four decision statuses `Proposed`, `Accepted`, `Superseded`, and `Rejected` in sort order 1-4. These are compatibility defaults, not proof that arbitrary feature status columns share one vocabulary.

## Audit Indexes

The catalog contains scope/time and correlation paths for user mutation attribution, governed append-only ledgers, agent-run audit envelopes, and memory proposals. CLN-22 extends retrieval paths without granting write or promotion authority to memory or semantic retrieval components.

## Deferred P2 Items

- `SemanticEmbeddings.SourceDocumentVersionId` is `int`, while `ProjectDocumentVersions.Id` is `bigint`. Memory and Database owners must perform a typed column migration before adding that foreign key. The filtered source index is added now; the relationship is P2 debt because current writes do not use it as a generic authority path.
- Flexible statuses listed above require an owned cross-layer vocabulary before check constraints can be safely introduced.

## Outcome

The audited schema has no known P0 or P1 constraint/index omission. Remediations are manifest-owned and idempotent; remaining issues are named P2 contract debt with owners and reasons, not hidden cleanup backlog.

## Killjoy Line

An index makes a query cheaper. It does not make the indexed data authoritative.
