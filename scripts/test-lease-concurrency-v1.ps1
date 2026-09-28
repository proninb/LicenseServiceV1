param(
    [string]$BaseUri = "https://localhost:8443",
    [string]$Product = "ServerEngineV4",
    [int]$MaxActiveServers = 10,
    [int]$ExtraRequests = 4
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Net.Http

if ($MaxActiveServers -lt 1) {
    throw "MaxActiveServers must be at least 1."
}

if ($ExtraRequests -lt 1) {
    throw "ExtraRequests must be at least 1."
}

$requestCount = $MaxActiveServers + $ExtraRequests
$runId = [Guid]::NewGuid().ToString("N")

$client = [System.Net.Http.HttpClient]::new()

try {
    $tasks = @()
    $instanceIds = @()

    for ($i = 0; $i -lt $requestCount; $i++) {
        $instanceId = "dev-concurrency-$runId-$i"
        $instanceIds += $instanceId

        $body = @{
            serverInstanceId = $instanceId
            product = $Product
        } | ConvertTo-Json -Compress

        $request =
            [System.Net.Http.HttpRequestMessage]::new(
                [System.Net.Http.HttpMethod]::Post,
                "$BaseUri/api/v1/lease/acquire")

        $request.Headers.Add(
            "X-CW-Development-Authentication",
            "1")

        $request.Content =
            [System.Net.Http.StringContent]::new(
                $body,
                [System.Text.Encoding]::UTF8,
                "application/json")

        $tasks += $client.SendAsync($request)
    }

    [System.Threading.Tasks.Task]::WaitAll(
        [System.Threading.Tasks.Task[]]$tasks)

    $results = @()

    for ($i = 0; $i -lt $tasks.Count; $i++) {
        $response = $tasks[$i].Result
        $content =
            $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

        $leaseId = $null

        if ($response.IsSuccessStatusCode) {
            try {
                $json = $content | ConvertFrom-Json
                $leaseId = $json.leaseId
            }
            catch {
                throw "Successful acquire returned invalid JSON."
            }
        }

        $results += [pscustomobject]@{
            InstanceId = $instanceIds[$i]
            StatusCode = [int]$response.StatusCode
            LeaseId = $leaseId
            Body = $content
        }
    }

    $successes =
        @($results | Where-Object { $_.StatusCode -eq 200 })

    $conflicts =
        @($results | Where-Object { $_.StatusCode -eq 409 })

    $unexpected =
        @($results | Where-Object {
            $_.StatusCode -ne 200 -and
            $_.StatusCode -ne 409
        })

    $results |
        Select-Object InstanceId, StatusCode, LeaseId |
        Format-Table -AutoSize

    Write-Host ""
    Write-Host "Requests:   $requestCount"
    Write-Host "Succeeded:  $($successes.Count)"
    Write-Host "Conflicts:  $($conflicts.Count)"
    Write-Host "Unexpected: $($unexpected.Count)"

    if ($unexpected.Count -ne 0) {
        Write-Host ""
        $unexpected |
            Select-Object InstanceId, StatusCode, Body |
            Format-List

        throw "Concurrency test returned unexpected HTTP responses."
    }

    if ($successes.Count -gt $MaxActiveServers) {
        throw "FAIL: concurrent acquire exceeded MaxActiveServers=$MaxActiveServers."
    }

    if ($conflicts.Count -eq 0) {
        throw "FAIL: no request was rejected with 409; the active-server limit was not exercised."
    }

    # Release every lease created by this test so the run is repeatable.
    foreach ($success in $successes) {
        $releaseBody = @{
            leaseId = $success.LeaseId
            serverInstanceId = $success.InstanceId
        } | ConvertTo-Json -Compress

        $releaseRequest =
            [System.Net.Http.HttpRequestMessage]::new(
                [System.Net.Http.HttpMethod]::Post,
                "$BaseUri/api/v1/lease/release")

        $releaseRequest.Headers.Add(
            "X-CW-Development-Authentication",
            "1")

        $releaseRequest.Content =
            [System.Net.Http.StringContent]::new(
                $releaseBody,
                [System.Text.Encoding]::UTF8,
                "application/json")

        $releaseResponse =
            $client.SendAsync($releaseRequest).GetAwaiter().GetResult()

        if (-not $releaseResponse.IsSuccessStatusCode) {
            $releaseContent =
                $releaseResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()

            throw "Cleanup release failed: HTTP $([int]$releaseResponse.StatusCode) $releaseContent"
        }
    }

    Write-Host ""
    Write-Host "LEASE-CONCURRENCY-V1 passed."
    Write-Host "No more than MaxActiveServers leases were admitted."
    Write-Host "All leases created by this test were released."

    if ($successes.Count -lt $MaxActiveServers) {
        Write-Warning (
            "Only $($successes.Count) of $MaxActiveServers slots were available. " +
            "Existing active leases for Product=$Product likely occupied the remainder. " +
            "For an exact N-of-(N+M) test, release existing leases first or use a dedicated test entitlement."
        )
    }
}
finally {
    $client.Dispose()
}
