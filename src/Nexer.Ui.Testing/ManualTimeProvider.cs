namespace Nexer.Ui.Testing;

/// <summary>
/// A deterministic <see cref="TimeProvider"/> for tests: time only moves when <see cref="Advance"/> is called, and
/// due timers fire synchronously on the caller's thread.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> timers = [];
    private readonly Lock gate = new();
    private long ticks;

    /// <summary>Creates a provider starting at <paramref name="start"/> (default: 2000-01-01 UTC).</summary>
    public ManualTimeProvider(DateTimeOffset? start = null)
    {
        ticks = (start ?? new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)).UtcTicks;
    }

    /// <inheritdoc/>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc/>
    public override long GetTimestamp()
    {
        lock (gate) return ticks;
    }

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow()
    {
        lock (gate) return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    /// <inheritdoc/>
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <summary>Moves time forward, firing due timers in due-time order.</summary>
    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
        long target;
        lock (gate) target = ticks + delta.Ticks;
        while (true)
        {
            ManualTimer? next = null;
            lock (gate)
            {
                foreach (var t in timers)
                {
                    if (t.DueTicks <= target && (next == null || t.DueTicks < next.DueTicks)) next = t;
                }

                if (next == null)
                {
                    ticks = target;
                    return;
                }

                ticks = Math.Max(ticks, next.DueTicks);
                if (next.PeriodTicks > 0) next.DueTicks += next.PeriodTicks;
                else timers.Remove(next);
            }

            next.Fire();
        }
    }

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public long DueTicks;
        public long PeriodTicks;

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner.gate)
            {
                owner.timers.Remove(this);
                if (dueTime == Timeout.InfiniteTimeSpan) return true;
                DueTicks = owner.ticks + Math.Max(0, dueTime.Ticks);
                PeriodTicks = period == Timeout.InfiniteTimeSpan || period <= TimeSpan.Zero ? 0 : period.Ticks;
                owner.timers.Add(this);
                return true;
            }
        }

        public void Dispose()
        {
            lock (owner.gate) owner.timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
