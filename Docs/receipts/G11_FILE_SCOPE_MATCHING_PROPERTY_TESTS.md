# G11 - File-Scope Matching Property Tests

## Purpose

Add deterministic property-style fast unit tests for bounded-run file-scope matching.

File-scope property tests are not filesystem authority.

## Files Changed

- `IronDev.UnitTests/Governance/FileScopeMatchingPropertyFixtures.cs`
- `IronDev.UnitTests/Governance/FileScopeMatchingPropertyTests.cs`
- `Docs/receipts/G11_FILE_SCOPE_MATCHING_PROPERTY_TESTS.md`

## Core Seams Tested

- `BoundedRunAuthorityGrantFileScope.IsSafeRelativeGlob`
- `BoundedRunAuthorityGrantFileScope.IsAllowed`
- `BoundedRunAuthorityGrantFileScope.IsForbidden`
- `BoundedRunAuthorityGrantMatcher.Evaluate` for minimal file-scope propagation only

## Property-Style Approach

G11 uses deterministic table/property loops. It does not add FsCheck, Hypothesis, QuickCheck, random fuzzing, or any package reference.

The tests use fixed in-memory path and glob cases. Candidate paths are treated as strings only.

## Corpus Coverage

Safe path corpus includes normal source, tests, docs, root README, dashed/underscored names, nested paths, case variants, and Windows slash forms.

Unsafe path corpus includes null, empty, whitespace, rooted paths, UNC-like paths, home-rooted paths, URI paths, drive-rooted paths, forward-slash traversal, and backslash traversal.

Allowed glob corpus includes single-star, double-star, literal, and question-mark matching.

Forbidden glob corpus includes secrets folders, generated/designer files, bin/obj paths, and root secret-material extension patterns.

Normalization coverage includes slash/backslash equivalence, case-insensitive matching, and leading/trailing whitespace trimming.

Matcher propagation coverage is intentionally small:

- unsafe path -> `RequestedFilePathUnsafe`
- forbidden path -> `RequestedFileForbidden`
- unmatched path -> `RequestedFileNotAllowed`
- allowed path -> inside grant envelope with non-execution, non-approval, non-policy warnings

## Purity Boundary

G11 tests pure string/path-scope logic only.

G11 does not read candidate paths from the workspace. It does not write files. It does not apply patches. It does not execute git. It does not execute tools. It does not instantiate source apply executors. It does not call providers, models, API, CLI, SQL, Infrastructure, workers, persistence, or UI.

A source-guard test reads only the G11 test source files and `IronDev.UnitTests.csproj` to prove dependency hygiene.

## Dependencies Excluded

- No production code changes.
- No Infrastructure reference.
- No API/CLI/SQL dependency.
- No project or package reference changes.
- No property-testing package.
- No integration test deletion.
- No CI rewrite.

## Boundary Rules

- Matching a path is not permission to touch it.
- An allowed glob is not source apply.
- An allowed glob is not approval.
- An allowed glob is not policy satisfaction.
- An allowed glob is not execution authority.
- A bounded grant is not source mutation authority.
- A bounded grant is necessary but not sufficient.
- Forbidden match wins.
- Unsafe path fails closed.
- Path normalization is not filesystem access.
- String matching is not workspace mutation.
- Fast file-scope tests are not integration proof.
- Fast file-scope tests are not executor proof.
- Fast file-scope tests are not git proof.

## Known Limitations

G11 tests deterministic file-scope string matching only.

G11 does not read workspace files as candidate paths.

G11 does not write files, apply patches, execute git, execute tools, test source apply execution, test the full bounded-run authority lifecycle, test API or CLI, test SQL persistence, prove filesystem safety, or grant authority.

## Validation

Validation run for this PR:

- `dotnet restore IronDev.slnx`: passed with existing NU1510 warnings.
- `dotnet build IronDev.slnx --no-restore`: passed, 0 errors / existing warnings.
- `dotnet build IronDev.UnitTests/IronDev.UnitTests.csproj --no-restore`: passed, 0 errors.
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build`: 284/284 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BoundedRunAuthority`: 65/65 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~SourceApplyConsumesBoundedAuthority`: 21/21 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests`: 9/9 passed.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## GitHub CI

- `fast-unit-ci`: passed on head `3f66a2461941698bbe1c28365b60cd684df65ccd`.
- Run: `28556143717`.
- Job: `84663875660`.

## Next Intended Slice

G12 - Fixture builders for authority grants/evidence/receipts.

Review line: Fixture builders are not authority builders.

## Killjoy

Matching a path is not permission to touch it.
