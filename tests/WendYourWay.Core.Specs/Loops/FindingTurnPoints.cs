using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;

namespace WendYourWay.Core.Specs.Loops;

/// <summary>
/// Turn points (PLAN T100, STORY-043; user: "45° over 30 m"): at a junction with a choice, where the
/// route's bearing over the 30 m after differs from the 30 m before by 45 degrees or more; junctions
/// closer than 30 m are one, the sharpest speaking; said as "Left onto ...", "Sharp right onto ...",
/// "Turn back onto ...". On lines drawn here, in metres, north up.
/// </summary>
public class FindingTurnPoints
{
    //  North 200 m to a junction, then 200 m on at this bearing (degrees clockwise from north).
    private static (NodePoint[] Line, RouteJunction[] Junctions) Bend(double degrees, int ways = 3, string onto = "Bush Road")
    {
        var radians = degrees * Math.PI / 180;
        NodePoint[] line = [new(0, 0), new(0, 200), new(200 * Math.Sin(radians), 200 + (200 * Math.Cos(radians)))];
        return (line, [new RouteJunction(1, ways, onto)]);
    }

    private static IReadOnlyList<TurnPoint> Turns((NodePoint[] Line, RouteJunction[] Junctions) route) => TurnPoints.Find(route.Line, route.Junctions);

    [Test]
    public void A_right_angle_right_at_a_junction_is_a_turn_point() =>
        Assert.That(Turns(Bend(90)).Select(t => t.Words), Is.EqualTo(new[] { "Right onto Bush Road" }));

    [Test]
    public void A_right_angle_left_is_a_left() =>
        Assert.That(Turns(Bend(-90)).Select(t => t.Words), Is.EqualTo(new[] { "Left onto Bush Road" }));

    [Test]
    public void It_is_where_the_junction_is() =>
        Assert.That(Turns(Bend(90)).Single().At, Is.EqualTo(new NodePoint(0, 200)));

    //  The way on, where it bends with no other way to take, needs no word.
    [Test]
    public void A_bend_with_no_choice_isn_t() =>
        Assert.That(Turns(Bend(90, ways: 2)), Is.Empty);

    [TestCase(44.5, 0)]
    [TestCase(45.5, 1)]
    public void It_turns_45_degrees_or_more(double degrees, int count) =>
        Assert.That(Turns(Bend(degrees)), Has.Count.EqualTo(count));

    [TestCase(134, "Right onto Bush Road")]
    [TestCase(136, "Sharp right onto Bush Road")]
    [TestCase(-150, "Sharp left onto Bush Road")]
    [TestCase(164, "Sharp right onto Bush Road")]
    [TestCase(166, "Turn back onto Bush Road")]
    public void How_far_it_turns_is_said(double degrees, string words) =>
        Assert.That(Turns(Bend(degrees)).Single().Words, Is.EqualTo(words));

    [Test]
    public void It_is_said_onto_what_the_junction_says() =>
        Assert.That(Turns(Bend(90, onto: "the footpath")).Single().Words, Is.EqualTo("Right onto the footpath"));

    //  Across a road: left onto the crossing, then right 10 m on. Over 30 m it hardly turns.
    [Test]
    public void A_zig_zag_across_a_road_isn_t()
    {
        NodePoint[] line = [new(0, 0), new(0, 200), new(-10, 200), new(-10, 400)];
        Assert.That(TurnPoints.Find(line, [new RouteJunction(1, 4, "the crossing"), new RouteJunction(2, 4, "the path")]), Is.Empty);
    }

    //  Two turns 20 m apart are one: the sharper speaks, where it is.
    [Test]
    public void Turns_closer_than_30_m_are_one()
    {
        NodePoint[] line = [new(0, 0), new(0, 200), new(20, 200), new(20, 0)];
        var turns = TurnPoints.Find(line, [new RouteJunction(1, 3, "the lane"), new RouteJunction(2, 3, "the path")]);
        Assert.That(turns.Select(t => t.At), Is.EqualTo(new[] { new NodePoint(0, 200) }).Or.EqualTo(new[] { new NodePoint(20, 200) }));
    }

    //  Two turns 100 m apart are two.
    [Test]
    public void Turns_further_apart_are_each_said()
    {
        NodePoint[] line = [new(0, 0), new(0, 200), new(100, 200), new(100, 400)];
        var turns = TurnPoints.Find(line, [new RouteJunction(1, 3, "the lane"), new RouteJunction(2, 3, "the path")]);
        Assert.That(turns.Select(t => t.Words), Is.EqualTo(new[] { "Right onto the lane", "Left onto the path" }));
    }

    [Test]
    public void Its_distance_along_the_route_is_kept() =>
        Assert.That(Turns(Bend(90)).Single().AlongM, Is.EqualTo(200).Within(1e-9));
}
