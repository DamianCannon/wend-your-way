using WendYourWay.Cli;

namespace WendYourWay.Integration.Specs.Cli;

/// <summary>
/// wend keeps Windows from sleeping while it works: a Great Britain build was paused for 14 hours by
/// Modern Standby while its clock counted on (T035). Windows reports the system's execution state,
/// so this checks it really asked.
/// </summary>
[TestFixture]
[Platform("Win")]
public class KeepingTheLaptopAwake
{
    [Test]
    public void Then_Windows_is_asked_not_to_sleep_while_ct_works()
    {
        using var awake = KeepAwake.WhileRunning();
        Assert.That(KeepAwake.SystemRequired(), Is.True);
    }
}
