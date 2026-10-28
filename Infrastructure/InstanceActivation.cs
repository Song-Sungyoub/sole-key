namespace SoleKey.Infrastructure;

public sealed class InstanceActivation : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle show;
    private readonly RegisteredWaitHandle? registration;
    private volatile bool disposed;
    public bool IsPrimary { get; }
    public InstanceActivation(string name, Action activate)
    {
        show = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Show");
        mutex = new Mutex(false, name);
        try { IsPrimary = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
        if (IsPrimary)
            registration = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => { if (!disposed) activate(); }, null, Timeout.Infinite, false);
        else show.Set();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; registration?.Unregister(null); show.Dispose();
        if (IsPrimary) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
