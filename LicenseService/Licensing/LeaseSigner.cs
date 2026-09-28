using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography.X509Certificates;
using System.Security.Claims;

namespace LicenseService.Licensing;

public sealed class LeaseSigner
{
    private readonly IConfiguration configuration;

    public LeaseSigner(
        IConfiguration configuration)
    {
        this.configuration =
            configuration;
    }

    public string CreateToken(
        Guid leaseId,
        Guid customerId,
        string serverInstanceId,
        string product,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc,
        int maxConnections)
    {
        using var certificate =
            LoadCertificate();

        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException(
                "Lease signing certificate has no private key.");
        }

        var issuer =
            configuration["Lease:Issuer"]
            ?? throw new InvalidOperationException(
                "Lease:Issuer is required.");

        var audience =
            configuration["Lease:Audience"]
            ?? "ServerEngineV4";

        var claims =
            new Dictionary<string, object>
            {
                ["jti"] = leaseId.ToString("D"),
                ["sub"] = serverInstanceId,
                ["customer_id"] = customerId.ToString("D"),
                ["product"] = product,
                ["max_connections"] = maxConnections,
            };

        var descriptor =
            new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = audience,
                Claims = claims,
                IssuedAt = issuedAtUtc.UtcDateTime,
                NotBefore = issuedAtUtc.UtcDateTime,
                Expires = expiresAtUtc.UtcDateTime,
                SigningCredentials =
                    new SigningCredentials(
                        new X509SecurityKey(certificate),
                        SecurityAlgorithms.RsaSha256),
            };

        return
            new JsonWebTokenHandler()
                .CreateToken(descriptor);
    }

    private X509Certificate2 LoadCertificate()
    {
        var thumbprint =
            configuration["Lease:SigningCertificateThumbprint"];

        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            throw new InvalidOperationException(
                "Lease:SigningCertificateThumbprint is required.");
        }

        var storeName =
            StoreName.My;

        var storeLocation =
            string.Equals(
                configuration["Lease:SigningCertificateStore"],
                "CurrentUser",
                StringComparison.OrdinalIgnoreCase)
                ? StoreLocation.CurrentUser
                : StoreLocation.LocalMachine;

        using var store =
            new X509Store(
                storeName,
                storeLocation);

        store.Open(
            OpenFlags.ReadOnly);

        var found =
            store.Certificates.Find(
                X509FindType.FindByThumbprint,
                thumbprint,
                validOnly: false);

        if (found.Count == 0)
        {
            throw new InvalidOperationException(
                $"Lease signing certificate was not found: {thumbprint}");
        }

        return new X509Certificate2(
            found[0]);
    }
}
