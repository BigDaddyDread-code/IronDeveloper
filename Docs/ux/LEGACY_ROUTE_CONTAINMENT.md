# Legacy Route Containment

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
