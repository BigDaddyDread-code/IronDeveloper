# CLN-28 Memory Quality Benchmark Receipt

**Status:** Historical receipt

**Date:** 14 July 2026

## Delivered

- Added a fixed versioned ten-case memory retrieval corpus.
- Added executable top-1, top-5, wrong-scope, stale, authority-order, and no-result metrics.
- Recorded the deterministic reference baseline and acceptance thresholds.
- Added regression tests for corpus completeness, metrics, scope leakage, ordering, and no-result behaviour.
- Kept automatic injection disabled pending a separately recorded acceptable live-provider run.

## Boundary

This receipt proves the benchmark harness and fixed reference baseline. It does not claim live provider quality, approve automatic memory injection, or grant retrieval authority.
