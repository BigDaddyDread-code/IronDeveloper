$ErrorActionPreference = "Stop"

function Write-Section {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    Write-Host ""
    Write-Host "== $Name =="
}

function Invoke-GovernanceBoundaryTestLane {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Filter
    )

    Write-Section $Name
    dotnet test $script:Project `
        --no-restore `
        --no-build `
        --logger "console;verbosity=minimal" `
        --filter $Filter
}

function Invoke-ApiBoundaryTestLane {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Filter
    )

    Write-Section $Name
    dotnet test $script:ApiProject `
        --no-restore `
        --no-build `
        --logger "console;verbosity=minimal" `
        --filter $Filter
}

function Invoke-CliBoundaryTestLane {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Filter
    )

    Write-Section $Name
    dotnet test $script:CliProject `
        --no-restore `
        --no-build `
        --logger "console;verbosity=minimal" `
        --filter $Filter
}

$script:Project = "IronDev.IntegrationTests/IronDev.IntegrationTests.csproj"
$script:ApiProject = "IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj"
$script:CliProject = "IronDev.IntegrationTests/IronDev.IntegrationTests.csproj"

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
    "FullyQualifiedName~BlockC12LocalTestSafetyRegressionTests"
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

Invoke-ApiBoundaryTestLane `
    -Name "API boundary tests" `
    -Filter $apiBoundaryFilter

Invoke-CliBoundaryTestLane `
    -Name "CLI boundary tests" `
    -Filter $cliBoundaryFilter

Write-Section "Governance boundary CI complete"
Write-Host "A green check is evidence, not permission."
