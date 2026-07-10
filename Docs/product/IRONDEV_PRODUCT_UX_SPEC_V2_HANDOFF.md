# IronDev Product and UX Specification v2 - Developer Handoff and Acceptance

This module continues the [system behavior contract](IRONDEV_PRODUCT_UX_SPEC_V2_SYSTEM.md) and covers sections 22-24 and the appendices of the complete specification.

---

## 22. Developer handoff

### 22.1 React boundaries

Recommended feature boundaries:

```text
app/
  routing, session, tenant and project context
features/
  auth/
  projects/
  setup/
  board/
  chat/
    channels/
    thread/
    composer/
    context/
    artifactDrafts/
  workItems/
  library/
    documents/
    tools/
    members/
    governance/
components/
  shared interaction and state components
design-system/
  tokens, primitives, surfaces, typography
```

### 22.2 Truth-state rules

- Server state library/cache keys include tenant ID, project ID, route object ID, and relevant revision.
- Changing tenant clears all tenant-scoped queries and local drafts after explicit draft handling.
- Changing project clears work item, Chat channel, attached context, and Library detail state.
- Mutations do not locally mark success before the backend returns the accepted object and revision.
- A successful mutation is followed by targeted revalidation.
- Offline mode does not pretend to execute governed actions. Draft message composition may be retained locally only when policy permits and is clearly unsent.

### 22.3 Required response material

Action endpoints should return enough user-facing material to avoid client inference:

- allowed/blocked state;
- concise human message;
- next safe actions or remedy identifiers;
- current object revision;
- actor eligibility where appropriate;
- resulting object link/identifier on success;
- technical reason code and correlation ID for details;
- evidence references when generated.

### 22.4 Collaboration events

Useful event categories:

- channel message created/edited/deleted;
- mention created;
- read marker changed;
- ticket draft changed;
- assignment/waiting-on changed;
- document version created or processing changed;
- tool configuration/health changed;
- Work Item stage/gate changed;
- finding disposition, approval, continuation, and apply recorded.

The client may consume streaming events, but it must be able to reconcile from canonical REST/read models after reconnect.

### 22.5 Security and privacy

- Never place secrets in client logs, Chat messages, source excerpts, or downloadable evidence.
- Respect tenant, project, channel, and document visibility on every retrieval.
- Do not rely on hidden UI controls as authorization.
- Persist only the minimum client state needed for safe resume.
- Preserve actor attribution after membership removal according to audit policy.

---

## 23. Implementation order

This is an implementation sequence, not a product maturity label.

1. Resolve tenant entry and project context safely.
2. Establish target navigation and route outcome states.
3. Stabilize Board and Work Item against backend truth.
4. Make current Chat the first-class Chat surface and remove backstage metadata from the default rail.
5. Add channel/session navigation, explicit assistant invocation in shared channels, and multi-user message states.
6. Complete Chat-to-ticket draft review and backend-confirmed ticket creation.
7. Add Documents routes, upload/processing states, detail, versions, and Chat attachment.
8. Add Tools routes, effective scopes, health, configuration eligibility, and tool-use disclosure.
9. Add member administration and channel membership surfaces.
10. Add conflict handling, notifications, and presence without weakening authority boundaries.
11. Link complete evidence through Library and details.

A route enters primary navigation only when its normal journey is functional. Before that, direct access returns Not implemented rather than a decorative shell.

---

## 24. North-star acceptance test

A new user in a multi-user tenant can:

- sign in;
- skip tenant choice when only one tenant exists;
- choose a tenant when more than one exists;
- choose or connect a project;
- complete setup;
- understand the Board and who is waiting on whom;
- open Chat and distinguish human conversation from IronDev responses;
- ask IronDev to inspect project material and see the actual sources used;
- upload or attach a document and wait for honest processing completion;
- collaborate on a ticket draft without overwriting another user's update;
- create a real ticket and open its Work Item;
- understand a blocked gate;
- start a governed run when eligible;
- see failed and repaired attempts;
- disposition findings;
- approve, continue, and apply through separate explicit actions;
- see which human performed each consequential action;
- inspect reports, receipts, documents, tools, and audit material in Library;

without reading architecture documentation or interpreting internal backend terminology.

The experience should feel:

> Simple on the surface, rigorous underneath.

---

## Appendix A. Screen review checklist

For every screen and material state, design and implementation review must answer:

1. What is the user trying to accomplish?
2. What information is needed for the current decision?
3. What information is merely proof and belongs behind details?
4. What is the single primary action?
5. What can block the action?
6. How is the blocker explained in plain language?
7. What is the next safe action?
8. What technical evidence remains available on demand?
9. What happens after refresh or restart?
10. What happens when another user changes the object?
11. What happens when the backend is unavailable?
12. Does anything imply authority the backend has not granted?
13. Is the acting human clearly identified where it matters?
14. Is the tenant/project/channel/document scope unambiguous?

## Appendix B. Source alignment

This specification should be implemented alongside the existing project contracts for:

- Chat, Discussion, Ticket, and Build boundaries;
- tenant role vocabulary and the rule that role is not mutation authority;
- project channels, members, messages, context links, assistant turns, read markers, and pins;
- versioned project documents and artifact links;
- governed tool definitions, declared scopes, results, and evidence.

Where an existing backend contract and this UX target differ, the product team must record the implementation decision explicitly. The UI must not bridge the difference by inventing state.
