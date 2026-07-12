# Terminology Deprecation Map

**Status:** Canonical product-language contract

**Last reviewed:** 13 July 2026

**Programme slice:** CLN-06

## Rules

- Current product labels, API response prose, active documentation, and test names use canonical terms.
- Historical receipts remain unchanged.
- Quoted user content and fixtures may retain natural language that is not presented as product truth.
- Routes, persisted values, DTO/property names, filenames, and `data-testid` selectors are compatibility identifiers. They change only with an owning contract and migration proof.
- A renamed label does not change backend authority or capability.
- Invalid concepts are refused, not softened into a new synonym.

## Deprecation Map

| Old term | Canonical term | Classification | Current handling | Owning cleanup slice |
| --- | --- | --- | --- | --- |
| Project cockpit | Project-scoped Board | Deprecated product term | Use Board in current navigation, prose, and test names. Legacy `flow.cockpit.*` selectors remain compatibility identifiers. | CLN-29 and CLN-31 |
| Viewer catalogue | Governance control centre | Deprecated product term | Governance groups posture, attention, controls, exceptions, decisions, and Technical Evidence by user question. | CLN-31 |
| Technical viewers | Technical Evidence | Deprecated product term | Use Technical Evidence for the progressively disclosed read-only surface. Legacy viewer component identifiers remain bounded compatibility. | CLN-31 |
| FixRequired | Revise request | Deprecated display value | Display "Revise request". Do not rename a persisted or wire value without an explicit migration. | Owning finding-disposition contract |
| RejectFinding | Reject | Deprecated display value | Display "Reject". Preserve actor, reason, finding reference, and audit evidence. | Owning finding-disposition contract |
| DeferFix | FixInFollowUp | Deprecated display value | Display "Fix in follow-up". Deferral remains a reasoned human disposition, not silent dismissal. | Owning finding-disposition contract |
| AI approval | Invalid concept | Forbidden authority claim | Model output and agent review are advisory evidence only. A human/backend approval record is still required. | CLN-13 and CLN-24 |
| Green CI approval | Invalid concept | Forbidden authority claim | Green CI is validation evidence, never approval, continuation, apply, release, or deploy authority. | CI and release contracts |
| Memory truth | Project Canon, where governed | Deprecated authority claim | Retrieval matches, operational memory, proposals, and indexes are not Project Canon. Promotion requires governed human acceptance. | CLN-23 through CLN-27 |
| Chat decision | Decision candidate | Deprecated authority claim | A conversation may propose a decision; only a governed accepted record becomes authoritative. | CLN-12 and decision contracts |
| Planned501 | Not implemented | Deprecated product status | HTTP 501 may transport a refusal, but planning state is not product capability state. | CLN-13 and CLN-14 |
| Ticket | Work Item on product surfaces | Compatibility product term | Ticket remains the current backend identity substrate until a durable Work Item aggregate owns migration. | Deferred product slice after cleanup |
| Chat | Workshop for the project thinking surface | Compatibility product term | Chat remains a backend/domain term for sessions, channels, and messages. Current navigation says Workshop. | CLN-29 and CLN-31 |
| Memory safe | Advisory memory safety result | Forbidden authority shortcut | Safety classification does not approve or promote memory. | CLN-24 |

## Enforcement Boundary

CLN-06 corrects current visible language and records compatibility. CLN-07 turns the unambiguous deprecated phrases and removed disposition labels into automated contradiction checks. Broad substring bans are rejected because words such as "cockpit", "chat", "ticket", and "viewer" remain valid in historical titles, user-authored content, backend identities, and bounded technical contexts.

## Review Line

The product says what a user is looking at and never upgrades evidence into authority through wording.

## Killjoy Line

Renaming a button is easy; renaming a contract without migration proof is a behavior change wearing a copy-edit badge.
