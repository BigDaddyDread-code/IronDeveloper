# IronDev Chat — Grounding Test Matrix

> **Purpose:** Define pass/fail acceptance criteria for Chat context retrieval and answer quality.
> No features are implemented here. This is a test-only specification.
>
> **Constraints:** No Weaviate · No ticket deletion · No DB schema changes · SQL/local index only.

---

## Scoring Rubric

| Score | Meaning |
|---|---|
| **0** | Generic / wrong files — no real IronDev classes mentioned |
| **1** | Partially grounded — misses key files for the intent |
| **2** | Grounded — cites real files/classes ✅ Minimum acceptable |
| **3** | Fully grounded — files, classes, tests, risks, non-goals ✅ Merge quality |

**Minimum acceptable:** 2 · **Merge quality:** 3

---

## Anti-Pattern Flag List

Flag any answer whose *primary recommendation* relies on these generic terms **without** pairing them with a real IronDev file/class:

- `"Data Model"` without `DataModels.cs` / `ProjectTicket`
- `"Service Layer"` without `TicketService.cs` / `ChatHistoryService.cs`
- `"Database Schema"` without `Database/local_dev_setup.sql` or `rebuild_db.sql`
- `"UI Component"` without `TicketsWorkspaceView.xaml` / `ChatWorkspaceView.xaml`
- `"Controller"` (there are no controllers in the WPF client)
- `"Repository"` (pattern not used; Dapper services are used directly)
- `"TicketService"` when no real `TicketService.cs` snippet was retrieved
- `"ChatService"` when no real `ChatHistoryService.cs` snippet was retrieved

---

## Test Case Spec Format

```json
{
  "question":        "...",
  "intent":          "SavedTicketManagement | DraftTicketFlow | CodeQuery | General",
  "mustIncludeAny":  ["SymbolOrFile", ...],
  "mustNotLeadWith": ["WrongFile", ...],
  "mustMention":     ["concept or phrase", ...],
  "mustNotMention":  ["forbidden phrase", ...],
  "pasRule":         "...",
  "failRule":        "..."
}
```

---

## Test 1 — Delete Saved Tickets

**User question:**
> "What do I have to do to delete tickets? What files are affected?"

**Intent:** `SavedTicketManagement`

**Expected high-priority context:**

| Priority | File / Symbol |
|---|---|
| High | `IronDeveloper/ViewModels/Workspaces/TicketsWorkspaceViewModel.cs` |
| High | `IronDeveloper/Views/Workspaces/TicketsWorkspaceView.xaml` |
| High | `IronDev.Infrastructure/Services/TicketService.cs` |
| High | `IronDev.Core/Models/DataModels.cs` → `ProjectTicket` |
| Medium | `Database/local_dev_setup.sql` |
| Medium | `Database/rebuild_db.sql` |
| Medium | Ticket-related integration tests |
| Low | `DraftTicketDtos.cs` (do not lead with this) |
| Low | `CodebaseTicketGeneratorModels.cs` (do not lead with this) |

**Answer must include:**
- Explain this is about saved `ProjectTicket`, not `DraftTicket`
- Recommend soft delete / archive before hard delete
- Mention `DeleteSelectedTicketCommand` or `ArchiveSelectedTicketCommand` in `TicketsWorkspaceViewModel`
- Mention UI delete/archive action with confirmation in `TicketsWorkspaceView.xaml`
- Mention service method `DeleteTicketAsync` / `ArchiveTicketAsync` with tenant/project guard in `TicketService.cs`
- Mention tests

**Answer must NOT include:**
- "Modify DraftTicket" as the primary recommendation
- `"Service Layer"` without naming `TicketService.cs`
- Generic CRUD advice only

**Automated spec:**
```json
{
  "question": "What do I have to do to delete tickets? What files are affected?",
  "intent": "SavedTicketManagement",
  "mustIncludeAny": [
    "TicketsWorkspaceViewModel",
    "TicketsWorkspaceView.xaml",
    "ProjectTicket",
    "TicketService"
  ],
  "mustNotLeadWith": [
    "DraftTicketDtos.cs",
    "DraftTicket",
    "CodebaseTicketGeneratorModels.cs"
  ],
  "mustMention": ["soft delete", "tenant", "confirmation"],
  "mustNotMention": ["Weaviate"]
}
```

**Pass:** Answer references real saved-ticket files/classes; explicitly avoids treating `DraftTicket` as the saved ticket model.  
**Fail:** `DraftTicketDtos.cs` is the primary affected file cited.

---

## Test 2 — Delete Old Chat Sessions

**User question:**
> "What would I need to do to delete old chats from Chat History?"

**Intent:** `CodeQuery` (chat session deletion)

**Expected high-priority context:**

| Priority | File / Symbol |
|---|---|
| High | `IronDeveloper/ViewModels/Workspaces/ChatWorkspaceViewModel.cs` |
| High | `IronDeveloper/Views/Workspaces/ChatWorkspaceView.xaml` |
| High | `IronDev.Infrastructure/Services/ChatHistoryService.cs` |
| High | `ProjectChatSessions` table (referenced in `local_dev_setup.sql`) |
| High | `ChatMessages` table |
| Medium | `Database/local_dev_setup.sql` |
| Medium | `Database/rebuild_db.sql` |
| Medium | Chat persistence integration tests |

**Answer must include:**
- Distinguish deleting a chat session from deleting individual messages
- Recommend archive/soft-delete before hard delete
- Add delete/archive command in `ChatWorkspaceViewModel`
- Add UI action in `ChatWorkspaceView.xaml`
- Add service method in `ChatHistoryService` with tenant/project checks
- Handle active chat deletion safely (navigate away first)
- Tests for tenant isolation and list refresh

**Answer must NOT include:**
- Generic `"ChatService"` unless that is confirmed as a real class name
- `"database query"` without naming `ProjectChatSessions` / `ChatMessages`
- Hard-delete messages without discussing safety

**Automated spec:**
```json
{
  "question": "What would I need to do to delete old chats from Chat History?",
  "intent": "CodeQuery",
  "mustIncludeAny": [
    "ChatWorkspaceViewModel",
    "ChatHistoryService",
    "ProjectChatSessions",
    "ChatMessages"
  ],
  "mustNotLeadWith": ["DraftTicket", "TicketService"],
  "mustMention": ["session", "tenant", "archive"],
  "mustNotMention": ["Weaviate"]
}
```

**Pass:** Answer identifies actual chat session/message persistence areas with real class names.  
**Fail:** Answer only says "update ChatService and database".

---

## Test 3 — Ticket List Shows Noisy Markdown

**User question:**
> "The ticket list shows noisy markdown fragments. What should I change?"

**Intent:** `SavedTicketManagement` (UI rendering)

**Expected high-priority context:**

| Priority | File / Symbol |
|---|---|
| High | `IronDeveloper/Views/Workspaces/TicketsWorkspaceView.xaml` |
| High | `IronDeveloper/ViewModels/Workspaces/TicketsWorkspaceViewModel.cs` |
| Medium | Any `MarkdownPreviewConverter` / text sanitiser converter if indexed |
| Low | `WorkspaceListItem` / controls library |

**Answer must include:**
- Fix ticket list `DataTemplate` in `TicketsWorkspaceView.xaml`
- Use `Title` as the primary display line
- Use a one-line summary preview (truncated)
- Apply `TextTrimming="CharacterEllipsis"`
- Keep status/priority compact
- Do not change database schema

**Answer must NOT include:**
- Backend ticket deletion logic
- `DraftTicketService`
- `CodebaseTicketGeneratorModels` (unless specifically relevant)

**Automated spec:**
```json
{
  "question": "The ticket list shows noisy markdown fragments. What should I change?",
  "intent": "SavedTicketManagement",
  "mustIncludeAny": [
    "TicketsWorkspaceView.xaml",
    "DataTemplate",
    "TextTrimming"
  ],
  "mustNotLeadWith": ["DraftTicketService", "database", "schema"],
  "mustMention": ["DataTemplate", "Title", "summary"],
  "mustNotMention": ["Weaviate", "schema change"]
}
```

**Pass:** Answer focuses on XAML list template / converter fix.  
**Fail:** Answer recommends changing a model or database first.

---

## Test 4 — Status/Priority/Type Dropdowns Clipped

**User question:**
> "Status, priority and type dropdowns are clipped. They show 'Dr', 'Me', 'Tas'. What files should I fix?"

**Intent:** `CodeQuery` (ticket form UI)

**Expected high-priority context:**

| Priority | File / Symbol |
|---|---|
| High | `IronDeveloper/Views/Workspaces/TicketsWorkspaceView.xaml` |
| High | `IronDeveloperControls` — `SelectionField` style/template (if used) |
| Medium | `IronDeveloperControls` resource dictionaries / themes |

**Answer must include:**
- Inspect `SelectionField` selected-text presenter width
- Increase `MinWidth` or internal text container width
- Check `ComboBox`/`SelectionField` style for clipped content presenter
- Ensure full values `Draft`, `Medium`, `Task` display correctly
- State whether fix belongs in the app XAML or the controls library based on what is indexed

**Answer must NOT include:**
- Ticket service / backend changes
- Database schema changes
- LLM provider changes

**Automated spec:**
```json
{
  "question": "Status, priority and type dropdowns are clipped. What files should I fix?",
  "intent": "CodeQuery",
  "mustIncludeAny": [
    "TicketsWorkspaceView.xaml",
    "SelectionField",
    "MinWidth"
  ],
  "mustNotLeadWith": ["TicketService", "database", "DraftTicket"],
  "mustMention": ["width", "XAML", "style"],
  "mustNotMention": ["schema", "Weaviate"]
}
```

**Pass:** Answer points to ticket XAML / control style.  
**Fail:** Answer treats it as a data or model problem.

---

## Test 5 — Chat Answers Are Generic

**User question:**
> "Chat gives generic answers instead of real files. How do we fix grounding?"

**Intent:** `CodeQuery` (chat grounding / context retrieval)

**Expected high-priority context:**

| Priority | File / Symbol |
|---|---|
| High | `IronDeveloper/ViewModels/Workspaces/ChatWorkspaceViewModel.cs` |
| High | `IronDev.Infrastructure/Services/PromptContextBuilder.cs` |
| High | `IronDev.Infrastructure/Services/CodeIndexService.cs` |
| High | `IronDev.Infrastructure/Services/ProjectMemoryService.cs` |
| Medium | `IronDev.Core/Models/CodeIndexEntry.cs` |
| Medium | Chat / grounding integration tests |

**Answer must include:**
- Detect project/code intent in `PromptContextBuilder`
- Check `IndexingStatus` via `ProjectService.GetByIdAsync`
- If not indexed: show limited-context warning in prompt
- If indexed: retrieve relevant files/symbols from `CodeIndexService.GetRelevantSnippetsAsync`
- Inject `FilePath` + `SymbolName` into prompt as high-confidence context
- Add anti-generic prompt rule ("do not invent file names")
- Do not add Weaviate

**Answer must NOT include:**
- "Add Weaviate now" as the first step
- "Just improve the prompt text" without retrieval
- "Add embeddings" as the primary fix

**Automated spec:**
```json
{
  "question": "Chat gives generic answers instead of real files. How do we fix grounding?",
  "intent": "CodeQuery",
  "mustIncludeAny": [
    "PromptContextBuilder",
    "CodeIndexService",
    "GetRelevantSnippetsAsync",
    "ChatWorkspaceViewModel"
  ],
  "mustNotLeadWith": ["Weaviate", "embeddings"],
  "mustMention": ["IndexingStatus", "retrieval", "prompt"],
  "mustNotMention": ["Weaviate"]
}
```

**Pass:** Answer describes retrieval + prompt injection + index preflight.  
**Fail:** Answer says only "make the prompt better" or "add Weaviate".

---

## Test 6 — Draft Tickets Are Weak/Generic

**User question:**
> "Draft ticket generation is weak and generic. How do we make it specific to IronDev?"

**Intent:** `DraftTicketFlow`

**Expected high-priority context:**

| Priority | File / Symbol |
|---|---|
| High | `IronDev.Infrastructure/Builder/DraftTicketService.cs` |
| High | `IronDev.Core/Builder/DraftTicketDtos.cs` |
| High | `IronDev.Infrastructure/Services/PromptContextBuilder.cs` |
| High | `IronDev.Infrastructure/Services/CodeIndexService.cs` |
| Medium | `IronDeveloper/ViewModels/Workspaces/TicketsWorkspaceViewModel.cs` |
| Medium | `IronDeveloper/ViewModels/Workspaces/ChatWorkspaceViewModel.cs` |

**Answer must include:**
- Build `ProjectContext` before draft generation (decisions, summaries, code index)
- Include `RelevantFiles`, `RelevantSymbols`, `RecentDecisions` in the draft prompt
- Strict prompt instruction: no generic advice, no invented file/class names
- JSON output with `affectedFiles`, `linkedSymbols`, `testPlan`, `contextQuality`
- Weak draft detection: flag if no files matched and only generic phrases used

**Answer must NOT include:**
- Changing saved ticket deletion logic
- Weaviate
- Patch validation

**Automated spec:**
```json
{
  "question": "Draft ticket generation is weak and generic. How do we make it specific to IronDev?",
  "intent": "DraftTicketFlow",
  "mustIncludeAny": [
    "DraftTicketService",
    "DraftTicketDtos",
    "PromptContextBuilder",
    "CodeIndexService"
  ],
  "mustNotLeadWith": ["TicketsWorkspaceViewModel delete", "schema"],
  "mustMention": ["context", "affectedFiles", "prompt"],
  "mustNotMention": ["Weaviate", "patch validation"]
}
```

**Pass:** Answer focuses on `DraftTicketService` prompt/context improvement.  
**Fail:** Answer suggests unrelated UI changes only.

---

## Test 7 — Create Ticket + Plan Does Not Prefill Plan

**User question:**
> "Create Ticket + Plan opens the plan screen, but the plan fields are empty. What should we check?"

**Intent:** `CodeQuery` (ticket approval / plan prefill)

**Expected high-priority context:**

| Priority | File / Symbol |
|---|---|
| High | `IronDeveloper/ViewModels/Workspaces/TicketsWorkspaceViewModel.cs` |
| High | `IronDeveloper/ViewModels/Workspaces/ImplementationPlansWorkspaceViewModel.cs` |
| High | `IronDeveloper/ViewModels/Shell/ShellViewModel.cs` |
| High | `ProjectImplementationPlan` model (`DataModels.cs`) |
| Medium | `TicketsWorkspaceView.xaml` (button binding) |

**Answer must include:**
- Snapshot draft fields *before* clearing draft state on approval
- Ensure saved ticket `Id` is returned from `TicketService.SaveTicketAsync`
- Pass `Title`, `Summary`, acceptance criteria, tests into plan prefill
- Check `ShellViewModel` navigation callback wiring
- Verify plan `Title` includes ticket title

**Answer must NOT include:**
- Database schema change as first answer
- LLM provider config
- Weaviate

**Automated spec:**
```json
{
  "question": "Create Ticket + Plan opens the plan screen, but the plan fields are empty. What should we check?",
  "intent": "CodeQuery",
  "mustIncludeAny": [
    "TicketsWorkspaceViewModel",
    "ImplementationPlansWorkspaceViewModel",
    "ShellViewModel",
    "ProjectImplementationPlan"
  ],
  "mustNotLeadWith": ["schema", "Weaviate", "LLM"],
  "mustMention": ["draft state", "navigation", "prefill"],
  "mustNotMention": ["Weaviate"]
}
```

**Pass:** Answer identifies draft state clearing / navigation callback as the risk.  
**Fail:** Answer only says "check the plan service".

---

## Test 8 — Index Project First Does Not Resume Draft

**User question:**
> "When I click Index Project First, indexing runs but the draft ticket is not generated after Ready. What should be fixed?"

**Intent:** `DraftTicketFlow` (index preflight / resume)

**Expected high-priority context:**

| Priority | File / Symbol |
|---|---|
| High | `IronDeveloper/ViewModels/Workspaces/TicketsWorkspaceViewModel.cs` |
| High | `IronDeveloper/ViewModels/Shell/ShellViewModel.cs` |
| High | `IronDeveloper/ViewModels/Workflow/ProjectOverviewViewModel.cs` |
| High | `IronDev.Infrastructure/Services/CodeIndexService.cs` |
| High | `IronDev.IntegrationTests/DraftPreflightTests.cs` |
| Medium | Index status properties / `SetIndexStatus` / `IsDraftIndexing` |

**Answer must include:**
- Check for pending `ChatTicketContext` stored while indexing ran
- `IsDraftIndexing` / `ShouldGenerateDraftAfterIndex` flag pattern
- `SetIndexStatus("Ready")` must resume generation exactly once
- Avoid checking status before indexing actually completes
- `Continue Without Index` remains an explicit override path
- Tests for `Ready` transition and no double-generation

**Answer must NOT include:**
- Weaviate
- Database schema change
- "just rerun indexing" as the only advice

**Automated spec:**
```json
{
  "question": "When I click Index Project First, indexing runs but draft is not generated after Ready. What should be fixed?",
  "intent": "DraftTicketFlow",
  "mustIncludeAny": [
    "TicketsWorkspaceViewModel",
    "SetIndexStatus",
    "IsDraftIndexing",
    "ChatTicketContext"
  ],
  "mustNotLeadWith": ["Weaviate", "schema"],
  "mustMention": ["pending context", "Ready", "resume"],
  "mustNotMention": ["Weaviate"]
}
```

**Pass:** Answer identifies pending context + `SetIndexStatus("Ready")` propagation as the fix.  
**Fail:** Answer says only "rerun indexing".

---

## Test 9 — Local LLM Provider Setup

**User question:**
> "How can another developer run IronDev with Ollama or a local LLM?"

**Intent:** `CodeQuery` (LLM provider configuration)

**Expected high-priority context:**

| Priority | File / Symbol |
|---|---|
| High | `IronDev.Core/Models/LlmOptions.cs` |
| High | `IronDev.Infrastructure/Services/OpenAiLlmService.cs` |
| High | `IronDev.Infrastructure/Services/LocalOpenAiCompatibleLlmService.cs` |
| High | `IronDev.Infrastructure/Services/OllamaLlmService.cs` |
| High | `IronDeveloper/App.xaml.cs` (DI provider switch) |
| Medium | `appsettings.Development.json` |
| Medium | `Docs/local-development.md` |

**Answer must include:**
- Set `Provider = "OpenAI"` / `"LocalOpenAI"` / `"Ollama"` in config
- Provide `BaseUrl` examples for local endpoints
- Do not commit API keys; use env var `OPENAI_API_KEY` or appsettings override
- `ILLMService` abstraction — ViewModels must not reference the provider directly
- Point to `Docs/local-development.md` for full setup

**Answer must NOT include:**
- Changing ticket workflow
- Weaviate
- Hardcoded API key or model name in source

**Automated spec:**
```json
{
  "question": "How can another developer run IronDev with Ollama or a local LLM?",
  "intent": "CodeQuery",
  "mustIncludeAny": [
    "LlmOptions",
    "OllamaLlmService",
    "LocalOpenAiCompatibleLlmService",
    "App.xaml.cs",
    "ILLMService"
  ],
  "mustNotLeadWith": ["TicketService", "Weaviate"],
  "mustMention": ["Provider", "BaseUrl", "appsettings"],
  "mustNotMention": ["Weaviate", "hardcoded"]
}
```

**Pass:** Answer references provider files and config.  
**Fail:** Answer says "set `OPENAI_API_KEY` only".

---

## Test 10 — Fresh Local DB Setup

**User question:**
> "What does a new developer need to do to set up the database and log in locally?"

**Intent:** `General` / `CodeQuery` (onboarding)

**Expected high-priority context:**

| Priority | File / Symbol |
|---|---|
| High | `Database/local_dev_setup.sql` |
| High | `Database/rebuild_db.sql` |
| High | `Docs/local-development.md` |
| High | `README.md` |
| Medium | `IronDev.Infrastructure/Services/UserService.cs` (password hash / login) |
| Medium | `appsettings.Development.json` |

**Answer must include:**
- Create `IronDeveloper` database on local SQL Server
- Run `Database/local_dev_setup.sql`
- Default login: `bob@irondev.local` / `change-me-local-only`
- After first login, project starts in **Needs Index** state
- Configure AI provider in appsettings
- Run `dotnet build` then `dotnet test`

**Answer must NOT include:**
- Production deployment steps
- Real passwords or API keys committed in source
- Weaviate as a required dependency

**Automated spec:**
```json
{
  "question": "What does a new developer need to do to set up the database and log in locally?",
  "intent": "General",
  "mustIncludeAny": [
    "local_dev_setup.sql",
    "rebuild_db.sql",
    "local-development.md",
    "README.md"
  ],
  "mustNotLeadWith": ["Weaviate", "production"],
  "mustMention": ["IronDeveloper database", "bob@irondev.local", "dotnet build"],
  "mustNotMention": ["Weaviate required", "manual table edits"]
}
```

**Pass:** Answer matches the local setup docs and references real scripts.  
**Fail:** Answer says "manually create tables" or omits `local_dev_setup.sql`.

---

## Automated Test Harness — GroundingTestCase Model

The spec above is codified in `IronDev.IntegrationTests/ChatGroundingSpecTests.cs`.

Each `GroundingTestCase` is evaluated against the output of `PromptContextBuilder.ClassifyIntent`
and `PromptContextBuilder.ExpandSearchQueries` without requiring a live LLM call.

Answer-quality scoring (score 0–3) is a **manual** review step; the automated tests cover:

1. **Intent detection** — `ClassifyIntent(question)` returns the expected `ChatIntent`
2. **Query expansion** — `ExpandSearchQueries` includes all `mustIncludeAny` terms for the intent
3. **Retrieval ranking** — `RankSnippetsByIntent` places `mustIncludeAny` symbols before `mustNotLeadWith` symbols
4. **Prompt rule presence** — `BuildAsync` prompt contains required architectural context rules
5. **Not-indexed warning** — `BuildAsync` includes `LIMITED CONTEXT WARNING` when project is not indexed

See `IronDev.IntegrationTests/ChatGroundingTests.cs` for implemented tests covering Tests 1, 5, 6, 8
and `IronDev.IntegrationTests/ChatGroundingSpecTests.cs` for the full 10-case intent + expansion matrix.
