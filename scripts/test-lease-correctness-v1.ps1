param(
    [string]$BaseUri = "https://localhost:8443",
    [string]$ServerInstanceId = "dev-server-correctness-001",
    [string]$Product = "ServerEngineV4"
)

$ErrorActionPreference = "Stop"

$headers = @{
    "X-CW-Development-Authentication" = "1"
}

function Invoke-LeasePost {
    param(
        [string]$Path,
        [hashtable]$Body
    )

    Invoke-RestMethod `
        -Method Post `
        -Uri "$BaseUri$Path" `
        -Headers $headers `
        -ContentType "application/json" `
        -Body ($Body | ConvertTo-Json)
}

$acquired = Invoke-LeasePost `
    -Path "/api/v1/lease/acquire" `
    -Body @{
        serverInstanceId = $ServerInstanceId
        product = $Product
    }

if (-not $acquired.leaseId -or
    -not $acquired.leaseToken) {
    throw "Acquire did not return a signed lease."
}

$renewed = Invoke-LeasePost `
    -Path "/api/v1/lease/renew" `
    -Body @{
        leaseId = $acquired.leaseId
        serverInstanceId = $ServerInstanceId
    }

if (-not $renewed.leaseId -or
    $renewed.leaseId -eq $acquired.leaseId) {
    throw "Renew did not create a successor lease."
}

Invoke-LeasePost `
    -Path "/api/v1/lease/release" `
    -Body @{
        leaseId = $renewed.leaseId
        serverInstanceId = $ServerInstanceId
    } | Out-Null

Invoke-LeasePost `
    -Path "/api/v1/lease/release" `
    -Body @{
        leaseId = $renewed.leaseId
        serverInstanceId = $ServerInstanceId
    } | Out-Null

$rejected = $false

try {
    Invoke-LeasePost `
        -Path "/api/v1/lease/renew" `
        -Body @{
            leaseId = $renewed.leaseId
            serverInstanceId = $ServerInstanceId
        } | Out-Null
}
catch {
    if ($_.Exception.Response.StatusCode.value__ -eq 404) {
        $rejected = $true
    }
    else {
        throw
    }
}

if (-not $rejected) {
    throw "Renew of a released lease was not rejected."
}

Write-Host "LEASE-CORRECTNESS-V1 smoke test passed."
Write-Host "Acquire: $($acquired.leaseId)"
Write-Host "Renew:   $($renewed.leaseId)"
