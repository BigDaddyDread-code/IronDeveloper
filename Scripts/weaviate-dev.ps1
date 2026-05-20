param(
    [ValidateSet("up", "down", "restart", "status", "ready", "schema", "meta", "smoke")]
    [string]$Command = "status"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$composeFile = Join-Path $repoRoot "docker-compose.weaviate.yml"
$endpoint = "http://localhost:8080"
$containerName = "irondev-weaviate"

function Invoke-DockerCompose {
    param([string[]]$ComposeArgs)

    docker compose -f $composeFile @ComposeArgs
}

function Test-WeaviateReady {
    try {
        $response = Invoke-WebRequest -Uri "$endpoint/v1/.well-known/ready" -UseBasicParsing -TimeoutSec 5
        return $response.StatusCode -eq 200
    }
    catch {
        return $false
    }
}

function Get-WeaviateContainerId {
    $id = docker ps -a --filter "name=^/$containerName$" --format "{{.ID}}"
    if ([string]::IsNullOrWhiteSpace($id)) {
        return $null
    }

    return $id.Trim()
}

function Show-WeaviateStatus {
    $rows = docker ps -a --filter "name=^/$containerName$" --format "table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}"
    if ([string]::IsNullOrWhiteSpace($rows)) {
        Write-Host "No $containerName container found."
        return
    }

    $rows
}

switch ($Command) {
    "up" {
        $existingContainerId = Get-WeaviateContainerId
        if ($existingContainerId) {
            docker start $containerName | Out-Null
        }
        else {
            Invoke-DockerCompose @("up", "-d")
        }

        Write-Host "Waiting for Weaviate at $endpoint ..."
        for ($i = 0; $i -lt 30; $i++) {
            if (Test-WeaviateReady) {
                Write-Host "Weaviate is ready."
                exit 0
            }
            Start-Sleep -Seconds 1
        }

        throw "Weaviate did not become ready within 30 seconds."
    }
    "down" {
        $existingContainerId = Get-WeaviateContainerId
        if ($existingContainerId) {
            docker stop $containerName | Out-Null
        }
        else {
            Invoke-DockerCompose @("down")
        }
    }
    "restart" {
        $existingContainerId = Get-WeaviateContainerId
        if ($existingContainerId) {
            docker restart $containerName | Out-Null
        }
        else {
            Invoke-DockerCompose @("restart")
        }
    }
    "status" {
        Show-WeaviateStatus
    }
    "ready" {
        Invoke-WebRequest -Uri "$endpoint/v1/.well-known/ready" -UseBasicParsing
    }
    "schema" {
        Invoke-RestMethod "$endpoint/v1/schema" | ConvertTo-Json -Depth 20
    }
    "meta" {
        Invoke-RestMethod "$endpoint/v1/meta" | ConvertTo-Json -Depth 20
    }
    "smoke" {
        if (-not (Test-WeaviateReady)) {
            throw "Weaviate is not ready at $endpoint."
        }

        $className = "IronDevMemorySmokeTest"
        $schema = Invoke-RestMethod "$endpoint/v1/schema"
        $exists = $schema.classes | Where-Object { $_.class -eq $className }

        if (-not $exists) {
            $body = @{
                class = $className
                vectorizer = "none"
                properties = @(
                    @{
                        name = "title"
                        dataType = @("text")
                    },
                    @{
                        name = "content"
                        dataType = @("text")
                    }
                )
            } | ConvertTo-Json -Depth 10

            Invoke-RestMethod `
                -Method Post `
                -Uri "$endpoint/v1/schema" `
                -ContentType "application/json" `
                -Body $body | Out-Null
        }

        $afterCreate = Invoke-RestMethod "$endpoint/v1/schema"
        if (-not ($afterCreate.classes | Where-Object { $_.class -eq $className -and $_.vectorizer -eq "none" })) {
            throw "Smoke test collection was not created with vectorizer none."
        }

        Invoke-RestMethod -Method Delete -Uri "$endpoint/v1/schema/$className" | Out-Null
        Write-Host "Weaviate smoke test passed."
    }
}
