using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;

namespace WendYourWay.Core.Specs.Loops;

/// <summary>
/// A Request's Distance must be one the generator can sensibly meet: a bigger one would load a
/// slice the size of a county (Slice 1 review).
/// </summary>
public class MakingARequest
{
    //  The same Request at other Leans, as a background search makes it after a slider moves
    //  (T053b): the same start, Distance and Activity. A Gravel Ride's was made as a Run, its
    //  Loops found without Obstacle costs (found 2026-11-08, T083).
    [Test]
    public void At_other_Leans_it_is_the_same_Request_but_for_its_Leans()
    {
        var asked = new Request(new NodePoint(100, 200), 40, 0.5, -0.5, Activity.GravelRide);
        var again = asked.At(new Leans(-1, 1));
        Assert.That((again.Start, again.DistanceKm, again.Activity, again.Leans), Is.EqualTo((asked.Start, 40.0, Activity.GravelRide, new Leans(-1, 1))));
    }

    [TestCase(0.5)]
    [TestCase(100.5)]
    [TestCase(double.NaN)]
    public void A_Distance_outside_1_to_100_km_is_refused(double km) =>
        Assert.That(() => new Request(new NodePoint(0, 0), km), Throws.TypeOf<ArgumentOutOfRangeException>());

    [TestCase(1)]
    [TestCase(100)]
    public void A_Distance_from_1_to_100_km_is_accepted(double km) =>
        Assert.That(new Request(new NodePoint(0, 0), km).DistanceKm, Is.EqualTo(km));

    [TestCase(-1.01)]
    [TestCase(1.01)]
    [TestCase(double.NaN)]
    public void A_Hills_Lean_outside_minus_1_to_1_is_refused(double lean) =>
        Assert.That(() => new Request(new NodePoint(0, 0), 10, lean), Throws.TypeOf<ArgumentOutOfRangeException>());

    [TestCase(-1)]
    [TestCase(1)]
    public void A_Hills_Lean_from_minus_1_to_1_is_accepted(double lean) =>
        Assert.That(new Request(new NodePoint(0, 0), 10, lean).HillsLean, Is.EqualTo(lean));

    [TestCase(-1.01)]
    [TestCase(1.01)]
    [TestCase(double.NaN)]
    public void An_Off_road_Lean_outside_minus_1_to_1_is_refused(double lean) =>
        Assert.That(() => new Request(new NodePoint(0, 0), 10, OffRoadLean: lean), Throws.TypeOf<ArgumentOutOfRangeException>());

    [Test]
    public void A_Requests_Leans_are_its_Hills_and_Off_road_Leans() =>
        Assert.That(new Request(new NodePoint(0, 0), 10, HillsLean: -0.5, OffRoadLean: 1).Leans, Is.EqualTo(new Leans(Hills: -0.5, OffRoad: 1)));
}
