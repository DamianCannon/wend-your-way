using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Routing;

/// <summary>
/// A Loop takes a few hundred Paths over the same slice. Each used to allocate arrays the size of
/// the slice and an enumerator per Segment it looked at: 5.5 GB for a 40 km Loop in London
/// (T018). A <see cref="PathSearch"/> keeps its working memory from one Path to the next.
/// </summary>
[TestFixture]
public class PathFinderMemory
{
    [Test]
    public void Then_finding_another_Path_with_the_same_search_allocates_almost_nothing()
    {
        var slice = GridNetwork.Create(junctionsAcross: 200);   // 40,000 junctions
        var used = new HashSet<int> { 5, 50, 500 };
        var cost = new RunCostModel(ScoringWeights.Default, used);
        var search = new PathSearch(slice);
        search.Find(PathEnd.AtNode(0), PathEnd.AtNode(slice.NodeCount - 1), cost);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var path = search.Find(PathEnd.AtNode(199), PathEnd.AtNode(slice.NodeCount - 200), cost);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        //  The Path itself is a few hundred Traversals; anything the size of the slice is far more.
        Assert.That(path, Is.Not.Null);
        Assert.That(allocated, Is.LessThan(64 * 1024), "bytes allocated by the second Path");
    }
}
