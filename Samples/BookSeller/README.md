# BookSeller — the demo target

A deliberately small, buildable .NET solution that the IronDeveloper governed
loop works **against**. It is a sample only:

- It references no IronDeveloper namespaces and grants nothing — no approval
  power, no gate movement, no trust. The containment guardrail
  (`DemoContainmentStaticBoundaryTests`) enforces this the moment this
  directory exists.
- It compiles and its tests pass **as-is**. The demo tickets in
  `TestFixtures/BookSeller/tickets.json` describe features that are
  deliberately *not implemented here* — the Builder earns the demo by solving
  them live through the governed loop. Nothing in this repo pre-bakes the
  answers.

## Layout

- `src/BookSeller.Domain` — Book, Catalog, PricingService (minimal on purpose)
- `tests/BookSeller.Domain.Tests` — MSTest suite proving the harness runs

## Build

```
dotnet build BookSeller.slnx
dotnet test BookSeller.slnx
```
