---
id: 20260521215528085-model-role-settings-direction
project: IronDev
title: Model Role Settings Direction
document_type: Discussion
authority: WorkingDraft
source: SeedBaseline
dogfood_run_id:
created_utc: 2026-05-21T21:55:28.0858398+00:00
---

# Model Role Settings Direction

IronDev should choose models by agent role. Cheap models should handle routing, summarisation, and Test Agent execution. Stronger models should handle planning, difficult failure diagnosis, and code proposal review.

Every trace should record agent role, provider, model, and DogfoodRunId when present.
