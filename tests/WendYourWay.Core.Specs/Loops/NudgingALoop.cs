using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Loops;

/// <summary>
/// Nudges (PLAN T075; SPEC R9.3): a Via puts a new stop on the Path it's dragged from, an Avoid
/// takes a Segment off its Path; only those Paths are found again, the rest of the Loop kept.
/// </summary>
public class NudgingALoop
{
    //  A grid of paths, 300 m blocks across and 200 m down. The Loop: from S to its Anchor B by
    //  A, to its Anchor H by E, and back to S by G, F and C: SAB, BEH, HGFCS.
    private static readonly AsciiNetwork Grid = AsciiNetwork.Parse("""
        S-----A-----B
        |     |     |
        |     |     |
        C-----D-----E
        |     |     |
        |     |     |
        F-----G-----H
        """);

    private static readonly Loop Original = new(Grid.Node('S'), [Grid.Node('B'), Grid.Node('H')], Grid.Walk("SABEHGFCS"));

    private static readonly ScoringWeights Weights = ScoringWeights.Default;

    private static string Junctions(Loop loop) =>
        new([.. GeneratingALoop.JunctionsAlong(Grid.Slice, loop).Select(Grid.NameOf)]);

    private static int TraversalOf(Loop loop, string ends) => loop.Traversals.ToList().FindIndex(t => t.Segment == Grid.Segment(ends));

    //  Dragged from the S-B Path (grabbed between A and B) to D: S to D, D to B (either way round
    //  the block, as both are as cheap), then B to H and H to S as before.
    [Test]
    public void A_Via_goes_through_the_place_dragged_to_and_keeps_the_other_Paths()
    {
        var nudged = Nudges.Via(Grid.Slice, Original, TraversalOf(Original, "A-B"), Grid.Node('D'), Weights)!;
        Assert.That(Junctions(nudged), Does.StartWith("S").And.Contain("D").And.EndWith("BEHGFCS"));
    }

    [Test]
    public void The_Via_is_a_stop_between_the_Path_s_two() =>
        Assert.That(Nudges.Via(Grid.Slice, Original, TraversalOf(Original, "A-B"), Grid.Node('D'), Weights)!.Anchors.Select(Grid.NameOf),
            Is.EqualTo(new[] { 'D', 'B', 'H' }));

    //  T126 (user: "I can't nudge onto it - instead the waypoint is added on a nearby path"):
    //  dragged onto a Segment's middle, the Loop goes along that whole Segment, in whichever
    //  direction costs less, the stop at the end given (the one nearer the drop).
    [Test]
    public void Through_a_Segment_the_Loop_goes_along_it_once()
    {
        var nudged = Nudges.Through(Grid.Slice, Original, TraversalOf(Original, "A-B"), Grid.Segment("C-D"), Grid.Node('D'), Weights)!;
        Assert.That(nudged.Traversals.Count(t => t.Segment == Grid.Segment("C-D")), Is.EqualTo(1));
    }

    //  From S, C-D is cheaper taken from C (S-C-D-A-B, 1,200 m) than from D (S-A-D-C-S... back
    //  round to B), and the Paths after B are kept.
    [Test]
    public void Through_a_Segment_it_is_taken_the_cheaper_way_round_and_the_other_Paths_kept() =>
        Assert.That(Junctions(Nudges.Through(Grid.Slice, Original, TraversalOf(Original, "A-B"), Grid.Segment("C-D"), Grid.Node('D'), Weights)!),
            Is.EqualTo("SCDABEHGFCS"));

    //  Grabbed on the way home (H to S), C-D is cheaper taken from D (H-…-D-C-S, 1,000 m) than
    //  from C (H-G-F-C-D and back to S, 1,500 m): against the way it's drawn, and joined up.
    [Test]
    public void Through_a_Segment_it_may_be_taken_against_the_way_it_is_drawn() =>
        Assert.That(Junctions(Nudges.Through(Grid.Slice, Original, TraversalOf(Original, "G-F"), Grid.Segment("C-D"), Grid.Node('D'), Weights)!),
            Does.StartWith("SABEH").And.EndWith("DCS"));

    [Test]
    public void Through_a_Segment_the_stop_is_at_the_end_given() =>
        Assert.That(Nudges.Through(Grid.Slice, Original, TraversalOf(Original, "A-B"), Grid.Segment("C-D"), Grid.Node('C'), Weights)!.Anchors.Select(Grid.NameOf),
            Is.EqualTo(new[] { 'C', 'B', 'H' }));

    //  A dead end, F, off D: along D-F and back is the only way, so it's gone along twice.
    private static readonly AsciiNetwork Spur = AsciiNetwork.Parse("""
        S-----A-----B
        |           |
        |           |
        C-----D-----E
              |
              |
              F
        """);

    [Test]
    public void Through_a_dead_end_the_Loop_goes_along_it_and_back()
    {
        var round = new Loop(Spur.Node('S'), [Spur.Node('B')], Spur.Walk("SABEDCS"));
        var grabbed = round.Traversals.ToList().FindIndex(t => t.Segment == Spur.Segment("A-B"));
        var nudged = Nudges.Through(Spur.Slice, round, grabbed, Spur.Segment("D-F"), Spur.Node('F'), Weights)!;
        Assert.That(nudged.Traversals.Count(t => t.Segment == Spur.Segment("D-F")), Is.EqualTo(2));
    }

    [Test]
    public void Through_a_Segment_already_on_the_stretch_the_Loop_is_as_it_was() =>
        Assert.That(Nudges.Through(Grid.Slice, Original, TraversalOf(Original, "A-B"), Grid.Segment("A-B"), Grid.Node('A'), Weights), Is.SameAs(Original));

    [Test]
    public void Through_a_Segment_avoided_there_is_no_way() =>
        Assert.That(Nudges.Through(Grid.Slice, Original, TraversalOf(Original, "A-B"), Grid.Segment("C-D"), Grid.Node('D'), Weights,
            avoided: new HashSet<int> { Grid.Segment("C-D") }), Is.Null);

    [Test]
    public void Through_a_Segment_with_a_stop_not_at_its_ends_is_refused() =>
        Assert.That(() => Nudges.Through(Grid.Slice, Original, TraversalOf(Original, "A-B"), Grid.Segment("C-D"), Grid.Node('H'), Weights),
            Throws.TypeOf<ArgumentOutOfRangeException>());

    //  A-B avoided: the S-B Path goes round (by D and E), the other two kept.
    [Test]
    public void An_Avoid_takes_the_Segment_off_its_Path_and_keeps_the_others()
    {
        var nudged = Nudges.Avoid(Grid.Slice, Original, Weights, avoided: new HashSet<int> { Grid.Segment("A-B") })!;
        Assert.That(nudged.Traversals.Select(t => t.Segment), Does.Not.Contain(Grid.Segment("A-B")));
        Assert.That(Junctions(nudged), Does.StartWith("S").And.EndWith("BEHGFCS"));
    }

    //  An Avoid stays avoided when the Loop is nudged again.
    [Test]
    public void A_later_Via_keeps_clear_of_what_was_avoided()
    {
        var avoided = new HashSet<int> { Grid.Segment("A-B") };
        var once = Nudges.Avoid(Grid.Slice, Original, Weights, avoided)!;
        var twice = Nudges.Via(Grid.Slice, once, 0, Grid.Node('A'), Weights, avoided)!;
        Assert.That(twice.Traversals.Select(t => t.Segment), Does.Not.Contain(Grid.Segment("A-B")));
    }

    //  The Path the Avoid is on found again, wherever in the Loop: here the last.
    [Test]
    public void An_Avoid_on_a_later_Path_keeps_the_ones_before()
    {
        var nudged = Nudges.Avoid(Grid.Slice, Original, Weights, avoided: new HashSet<int> { Grid.Segment("G-F") })!;
        Assert.That(Junctions(nudged), Does.StartWith("SABEH").And.Not.Contain("GF"));
    }

    //  No way round: none.
    [Test]
    public void With_no_way_round_there_s_no_Loop()
    {
        var line = AsciiNetwork.Parse("""
            S-----A-----B
            """);
        var there = new Loop(line.Node('S'), [line.Node('B')], line.Walk("SABAS"));
        Assert.That(Nudges.Avoid(line.Slice, there, Weights, new HashSet<int> { line.Segment("A-B") }), Is.Null);
    }

    //  A long Loop with no Anchors left (as some Destination Loops and trimmed Loops are): only
    //  the stretch round a Nudge changes, up to 2 km either side, so it keeps its far side and
    //  its length. Along the top 7.15 km, A to B 1.05 km with a way below it, E to F; the sides
    //  400 m; the bottom 7.15 km. Redoing its whole Path, start to start, once left nothing.
    private static readonly AsciiNetwork Ring = AsciiNetwork.Parse("""
        S------------------------------------------------------------A--------------------B------------------------------------------------------------D
        |                                                            |                    |                                                            |
        |                                                            E--------------------F                                                            |
        |                                                                                                                                              |
        H----------------------------------------------------------------------------------------------------------------------------------------------I
        """);

    private static readonly Loop RoundTheRing = new(Ring.Node('S'), [], Ring.Walk("SABDIHS"));

    private static double LengthOf(AsciiNetwork map, Loop loop) => loop.Traversals.Sum(t => map.Slice.Segments[t.Segment].LengthM);

    [Test]
    public void An_Avoid_on_a_Loop_with_no_Anchors_goes_round_and_keeps_the_far_side()
    {
        var nudged = Nudges.Avoid(Ring.Slice, RoundTheRing, Weights, new HashSet<int> { Ring.Segment("A-B") })!;
        Assert.That(new string([.. GeneratingALoop.JunctionsAlong(Ring.Slice, nudged).Select(Ring.NameOf)]), Is.EqualTo("SAEFBDIHS"));
        Assert.That(LengthOf(Ring, nudged), Is.EqualTo(LengthOf(Ring, RoundTheRing) + 400).Within(1e-6));
    }

    [Test]
    public void A_Via_on_a_Loop_with_no_Anchors_keeps_its_length_but_for_the_way_to_the_Via()
    {
        var nudged = Nudges.Via(Ring.Slice, RoundTheRing, 1, Ring.Node('E'), Weights)!;
        Assert.That(new string([.. GeneratingALoop.JunctionsAlong(Ring.Slice, nudged).Select(Ring.NameOf)]), Does.Contain("E").And.EndWith("DIHS"));
        Assert.That(LengthOf(Ring, nudged), Is.EqualTo(LengthOf(Ring, RoundTheRing) + 400).Within(1e-6));
    }

    //  T079: a stop removed, the stretch round it found again without it; here the Via just
    //  added, so the Loop is as it was.
    [Test]
    public void Removing_a_Via_puts_the_Loop_back()
    {
        var via = Nudges.Via(Grid.Slice, Original, TraversalOf(Original, "A-B"), Grid.Node('D'), Weights)!;
        var removed = Nudges.WithoutStop(Grid.Slice, via, 0, Weights)!;
        Assert.That((Junctions(removed), removed.Anchors.Select(Grid.NameOf)), Is.EqualTo(("SABEHGFCS", new[] { 'B', 'H' })).Using<(string, IEnumerable<char>)>(
            (a, b) => a.Item1 == b.Item1 && a.Item2.SequenceEqual(b.Item2)));
    }

    //  A Loop's only stop removed still leaves a Loop: the stretch round it, not start to start.
    [Test]
    public void Removing_the_only_stop_leaves_a_Loop()
    {
        var oneStop = new Loop(Ring.Node('S'), [Ring.Node('B')], Ring.Walk("SABDIHS"));
        var removed = Nudges.WithoutStop(Ring.Slice, oneStop, 0, Weights)!;
        Assert.That((removed.Anchors.Count, LengthOf(Ring, removed)), Is.EqualTo((0, LengthOf(Ring, oneStop))));
    }

    //  An Avoid removed: the stretch round where it was found again with it allowed back.
    [Test]
    public void Again_without_an_Avoid_the_Loop_may_use_it_again()
    {
        var avoided = new HashSet<int> { Grid.Segment("A-B") };
        var round = Nudges.Avoid(Grid.Slice, Original, Weights, avoided)!;
        var again = Nudges.Again(Grid.Slice, round, 0, Weights, avoided: new HashSet<int>())!;
        Assert.That(Junctions(again), Is.EqualTo("SABEHGFCS"));
    }

    //  Every Nudge's Loop is whole: each Segment starts where the last ended, back at the start.
    [Test]
    public void A_nudged_Loop_is_whole()
    {
        var nudged = Nudges.Via(Grid.Slice, Original, TraversalOf(Original, "E-H"), Grid.Node('C'), Weights)!;
        var junctions = GeneratingALoop.JunctionsAlong(Grid.Slice, nudged);
        Assert.That((junctions[0], junctions[^1]), Is.EqualTo((Grid.Node('S'), Grid.Node('S'))));
    }
}
