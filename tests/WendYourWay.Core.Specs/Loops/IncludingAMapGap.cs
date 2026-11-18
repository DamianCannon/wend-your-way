using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Loops;

/// <summary>
/// Include this time (#150, T146c): a possible Map gap, 5 m to 15 m, is forbidden to every search; the
/// user may include one on a Loop, which a Nudge through it may then use, as may every Nudge after.
/// </summary>
public class IncludingAMapGap
{
    //  The Nudge specs' grid (NudgingALoop), C-D a possible gap; the Loop SAB, BEH, HGFCS.
    private static readonly AsciiNetwork Grid = AsciiNetwork.Parse("""
        S-----A-----B
        |     |     |
        |     |     |
        C-----D-----E
        |     |     |
        |     |     |
        F-----G-----H
        """);

    private static readonly NetworkSlice Gapped = new(
        [.. Enumerable.Range(0, Grid.Slice.NodeCount).Select(Grid.Slice.Node)],
        [.. Grid.Slice.Segments.Select((s, i) => i == Grid.Segment("C-D") ? s with { Access = AccessKind.Forbidden, MapGap = MapGapKind.Possible } : s)]);

    private static readonly Loop Original = new(Grid.Node('S'), [Grid.Node('B'), Grid.Node('H')], Grid.Walk("SABEHGFCS"));

    private static readonly HashSet<int> Included = [Grid.Segment("C-D")];

    private static int TraversalOf(Loop loop, string ends) => loop.Traversals.ToList().FindIndex(t => t.Segment == Grid.Segment(ends));

    [Test]
    public void Not_included_a_possible_gap_can_t_be_gone_through() =>
        Assert.That(Nudges.Through(Gapped, Original, TraversalOf(Original, "A-B"), Grid.Segment("C-D"), Grid.Node('D'), ScoringWeights.Default), Is.Null);

    [Test]
    public void Included_the_Loop_goes_through_it_once() =>
        Assert.That(Nudges.Through(Gapped, Original, TraversalOf(Original, "A-B"), Grid.Segment("C-D"), Grid.Node('D'), ScoringWeights.Default, included: Included)!
            .Traversals.Count(t => t.Segment == Grid.Segment("C-D")), Is.EqualTo(1));

    //  Dragged to D: S to D by C and the gap would be as cheap as by A; the gap not included, never by it.
    [Test]
    public void Not_included_a_Via_never_takes_it() =>
        Assert.That(Nudges.Via(Gapped, Original, TraversalOf(Original, "A-B"), Grid.Node('D'), ScoringWeights.Default)!
            .Traversals.Any(t => t.Segment == Grid.Segment("C-D")), Is.False);
}
