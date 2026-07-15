# Clean-Clone Qualification

**Status:** Canonical qualification runbook

## Repository Gate

On a clean machine with Git, .NET 10, Node/npm, and Rust/Cargo installed:

```powershell
.\Scripts\qualification\Invoke-CleanCloneQualification.ps1 -RepositoryUrl <repository-url> -Ref <commit-or-branch>
```

The script creates a new temporary clone, checks out the requested ref, restores and builds the solution, refuses known .NET and npm vulnerabilities, runs the documentation contract, performs locked frontend installation/build, and runs Cargo check. Its JSON evidence contains real timestamps only for checks actually executed.

Use `-ClonePath <empty-directory-path> -KeepClone` when the live LocalTest gate will continue from the same clone. The evidence records the exact checked-out commit and is also written after a failed or incomplete run.

## Live LocalTest Gate

From that same clone, continue with no author-only intervention:

1. Run `tools/localtest/start-pr-manual-test.ps1 -FreshSession -Reset` to apply migrations, seed LocalTest, and start API/UI.
2. Sign in visibly and select the tenant/project through product UI.
3. Open Board and create or open a Work Item.
4. Run the governed smoke through `tools/localtest/Invoke-LocalTestSmoke.ps1` and retain its evidence.
5. Open Governance and Audit in the UI.
6. Export support evidence through `tools/localtest/Invoke-LocalTestResetAndSupport.ps1 -Action ExportSupportBundle` once CLN-41 is merged.

The qualification is incomplete until every live step is recorded from the clean clone. Repository build evidence is not a substitute for database, API, UI, authentication, governance, audit, or support-export evidence.
