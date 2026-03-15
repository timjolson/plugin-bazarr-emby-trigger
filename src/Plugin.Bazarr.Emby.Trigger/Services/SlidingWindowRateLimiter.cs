using System;
using System.Collections.Generic;
using System.Linq;

namespace Plugin.Bazarr.Emby.Trigger.Services;

public class SlidingWindowRateLimiter
{
    private readonly object syncRoot = new object();
    private readonly Queue<DateTime> acceptedUtc = new Queue<DateTime>();

    public bool TryAcquire(DateTime utcNow, int limitPerHour)
    {
        lock (syncRoot)
        {
            TrimExpired(utcNow);
            if (limitPerHour <= 0 || acceptedUtc.Count >= limitPerHour)
            {
                return false;
            }

            acceptedUtc.Enqueue(utcNow);
            return true;
        }
    }

    public DateTime? GetNextAvailableUtc(DateTime utcNow, int limitPerHour)
    {
        lock (syncRoot)
        {
            TrimExpired(utcNow);
            if (limitPerHour <= 0 || acceptedUtc.Count < limitPerHour)
            {
                return utcNow;
            }

            return acceptedUtc.Peek().AddHours(1);
        }
    }

    public IReadOnlyCollection<DateTime> Snapshot(DateTime utcNow)
    {
        lock (syncRoot)
        {
            TrimExpired(utcNow);
            return acceptedUtc.ToList();
        }
    }

    private void TrimExpired(DateTime utcNow)
    {
        while (acceptedUtc.Count > 0 && acceptedUtc.Peek().AddHours(1) <= utcNow)
        {
            acceptedUtc.Dequeue();
        }
    }
}
