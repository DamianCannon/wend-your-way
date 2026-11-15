using WendYourWay.Core.Network;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Network;

/// <summary>
/// Off-road networks (CONTEXT.md): green ground joined at its junctions and across a single road
/// Segment of up to 100 m. Which network a junction is in, so a Loop can be built round one.
/// </summary>
public class FindingOffRoadNetworks
{
    //  Two paths 500 m long, A to B and C to D, joined by a 100 m crossing B=C; a 300 m road D to E.
    private static readonly AsciiNetwork Map = AsciiNetwork.Parse("""
        A.........B=C.........D======E
        """);

    private static readonly OffRoadNetworks Networks = OffRoadNetworks.Of(Map.Slice);

    [Test]
    public void Junctions_joined_by_paths_and_a_crossing_are_in_one_network() =>
        Assert.That(new[] { 'A', 'B', 'C', 'D' }.Select(j => Networks.NetworkAt(Map.Node(j))).Distinct().Count(), Is.EqualTo(1));

    [Test]
    public void A_junction_only_roads_meet_is_in_none() =>
        Assert.That(Networks.NetworkAt(Map.Node('E')), Is.EqualTo(-1));

    [Test]
    public void A_network_measures_its_green_ground() =>
        Assert.That(Networks.MetresOf(Networks.NetworkAt(Map.Node('A'))), Is.EqualTo(1000).Within(1e-9));
}
