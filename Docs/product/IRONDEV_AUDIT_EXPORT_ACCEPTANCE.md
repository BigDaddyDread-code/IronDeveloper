# IronDev Audit Export Acceptance

**Status:** Accepted repository-side; non-author desktop qualification remains a separate release gate
**Qualified against:** `main` after AUDIT-DETAIL-1
**Date:** 12 July 2026

## Delivered Contract

The Audit surface now provides:

- project-scoped ledger filters;
- a backend-produced JSON package capped at 250 rows;
- explicit returned count and truncation truth;
- schema version, generation time, warnings, and ordered-item SHA-256;
- download only after successful package generation;
- canonical `/projects/{projectId}/library/audit/events/{ledgerId}` inspection;
- inert treatment of malformed, absolute, and cross-project evidence targets;
- an explicit read-only boundary that grants no approval, continuation, apply, replay, or other authority.

The browser serializes the package returned by the API. It does not reconstruct rows from visible table content.

## Automated Evidence

Backend projector tests prove:

- the 250-row cap and truncation behavior;
- stable ordered-item hashing;
- redaction of secret-looking summaries;
- removal of unsafe evidence links;
- empty export integrity.

Frontend Playwright tests prove:

- applied filters reach the export request;
- download remains disabled until generation succeeds;
- package count and hash are displayed;
- Audit rows open canonical event routes;
- cross-project and absolute evidence links remain inert;
- an event absent from the bounded result receives an honest no-inference state.

The TypeScript production build and focused backend tests pass for the delivered slices.

## Live LocalTest Evidence

The supported LocalTest smoke signs in as the seeded user, selects the visible tenant and project path, reaches Audit through Library navigation, generates the real backend package, and verifies:

- ledger rows load from the API;
- download is disabled before generation;
- the generated package exposes a 64-character lowercase SHA-256;
- the backend boundary states that the export grants no authority;
- download becomes available only after successful generation.

The smoke does not download a file because filesystem download behavior is not the API contract under test.

## Known Exclusions

- Audit analytics, charts, trends, reporting, and compliance scoring are not implemented.
- The package is not a signature, approval, forensic-completeness claim, or mutation receipt.
- Raw payload JSON, credentials, source content, prompts, completions, and private reasoning are not exported.
- Event detail is backed by the bounded ledger result. A missing row is reported honestly rather than fetched or inferred through a hidden contract.
- Final non-author Tauri desktop qualification remains required by the V2 release gate.

## Acceptance Line

A signed-in project member can generate and download a bounded, non-secret project Audit package and inspect its returned events without gaining or replaying consequential authority.
