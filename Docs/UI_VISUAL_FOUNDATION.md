# IronDev UI Visual Foundation

Status: historical WPF note. The `IronDeveloper` WPF project has been retired; future UI visual work belongs in `IronDev.TauriShell` and must stay API-bound.

This is the first visual-system slice for the IronDev cockpit. It is not a rewrite and does not choose a new production UI framework.

## Product Tone

IronDev should feel calm, technical, high-trust, traceable, controlled, and momentum-oriented.

The UI should not feel like a CRUD admin panel, default WPF, a noisy dashboard, or a fake demo surface.

## Token Layers

The retired WPF app owned app-level XAML aliases for visual tokens, component styles, and app styles. Those files are no longer part of the tracked product shell.

The tokens cover:

- colour
- typography
- spacing
- radius
- borders
- elevation
- state opacity
- icon sizing

## Component Contracts

Reusable component names are standardized around:

- `AppShell`
- `WorkspaceHeader`
- `CrudCommandBar`
- `CommandButton`
- `SurfacePanel`
- `ContextInspector`
- `InspectorSection`
- `StatusBadge`
- `EmptyState`
- `SearchField`
- `FilterChip`
- `WorkspaceListPane`
- `WorkspaceListItem`

This historical slice added app-owned visual aliases and applied them to the retired Tickets workspace shell. Current visual work should implement equivalent contracts in `IronDev.TauriShell`.

## Showcase Surface

The historical showcase surface was `TicketsWorkspaceView`. The current shell equivalent is `IronDev.TauriShell/src/features/tickets/TicketsWorkspace.tsx`.

This slice proves:

- app-level visual tokens could be merged through `AppStyles.xaml`
- command buttons could use app-level style aliases
- ticket shell controls exposed stable AutomationIds
- the existing workflow remains intact

## Rule

Future visual work must extend tokens or reusable component styles first. Do not scatter hardcoded colors through views.
