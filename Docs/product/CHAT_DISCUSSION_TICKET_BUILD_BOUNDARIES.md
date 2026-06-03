# Chat, Discussion, Ticket, And Build Boundaries

IronDev separates thinking, planning, and execution so the product does not collapse into one overloaded cockpit.

## Product Rule

Chat is for thinking and shaping work.

Build is for executing work.

Tickets are the handoff boundary.

Discussions are saved project thinking.

## Workspace Boundaries

| Concept | Boundary |
| --- | --- |
| Chat | Conversation and intent discovery. Chat can review project state, answer project-aware questions, and expose response actions. Chat must not contain build execution controls, sandbox run state, patch validation, or run logs. |
| Discussion | Saved project thinking captured from Chat. A Discussion preserves markdown content and can later become a Document, Decision, or Ticket. |
| Document | Versioned project memory. Documents store durable context that retrieval and project knowledge can cite. |
| Decision | Accepted project guardrail. Decisions describe what IronDev should respect when planning or building. |
| Ticket | Structured, buildable work. Tickets carry intent, acceptance criteria, source links, readiness, and execution evidence. |
| Build | Controlled execution workflow. Build starts from a Ticket or valid buildable plan and owns sandbox runs, validation, evidence, and review packages. |
| Run | Execution evidence and history. Runs record what executed, what passed or failed, and what needs review. |
| Knowledge | Stored and retrievable project context, including documents, decisions, discussions, traces, and indexing status. |

## Allowed Flow

```text
Chat
-> Discussion
-> Document / Decision / Ticket
-> Build
-> Run evidence
-> Review package
```

## UI Rules

- The Chat composer only supports message entry and sending.
- Chat response actions may include Copy Markdown, Save Discussion, View Sources, and later Create Ticket when backed by a real draft flow.
- Permanent Build controls must not appear beside the Chat input.
- Build handoff appears only after a Ticket or valid buildable plan exists.
- Context and sources in Chat are secondary and collapsible.
- Build owns sandbox execution, command evidence, run review, and approval boundaries.

## Current Implementation Notes

- Chat persists messages through project-scoped backend chat sessions.
- Assistant responses can be saved as project-scoped discussion documents.
- Build remains a separate workspace and uses the discussion-to-ticket-to-run backend spine.
- Future work should add first-class Discussion browse/detail support under Knowledge before expanding Chat actions further.
