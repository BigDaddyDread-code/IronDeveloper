---
id: 20260522120500000-failure-package-quality-audit
project: IronDev
title: FAILURE_PACKAGE_QUALITY_AUDIT
document_type: Audit
authority: Accepted
source: C:\Users\bob\source\repos\AIDeveloper\Docs\FAILURE_PACKAGE_QUALITY_AUDIT.md
dogfood_run_id: AlphaTestPhase-094-103
created_utc: 2026-05-22T12:00:00.0000000+00:00
---

# Failure Package Quality Audit

## Purpose

This audit checks whether failure packages are useful enough for Codex repair work.

## Current Good Shape

Failure package paths exist for IronDev and BookSeller.

Useful packages include:

- Goal ID.
- Failed plan path.
- Expected vs actual behaviour.
- Evidence/log paths.
- Repro command.
- Validation command.
- Likely investigation areas.
- Safety rules.
- Boundary on what not to change.

## Current Limitation

Failure package quality is still mostly deterministic and local. It does not yet include production provider LLM traces by default.

## Quality Bar

A failure package is useful only if Codex can answer:

- What failed?
- How do I reproduce it?
- What evidence proves it?
- What should I inspect first?
- What must I avoid changing?
- How do I validate the repair?

## Boundary

Failure packages do not authorize auto-fix or patch apply.


