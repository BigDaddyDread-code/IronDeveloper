$ErrorActionPreference = "Stop"

function Write-Section {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    Write-Host ""
    Write-Host "== $Name =="
}

function ConvertTo-SafeLaneName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $safe = [Regex]::Replace($Name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim("-")
    if ([string]::IsNullOrWhiteSpace($safe)) {
        return "ci-lane"
    }

    return $safe
}

function Invoke-GovernanceBoundaryTestLane {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Filter
    )

    Write-Section $Name
    $safeLaneName = ConvertTo-SafeLaneName $Name
    dotnet test $script:Project `
        --no-restore `
        --no-build `
        --logger "console;verbosity=minimal" `
        --logger "trx;LogFileName=$safeLaneName.trx" `
        --results-directory $script:TestResultsRoot `
        --filter $Filter
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed."
    }
}

function Invoke-ApiBoundaryTestLane {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Filter
    )

    Write-Section $Name
    $safeLaneName = ConvertTo-SafeLaneName $Name
    dotnet test $script:ApiProject `
        --no-restore `
        --no-build `
        --logger "console;verbosity=minimal" `
        --logger "trx;LogFileName=$safeLaneName.trx" `
        --results-directory $script:TestResultsRoot `
        --filter $Filter
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed."
    }
}

function Invoke-CliBoundaryTestLane {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Filter
    )

    Write-Section $Name
    $safeLaneName = ConvertTo-SafeLaneName $Name
    dotnet test $script:CliProject `
        --no-restore `
        --no-build `
        --logger "console;verbosity=minimal" `
        --logger "trx;LogFileName=$safeLaneName.trx" `
        --results-directory $script:TestResultsRoot `
        --filter $Filter
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed."
    }
}

$script:RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$script:ArtifactRoot = Join-Path $script:RepoRoot "artifacts\ci\governance-boundary"
$script:TestResultsRoot = Join-Path $script:ArtifactRoot "test-results"
$script:Project = "IronDev.IntegrationTests/IronDev.IntegrationTests.csproj"
$script:ApiProject = "IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj"
$script:CliProject = "IronDev.IntegrationTests/IronDev.IntegrationTests.csproj"

if (Test-Path -LiteralPath $script:ArtifactRoot) {
    Remove-Item -LiteralPath $script:ArtifactRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $script:TestResultsRoot -Force | Out-Null
& (Join-Path $PSScriptRoot "write-ci-evidence-summary.ps1") `
    -ArtifactDirectory $script:ArtifactRoot `
    -WorkflowName "governance-boundary-ci" `
    -LaneName "governance-boundary" `
    -CommandCategory "dotnet test" `
    -ResultStatus "Started"

Write-Section "Governance boundary CI"
Write-Host "GitHub Actions CI reports evidence only."
Write-Host "CI is not approval, policy satisfaction, merge readiness, release readiness, deployment readiness, or execution permission."

$bSeriesBoundaryFilter = @(
    "FullyQualifiedName~BlockB01AuthorityProfileKindUnificationTests",
    "FullyQualifiedName~BlockB03AuthorityProfileVocabularyDriftTests",
    "FullyQualifiedName~BlockB04AskBeforeMutationRunProfileTests",
    "FullyQualifiedName~BlockB05BoundedRunAuthorityProfileTests",
    "FullyQualifiedName~BlockB06AuthorityProfileStatusCanonicalModelTests",
    "FullyQualifiedName~BlockB07ProposalOnlyMutationStatusProofTests",
    "FullyQualifiedName~BlockB08AskBeforeMutationBoundaryProofTests",
    "FullyQualifiedName~BlockB09BoundedRunAuthorityDownstreamProofTests",
    "FullyQualifiedName~BlockB10CanonicalAuthorityGlossaryTests",
    "FullyQualifiedName~BlockB11StatusAuthorityGlossaryAdoptionTests",
    "FullyQualifiedName~BlockB12HostileProfileTextEligibilityProofTests"
) -join "|"

$compatibilityBoundaryFilter = @(
    "FullyQualifiedName~BlockBQRunAuthorityProfileContractTests",
    "FullyQualifiedName~BlockBRBoundedRunAuthorityGrantTests",
    "FullyQualifiedName~BlockBSOperationEligibilityEvaluatorTests",
    "FullyQualifiedName~BlockBTAuthorityProfileStatusMappingTests",
    "FullyQualifiedName~BlockBUSourceApplyConsumesBoundedAuthorityTests"
) -join "|"

$securityBoundaryFilter = @(
    "FullyQualifiedName~BlockC11SecretScanningRegressionTests",
    "FullyQualifiedName~BlockC12LocalTestSafetyRegressionTests",
    "FullyQualifiedName~BlockC13ProductionEnvironmentSafetyRegressionTests",
    "FullyQualifiedName~BlockC14SensitiveApiRateLimitAuthBoundaryTests",
    "FullyQualifiedName~BlockC15SecurityAuditLogBoundaryTests",
    "FullyQualifiedName~BlockC16CiArtifactRetentionBoundaryTests"
) -join "|"

$apiBoundaryFilter = @(
    "FullyQualifiedName~BlockOOperationalReadinessApiSurfaceTests",
    "FullyQualifiedName~OperationalDebuggingApiContractTests",
    "FullyQualifiedName~RunsEndpointContractTests",
    "FullyQualifiedName~WorkflowContinuationApiRegressionTests"
) -join "|"

$cliBoundaryFilter = @(
    "TestCategory=ApiCliContract",
    "TestCategory=ApiCliReleaseGate"
) -join "|"

Invoke-GovernanceBoundaryTestLane `
    -Name "B-series profile boundary tests" `
    -Filter $bSeriesBoundaryFilter

Invoke-GovernanceBoundaryTestLane `
    -Name "BQ-BU compatibility boundary tests" `
    -Filter $compatibilityBoundaryFilter

Invoke-GovernanceBoundaryTestLane `
    -Name "Security boundary tests" `
    -Filter $securityBoundaryFilter

# The static boundary category is the wall of source/contract scans, including the
# read-only UI surface checks. It previously ran in no CI lane, so a broken static
# check could stay green for weeks. Selection here is coverage, not approval.
Invoke-GovernanceBoundaryTestLane `
    -Name "Static boundary tests" `
    -Filter "TestCategory=StaticBoundary"

Invoke-ApiBoundaryTestLane `
    -Name "API boundary tests" `
    -Filter $apiBoundaryFilter

Invoke-CliBoundaryTestLane `
    -Name "CLI boundary tests" `
    -Filter $cliBoundaryFilter

Write-Section "Governance boundary CI complete"
Write-Host "A green check is evidence, not permission."
& (Join-Path $PSScriptRoot "write-ci-evidence-summary.ps1") `
    -ArtifactDirectory $script:ArtifactRoot `
    -WorkflowName "governance-boundary-ci" `
    -LaneName "governance-boundary" `
    -CommandCategory "dotnet test" `
    -ResultStatus "Passed"
& (Join-Path $PSScriptRoot "test-ci-evidence-artifact-safety.ps1") `
    -ArtifactDirectory $script:ArtifactRoot
