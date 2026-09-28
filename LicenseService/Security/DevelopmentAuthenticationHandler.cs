using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace LicenseService.Security;

public static class DevelopmentAuthenticationDefaults
{
    public const string Scheme = "CW.Development";
}

public sealed class DevelopmentAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IConfiguration configuration;

    public DevelopmentAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        this.configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Context.Request.Headers.TryGetValue(
                "X-CW-Development-Authentication",
                out var values) ||
            values.Count != 1 ||
            !string.Equals(values[0], "1", StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var tenantId = configuration["DevelopmentAuthentication:TenantId"];
        var clientId = configuration["DevelopmentAuthentication:ClientId"];
        var role =
            configuration["Authorization:RequiredApplicationRole"]
            ?? "ServerEngine.Access";

        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(clientId))
        {
            return Task.FromResult(
                AuthenticateResult.Fail(
                    "Development authentication requires TenantId and ClientId."));
        }

        var claims = new[]
        {
            new Claim("tid", tenantId),
            new Claim("azp", clientId),
            new Claim(ClaimTypes.Role, role),
        };

        var identity =
            new ClaimsIdentity(
                claims,
                Scheme.Name);

        var principal =
            new ClaimsPrincipal(identity);

        var ticket =
            new AuthenticationTicket(
                principal,
                Scheme.Name);

        return Task.FromResult(
            AuthenticateResult.Success(ticket));
    }
}
