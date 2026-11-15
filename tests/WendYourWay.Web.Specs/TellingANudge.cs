using WendYourWay.Data;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// What the Loop card says after a Nudge (T076, T079; moved out of the page in T123): why one
/// couldn't be made, and how far one made is from the Distance asked for.
/// </summary>
public class TellingANudge
{
    private static readonly LatLon Somewhere = new(51.5650, -0.1500);

    [Test]
    public void An_Avoid_with_no_way_round_says_so() =>
        Assert.That(NudgeNotes.Failed(new AvoidNudge(Somewhere)), Is.EqualTo("No way round there: the Loop is as it was"));

    [Test]
    public void A_stop_removed_with_no_way_without_it_says_so() =>
        Assert.That(NudgeNotes.Failed(new RemoveStopNudge(1)), Is.EqualTo("No way without it: the Loop is as it was"));

    [Test]
    public void An_Avoid_removed_with_no_way_without_it_says_so() =>
        Assert.That(NudgeNotes.Failed(new RemoveAvoidNudge(0)), Is.EqualTo("No way without it: the Loop is as it was"));

    [Test]
    public void A_drag_with_no_way_there_says_so() =>
        Assert.That(NudgeNotes.Failed(new ViaNudge(Somewhere, Somewhere)), Is.EqualTo("No way there: the Loop is as it was"));

    [Test]
    public void Longer_than_asked_it_says_by_how_much() => Assert.That(NudgeNotes.Made(1.26), Is.EqualTo("1.3 km longer than you asked"));

    [Test]
    public void Shorter_than_asked_it_says_by_how_much() => Assert.That(NudgeNotes.Made(-0.84), Is.EqualTo("0.8 km shorter than you asked"));

    [Test]
    public void Within_the_Distance_it_says_nothing() => Assert.That(NudgeNotes.Made(0), Is.Null);
}
