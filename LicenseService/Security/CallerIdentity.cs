using System.Security.Claims;

namespace LicenseService.Security;

public readonly record struct CallerIdentity(
    string TenantId,
    string ClientId);

public static class CallerIdentityReader
{
    public static bool TryRead(
        ClaimsPrincipal principal,
        out CallerIdentity identity)
    {
        var tenantId =
            principal.FindFirstValue("tid");

        var clientId =
            principal.FindFirstValue("azp")
            ?? principal.FindFirstValue("appid");

        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(clientId))
        {
            identity = default;
            return false;
        }

        identity =
            new CallerIdentity(
                tenantId,
                clientId);

        return true;
    }
}
