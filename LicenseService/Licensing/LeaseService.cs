using LicenseService.Data;
using LicenseService.Security;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace LicenseService.Licensing;

public sealed class LeaseService
{
    private readonly IDbContextFactory<LicenseDbContext> databaseFactory;
    private readonly LeaseSigner signer;
    private readonly IConfiguration configuration;
    private readonly ILogger<LeaseService> logger;

    public LeaseService(
        IDbContextFactory<LicenseDbContext> databaseFactory,
        LeaseSigner signer,
        IConfiguration configuration,
        ILogger<LeaseService> logger)
    {
        this.databaseFactory =
            databaseFactory;

        this.signer =
            signer;

        this.configuration =
            configuration;

        this.logger =
            logger;
    }

    public Task<LeaseResult> AcquireAsync(
        CallerIdentity caller,
        AcquireLeaseRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                request.ServerInstanceId) ||
            request.ServerInstanceId.Length > 128 ||
            string.IsNullOrWhiteSpace(
                request.Product) ||
            request.Product.Length > 64)
        {
            return Task.FromResult(
                LeaseResult.Failed(
                    LeaseFailure.InvalidRequest,
                    "Invalid Server instance or product."));
        }

        return IssueAsync(
            caller,
            request.ServerInstanceId,
            request.Product,
            null,
            cancellationToken);
    }

    public Task<LeaseResult> RenewAsync(
        CallerIdentity caller,
        RenewLeaseRequest request,
        CancellationToken cancellationToken)
    {
        if (request.LeaseId == Guid.Empty ||
            string.IsNullOrWhiteSpace(
                request.ServerInstanceId) ||
            request.ServerInstanceId.Length > 128)
        {
            return Task.FromResult(
                LeaseResult.Failed(
                    LeaseFailure.InvalidRequest,
                    "Invalid lease or Server instance."));
        }

        return IssueAsync(
            caller,
            request.ServerInstanceId,
            null,
            request.LeaseId,
            cancellationToken);
    }

    private async Task<LeaseResult> IssueAsync(
        CallerIdentity caller,
        string serverInstanceId,
        string? acquireProduct,
        Guid? renewLeaseId,
        CancellationToken cancellationToken)
    {
        await using var database =
            await databaseFactory.CreateDbContextAsync(
                cancellationToken);

        await using var transaction =
            await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var now =
            DateTimeOffset.UtcNow;

        var customer =
            await database.Customers
                .SingleOrDefaultAsync(
                    value =>
                        value.EntraTenantId == caller.TenantId &&
                        value.EntraClientId == caller.ClientId,
                    cancellationToken);

        if (customer is null)
        {
            return LeaseResult.Failed(
                LeaseFailure.CustomerNotFound,
                "No customer is mapped to the authenticated ServerEngine application.");
        }

        if (!customer.Enabled)
        {
            return LeaseResult.Failed(
                LeaseFailure.CustomerDisabled,
                "Customer is disabled.");
        }

        string product;

        if (renewLeaseId.HasValue)
        {
            var renewalIdentity =
                await database.Leases
                    .AsNoTracking()
                    .Where(
                        value =>
                            value.Id == renewLeaseId.Value &&
                            value.CustomerId == customer.Id &&
                            value.ServerInstanceId == serverInstanceId)
                    .Select(
                        value =>
                            new
                            {
                                value.Product,
                            })
                    .SingleOrDefaultAsync(
                        cancellationToken);

            if (renewalIdentity is null)
            {
                return LeaseResult.Failed(
                    LeaseFailure.LeaseNotFound,
                    "Lease was not found.");
            }

            product =
                renewalIdentity.Product;
        }
        else
        {
            product =
                acquireProduct!;
        }

        // Entitlement is the serialization root for all lease mutations of
        // one customer/product pair.
        var entitlement =
            await database.Entitlements
                .FromSqlInterpolated(
                    $@"SELECT *
                       FROM [Entitlements] WITH (UPDLOCK, HOLDLOCK)
                       WHERE [CustomerId] = {customer.Id}
                         AND [Product] = {product}")
                .SingleOrDefaultAsync(
                    cancellationToken);

        if (entitlement is null ||
            !entitlement.Enabled)
        {
            return LeaseResult.Failed(
                LeaseFailure.EntitlementNotFound,
                "No active entitlement exists for this product.");
        }

        if (entitlement.ExpiresAtUtc <= now)
        {
            return LeaseResult.Failed(
                LeaseFailure.EntitlementExpired,
                "The entitlement has expired.");
        }

        LeaseRecord? currentLease;

        if (renewLeaseId.HasValue)
        {
            currentLease =
                await database.Leases
                    .SingleOrDefaultAsync(
                        value =>
                            value.Id == renewLeaseId.Value &&
                            value.CustomerId == customer.Id &&
                            value.ServerInstanceId == serverInstanceId &&
                            value.Product == product &&
                            value.ReleasedAtUtc == null &&
                            value.ExpiresAtUtc > now,
                        cancellationToken);

            if (currentLease is null)
            {
                return LeaseResult.Failed(
                    LeaseFailure.LeaseNotFound,
                    "Active lease was not found.");
            }
        }
        else
        {
            currentLease =
                await database.Leases
                    .Where(
                        value =>
                            value.CustomerId == customer.Id &&
                            value.ServerInstanceId == serverInstanceId &&
                            value.Product == product &&
                            value.ReleasedAtUtc == null &&
                            value.ExpiresAtUtc > now)
                    .OrderByDescending(
                        value =>
                            value.ExpiresAtUtc)
                    .FirstOrDefaultAsync(
                        cancellationToken);
        }

        var instance =
            await database.ServerInstances
                .SingleOrDefaultAsync(
                    value =>
                        value.Id == serverInstanceId,
                    cancellationToken);

        if (instance is null)
        {
            if (renewLeaseId.HasValue)
            {
                return LeaseResult.Failed(
                    LeaseFailure.LeaseNotFound,
                    "Server instance for the active lease was not found.");
            }

            instance =
                new ServerInstance
                {
                    Id =
                        serverInstanceId,
                    CustomerId =
                        customer.Id,
                    FirstSeenUtc =
                        now,
                    LastSeenUtc =
                        now,
                };

            database.ServerInstances.Add(
                instance);
        }
        else
        {
            if (instance.CustomerId !=
                customer.Id)
            {
                return LeaseResult.Failed(
                    LeaseFailure.InvalidCaller,
                    "Server instance belongs to another customer.");
            }

            if (instance.Disabled)
            {
                return LeaseResult.Failed(
                    LeaseFailure.InstanceDisabled,
                    "Server instance is disabled.");
            }

            instance.LastSeenUtc =
                now;
        }

        if (currentLease is null)
        {
            var activeServers =
                await database.Leases
                    .Where(
                        value =>
                            value.CustomerId == customer.Id &&
                            value.Product == product &&
                            value.ReleasedAtUtc == null &&
                            value.ExpiresAtUtc > now)
                    .Select(
                        value =>
                            value.ServerInstanceId)
                    .Distinct()
                    .CountAsync(
                        cancellationToken);

            if (activeServers >=
                entitlement.MaxActiveServers)
            {
                return LeaseResult.Failed(
                    LeaseFailure.ActiveServerLimitReached,
                    "Maximum active ServerEngine instance count has been reached.");
            }
        }

        var leaseMinutes =
            entitlement.LeaseMinutes > 0
                ? entitlement.LeaseMinutes
                : configuration.GetValue(
                    "Lease:DefaultMinutes",
                    60);

        var expiresAt =
            now.AddMinutes(
                leaseMinutes);

        if (expiresAt >
            entitlement.ExpiresAtUtc)
        {
            expiresAt =
                entitlement.ExpiresAtUtc;
        }

        var lease =
            new LeaseRecord
            {
                Id =
                    Guid.NewGuid(),
                ServerInstanceId =
                    serverInstanceId,
                CustomerId =
                    customer.Id,
                EntitlementId =
                    entitlement.Id,
                Product =
                    product,
                IssuedAtUtc =
                    now,
                ExpiresAtUtc =
                    expiresAt,
                MaxConnections =
                    entitlement.MaxConnectionsPerServer,
            };

        string token;

        try
        {
            token =
                signer.CreateToken(
                    lease.Id,
                    customer.Id,
                    lease.ServerInstanceId,
                    lease.Product,
                    lease.IssuedAtUtc,
                    lease.ExpiresAtUtc,
                    lease.MaxConnections);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Lease signing failed for Server instance {ServerInstanceId}.",
                serverInstanceId);

            return LeaseResult.Failed(
                LeaseFailure.SigningFailed,
                "Lease signing failed.");
        }

        if (currentLease is not null)
        {
            currentLease.ReleasedAtUtc =
                now;
        }

        database.Leases.Add(
            lease);

        await database.SaveChangesAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        return LeaseResult.Success(
            new LeaseResponse(
                lease.Id,
                lease.ServerInstanceId,
                lease.Product,
                lease.IssuedAtUtc,
                lease.ExpiresAtUtc,
                lease.MaxConnections,
                token));
    }

    public async Task<bool> ReleaseAsync(
        CallerIdentity caller,
        ReleaseLeaseRequest request,
        CancellationToken cancellationToken)
    {
        await using var database =
            await databaseFactory.CreateDbContextAsync(
                cancellationToken);

        var lease =
            await database.Leases
                .SingleOrDefaultAsync(
                    value =>
                        value.Id == request.LeaseId &&
                        value.ServerInstanceId == request.ServerInstanceId,
                    cancellationToken);

        if (lease is null)
        {
            return false;
        }

        var customerMatches =
            await database.Customers
                .AnyAsync(
                    value =>
                        value.Id == lease.CustomerId &&
                        value.EntraTenantId == caller.TenantId &&
                        value.EntraClientId == caller.ClientId,
                    cancellationToken);

        if (!customerMatches)
        {
            return false;
        }

        if (lease.ReleasedAtUtc is null)
        {
            lease.ReleasedAtUtc =
                DateTimeOffset.UtcNow;

            await database.SaveChangesAsync(
                cancellationToken);
        }

        return true;
    }
}
