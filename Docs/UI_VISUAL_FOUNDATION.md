# IronDev UI Visual Foundation

This is the first visual-system slice for the IronDev cockpit. It is not a rewrite and does not choose a new production UI framework.

## Product Tone

IronDev should feel calm, technical, high-trust, traceable, controlled, and momentum-oriented.

The UI should not feel like a CRUD admin panel, default WPF, a noisy dashboard, or a fake demo surface.

## Token Layers

The WPF app owns app-level aliases in:

- `IronDeveloper/Themes/IronDevVisualTokens.xaml`
- `IronDeveloper/Themes/IronDevComponentStyles.xaml`
- `IronDeveloper/Themes/AppStyles.xaml`

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

The current WPF app already consumes the shared `IronDeveloperControls` library for many of these controls. This slice adds app-owned visual aliases and applies them to the Tickets workspace shell only.

## Showcase Surface

The showcase surface is `TicketsWorkspaceView`.

This slice proves:

- app-level visual tokens can be merged through `AppStyles.xaml`
- command buttons can use app-level style aliases
- ticket shell controls expose stable AutomationIds
- the existing workflow remains intact

## Rule

Future visual work must extend tokens or reusable component styles first. Do not scatter hardcoded colors through views.
