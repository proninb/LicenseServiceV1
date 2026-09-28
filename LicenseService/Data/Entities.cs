namespace LicenseService.Data;

public sealed class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string EntraTenantId { get; set; } = string.Empty;

    public string EntraClientId { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public List<Entitlement> Entitlements { get; set; } = [];
}

public sealed class Entitlement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public string Product { get; set; } = "ServerEngineV4";

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public int MaxActiveServers { get; set; } = 1;

    public int MaxConnectionsPerServer { get; set; } = 32;

    public int LeaseMinutes { get; set; } = 60;

    public bool Enabled { get; set; } = true;
}

public sealed class ServerInstance
{
    public string Id { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public DateTimeOffset FirstSeenUtc { get; set; }

    public DateTimeOffset LastSeenUtc { get; set; }

    public bool Disabled { get; set; }
}

public sealed class LeaseRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ServerInstanceId { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public Guid EntitlementId { get; set; }

    public string Product { get; set; } = string.Empty;

    public DateTimeOffset IssuedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? ReleasedAtUtc { get; set; }

    public int MaxConnections { get; set; }
}
