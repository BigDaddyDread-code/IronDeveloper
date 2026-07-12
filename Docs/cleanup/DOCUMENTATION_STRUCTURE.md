# Documentation Structure

**Status:** Canonical cleanup structure contract

**Last reviewed:** 13 July 2026

**Programme slice:** CLN-05

## Canonical Directories

```text
Docs/
  architecture/
  product/
  ux/
  api/
  memory/
  operations/
  testing/
  cleanup/
  dogfood/
  receipts/
  archive/
```

Each canonical directory has a `README.md` that states its ownership boundary. [Docs/README.md](../README.md) is the only new root-level documentation entry point.

## Existing Compatibility Layout

The repository also contains accepted ADR directories, client/CLI notes, decision records, release material, policy documents, and 118 pre-CLN-05 loose top-level Markdown files. Their presence does not make them canonical structure, and CLN-05 does not move them merely to produce a tidy tree.

## Move Gate

A document may move only when all of these are proven:

1. The documentation truth inventory classifies it as `Superseded` or `ArchiveCandidate`.
2. No runtime, test, script, generated-client, or route contract depends on its path.
3. Generated dogfood or knowledge metadata can be regenerated without changing historical meaning.
4. Every live relative link can be updated.
5. No immutable receipt must be rewritten to preserve a link.
6. CLN-07 documentation checks pass before and after the move.

If an old path must remain as a forwarding document, the move has not removed the duplicate identity and needs a separate justification.

## CLN-05 Decision

No legacy document moves in this slice. Reference sampling found current architecture, UX, frontend, and generated dogfood metadata pointing at obvious archive candidates. Creating the canonical boundaries now reduces future drift without pretending those dependencies are solved.

## Review Line

New documentation has an obvious owner, while old material stays stable until movement is demonstrably safe.

## Killjoy Line

A neat folder tree bought with broken evidence links is documentation damage, not cleanup.
