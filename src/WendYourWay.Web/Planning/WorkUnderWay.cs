namespace WendYourWay.Web.Planning;

/// <summary>
/// Work the app finishes before it stops (#142, T138, STORY-049): its start-up backup and Back up
/// now. Closing the window stops the app, and a backup cut off is no backup, so the host waits for
/// what's under way as it stops (up to its shutdown timeout).
/// </summary>
public sealed class WorkUnderWay : IHostedService
{
    private readonly Lock _gate = new();
    private readonly List<Task> _underWay = [];

    /// <summary>Runs the work, kept till it's done; what it gives back.</summary>
    public Task<T> Run<T>(Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var task = Task.Run(work);
        Keep(task);
        return task;
    }

    /// <summary>Runs the work, kept till it's done.</summary>
    public Task Run(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var task = Task.Run(work);
        Keep(task);
        return task;
    }

    private void Keep(Task task)
    {
        lock (_gate) _underWay.Add(task);
        _ = task.ContinueWith(done => { lock (_gate) _underWay.Remove(done); }, TaskScheduler.Default);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Done when everything under way is, however it ends.</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task[] underWay;
        lock (_gate) underWay = [.. _underWay];
        try
        {
            await Task.WhenAll(underWay).WaitAsync(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Each says its own failure; stopping goes on.
        }
    }
}
