# IronDev Product and UX Specification v2 - System Behavior

This module continues the [product surfaces contract](IRONDEV_PRODUCT_UX_SPEC_V2_SURFACES.md) and covers sections 15-21 of the complete specification.

---

## 15. System states and language contract

### 15.1 Universal state set

Every important screen intentionally designs:

- Loading
- Ready
- Empty
- Blocked
- Degraded/unavailable
- Permission required
- Conflict/stale
- Not found
- Not implemented

### 15.2 Empty is not unavailable

Examples:

- **No work yet** - real empty Board.
- **Work could not be loaded** - unavailable Board.
- **No documents match these filters** - filtered empty.
- **You do not have access to this channel** - permission state.

### 15.3 Not implemented

Copy:

> **Not implemented**  
> This destination exists, but the capability is not available.

Offer only a real safe navigation action, such as **Back to Library**. Do not show decorative disabled forms that imply partial functionality.

### 15.4 Prohibited product language

Do not use the following to mask missing behavior:

- Demo
- Alpha
- Beta
- Preview
- Experimental
- Coming soon
- Almost ready

A genuine LocalTest environment label is acceptable because it identifies the environment, not feature maturity.

---

## 16. Component inventory

| Component | Purpose | Key states |
| --- | --- | --- |
| Global header | Project context and primary navigation | default, offline, tenant switch available |
| Health indicator/drawer | Compact health and detailed diagnostics | healthy, attention, offline |
| Presence control | Active project collaborators | none, one, multiple, stale |
| Project tile | Select project | ready, setup required, unavailable |
| Connect tile | Start project connection | default, focus, disabled only with reason |
| Setup task | One current setup action | loading, blocked, submitting, complete, unavailable |
| Board card | Compact shared work state | normal, blocked, running, waiting on user, repaired, done |
| Assignee/waiting chip | Coordination | assigned, waiting, unassigned |
| Channel rail | Navigate Chat spaces | unread, mention, selected, archived, restricted |
| Chat message | Human, assistant, notice, or event content | sending, sent, failed, edited, deleted |
| Chat composer | Send and attach context | ready, sending, blocked, offline |
| Context picker | Attach project material | loading, searchable, empty, permission denied |
| Source row | Explain inspected context | used, unavailable, partial, stale |
| Artifact draft panel | Form document/decision/ticket | draft, incomplete, conflict, ready to review |
| Stage rail | Show Work Item lifecycle | current, complete, blocked, unavailable |
| Gate | Explain transition blocker | ready, blocked, reevaluating, unavailable |
| Contract summary/rail | Persistent work contract | compact, expanded, stale |
| Finding card | Review and disposition | open, disposition required, disposition recorded |
| Approval ceremony | Consume approval authority | eligible, ineligible, stale package, submitting |
| Apply preflight | Consume source-mutation authority | ready, conflict, drift, submitting |
| Document row/card | Durable project context | processing, ready, failed, archived, restricted |
| Upload panel | Add a project source | selecting, uploading, retry, unsupported |
| Tool card | Effective project capability | connected, setup required, unavailable, disabled |
| Tool scope panel | Explain capability and risk | read-only, mutating, restricted |
| Details drawer | Backstage evidence | loading, ready, unavailable |
| State panel | Empty/error/permission/not implemented | state-specific |

---

## 17. State matrix

| Surface | Loading | Empty | Blocked | Degraded/unavailable | Conflict | Not implemented |
| --- | --- | --- | --- | --- | --- | --- |
| Sign in | connection check behind form | n/a | invalid credentials | auth service unavailable | n/a | n/a |
| Tenant | tenants loading | zero access | selection denied | tenant service unavailable | membership changed | n/a |
| Projects | tiles loading | Connect tile first | project access denied | statuses unavailable | project changed | n/a |
| Setup | readiness loading | n/a | current task | check unavailable | task changed | unbuilt task route |
| Board | work loading | no work | project readiness | work unavailable | card changed during action | unbuilt subview |
| Chat | thread loading | conversation ready | send/context blocked | Chat or inspection unavailable | draft/message stale | unbuilt artifact action |
| Work Item | item loading | n/a | gate | run/evidence unavailable | contract/package stale | unbuilt stage action |
| Documents | documents loading | no documents | upload/read permission | processing or service unavailable | version stale | unsupported future editor |
| Tools | tools loading | no configured tools | permission/setup/policy | health unavailable | configuration changed | declared unbuilt connector |
| Members | members loading | no additional members | admin permission | directory unavailable | role changed | unbuilt invitation method |
| Library | section counts loading | section-specific | permission | partial section unavailable | n/a | destination-specific |

---

## 18. UX writing system

### 18.1 Pattern

1. Human sentence.
2. Consequence.
3. Next safe action.
4. Technical evidence on demand.

Example:

> **Cannot create the ticket**  
> The draft changed while you were reviewing it.  
> Review the latest draft, then try again.  
> `Draft revision mismatch` under Details.

### 18.2 Preferred terminology

| Use | Avoid as primary user language |
| --- | --- |
| Project setup | Provisioning |
| Chat | Prompt playground |
| Source inspected | Retrieval payload |
| Ready for review | Package state enum |
| Waiting on Robert | Actor ID |
| Not implemented | Coming soon / preview |
| Unavailable | Failed subsystem code |
| Review latest version | ETag mismatch |
| Applied successfully | Mutation operation completed |

### 18.3 Action labels

Use contextual actions:

- Board: New work
- Chat: Send, Ask IronDev, Review ticket draft
- Documents: Upload document
- Tools: Add tool or Request tool
- Decisions: Propose decision / Record decision when eligible
- Projects: Connect project
- Work Item: action returned by current backend state

Avoid one global **Add** button.

---

## 19. Responsive desktop behavior

### 19.1 1920 x 1080

- Full header labels.
- Board five-column pipeline visible where practical.
- Chat uses three panes: channels, conversation, working material.
- Work Item can show main stage plus contract rail.

### 19.2 1366 x 768

- Preserve readable density.
- Board uses compact cards and horizontal scroll rather than reordering stages.
- Chat channel rail narrows; working-material rail remains collapsible.
- Work Item contract rail may default compact.

### 19.3 Narrow Tauri window

- Primary navigation remains accessible through a compact menu without changing route hierarchy.
- Board retains left-to-right stage order with horizontal scroll.
- Chat conversation occupies the window; channels and context become drawers.
- Tables become stacked rows with labels.
- Primary action remains visible without covering content.
- Do not shrink operational text below the minimum readable size.

---

## 20. Accessibility

### 20.1 Keyboard

- Logical tab order follows visible reading order.
- Arrow-key navigation is supported for Board columns/cards, channel list, tabs, and menus where appropriate.
- `Enter`/`Space` activates complete clickable tiles and cards.
- Modals and drawers trap focus, return it to the invoking control, and close with `Escape` unless an authority ceremony requires explicit cancellation.
- The Chat composer supports send with a documented shortcut while retaining a way to insert new lines.

### 20.2 Focus and semantics

- Strong visible focus ring independent of color.
- Use buttons and links, not clickable `div` elements.
- Full project tiles have one accessible name and one action.
- Board columns use headings and card lists.
- Chat messages identify author, time, role, edit state, and thread relationship semantically.
- Status badges never carry meaning by color alone.

### 20.3 Live announcements

Announce:

- sign-in and loading errors;
- message send success/failure without reading the full message;
- new messages when the user is at the end of the thread;
- assistant inspection completion;
- readiness re-evaluation;
- run stage changes;
- conflict/stale responses;
- upload and processing state changes;
- approval/apply outcomes.

Do not repeatedly announce timers or every low-level backend event.

### 20.4 Text and contrast

- Normal operational text: at least 14 px equivalent.
- Dense metadata: at least 12 px equivalent with adequate contrast.
- Support 200% text zoom without loss of controls or horizontal page-level scrolling beyond intentionally scrollable Board/data regions.
- Reduced motion disables decorative transitions and preserves state changes through text.

---

## 21. Visual system

Character:

- quiet desktop engineering tool;
- calm, precise, compact, readable;
- evidence-first without bureaucracy;
- professional, not futuristic;
- no neon, glassmorphism, giant gradients, or hacker login styling.

Recommended tokens:

| Token | Value |
| --- | --- |
| Page ground | `#F4F4F1` |
| Panel | `#FFFFFF` |
| Primary text | `#22262B` |
| Secondary text | `#626A70` |
| Border | `#D9DDD9` |
| Primary accent | `#0E6B54` |
| Accent hover | `#0A5946` |
| Blocked/amber | `#A8690F` |
| Error/red | `#A63232` |
| Information blue | restrained and used only when needed |

Typography:

- Segoe UI Variable or equivalent modern system sans;
- Cascadia Code or equivalent for code, paths, IDs, and hashes;
- compact tags;
- thin neutral borders;
- restrained shadows;
- modest radii;
- clear focus states.

---
