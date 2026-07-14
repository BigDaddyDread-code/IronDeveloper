# Memory Quality Benchmark

**Status:** Canonical benchmark contract

**Last reviewed:** 15 July 2026

**Programme slice:** CLN-28

## Fixed Corpus

`tools/dogfood/benchmarks/memory-quality-v1.json` is the versioned ten-case benchmark for:

- exact-title retrieval
- narrow fact retrieval
- architecture retrieval
- accepted versus pending
- current versus stale
- wrong-project rejection
- wrong-tenant rejection
- conflict surfacing
- no-result behaviour
- broad-document domination

Changing expected IDs, scope exclusions, stale IDs, authority pairs, or reference results requires a benchmark version increment and review. A provider may supply new observed results; it must not rewrite the expected corpus to make itself pass.

## Report

The executable evaluator reports:

| Metric | Reference harness result | Acceptance threshold |
| --- | ---: | ---: |
| Top-1 accuracy | 100% (9/9 scorable cases) | at least 80% |
| Top-5 accuracy | 100% (9/9) | 100% |
| Wrong-scope result count | 0 | 0 |
| Stale result count | 0 | 0 |
| Authority-order errors | 0 | 0 |
| No-result errors | 0 | 0 |

## Baseline Truth

The recorded result is a deterministic reference-harness baseline proving corpus and metric behaviour. It is not a live SQL, in-memory semantic, OpenAI embedding, or Weaviate provider run. Provider acceptance requires recording its observed IDs against this unchanged corpus.

SQL-backed project context assembly is active today through the CLN-26 retrieval-security path. That current path is not disabled by this benchmark.

Automatic semantic/vector candidate injection remains disabled. Live-provider-gated semantic retrieval is future work. The reference harness passing does not authorize that future injection; a named live provider must separately meet and record the thresholds, and retrieval security/authority gates must still hold.

The evaluator fails closed when observed results are missing, duplicated, attached to an unknown case, or contain duplicate result IDs. A provider run must submit one explicit result envelope for every fixed case, including an empty envelope for the no-result case.

## Killjoy Line

A benchmark you edit until it passes is a demo, not a quality gate.
