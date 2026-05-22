---
id: 20260522004246342-alpha-0-1-manual-validation
project: IronDev
title: alpha-0.1-manual-validation
document_type: Guide
authority: WorkingDraft
source: C:\Users\bob\source\repos\AIDeveloper\Docs\alpha-0.1-manual-validation.md
dogfood_run_id: DogfoodDocsSeed-20260522-012
created_utc: 2026-05-22T00:42:46.3553229+00:00
---

# IronDev Alpha 0.1 Manual Validation

Version: 0.1.0-alpha
Workflow: Project-Aware Ticket and Proposal Workflow

## Purpose

Validate that IronDev can guide a small project through the alpha flow:

1. Create or import a project.
2. Confirm the project profile.
3. Index the project.
4. Chat with project context.
5. Create a structured ticket.
6. Check build readiness.
7. Generate a Builder proposal.
8. Review the diff.
9. Optionally apply/build/test in a sandbox.
10. Inspect trace output and exported context.

## Test Project

Use BookSeller or another small external .NET project. BookSeller must remain outside the IronDev repository.

Record:

- Project name:
- Project path:
- Solution/project file:
- Test framework:
- Build command:
- Test command:
- Safe write root:

## Pass Criteria

- IronDev imports or opens the project without creating duplicate records.
- Project profile fields are detected or editable and persist after reload.
- Index status is visible before and after indexing.
- Chat can answer project-aware questions using profile, memory, and code context.
- "Create a ticket to add book sorting" opens the draft ticket workflow.
- The ticket includes requirements, acceptance criteria, affected files or a clear note that none were found, non-goals, readiness, and open questions.
- Build readiness blocks missing profile data, stale index, unclear scope, or relevant unresolved open questions.
- Builder proposal generation is proposal-first and does not write files before approval.
- Proposal validation checks path safety, architecture compatibility, ticket coverage, and test framework compatibility.
- Sandbox apply cannot write outside SafeWriteRoot.
- Trace output shows the chain from chat to ticket to readiness to proposal.
- Context export includes IronDev version, project profile, memory, tickets, plans, and source traceability.

## Scenario 1: Existing Project Import

1. Start IronDev.
2. Import the external BookSeller folder.
3. Confirm detected profile fields.
4. Save the profile.
5. Index the project.
6. Close and reopen the project.

Expected:

- Profile values persist.
- Index status is visible.
- The project path still points to the external BookSeller folder.

## Scenario 2: Project Memory

1. Add or verify the Project Summary.
2. Add a Project Fact: "BookSeller currently uses in-memory storage."
3. Add a Recommendation: "SQLite plus Dapper is recommended for BookSeller persistence."
4. Add an Open Question: "Should BookSeller persistence use SQLite plus Dapper or another approach?"
5. Ask chat: "industry standard" after a persistence discussion.

Expected:

- Memory is stored as typed context, not a generic note.
- Pending recommendation is not treated as binding.
- Open question can block related build readiness.
- Short follow-up resolves against the active persistence topic once the resolver milestone lands.

## Scenario 3: Chat to Ticket

Prompt:

```text
Create a ticket to add book sorting.
```

Expected:

- IronDev opens the draft ticket workflow.
- Draft is structured and reviewable.
- Ticket source references include the chat session/message.
- No generic advice-only answer appears for the explicit ticket request.

## Scenario 4: Builder Proposal

1. Select the ticket from Scenario 3.
2. Run Build This or Generate Proposal.
3. Review the Builder Workbench diff.
4. Do not approve immediately.

Expected:

- Builder generates a proposal only.
- Diff is readable.
- No files are written before approval.
- Trace includes readiness and proposal generation.

## Scenario 5: Sandbox Apply/Build/Test

Only run this against an explicitly safe sandbox project.

1. Approve the proposal.
2. Run build.
3. Run tests.
4. Inspect trace output.

Expected:

- Files written are inside SafeWriteRoot.
- Build command and test command come from Project Profile.
- Build/test output is visible.
- Failures are captured without hiding the proposed changes.

## Scenario 6: Context Export

1. Export project context pack.
2. Open the exported text.

Expected:

- Export shows IronDev Alpha 0.1 and version.
- Export includes profile, summary, decisions, context documents, tickets, plans, observable state, and source traceability.
- Secrets are scrubbed.

## Regression Notes

Log each failure with:

- Step:
- Expected:
- Actual:
- Trace id or trace group:
- Screenshot or copied error:
- Whether the project was indexed:
- Whether the ticket was build-ready: