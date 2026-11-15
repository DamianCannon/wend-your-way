using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Loops;

/// <summary>
/// Moving the Hills slider re-ranks the Loops a Request found, with no new search (PLAN T053,
/// R10.4, ADR 0007): they're scored under the new Lean and chosen again by the whole rule.
/// </summary>
public class ReRankingTheLoopsFound
{
    //  2 km square of paths, a junction every 100 m; a 4 km Run from near the middle.
    private static readonly NetworkSlice Grid = GridNetwork.Create(junctionsAcross: 21);
    private static readonly Request FourKm = new(new NodePoint(1030, 970), DistanceKm: 4);

    [Test]
    public void At_the_Lean_they_were_found_with_the_Loops_offered_are_the_Requests() =>
        Assert.That(LoopGenerator.Find(Grid, FourKm, ScoringWeights.Default).Offer(FourKm.Leans).Select(l => l.Traversals),
            Is.EqualTo(LoopGenerator.Offer(Grid, FourKm, ScoringWeights.Default).Select(l => l.Traversals)));

    //  The western half of the grid is hilly, each path climbing 3 m and dropping 3 m, the eastern
    //  half flat, and the start on the line between them (T050): found at the centre, where
    //  either half will do, then leant one way or the other.
    [TestCase(-1.0, false, TestName = "Re_ranked_to_fewer_hills_Loop_1_keeps_to_the_flat")]
    [TestCase(1.0, true, TestName = "Re_ranked_to_more_hills_Loop_1_heads_for_the_hills")]
    public void Re_ranked_Loop_1_heads_for_the_ground_leant_to(double lean, bool west)
    {
        var grid = GridNetwork.Create(junctionsAcross: 21, climbing: m => m.X < 1000 ? 3 : 0);
        var loop = LoopGenerator.Find(grid, new Request(new NodePoint(1000, 970), DistanceKm: 4), ScoringWeights.Default).Offer(new Leans(Hills: lean))[0];
        var junctions = GeneratingALoop.JunctionsAlong(grid, loop);
        Assert.That(junctions.Count(n => grid.Node(n).X < 1000) > junctions.Count / 2, Is.EqualTo(west));
    }

    [Test]
    public void Re_ranked_the_Loops_offered_come_best_first_under_the_new_Lean()
    {
        var grid = GridNetwork.Create(junctionsAcross: 21, climbing: m => m.X < 1000 ? 3 : 0);
        var offered = LoopGenerator.Find(grid, new Request(new NodePoint(1000, 970), DistanceKm: 4), ScoringWeights.Default).Offer(new Leans(Hills: 1));
        Assert.That(offered.Select(l => LoopScorer.Score(grid, l, ScoringWeights.Default with { HillsLean = 1 })), Is.Ordered.Descending);
    }

    //  A background search at the new Lean adds its Loops to those kept (T053b): found leaning to
    //  fewer hills, then to more, the Loops kept offer the hills.
    [Test]
    public void With_the_Loops_found_at_another_Lean_the_best_of_both_is_offered()
    {
        var grid = GridNetwork.Create(junctionsAcross: 21, climbing: m => m.X < 1000 ? 3 : 0);
        var start = new NodePoint(1000, 970);
        var flat = LoopGenerator.Find(grid, new Request(start, DistanceKm: 4, HillsLean: -1), ScoringWeights.Default);
        var hilly = LoopGenerator.Find(grid, new Request(start, DistanceKm: 4, HillsLean: 1), ScoringWeights.Default);
        var both = flat.With(hilly);
        Assert.Multiple(() =>
        {
            Assert.That(both.Count, Is.EqualTo(flat.Count + hilly.Count));
            Assert.That(both.Offer(new Leans(Hills: 1))[0].Traversals, Is.EqualTo(hilly.Offer(new Leans(Hills: 1))[0].Traversals));
            Assert.That(both.Offer(new Leans(Hills: -1))[0].Traversals, Is.EqualTo(flat.Offer(new Leans(Hills: -1))[0].Traversals));
        });
    }

    [Test]
    public void Only_Loops_found_on_the_same_slice_can_be_kept_together()
    {
        var found = LoopGenerator.Find(Grid, FourKm, ScoringWeights.Default);
        var elsewhere = LoopGenerator.Find(GridNetwork.Create(junctionsAcross: 21), FourKm, ScoringWeights.Default);
        Assert.That(() => found.With(elsewhere), Throws.ArgumentException);
    }

    [Test]
    public void With_the_Loops_found_at_another_Lean_Loop_1_scores_at_least_as_well_as_either_sides()
    {
        var grid = GridNetwork.Create(junctionsAcross: 21, climbing: m => m.X < 1000 ? 3 : 0);
        var start = new NodePoint(1030, 970);
        var centre = LoopGenerator.Find(grid, new Request(start, DistanceKm: 4), ScoringWeights.Default);
        var hilly = LoopGenerator.Find(grid, new Request(start, DistanceKm: 4, HillsLean: 0.5), ScoringWeights.Default);
        var leaning = ScoringWeights.Default with { HillsLean = 0.5 };
        double First(FoundLoops found) => LoopScorer.Score(grid, found.Offer(new Leans(Hills: 0.5))[0], leaning);
        Assert.That(First(centre.With(hilly)), Is.GreaterThanOrEqualTo(Math.Max(First(centre), First(hilly))));
    }
}
