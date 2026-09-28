# CW License Service V1

Standalone ASP.NET Core License Service:

```text
Kestrel HTTPS
Windows Service
Blazor admin UI
REST lease API
Microsoft Entra bearer authentication
SQL Server
RS256 signed lease token
```

No IIS is required.

## API

```text
POST /api/v1/lease/acquire
POST /api/v1/lease/renew
POST /api/v1/lease/release
```

All lease API endpoints require:

```text
Microsoft Entra bearer token
application role: ServerEngine.Access
```

The service identifies the customer from:

```text
tid
azp / appid
```

`server_instance_id` is created by ServerEngine and is not a Project identity.

## Important V1 boundary

This version enforces concurrent Server count in SQL and signs short-lived
leases, but `server_instance_id` is still a presented identifier. Per-installation
cryptographic activation/attestation is a later slice.

## Development setup

1. Install .NET 9 SDK.
2. Configure SQL Server connection string in `LicenseService/appsettings.json`.
3. Configure the License Service Entra API registration under `AzureAdApi`.
4. Create the `ServerEngine.Access` application role on the License Service API.
5. Grant the ServerEngine daemon application that application permission and
   grant admin consent.
6. Create a lease-signing RSA certificate with a private key in the Windows
   certificate store and set its thumbprint under `Lease`.
7. Configure Kestrel HTTPS certificate settings.
8. Run:

```powershell
dotnet restore .\LicenseService\LicenseService.csproj
dotnet build .\LicenseService\LicenseService.csproj -c Release
dotnet run --project .\LicenseService\LicenseService.csproj
```

Admin UI is intentionally localhost-only in V1:

```text
https://localhost:8443/
```

Remote callers can use the protected `/api/v1/lease/*` endpoints.

## Publish / Windows Service

```powershell
.\scripts\publish.ps1

.\scripts\install-windows-service.ps1 `
    -PublishDirectory .\publish
```

## First configuration

Open the local Blazor UI and create:

```text
Customer
    Name
    Entra tenant ID
    Entra ServerEngine client ID

Entitlement
    Product = ServerEngineV4
    Expires
    Max active servers
    Max connections per server
    Lease minutes
```

After that, an authenticated ServerEngine can acquire a lease.

## Lease token

The response includes an RS256 JWT/JWS lease token containing:

```text
jti
sub = server_instance_id
customer_id
product
max_connections
iat / nbf / exp
iss / aud
```

The next ServerEngine slice should verify this token locally and stop trusting
the unsigned development `server.lease` file.

## Development-only API authentication

Local API testing may use an explicit Development-only authentication scheme.

It is accepted only when all three are true:

ASPNETCORE_ENVIRONMENT=Development
DevelopmentAuthentication:Enabled=true
X-CW-Development-Authentication: 1

The configured development TenantId and ClientId become the tid and azp claims
and the request receives the configured ServerEngine.Access role.

The service fails during startup if this mode is enabled outside Development.
Disable it before production deployment.


## Lease concurrency control

Lease mutation is serialized per `(CustomerId, Product)` by locking the
corresponding Entitlement row with SQL Server `UPDLOCK, HOLDLOCK`.

The Entitlement row is the concurrency root because it owns the lease limits
for that customer/product pair. Different customers and products remain
independent.

All acquire/renew flows take this lock before reading the current lease,
counting active servers, or writing lease state. Renew may read the historical
lease once to discover Product, but it revalidates the exact active lease after
the Entitlement lock is held.

Blind SQL retry is not used as the primary synchronization mechanism.
