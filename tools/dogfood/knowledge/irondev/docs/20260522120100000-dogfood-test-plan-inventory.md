---
id: 20260522120100000-dogfood-test-plan-inventory
project: IronDev
title: DOGFOOD_TEST_PLAN_INVENTORY
document_type: Inventory
authority: Accepted
source: C:\Users\bob\source\repos\AIDeveloper\Docs\DOGFOOD_TEST_PLAN_INVENTORY.md
dogfood_run_id: AlphaTestPhase-094-103
created_utc: 2026-05-22T12:00:00.0000000+00:00
---

# Dogfood Test Plan Inventory

## Purpose

This document summarises the machine-readable dogfood test plan inventory.

The full inventory is stored at:

`tools/dogfood/test-plan-inventory.json`

## Current Counts

- Total plans: 94
- BookSeller-scoped plans: 43
- IronDev/self-improvement plans: 51

## Major Plan Groups

- Memory spine proofs.
- Codex-facing memory search.
- Agent model profile and agent wrapper proofs.
- TesterAgent execution plans.
- RetrieverAgent context bundle plans.
- PlannerAgent draft-only plans.
- SupervisorAgent coordination plans.
- QualityAgent/code standards plans.
- CriticAgent/failure review plans.
- Builder preview safety plans.
- Disposable workspace apply proof.
- BookSeller project-scoped fixture and chaos plans.
- Self-improvement regression packs.

## Interpretation

The plan set is now broad enough to be useful, but it is also large enough that individual historical packs should not all be treated as the daily safety net.

The right next move is to keep the historical plans as archive/regression evidence and create a compact main alpha regression pack for day-to-day confidence.

## Boundary

This inventory does not prove any new behaviour. It documents the current proof surface.


