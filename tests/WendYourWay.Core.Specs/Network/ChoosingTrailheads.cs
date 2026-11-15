using WendYourWay.Core.Network;

namespace WendYourWay.Core.Specs.Network;

/// <summary>
/// Trailheads (PLAN T023): car parks beside a large Off-road network, none within 250 m of another
/// on the same network (1 km till "include them all", user, 2026-11-02), the named and then the larger kept first.
/// </summary>
public class ChoosingTrailheads
{
    private static TrailheadCandidate Park(long id, double x, string? name = null, double areaM2 = 0, int network = 1) =>
        new(id, new NodePoint(x, 0), name, areaM2, network, 20_000);

    private static long[] Kept(params TrailheadCandidate[] candidates) => [.. Trailheads.Thin(candidates).Select(t => t.OsmId).Order()];

    [Test]
    public void Of_two_car_parks_close_together_on_one_network_the_named_one_is_kept() =>
        Assert.That(Kept(Park(1, 0, areaM2: 5000), Park(2, 200, name: "Sheen Gate")), Is.EqualTo(new[] { 2L }));

    [Test]
    public void Of_two_unnamed_ones_the_larger_is_kept() =>
        Assert.That(Kept(Park(1, 0, areaM2: 800), Park(2, 200, areaM2: 3000)), Is.EqualTo(new[] { 2L }));

    [Test]
    public void Car_parks_250_m_or_more_apart_are_both_kept() =>
        Assert.That(Kept(Park(1, 0), Park(2, 250)), Is.EqualTo(new[] { 1L, 2L }));

    [Test]
    public void Car_parks_close_together_on_different_networks_are_both_kept() =>
        Assert.That(Kept(Park(1, 0, network: 1), Park(2, 50, network: 2)), Is.EqualTo(new[] { 1L, 2L }));

    //  A chain: the middle one is dropped by the first, so the third is kept though it's near the middle.
    [Test]
    public void Only_the_ones_kept_crowd_out_others() =>
        Assert.That(Kept(Park(1, 0, name: "A"), Park(2, 200), Park(3, 400, name: "C")), Is.EqualTo(new[] { 1L, 3L }));
}
