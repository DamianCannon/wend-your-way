using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Loops;

/// <summary>
/// A Loop is a few hundred Paths over one slice; what it allocates must not grow with the slice
/// for each of them (T018: 5.5 GB for a 40 km Loop in London, most of it per-Path arrays).
/// </summary>
[TestFixture]
public class GeneratingALoopFrugally
{
    [Test]
    public void Then_a_Loop_allocates_a_few_slices_worth_not_one_per_Path()
    {
        var grid = GridNetwork.Create(junctionsAcross: 150);   // 22,500 junctions, 15 km across
        var request = new Request(new NodePoint(7500, 7500), DistanceKm: 10);
        LoopGenerator.Generate(grid, request, ScoringWeights.Default);   // compiled and warm

        var before = GC.GetAllocatedBytesForCurrentThread();
        var loop = LoopGenerator.Generate(grid, request, ScoringWeights.Default);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        //  The search's working memory is about 21 bytes a junction; a slice's worth for every
        //  Path would be hundreds of them.
        //  Cutting spurs out of every candidate (T042) adds about three, for copies of the
        //  candidates: it grows with the Loops, not the slice.
        var sliceWorth = 21.0 * grid.NodeCount;
        TestContext.Out.WriteLine($"{allocated / 1048576.0:0.0} MB allocated, {allocated / sliceWorth:0} slices' worth");
        Assert.That(loop, Is.Not.Null);
        Assert.That(allocated / sliceWorth, Is.LessThan(25), "slices' worth of working memory");
    }
}
