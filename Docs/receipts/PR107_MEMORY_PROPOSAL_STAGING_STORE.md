# PR107 - Memory Proposal Staging Store Receipt

## Summary

PR107 adds a durable SQL-backed memory proposal staging store.

The staged proposal record is evidence for review only. It does not create accepted memory, promote memory, satisfy policy, grant approval, grant execution, continue workflow, mutate source, or index retrieval content.

## Added objects

- `memory.MemoryProposal`
- `memory.MemoryProposalEvidenceReference`
- `memory.MemoryProposalGroundingReference`
- `memory.MemoryProposalWorkflowReference`
- `memory.usp_MemoryProposal_Create`
- `memory.usp_MemoryProposal_Get`
- `memory.usp_MemoryProposal_ListByProject`
- `memory.usp_MemoryProposal_ListByStatus`
- `memory.usp_MemoryProposal_ListByWorkflowRun`
- `memory.usp_MemoryProposal_ListBySource`

## Guardrails

- SQL check constraints keep authority flags false.
- Insert triggers reject raw/private reasoning and authority language.
- Update/delete triggers keep proposal rows append-only.
- Runtime role can execute approved procedures and read the staging tables.
- Runtime role is denied direct insert, update, delete, and schema alteration.

## Non-goals

No API, CLI, UI, scheduler, worker, model call, vector indexing, retrieval authority, accepted memory creation, source mutation, or memory promotion is introduced.
