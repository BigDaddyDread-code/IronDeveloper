# IronDev Multi-User Chat and Collaboration Specification

**Version:** 1.0  
**Date:** 10 July 2026  
**Scope:** Chat, channels, participants, mentions, source inspection, shared drafts, and artifact handoff

This document defines the detailed product contract for IronDev Chat in a multi-user project. It extends the existing separation between Chat, Discussion, Document, Decision, Ticket, Build, and Run evidence.

Related project material:

- [Chat, Discussion, Ticket, and Build boundaries](CHAT_DISCUSSION_TICKET_BUILD_BOUNDARIES.md)
- [Project channel contracts](../../IronDev.Core/Channels/ProjectChannelModels.cs)
- [Tenant role vocabulary](../../IronDev.Infrastructure/Services/UserService.cs)

---

## 1. Product position

Chat is a primary product surface. It is the place where people and IronDev:

- ask questions;
- inspect project context;
- discuss choices;
- save durable thinking;
- form structured drafts;
- link conversations to work.

Chat is not Build, approval, source apply, or a generic model playground.

The top-level product navigation is:

```text
Board | Chat | Work Item | Library
```

**New work** on the Board starts a new direct IronDev session in Chat.

---

## 2. Collaboration model

### 2.1 Channel types

| Type | Participants | Typical use |
| --- | --- | --- |
| Project channel | Project-visible members | General, architecture, tickets, review, release |
| Members-only channel | Selected members | Restricted technical or product discussion |
| Direct IronDev session | Creator plus IronDev by default | Exploration and ticket formation |
| Work Item channel | Users who can view the linked item | Discussion tied to one ticket/work item |
| Run/review channel | Users who can view the linked evidence | Failure, finding, or review discussion |

A direct session may be shared through an explicit membership or visibility action. It is never silently published to the project.

### 2.2 Channel roles

- Owner
- Moderator
- Member
- Read only

These roles control channel visibility, membership, pinning, and moderation. They do not grant workflow authority, approval eligibility, source mutation, or tool access.

### 2.3 Message roles

- User
- Assistant
- System notice
- Event link

Every message has an author/role, created time, status, and optional reply/thread relationship. Edited and deleted states remain visible.

---

## 3. Chat workspace

### 3.1 Desktop layout

```text
+--------------------+----------------------------------+----------------------+
| Channel rail       | Thread                           | Context and draft    |
|                    |                                  |                      |
| Search             | Channel header                   | Sources              |
| Mentions           | Messages                         | Candidate criteria   |
| Project channels   | Replies                          | Open questions       |
| Direct sessions    |                                  | Artifact actions     |
| Linked work        | Composer                         |                      |
+--------------------+----------------------------------+----------------------+
```

The thread is always the main surface. Channel and context rails collapse at narrower widths.

### 3.2 Channel header

Show:

- channel/session name;
- visibility;
- linked project object when applicable;
- compact participant count;
- notification level;
- one overflow menu.

Do not show provider/model information, raw trace IDs, mode confidence, or governance prose permanently.

### 3.3 Thread behavior

- Load the latest useful message range and allow older history retrieval.
- Preserve reading position when navigating away and returning.
- New messages append without stealing focus.
- When the user is not at the bottom, show **New messages** rather than jumping.
- Use a date separator and compact unread marker.
- Replies may open an inline thread or a side panel, but the relationship remains accessible by keyboard and screen reader.

---

## 4. Human and IronDev interaction

### 4.1 Explicit invocation

In a direct IronDev session, Send creates an assistant request.

In shared channels, IronDev responds only when the user:

- mentions `@IronDev`;
- activates **Ask IronDev** on a message;
- selects messages and chooses **Ask IronDev about selection**; or
- uses a clearly labelled composer mode.

This prevents unsolicited assistant participation and makes the requesting human unambiguous.

### 4.2 Assistant turn states

- Requested
- Inspecting context
- Answered
- Failed
- Refused

The UI may combine Requested and Inspecting into one understandable progress state. It must not invent substeps.

Failure copy:

> IronDev could not answer this request. Your message is saved. Try again or view details.

Refusal copy is specific to the policy or missing permission without exposing unnecessary internal rules.

### 4.3 Source inspection

Assistant answers show a compact **Sources used** control. The context rail lists:

- repository files/symbols;
- documents;
- decisions;
- tickets/work items;
- runs/findings;
- tool activity.

For each source show label, type, relevance, status, and reason used. Evidence metadata is disclosed separately.

States:

- Used
- Partially available
- Unavailable
- Stale since answer
- Access removed

Editing or deleting a source message does not rewrite the assistant answer. The answer is marked as based on an earlier source state.

---

## 5. Composer and context

### 5.1 Composer controls

The default composer includes:

- multi-line text input;
- mentions;
- attach/link context;
- send.

Optional secondary actions live in an attachment menu:

- Link repository file or symbol
- Link existing document
- Upload document
- Link decision
- Link ticket/work item
- Link run/finding

Do not place Build controls, tool selectors, provider selectors, or route metadata beside the input.

### 5.2 Upload from Chat

Upload follows the Documents contract.

1. Select file.
2. Upload to the current project.
3. Show Uploading/Processing.
4. On Ready, ask whether to attach it to the current request/session if that was not already explicit.
5. If processing fails, preserve the conversation and show a retry path.

IronDev cannot cite or claim to have read a document before the backend reports it Ready and included in the turn context.

### 5.3 Draft persistence

- Unsent text is saved per user, project, and channel when policy permits.
- Tenant/project switching does not carry the draft across scope.
- Offline drafts are visibly unsent.
- Attachments are not assumed uploaded merely because they appear in a local draft.

---

## 6. Saved thinking and artifact actions

### 6.1 Save Discussion

Creates a durable project Discussion with:

- title;
- selected message range or generated summary;
- source message links;
- creator and contributors;
- visibility;
- created time.

A Discussion remains project thinking. It is not a decision or ticket.

### 6.2 Create Document

Creates a versioned project document from selected conversation material. The user reviews title, type, content, visibility, and source links before creation.

### 6.3 Propose Decision

Creates a decision proposal or draft. Acceptance is a separate explicit backend-governed action. The action label must not say **Record decision** unless the current user is actually performing the accepted-decision ceremony.

### 6.4 Review ticket draft

A ticket draft contains:

- candidate title;
- problem/outcome;
- proposed change;
- business rules;
- acceptance criteria;
- constraints;
- affected areas;
- assumptions;
- open questions;
- conflicts;
- source links.

The draft panel prioritizes unresolved questions and conflicts. Confidence is advisory and secondary.

### 6.5 Create ticket

The user reviews the complete draft and chooses **Create ticket**. Backend success returns the real ticket/work item ID.

Ticket creation does not imply readiness, approval, execution, affected-file confirmation, or source mutation.

---

## 7. Multi-user draft and edit rules

### 7.1 Revision control

Shared artifact drafts include a backend revision.

- Save submits the reviewed revision.
- A stale save returns conflict.
- The UI shows who changed the draft and what sections changed when available.
- The primary action becomes **Review latest draft**.
- Never apply last-write-wins without explicit backend contract.

### 7.2 Contributors

Show creator, last editor, and contributor avatars/names. This is collaboration context, not an authority statement.

### 7.3 Confirmation eligibility

A user may contribute without being allowed to create a ticket or accept a decision. The backend returns available actions. An ineligible user sees:

> You can contribute to this draft, but you cannot create the ticket.

Safe action: **Request creation** or mention an eligible teammate, only if that capability is implemented.

### 7.4 Message edits and downstream material

When an edited message was used in an answer or draft:

- preserve the original linked revision;
- mark the answer/draft as based on earlier content;
- offer **Refresh from conversation**;
- require review before overwriting a structured draft.

---

## 8. Mentions, unread state, and notifications

### 8.1 Mentions

- `@person` creates a user mention.
- `@IronDev` creates an assistant request in shared channels.
- Mention suggestions include only users/agents visible and usable in the current project/channel.
- Mentions do not bypass document or object permissions.

### 8.2 Unread state

Channel unread count is based on the user's read marker. It is convenience state and may be eventually consistent.

### 8.3 Notification levels

- All
- Mentions
- None

Work Item waiting-on and approval requirements may create product notifications independently of channel notification level because they are workflow obligations, not chat subscriptions.

---

## 9. Presence and activity

Presence is lightweight and non-authoritative.

Show:

- active viewers in a channel or Work Item when real;
- typing indicator only when backed by real ephemeral events;
- last active only when policy and data support it.

Do not use presence as a lock. Concurrency is enforced through revisions and backend gates.

---

## 10. Moderation, retention, and audit

- Message edits are attributed and timestamped.
- Message deletion follows retention policy.
- A referenced message may become a tombstone rather than disappear.
- Pins are navigation convenience, not project truth.
- Channel archive is reversible navigation state when backend supports it.
- Assistant prompts/answers used for durable evidence store only the approved audit material; never expose hidden chain-of-thought.
- Membership removal immediately re-evaluates access, but prior authored history retains actor attribution.

---

## 11. Error and blocked copy

| Situation | User-facing copy | Next action |
| --- | --- | --- |
| Chat unavailable | Chat is unavailable. Your unsent message is preserved. | Retry |
| Inspection unavailable | IronDev could not inspect project sources. | Ask without sources / Retry, when allowed |
| Source access removed | One source used by this answer is no longer available to you. | View available sources |
| Draft stale | This draft changed while you were reviewing it. | Review latest draft |
| Ticket create blocked | The ticket cannot be created until the open questions are resolved. | Ask next question |
| Permission | You can read this channel but cannot post messages. | Return / Request access if real |
| Upload processing failed | The document was uploaded but could not be processed. | Retry processing / View details |
| Assistant refused | IronDev cannot perform that request under the current project policy. | View allowed options |

---

## 12. Accessibility

- Channel rail is a labelled navigation region.
- Message list is a live region with restrained announcements.
- Each message exposes author, role, time, status, and reply relationship.
- Composer errors are programmatically associated with the input.
- Mention suggestions are keyboard operable.
- Context rail can be reached and dismissed without losing composer focus.
- New-message announcements do not read full assistant responses automatically.
- Typing indicators and presence are not conveyed by color alone.

---

## 13. Acceptance tests

1. A user can open Chat and distinguish project channels, direct sessions, and linked Work Item channels.
2. IronDev does not respond in a shared channel without explicit invocation.
3. The requesting user is recorded for every assistant turn.
4. An answer shows the actual sources used and honest unavailable states.
5. A user can upload a document from Chat without losing the conversation.
6. The document is not used before processing is Ready.
7. Two users editing a ticket draft cannot silently overwrite each other.
8. An ineligible user cannot create a ticket merely because the button was visible earlier.
9. A message saying "approved" creates no approval.
10. Ticket creation returns a real ID and routes to the Work Item.
11. Channel membership does not bypass linked-object permissions.
12. Tenant/project switching clears scoped Chat state safely.
