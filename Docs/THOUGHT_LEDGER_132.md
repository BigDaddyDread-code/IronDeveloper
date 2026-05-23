---
id: THOUGHT_LEDGER_132
project: IronDev
title: ThoughtLedger 132
document_type: ArchitectureProof
authority: Accepted
status: Accepted
dogfood_run_id: ThoughtLedger132
created_utc: 2026-05-23T13:20:00Z
primary_retrieval_questions:
  - What is ThoughtLedger?
  - How does IDA explain visible reasoning?
  - Does ThoughtLedger expose hidden chain of thought?
  - What is the safer alternative to blocked actions?
boundary: Visible reasoning summary only. No raw hidden chain-of-thought. No writes, patches, tickets, or memory mutation.
---

# ThoughtLedger 132

ThoughtLedger is IDA's visible reasoning summary.

It explains what IDA can safely say about a decision without exposing raw hidden chain-of-thought.

ThoughtLedger records:

- current belief
- evidence summary
- uncertainties
- assumptions
- tempting actions
- blocked actions
- safer alternatives
- recommended next move
- observed project
- affected project

## Rules

ThoughtLedger must:

- stay concise and structured
- explain uncertainty instead of pretending certainty
- explain blocked actions and safer alternatives
- preserve observedProject and affectedProject
- consume ConscienceAgent output when supplied

ThoughtLedger must not:

- expose hidden chain-of-thought
- execute actions
- patch code
- create tickets
- mutate memory
- approve writes

## Boundary

ThoughtLedger is a visible reasoning summary only. It does not grant autonomy.
