using WendYourWay.Cli;

namespace WendYourWay.Integration.Specs.Cli;

/// <summary>
/// The laptop kept awake while a long job runs (#142, T138, STORY-049 AC9; user chose "Keep it
/// awake"): Windows asked, for as long as the job runs. Every job takes it: JobRuns and wend update.
/// </summary>
[TestFixture]
public class StayingAwakeWhileAJobRuns
{
    [Test]
    public void Windows_takes_the_request()
    {
        using var awake = new StayingAwake();
        Assert.That(awake.Granted, Is.EqualTo(OperatingSystem.IsWindows()));
    }

    [Test]
    public void It_lets_go_when_the_job_ends() =>
        Assert.That(() => new StayingAwake().Dispose(), Throws.Nothing);
}
