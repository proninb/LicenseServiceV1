namespace LicenseService.Licensing;

public sealed record AcquireLeaseRequest(
    string ServerInstanceId,
    string Product);

public sealed record RenewLeaseRequest(
    Guid LeaseId,
    string ServerInstanceId);

public sealed record ReleaseLeaseRequest(
    Guid LeaseId,
    string ServerInstanceId);

public sealed record LeaseResponse(
    Guid LeaseId,
    string ServerInstanceId,
    string Product,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    int MaxConnections,
    string LeaseToken);

public enum LeaseFailure
{
    None = 0,
    InvalidRequest,
    InvalidCaller,
    CustomerNotFound,
    CustomerDisabled,
    EntitlementNotFound,
    EntitlementExpired,
    InstanceDisabled,
    ActiveServerLimitReached,
    LeaseNotFound,
    SigningFailed,
}

public sealed record LeaseResult(
    LeaseResponse? Lease,
    LeaseFailure Failure,
    string Detail)
{
    public bool Succeeded =>
        Lease is not null &&
        Failure == LeaseFailure.None;

    public static LeaseResult Success(
        LeaseResponse lease) =>
        new(
            lease,
            LeaseFailure.None,
            string.Empty);

    public static LeaseResult Failed(
        LeaseFailure failure,
        string detail) =>
        new(
            null,
            failure,
            detail);
}
