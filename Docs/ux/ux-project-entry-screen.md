# UX-PROJECT-1 - Project Tile Entry Screen

Status: implemented slice

Successful sign-in now resolves the tenant, then asks the user to choose a project. The project is the boundary between identity and work, so the interactive shell does not enter a project through `VITE_IRONDEV_PROJECT_ID`, `irondev.projectId`, or any other fallback ID.

## Journey

1. Sign in.
2. Auto-select the tenant only when there is exactly one valid tenant or the backend profile already names one.
3. Show the project tile screen.
4. Route from the selected tile:
   - backend readiness ready -> Board
   - backend readiness setup required -> Project Setup
   - readiness unavailable -> Project Setup
5. Connect another project through a dedicated Connect Project screen.

An explicit sign-in clears `irondev.selectedProjectId`. A later restart with a still-valid selected project may restore that project from `irondev.selectedProjectId`; that is separate from the sign-in journey.

## Project Tiles

Each project is a single semantic button. The tile shows only:

- project name
- local repository path
- concise readiness state

The tile does not show project IDs, tenant IDs, fallback IDs, API URLs, model details, tokens, run evidence, or governance explanations.

Readiness is rendered from the backend provisioning readiness request. The frontend does not infer readiness from names, files, paths, or prior client state.

## Connect Project

The final grid tile opens a dedicated screen with:

- project name
- editable local repository path
- Tauri folder browsing when the dialog plugin is available

BrowserOnly mode uses the editable path field. A successful create selects the returned project through the backend and opens Project Setup, never Board. A failed create keeps the entered values and shows one inline error.

## Project Switching

The current project name in the shell header opens the project tile screen. Choosing another project clears the active work item before routing to that project's Board or Project Setup.
