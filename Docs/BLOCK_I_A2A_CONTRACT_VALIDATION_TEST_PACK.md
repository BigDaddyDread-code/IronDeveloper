# Block I A2A Contract Validation Test Pack

## Purpose

PR96 adds a focused contract validation test pack for the Block I agent-to-agent handoff spine.

The pack proves that the A2A contract pieces compose as evidence-only backend contracts:

- Agent Handoff contract
- allowed-use evidence references
- no-authority-transfer validation
- durable handoff store contract
- ThoughtLedger handoff entry
- grounding evidence reference

This document describes the test pack. It does not define a runtime, transport, API, CLI, workflow runner, or execution path.

## Composition Chain

The tested composition is:

```text
AgentHandoff
-> allowed-use evidence
-> AgentHandoffAuthorityTransferValidator
-> durable handoff store contract
-> ThoughtLedgerHandoffEntry
-> GroundingEvidenceReference
```

The chain must preserve the same project, run, source agent, target agent, subject, evidence identity, allowed-use values, and constraints across the handoff, ThoughtLedger entry, and grounding receipt surfaces.

## Evidence-Only Semantics

A valid handoff record means validated context and evidence were recorded. It does not mean the target agent received it, accepted it, acted on it, continued workflow, satisfied policy, received approval, or received authority.

A ThoughtLedger handoff entry means a validated durable handoff was summarized safely. It does not deliver the envelope, continue workflow, grant approval, satisfy policy, mutate source, promote memory, or approve release.

A grounding evidence reference means a claim is tied to evidence. It does not make the claim true, accepted, approved, executable, or promotable.

## Authority Boundary

The test pack proves that the composed A2A spine does not create:

- approval
- execution permission
- workflow continuation
- source mutation
- memory promotion
- accepted memory
- release approval
- policy satisfaction
- authority transfer

Authority flags must remain false across handoff, durable-store-facing request shapes, ThoughtLedger entries, and grounding references.

## Hidden Reasoning Boundary

The test pack rejects hidden or private reasoning markers in handoff summaries, evidence summaries, metadata notes, ThoughtLedger handoff summaries, grounding summaries, and grounding claims.

It also rejects raw dump style evidence that tries to move private prompt, completion, chain-of-thought, scratchpad, or hidden reasoning text through the A2A spine.

## Static No-Runtime Boundary

The test pack verifies that PR96 does not add references from API, CLI, or database surfaces to the A2A contract validation pack.

PR96 adds no SQL migration, stored procedure, controller, endpoint, CLI command, runtime service, scheduler, orchestrator, message bus, queue, transport, receiver, dispatcher, workflow runner, LangGraph path, source apply path, memory promotion path, accepted memory path, approval satisfaction path, or execution path.

## What This Test Pack Proves

The pack proves that PR90 through PR95 contracts compose without turning evidence into authority.

It proves allowed-use evidence stays bounded, authority-transfer validation blocks dangerous shapes, durable storage remains a filing cabinet, ThoughtLedger entries remain summaries, and grounding references remain receipts.

## What This Test Pack Does Not Prove

This pack does not prove runtime delivery, inbox processing, target-agent acceptance, workflow continuation, LangGraph orchestration, API or CLI exposure, source apply, memory promotion, release approval, policy satisfaction, or execution.

Those capabilities remain explicitly outside Block I at this point.
