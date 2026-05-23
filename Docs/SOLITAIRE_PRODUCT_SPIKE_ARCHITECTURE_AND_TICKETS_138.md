---
id: SOLITAIRE_PRODUCT_SPIKE_ARCHITECTURE_AND_TICKETS_138
project: IronDev
title: Solitaire Product Spike Architecture And Associated Tickets 138
document_type: ArchitecturePlan
authority: Proposed
status: Draft
source_intake: SOLITAIRE_PRODUCT_SPIKE_INTAKE_137
recommended_next_slice: 139 - Build Solitaire In Disposable Workspace
created_utc: 2026-05-23T09:00:00Z
primary_retrieval_questions:
  - What comes after Solitaire product spike intake?
  - Should IDA jump straight from Solitaire intake to build?
  - What architecture and tickets are needed before building Solitaire?
  - What should slice 139 do for Solitaire?
boundary: Planning package only. This does not build Solitaire, create a disposable workspace, apply patches, or write the real repository.
---

# Solitaire Product Spike Architecture And Associated Tickets 138

## Purpose

This document turns the vague product request `i want build solitare` into a controlled, reviewable planning package for IronDev/IDA.

The goal is not to build Solitaire yet. The goal is to produce enough architecture, requirements, boundaries, and draft tickets that a later disposable workspace spike can be attempted safely.

This document answers the retrieval question: should IDA jump straight from Solitaire intake to build, or create architecture tickets first? IDA should create the architecture and draft ticket layer first, then attempt the disposable workspace build in slice 139 only after that planning package is available.

## Decision

Proceed with a Solitaire product spike plan using safe defaults.

Accepted defaults:

| Area | Decision |
| --- | --- |
| Project | Solitaire |
| Product type | Small game/product spike |
| Platform | WPF |
| Game type | Klondike Solitaire |
| Scope | Playable vertical slice |
| Input model | Click-to-move first |
| Drag/drop | Out of scope for first spike |
| Save/load | Out of scope for first spike |
| Animations | Out of scope for first spike |
| Persistence | None |
| Tests | Deterministic core rule tests |
| Build target | Disposable workspace only |
| Real repo writes | Blocked |

## Boundary

This planning package must not:

- create Solitaire app files
- create a disposable workspace
- apply patches
- mutate the developer working tree
- mutate the real IronDev repository through generated app changes
- start autonomous repair
- treat the intake prompt as build approval

Future build attempts must go through:

1. RetrieverAgent weighted context bundle.
2. ConscienceAgent review.
3. ThoughtLedger explanation.
4. Supervisor Tier 4 disposable workspace apply/build/test path.
5. TesterAgent execution.
6. Evidence report.

## Architecture Overview

The first Solitaire spike should be intentionally boring, testable, and layered.

```text
Solitaire.Wpf
  UI shell and views only

Solitaire.Core
  rules, game state, deck, moves, validation

Solitaire.Tests
  deterministic tests for rule engine and state transitions
```

If the disposable workspace build mechanism prefers a single project initially, the same architecture can be represented by folders and namespaces first and split into projects later.

Recommended first-spike structure:

```text
/src/Solitaire.Core
  Card.cs
  Suit.cs
  Rank.cs
  CardColor.cs
  PileType.cs
  PileId.cs
  MoveRequest.cs
  MoveResult.cs
  SolitaireGameState.cs
  SolitaireGameEngine.cs
  KlondikeRules.cs
  DeckFactory.cs
  GameSetupService.cs

/src/Solitaire.Wpf
  App.xaml
  MainWindow.xaml
  MainWindow.xaml.cs
  ViewModels/MainWindowViewModel.cs
  ViewModels/CardViewModel.cs
  ViewModels/PileViewModel.cs
  Services/GameViewModelMapper.cs

/tests/Solitaire.Core.Tests
  DeckFactoryTests.cs
  GameSetupServiceTests.cs
  KlondikeRulesTests.cs
  SolitaireGameEngineTests.cs
```

## Core Rules

Setup:

- Use one standard 52-card deck.
- Shuffle deck.
- Deal seven tableau columns.
- Tableau column 0 receives one card, column 1 receives two cards, up to column 6 receiving seven cards.
- Only the last card in each tableau column is face up.
- Remaining cards go to stock.
- Waste starts empty.
- Foundations start empty.

Stock and waste:

- First spike uses draw-one behaviour.
- Clicking stock moves one card from stock to waste and turns it face up.
- If stock is empty, first spike should block with `StockEmpty`.

Tableau:

- Face-down cards cannot be moved.
- Tableau builds downward by rank.
- Tableau builds with alternating colours.
- A King may move to an empty tableau column.
- A sequence may move only if all moved cards are face up and already form a valid descending alternating sequence.
- After moving cards away from a tableau column, the new top card flips face up if it was face down.

Foundation:

- Foundations build upward by suit.
- Ace starts each foundation.
- Only the next rank of the same suit can be added.

Win:

- The game is won when all four foundations contain thirteen cards.

## UI Architecture

The WPF UI should be simple and disposable-spike friendly.

MainWindow layout:

- top row: stock, waste, four foundation piles, new game button, status text
- main area: seven tableau columns

Interaction model:

1. First click selects a face-up card or pile top.
2. Second click selects destination pile.
3. ViewModel sends `MoveRequest` to `SolitaireGameEngine`.
4. Engine returns `MoveResult`.
5. ViewModel refreshes observable pile/card state.

No drag/drop, animations, scoring, undo, save/load, or persistence in the first spike.

## Testing Strategy

Core tests matter more than UI tests in the first spike.

Required tests:

1. Deck contains 52 unique cards.
2. Initial deal creates seven tableau piles with correct counts.
3. Initial deal leaves only the top tableau card face up.
4. Remaining stock count is correct after deal.
5. Ace can move to empty foundation.
6. Non-Ace cannot move to empty foundation.
7. Foundation accepts next rank of same suit.
8. Foundation rejects wrong suit.
9. Tableau accepts descending alternating move.
10. Tableau rejects same-colour descending move.
11. Tableau rejects ascending move.
12. Empty tableau accepts King.
13. Empty tableau rejects non-King.
14. Moving from tableau flips newly exposed top card.
15. Win detected when all foundations complete.

## Associated Draft Artefacts

### SOLITAIRE_PRODUCT_SPIKE_SPEC_138

Defines the agreed Solitaire spike scope, defaults, architecture, non-goals, and test expectations.

Status: Draft until reviewed.

### SOLITAIRE_DISPOSABLE_BUILD_TICKET_DRAFT_138

Defines the future ticket for attempting a disposable workspace build.

Status: Draft. Must not be treated as build approval.

### SOLITAIRE_RULE_ENGINE_TICKET_DRAFT_138

Defines the core domain/rule-engine build slice.

Status: Draft.

### SOLITAIRE_WPF_UI_TICKET_DRAFT_138

Defines the minimal WPF UI for the disposable spike.

Status: Draft.

### SOLITAIRE_TEST_EVIDENCE_TICKET_DRAFT_138

Defines tests and evidence expected from the disposable spike.

Status: Draft.

### SOL-139-001 Attempt Solitaire Disposable Workspace Build

Future draft ticket for attempting the Solitaire vertical slice inside an explicit disposable workspace.

Hard boundaries:

- no real repo writes
- no developer working tree mutation
- no patch outside disposable workspace
- no self-approval
- no autonomous repair outside the cage

## Recommended Smoke Plan For 138

The 138 smoke should prove:

- consumes `SOLITAIRE_PRODUCT_SPIKE_INTAKE_137`
- creates or retrieves `SOLITAIRE_PRODUCT_SPIKE_ARCHITECTURE_AND_TICKETS_138`
- includes WPF, Klondike, click-to-move defaults
- includes disposable workspace only boundary
- does not create Solitaire app files
- does not create a disposable workspace
- does not apply patches
- main alpha regression remains green

## Recommended Next Slice

```text
139 - Build Solitaire In Disposable Workspace
```

Only begin 139 after 138 proves the architecture/spec/ticket path is stable.

## Blunt Assessment

This is the correct next move.

Do not jump straight from intake to build.

The architecture and draft ticket layer is what turns IronDev from a clever prompt runner into a controlled product-building cockpit.
