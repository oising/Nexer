namespace Nexer.Ui;

/// <summary>Synchronization context that marshals work onto the application's UI loop.</summary>
internal sealed class UiSynchronizationContext(Application application) : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) => application.Post(() => d(state));

    public override void Send(SendOrPostCallback d, object? state) => application.Invoke(() => d(state));

    public override SynchronizationContext CreateCopy() => this;
}

/// <summary>
/// A timer whose callback runs on the UI thread (Jexer <c>TTimer</c>). Created by <see cref="Application.AddTimer"/>;
/// dispose to stop it.
/// </summary>
public sealed class UiTimer : IDisposable
{
    private readonly Application application;
    private readonly Action action;
    private ITimer? timer;
    private bool disposed;

    internal UiTimer(Application application, TimeSpan interval, Action action, bool recurring)
    {
        this.application = application;
        this.action = action;
        Interval = interval;
        Recurring = recurring;
    }

    /// <summary>The interval.</summary>
    public TimeSpan Interval { get; }

    /// <summary>Whether the timer repeats.</summary>
    public bool Recurring { get; }

    /// <summary>True once the timer has been disposed (or a one-shot timer has fired).</summary>
    public bool IsDisposed => disposed;

    internal void Start(TimeProvider time)
    {
        if (disposed || timer != null) return;
        timer = time.CreateTimer(static s => ((UiTimer)s!).Elapsed(), this, Interval,
            Recurring ? Interval : Timeout.InfiniteTimeSpan);
    }

    private void Elapsed() => application.Post(Fire);

    private void Fire()
    {
        if (disposed) return;
        if (!Recurring) Dispose();
        action();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer?.Dispose();
        timer = null;
        application.RemoveTimer(this);
    }
}
