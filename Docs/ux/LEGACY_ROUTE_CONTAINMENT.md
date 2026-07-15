# Legacy Route Containment

**Status:** Canonical compatibility-route contract

**Last reviewed:** 15 July 2026

**Programme slice:** CLN-31

Legacy routes are compatibility inputs, not product navigation. The shell keeps safe deep links working, replaces them with their project-scoped canonical destination, and leaves a visible compatibility notice linking to the canonical surface.

| Legacy path | Canonical surface | Handling |
|---|---|---|
| `/chat` | Workshop | Redirect with notice after scope is known |
| `/projects/{projectId}/chat` and its session/channel descendants | Workshop | Preserve session/channel identity and redirect with notice |
| `/tickets` | Board | Redirect with notice |
| `/build` | Board | Redirect with notice |
| `/runs` | Board | Redirect with notice |
| `/batch` | Board | Redirect with notice |
| `/knowledge` | Library | Redirect with notice |
| `/settings` | Project Settings when project scope exists | Redirect with notice |

The retained governance/evidence viewers are a second compatibility class. They keep their safe read-only URLs and render `Legacy evidence view`, `Back to Governance`, and `Open canonical surface` notices through `GovernanceHost`. They do not appear in primary product navigation.

Compatibility never upgrades authority. Redirect resolution does not prove tenant membership, project access, work-item ownership, readiness, approval, or permission to execute.

`legacyCanonicalPath` is the single executable resolver used by `FlowShell` and the route inventory tests. The visible notice records the original source path and resolved canonical target. Project-scoped chat session/channel identities are preserved; encoded channel references are decoded once and encoded once so redirects do not corrupt them.
