using LicenseService.Licensing;
using LicenseService.Security;

namespace LicenseService.Api;

public static class LeaseEndpoints
{
    public static void MapLeaseEndpoints(
        this WebApplication app)
    {
        var group =
            app.MapGroup("/api/v1/lease")
                .RequireAuthorization(
                    "ServerEngine");

        group.MapPost(
            "/acquire",
            AcquireAsync);

        group.MapPost(
            "/renew",
            RenewAsync);

        group.MapPost(
            "/release",
            ReleaseAsync);
    }

    private static async Task<IResult> AcquireAsync(
        HttpContext context,
        AcquireLeaseRequest request,
        LeaseService leases,
        CancellationToken cancellationToken)
    {
        if (!CallerIdentityReader.TryRead(
                context.User,
                out var caller))
        {
            return Results.Forbid();
        }

        var result =
            await leases.AcquireAsync(
                caller,
                request,
                cancellationToken);

        return ToHttpResult(
            result);
    }

    private static async Task<IResult> RenewAsync(
        HttpContext context,
        RenewLeaseRequest request,
        LeaseService leases,
        CancellationToken cancellationToken)
    {
        if (!CallerIdentityReader.TryRead(
                context.User,
                out var caller))
        {
            return Results.Forbid();
        }

        var result =
            await leases.RenewAsync(
                caller,
                request,
                cancellationToken);

        return ToHttpResult(
            result);
    }

    private static async Task<IResult> ReleaseAsync(
        HttpContext context,
        ReleaseLeaseRequest request,
        LeaseService leases,
        CancellationToken cancellationToken)
    {
        if (!CallerIdentityReader.TryRead(
                context.User,
                out var caller))
        {
            return Results.Forbid();
        }

        var released =
            await leases.ReleaseAsync(
                caller,
                request,
                cancellationToken);

        return released
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static IResult ToHttpResult(
        LeaseResult result)
    {
        if (result.Succeeded)
        {
            return Results.Ok(
                result.Lease);
        }

        return result.Failure switch
        {
            LeaseFailure.InvalidRequest =>
                Results.BadRequest(
                    new
                    {
                        error =
                            "invalid_request",
                        detail =
                            result.Detail,
                    }),

            LeaseFailure.ActiveServerLimitReached =>
                Results.Conflict(
                    new
                    {
                        error =
                            "active_server_limit_reached",
                        detail =
                            result.Detail,
                    }),

            LeaseFailure.CustomerNotFound or
            LeaseFailure.CustomerDisabled or
            LeaseFailure.EntitlementNotFound or
            LeaseFailure.EntitlementExpired or
            LeaseFailure.InstanceDisabled =>
                Results.Json(
                    new
                    {
                        error =
                            result.Failure.ToString(),
                        detail =
                            result.Detail,
                    },
                    statusCode:
                        StatusCodes.Status403Forbidden),

            LeaseFailure.LeaseNotFound =>
                Results.NotFound(
                    new
                    {
                        error =
                            "lease_not_found",
                        detail =
                            result.Detail,
                    }),

            LeaseFailure.SigningFailed =>
                Results.Json(
                    new
                    {
                        error =
                            "lease_signing_failed",
                        detail =
                            "Lease signing failed.",
                    },
                    statusCode:
                        StatusCodes.Status500InternalServerError),

            _ =>
                Results.BadRequest(
                    new
                    {
                        error =
                            result.Failure.ToString(),
                        detail =
                            result.Detail,
                    }),
        };
    }
}
