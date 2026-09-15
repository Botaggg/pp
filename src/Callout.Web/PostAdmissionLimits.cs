using System.Threading.RateLimiting;

namespace Callout.Web;

/// <summary>Process-wide bounds on admitted login and submission work.</summary>
public sealed class PostAdmissionLimits : IDisposable
{
    private readonly FixedWindowRateLimiter _login = CreateLimiter(30);
    private readonly FixedWindowRateLimiter _request = CreateLimiter(100);

    public bool TryAcquireLogin() => TryAcquire(_login);
    public bool TryAcquireRequest() => TryAcquire(_request);

    private static FixedWindowRateLimiter CreateLimiter(int permits) => new(new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(15),
        QueueLimit = 0,
        AutoReplenishment = true
    });

    private static bool TryAcquire(FixedWindowRateLimiter limiter)
    {
        using var lease = limiter.AttemptAcquire();
        return lease.IsAcquired;
    }

    public void Dispose()
    {
        _login.Dispose();
        _request.Dispose();
    }
}
