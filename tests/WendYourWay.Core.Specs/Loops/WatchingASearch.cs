using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Loops;

/// <summary>
/// A search says how far it has got as it goes (PLAN T054): each Loop tried, of how many it will
/// try, and the Loops found so far, from which the Loops offered can be chosen before it ends.
/// </summary>
public class WatchingASearch
{
    //  2 km square of paths, a junction every 100 m; a 4 km Run from near the middle.
    private static readonly NetworkSlice Grid = GridNetwork.Create(junctionsAcross: 21);
    private static readonly Request FourKm = new(new NodePoint(1030, 970), DistanceKm: 4);

    private static List<SearchProgress> Watch(out FoundLoops found)
    {
        var seen = new List<SearchProgress>();
        found = LoopGenerator.Find(Grid, FourKm, ScoringWeights.Default, seen.Add);
        return seen;
    }

    [Test]
    public void Each_Loop_tried_is_counted_one_at_a_time()
    {
        var seen = Watch(out _);
        Assert.That(seen.Select(p => p.Tried), Is.EqualTo(Enumerable.Range(1, seen.Count)));
    }

    [Test]
    public void It_ends_having_tried_all_it_said_it_would() =>
        Assert.That(Watch(out _)[^1] is var last && last.Tried == last.ToTry, Is.True);

    [Test]
    public void It_never_says_it_has_tried_more_than_it_will() =>
        Assert.That(Watch(out _).Select(p => p.ToTry - p.Tried), Is.All.GreaterThanOrEqualTo(0));

    [Test]
    public void Each_Loop_is_reported_as_it_s_tried() =>
        Assert.That(Watch(out _).Count(p => p.Latest is not null), Is.GreaterThan(100));

    [Test]
    public void The_Loops_found_so_far_grow_as_it_goes_to_nearly_all_it_finds()
    {
        var counts = Watch(out var found).Select(p => p.SoFar.Count).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(counts, Is.Ordered.Ascending);
            // All but those with their spurs cut out, which come after the last Loop tried.
            Assert.That(counts[^1], Is.GreaterThan(found.Count / 2));
        });
    }

    [Test]
    public void Watched_it_finds_what_it_finds_unwatched()
    {
        Watch(out var found);
        Assert.That(found.Offer(Leans.Centre).Select(l => l.Traversals),
            Is.EqualTo(LoopGenerator.Find(Grid, FourKm, ScoringWeights.Default).Offer(Leans.Centre).Select(l => l.Traversals)));
    }
}
