using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// A backup under way is finished before the app stops (#142, T138, STORY-049): closing the window
/// now stops the app, perhaps mid-backup. The app's start-up backup and Back up now run as work the
/// host waits for as it stops.
/// </summary>
public class FinishingBackupsBeforeStopping
{
    [Test]
    public async Task Stopping_waits_for_work_under_way()
    {
        var underWay = new WorkUnderWay();
        var done = new TaskCompletionSource();
        _ = underWay.Run(() => done.Task);
        var stopping = underWay.StopAsync(CancellationToken.None);
        await Task.Delay(100);
        Assert.That(stopping.IsCompleted, Is.False);
        done.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(stopping.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task With_nothing_under_way_it_stops_at_once() =>
        await new WorkUnderWay().StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));

    [Test]
    public async Task Work_that_fails_doesnt_stop_it_stopping()
    {
        var underWay = new WorkUnderWay();
        _ = underWay.Run(() => Task.FromException(new IOException("No room.")));
        await underWay.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task What_it_ran_comes_back()
    {
        var underWay = new WorkUnderWay();
        Assert.That(await underWay.Run(() => Task.FromResult("Backed up.")), Is.EqualTo("Backed up."));
    }
}
