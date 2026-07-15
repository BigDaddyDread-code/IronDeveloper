# CLN-28 Memory Quality Benchmark Receipt

**Status:** Historical receipt

**Date:** 15 July 2026

## Delivered

- Added a fixed versioned ten-case memory retrieval corpus.
- Added executable top-1, top-5, wrong-scope, stale, authority-order, and no-result metrics.
- Recorded the deterministic reference baseline and acceptance thresholds.
- Added regression tests for corpus completeness, every acceptance metric, malformed observed-result envelopes, scope leakage, ordering, stale results, and no-result behaviour.
- Kept automatic semantic/vector candidate injection disabled pending a separately recorded acceptable live-provider run; existing SQL-backed project context assembly remains active.

## Boundary

This receipt proves the benchmark harness and fixed reference baseline. It does not claim live provider quality, approve automatic semantic/vector candidate injection, disable current SQL-backed context assembly, or grant retrieval authority.
