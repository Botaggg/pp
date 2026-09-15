namespace Callout.Core;

public sealed class AdminSession
{
    public Guid Id { get; set; }
    public string CredentialVersion { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

// Unique keys make authenticator/recovery codes single-use across app instances.
public sealed class UsedAdminCode
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
