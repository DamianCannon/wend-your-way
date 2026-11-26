using WendYourWay.Core.Loops;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Loops;

/// <summary>
/// Wend It! (T194, #198; STORY-052; user: "a 'Wend It!' button that comes up with variations on the route"): a route
/// found again through its turning points, its start and turning points kept (the user chose "Its shape"), and only
/// variations with less road kept: no more Main Road, about as long, and on the route for half their length or more.
/// </summary>
public class WendingARoute
{
    //  Out along a road from S, past A, to B and back the same way (4.8 km): the user's usual run. A path runs beside
    //  the road from A to B, 300 m off it.
    private static readonly AsciiNetwork OutAndBack = AsciiNetwork.Parse("""
        S=======================A=======================B
                                :                       :
                                :                       :
                                C.......................D
        """);

    private static readonly Loop Run = OutAndBack.LoopThrough("SABAS");

    private static IReadOnlyList<Variation> Wended(AsciiNetwork network, Loop route) => WendIt.Of(network.Slice, route, ScoringWeights.Default);

    //  It goes to B and back as the run does, but one way along the path: 5.4 km, 1.2 km less road.
    [Test]
    public void A_variation_takes_the_path_one_way_and_keeps_the_turn()
    {
        var wended = Wended(OutAndBack, Run)[0];
        Assert.That(GeneratingALoop.JunctionsAlong(OutAndBack.Slice, wended.Loop).Select(OutAndBack.NameOf),
            Contains.Item('B').And.Contains('C').And.Contains('D'));
    }

    [Test]
    public void It_says_how_much_road_it_saves() =>
        Assert.That(Math.Round(Wended(OutAndBack, Run)[0].RoadSavedM), Is.EqualTo(1200));

    //  S to A both ways and A to B one way, 3.6 km of its 5.4 km, lie on the run.
    [Test]
    public void It_says_how_much_of_the_route_it_keeps() =>
        Assert.That(Math.Round(Wended(OutAndBack, Run)[0].KeptShare, 2), Is.EqualTo(0.67));

    //  Out on the path and back on the road, or out on the road and back on the path: the same ground, offered once.
    [Test]
    public void Variations_on_the_same_ground_are_offered_once() =>
        Assert.That(Wended(OutAndBack, Run), Has.Count.EqualTo(1));

    //  The run isn't trimmed to a stub: an out-and-back is the shape of it.
    [Test]
    public void It_is_about_as_long_as_the_route() =>
        Assert.That(Breakdown.Of(OutAndBack.Slice, Wended(OutAndBack, Run)[0].Loop).LengthM, Is.EqualTo(5400).Within(1));

    //  Along the path the whole way there is nothing better to offer: none.
    [Test]
    public void A_route_with_no_road_to_save_has_none() =>
        Assert.That(Wended(OutAndBack, OutAndBack.LoopThrough("SACDBDCAS")), Is.Empty);

    //  A way round on paths 1 km off the road: less road, but far longer and off the run. None.
    private static readonly AsciiNetwork FarRound = AsciiNetwork.Parse("""
        S===========A===========B
        :                       :
        :                       :
        :                       :
        :                       :
        :                       :
        :                       :
        :                       :
        :                       :
        :                       :
        C.......................D
        """);

    [Test]
    public void A_variation_far_longer_and_off_the_route_isn_t_offered() =>
        Assert.That(Wended(FarRound, FarRound.LoopThrough("SABAS")), Is.Empty);
    //  The run turns at the end of a lane, E: a dead end. The variation goes there too, not trimmed short of it.
    private static readonly AsciiNetwork ToADeadEnd = AsciiNetwork.Parse("""
        S=======================A=======================B=====E
                                :                       :
                                :                       :
                                C.......................D
        """);

    [Test]
    public void It_still_turns_where_the_route_does_at_a_dead_end() =>
        Assert.That(GeneratingALoop.JunctionsAlong(ToADeadEnd.Slice, Wended(ToADeadEnd, ToADeadEnd.LoopThrough("SABEBAS"))[0].Loop).Select(ToADeadEnd.NameOf),
            Contains.Item('E'));
}
