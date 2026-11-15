using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// A probe, not a check (T047): from start 8 of the benchmark, in the City, the Loop through
/// Tower Bridge and Westminster Bridge the user asked for, scored beside the generator's.
/// </summary>
[TestFixture]
[Explicit("A probe of start 8")]
[Category("RealNetwork")]
public class ProbingStart8
{
    [Test]
    public async Task Tower_Bridge_and_Westminster_Bridge()
    {
        await using var dataSource = await RealNetwork.OpenOrIgnore(RealNetwork.London);
        var start = await RealNetwork.BritishNationalGrid(dataSource, 51.5136, -0.0794);
        var tower = await RealNetwork.BritishNationalGrid(dataSource, 51.5055, -0.0754);
        var westminster = await RealNetwork.BritishNationalGrid(dataSource, 51.5008, -0.1218);
        var request = new Request(start, 10.14);
        var slice = await new PostgisNetworkSlices(dataSource).AroundAsync(request.Start, request.SliceRadiusM);
        int Nearest(NodePoint p) => Enumerable.Range(0, slice.NodeCount).Where(n => slice.SegmentsAt(n).Length > 0).MinBy(n => slice.Node(n).DistanceTo(p));

        var generated = LoopGenerator.Generate(slice, request, ScoringWeights.Default)!;
        await Report("generator's", generated);
        foreach (var (name, stops) in new[] { ("Tower then Westminster", new[] { Nearest(tower), Nearest(westminster) }), ("Westminster then Tower", new[] { Nearest(westminster), Nearest(tower) }) })
        {
            var search = new PathSearch(slice);
            var used = new HashSet<int>();
            var traversals = new List<Traversal>();
            int[] all = [Nearest(start), .. stops, Nearest(start)];
            for (var i = 0; i + 1 < all.Length; i++)
            {
                var path = search.Find(PathEnd.AtNode(all[i]), PathEnd.AtNode(all[i + 1]), new RunCostModel(ScoringWeights.Default, used))!;
                traversals.AddRange(path.Traversals);
                foreach (var t in path.Traversals) slice.Segments[t.Segment].Cover(used);
            }
            await Report(name, new Loop(all[0], stops, traversals));
            foreach (var c in traversals.Select(t => slice.Segments[t.Segment]).Where(x => x.Crossing is not null && x.CrossesMainRoad).Select(x => (x.Key.OsmWayId, x.Crossing)).Distinct())
                await TestContext.Out.WriteLineAsync($"  {name}: Main Road crossing way {c.OsmWayId}, {c.Crossing}");
        }

        async Task Report(string name, Loop loop)
        {
            var b = Breakdown.Of(slice, loop);
            await TestContext.Out.WriteLineAsync($"{name}: {b.LengthM / 1000:0.0} km, score {LoopScorer.Score(slice, loop, ScoringWeights.Default):0.000}, knots {Knots.Of(slice, loop)}, " +
                $"Connector {b.ConnectorLengthM / 1000:0.00} km, Waterside {b.WatersidePercent:0}%, Flow {b.FlowM / 1000:0.0} km, Crossings {b.Crossings} ({b.MainRoadCrossings} of Main Roads)");
        }
    }
}
