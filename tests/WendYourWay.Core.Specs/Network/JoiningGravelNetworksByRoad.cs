using WendYourWay.Core.Network;

namespace WendYourWay.Core.Specs.Network;

/// <summary>
/// Gravel Off-road networks joined through road links (PLAN T074): two networks are one where a
/// route along the roads given joins a junction of one to a junction of the other within the link,
/// however many Segments it takes. Junctions are OSM node ids; a network is its number.
/// </summary>
public class JoiningGravelNetworksByRoad
{
    // Networks 0 and 1, at junctions 1 and 100; roads between them as given.
    private static int[] Join(double linkM, params (long A, long B, double M)[] roads) =>
        RoadLinking.Join(networks: 2, new Dictionary<long, int> { [1] = 0, [100] = 1 }, roads, linkM);

    [Test]
    public void A_route_of_several_Segments_within_the_link_joins_them() =>
        Assert.That(Join(1000, (1, 2, 300), (2, 3, 300), (3, 100, 300)), Is.EqualTo(new[] { 0, 0 }));

    [Test]
    public void One_longer_than_the_link_doesn_t() =>
        Assert.That(Join(1000, (1, 2, 400), (2, 3, 400), (3, 100, 400)), Is.EqualTo(new[] { 0, 1 }));

    //  The two searches meet halfway along a road, and the whole route counts.
    [Test]
    public void Reaching_out_from_both_ends_counts_the_whole_route() =>
        Assert.That(Join(1000, (1, 2, 450), (2, 3, 200), (3, 100, 450)), Is.EqualTo(new[] { 0, 1 }));

    //  The shortest way between them counts, not the first found.
    [Test]
    public void The_shortest_route_counts() =>
        Assert.That(Join(1000, (1, 2, 900), (2, 100, 900), (1, 3, 400), (3, 4, 400), (4, 100, 100)), Is.EqualTo(new[] { 0, 0 }));

    //  Joined in a chain: 0 to 2 by way of 1.
    [Test]
    public void Networks_join_in_a_chain() =>
        Assert.That(
            RoadLinking.Join(networks: 3, new Dictionary<long, int> { [1] = 0, [100] = 1, [200] = 2 }, [(1, 100, 500), (100, 200, 500)], 1000),
            Is.EqualTo(new[] { 0, 0, 0 }));

    [Test]
    public void No_roads_join_nothing() =>
        Assert.That(Join(1000), Is.EqualTo(new[] { 0, 1 }));
}
