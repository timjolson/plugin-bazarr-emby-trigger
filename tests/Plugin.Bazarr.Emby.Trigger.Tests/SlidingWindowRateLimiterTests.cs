using Plugin.Bazarr.Emby.Trigger.Services;

namespace Plugin.Bazarr.Emby.Trigger.Tests;

public class SlidingWindowRateLimiterTests
{
    [Fact]
    public void TryAcquire_RejectsRequestsPastHourlyLimit()
    {
        var limiter = new SlidingWindowRateLimiter();
        var now = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(limiter.TryAcquire(now, 2));
        Assert.True(limiter.TryAcquire(now.AddMinutes(1), 2));
        Assert.False(limiter.TryAcquire(now.AddMinutes(2), 2));
    }

    [Fact]
    public void TryAcquire_OpensWindowAfterOneHour()
    {
        var limiter = new SlidingWindowRateLimiter();
        var now = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(limiter.TryAcquire(now, 1));
        Assert.False(limiter.TryAcquire(now.AddMinutes(30), 1));
        Assert.True(limiter.TryAcquire(now.AddHours(1).AddSeconds(1), 1));
    }
}
