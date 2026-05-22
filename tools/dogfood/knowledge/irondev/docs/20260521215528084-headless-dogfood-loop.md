---
id: 20260521215528084-headless-dogfood-loop
project: IronDev
title: Headless Dogfood Loop
document_type: Discussion
authority: WorkingDraft
source: SeedBaseline
dogfood_run_id:
created_utc: 2026-05-21T21:55:28.0843294+00:00
---

# Headless Dogfood Loop

IronDev needs a command-line control port so Codex can reset a test world, run messy prompt variants, inspect traces, patch IronDev, and run again.

Dogfood runs are identified by DogfoodRunId. Replay tests assert behaviour such as route, action blocking, dry-run safety, and generated draft artefacts rather than exact prose.
