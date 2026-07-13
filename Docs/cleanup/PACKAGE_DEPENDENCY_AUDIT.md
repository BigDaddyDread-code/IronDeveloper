# Package and Dependency Audit

**Status:** Supporting dependency audit

**Snapshot:** 15 July 2026

**Programme slice:** CLN-37

This audit separates safe dependency cleanup from upgrades that can change builds, generated contracts, browser behavior, test discovery, or CI runtime. CLN-37 removes only dependencies proven redundant or unused. Upgrades remain isolated follow-ups with their own validation.

## Safe cleanup applied

| Ecosystem | Finding | Action | Proof |
|---|---|---|---|
| .NET integration tests | SDK warning `NU1510` identified explicit `Microsoft.Extensions.Configuration.Json` and `Microsoft.Extensions.DependencyInjection` references as unnecessary under the existing framework/project graph | Removed both top-level references | restore/build and focused integration discovery using isolated artifacts |
| Tauri npm | `depcheck` found direct `@tauri-apps/api` unused by source; `@tauri-apps/plugin-dialog` is the actual imported package and retains its own API dependency | Removed the unused direct manifest entry and refreshed the lockfile | `npm ls`, TypeScript/Vite build, package-lock consistency |

No application package was removed solely because it was transitive or duplicated at a different Cargo major version.

## Findings requiring isolated upgrade PRs

| Area | Current finding | Required follow-up | Why not changed here |
|---|---|---|---|
| NuGet vulnerability | `Microsoft.OpenApi` 2.4.1 is a high-severity transitive finding (`GHSA-v5pm-xwqc-g5wc`) in API and integration projects via `Microsoft.AspNetCore.OpenApi` 10.0.5 and `Swashbuckle.AspNetCore` 10.1.7 | Upgrade the owning OpenAPI packages together, regenerate/compare the contract, then run API and client contract suites | A transitive override can create binary or generated-contract drift |
| NuGet deprecated tests | `MSTest.TestAdapter` and `MSTest.TestFramework` 3.7.0 are reported `Legacy` in UnitTests and IntegrationTests.Api | Migrate each test project to MSTest 4 with discovery/count comparison | Test-runner upgrades can alter discovery and execution semantics |
| npm security | Five findings: direct Vite high; transitive `js-yaml` and Redocly moderate; Babel and esbuild low | Patch Vite/OpenAPI tooling in a frontend dependency PR and run build, generation diagnostics, Playwright, and Tauri build | Lockfile changes are upgrades, not unused-package cleanup |
| npm drift | Patch/minor updates are available for Playwright, Tauri CLI, React, React DOM, React types and Vite; major updates exist for plugin-react, Node types, TypeScript and Vite | Group compatible patch updates narrowly; isolate every major | Major tooling upgrades can change compilation and browser behavior |
| GitHub Actions | Workflows currently pin checkout v4, setup-dotnet v4, setup-node v4, and upload-artifact v4 | Check official release notes at execution time and upgrade one action family at a time after runner/runtime compatibility review | New majors change embedded Node/runtime and artifact behavior |
| Cargo advisories | `cargo-audit` is not installed in the current toolchain | Add a pinned advisory scan to CI before claiming Rust vulnerability-clean | `cargo tree -d` reports version multiplicity, not vulnerabilities |

## Duplicate-version and SDK review

- Repeated top-level NuGet packages in the main solution use consistent versions; the audit found no same-package top-level version split requiring consolidation.
- Cargo reports duplicate transitive majors for packages such as `bitflags`, `indexmap`, `thiserror`, `toml`, and Windows support crates. They are selected by the Tauri graph; forcing them together without upstream compatibility evidence is rejected.
- `global.json` pins .NET SDK 10.0.301 with roll-forward disabled, and production projects target `net10.0`. No SDK change is included.
- The frontend manifest pins npm 11.13.0 and Node 24.16.0 through its engines/package-manager contract. Version drift from the active workstation is evidence for reproducibility checks, not permission to rewrite the contract.

## Re-run commands

```powershell
dotnet list IronDev.slnx package --vulnerable --include-transitive
dotnet list IronDev.slnx package --deprecated
dotnet nuget why IronDev.Api/IronDev.Api.csproj Microsoft.OpenApi
Set-Location IronDev.TauriShell
npm audit
npm outdated
npx --yes depcheck --json
Set-Location src-tauri
cargo tree -d
```

Audit output is time-sensitive. A clean restore or build does not supersede a vulnerability result, and an available upgrade does not prove behavioral compatibility.
