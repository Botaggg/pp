namespace Callout.Web;

/// <summary>Bound from the "Admin" configuration section.</summary>
public class AdminOptions
{
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// An ASP.NET Core <c>PasswordHasher</c> hash. Never the plaintext password.
    /// Generate with: dotnet run --project src/Callout.Web -- hash-password
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;
}

public class AdminCredentials
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
}
