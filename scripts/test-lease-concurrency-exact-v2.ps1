param(
    [string]$BaseUri = "https://localhost:8443",
    [string]$SqlServer = "localhost",
    [string]$Database = "CWLicenseService",
    [string]$TenantId = "11111111-1111-1111-1111-111111111111",
    [string]$ClientId = "22222222-2222-2222-2222-222222222222",
    [int]$RequestCount = 5
)

$ErrorActionPreference = "Stop"

if ($RequestCount -lt 2) {
    throw "RequestCount must be at least 2."
}

Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.Data

$runId = [Guid]::NewGuid().ToString("N")
$product = "ServerEngineV4-Concurrency-$runId"

$connectionString =
    "Server=$SqlServer;Database=$Database;Integrated Security=True;TrustServerCertificate=True"

function Invoke-SqlScalar {
    param(
        [string]$Sql,
        [hashtable]$Parameters = @{}
    )

    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)

    try {
        $connection.Open()

        $command = $connection.CreateCommand()
        $command.CommandText = $Sql

        foreach ($entry in $Parameters.GetEnumerator()) {
            [void]$command.Parameters.AddWithValue(
                "@$($entry.Key)",
                $entry.Value)
        }

        return $command.ExecuteScalar()
    }
    finally {
        $connection.Dispose()
    }
}

function Invoke-SqlNonQuery {
    param(
        [string]$Sql,
        [hashtable]$Parameters = @{}
    )

    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)

    try {
        $connection.Open()

        $command = $connection.CreateCommand()
        $command.CommandText = $Sql

        foreach ($entry in $Parameters.GetEnumerator()) {
            [void]$command.Parameters.AddWithValue(
                "@$($entry.Key)",
                $entry.Value)
        }

        [void]$command.ExecuteNonQuery()
    }
    finally {
        $connection.Dispose()
    }
}

$customerId =
    Invoke-SqlScalar `
        -Sql @"
SELECT [Id]
FROM [Customers]
WHERE [EntraTenantId] = @tenantId
  AND [EntraClientId] = @clientId
  AND [Enabled] = 1;
"@ `
        -Parameters @{
            tenantId = $TenantId
            clientId = $ClientId
        }

if ($null -eq $customerId -or
    $customerId -is [System.DBNull]) {
    throw "Enabled development Customer was not found."
}

$entitlementId = [Guid]::NewGuid()
$expiresAtUtc = [DateTimeOffset]::UtcNow.AddHours(2)

Invoke-SqlNonQuery `
    -Sql @"
INSERT INTO [Entitlements]
(
    [Id],
    [CustomerId],
    [Product],
    [ExpiresAtUtc],
    [MaxActiveServers],
    [MaxConnectionsPerServer],
    [LeaseMinutes],
    [Enabled]
)
VALUES
(
    @id,
    @customerId,
    @product,
    @expiresAtUtc,
    1,
    32,
    60,
    1
);
"@ `
    -Parameters @{
        id = $entitlementId
        customerId = $customerId
        product = $product
        expiresAtUtc = $expiresAtUtc
    }

$client = New-Object System.Net.Http.HttpClient
$successes = @()
$results = @()
$instanceIds = @()

try {
    $tasks = @()

    for ($i = 0; $i -lt $RequestCount; $i++) {
        $instanceId = "dev-exact-$runId-$i"
        $instanceIds += $instanceId

        $body = @{
            serverInstanceId = $instanceId
            product = $product
        } | ConvertTo-Json -Compress

        $request =
            New-Object System.Net.Http.HttpRequestMessage(
                [System.Net.Http.HttpMethod]::Post,
                "$BaseUri/api/v1/lease/acquire")

        $request.Headers.Add(
            "X-CW-Development-Authentication",
            "1")

        $request.Content =
            New-Object System.Net.Http.StringContent(
                $body,
                [System.Text.Encoding]::UTF8,
                "application/json")

        $tasks += $client.SendAsync($request)
    }

    for ($i = 0; $i -lt $tasks.Count; $i++) {
        try {
            $response =
                $tasks[$i].GetAwaiter().GetResult()

            $content =
                $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

            $leaseId = $null

            if ($response.IsSuccessStatusCode) {
                $json = $content | ConvertFrom-Json
                $leaseId = $json.leaseId
            }

            $results += [pscustomobject]@{
                InstanceId = $instanceIds[$i]
                StatusCode = [int]$response.StatusCode
                LeaseId = $leaseId
                Body = $content
            }
        }
        catch {
            $results += [pscustomobject]@{
                InstanceId = $instanceIds[$i]
                StatusCode = 0
                LeaseId = $null
                Body = $_.Exception.ToString()
            }
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
    Write-Host "Product:    $product"
    Write-Host "Requests:   $RequestCount"
    Write-Host "Succeeded:  $($successes.Count)"
    Write-Host "Conflicts:  $($conflicts.Count)"
    Write-Host "Unexpected: $($unexpected.Count)"

    if ($unexpected.Count -ne 0) {
        Write-Host ""
        $unexpected |
            Select-Object InstanceId, StatusCode, Body |
            Format-List

        throw "FAIL: unexpected HTTP response(s)."
    }

    if ($successes.Count -ne 1) {
        throw "FAIL: expected exactly 1 successful acquire, got $($successes.Count)."
    }

    if ($conflicts.Count -ne ($RequestCount - 1)) {
        throw "FAIL: expected exactly $($RequestCount - 1) conflicts, got $($conflicts.Count)."
    }

    Write-Host ""
    Write-Host "LEASE-CONCURRENCY-EXACT-V2 passed."
    Write-Host "Exactly 1 lease was admitted; all other concurrent requests returned 409."
}
finally {
    foreach ($success in $successes) {
        try {
            $releaseBody = @{
                leaseId = $success.LeaseId
                serverInstanceId = $success.InstanceId
            } | ConvertTo-Json -Compress

            $releaseRequest =
                New-Object System.Net.Http.HttpRequestMessage(
                    [System.Net.Http.HttpMethod]::Post,
                    "$BaseUri/api/v1/lease/release")

            $releaseRequest.Headers.Add(
                "X-CW-Development-Authentication",
                "1")

            $releaseRequest.Content =
                New-Object System.Net.Http.StringContent(
                    $releaseBody,
                    [System.Text.Encoding]::UTF8,
                    "application/json")

            $releaseResponse =
                $client.SendAsync($releaseRequest).GetAwaiter().GetResult()

            if (-not $releaseResponse.IsSuccessStatusCode) {
                Write-Warning "Lease cleanup release failed for $($success.InstanceId)."
            }
        }
        catch {
            Write-Warning "Lease cleanup failed: $($_.Exception.Message)"
        }
    }

    $client.Dispose()

    try {
        Invoke-SqlNonQuery `
            -Sql @"
DELETE FROM [Leases]
WHERE [CustomerId] = @customerId
  AND [Product] = @product;

DELETE FROM [ServerInstances]
WHERE [CustomerId] = @customerId
  AND [Id] LIKE @instancePrefix
  AND NOT EXISTS
  (
      SELECT 1
      FROM [Leases]
      WHERE [Leases].[ServerInstanceId] = [ServerInstances].[Id]
  );

DELETE FROM [Entitlements]
WHERE [Id] = @entitlementId
  AND [CustomerId] = @customerId
  AND [Product] = @product;
"@ `
            -Parameters @{
                customerId = $customerId
                product = $product
                instancePrefix = "dev-exact-$runId-%"
                entitlementId = $entitlementId
            }
    }
    catch {
        Write-Warning "SQL cleanup failed: $($_.Exception.Message)"
    }
}
