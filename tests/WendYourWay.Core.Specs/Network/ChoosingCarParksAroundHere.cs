using WendYourWay.Core.Network;

namespace WendYourWay.Core.Specs.Network;

/// <summary>
/// Car parks around here (PLAN T060): of the Trailheads in view, one per place, the one nearest
/// the middle of the map (an unnamed car park its network's own place) ("the first five entries are all in Trent Park when really
/// we just want one entry in the list", user, 2026-11-02); ranked by green ground up to three
/// times the Distance, then by nearness to the middle; the best ten. No drive, so no home.
/// </summary>
public class ChoosingCarParksAroundHere
{
    private static NearTrailhead At(int id, int network, double networkKm, double fromMiddleKm, string? place = null) =>
        new(id, network, networkKm * 1000, fromMiddleKm * 1000, place);

    private static int[] Best(double distanceKm, params NearTrailhead[] trailheads) =>
        [.. Trailheads.Around(trailheads, distanceKm).Select(t => t.Id)];

    [Test]
    public void Of_a_places_car_parks_the_one_nearest_the_middle_is_offered() =>
        Assert.That(Best(10, At(1, 7, 50, 4, "Trent Park"), At(2, 7, 50, 1, "Trent Park")), Is.EqualTo(new[] { 2 }));

    [Test]
    public void Every_place_of_a_network_is_offered_the_nearest_the_middle_first() =>
        Assert.That(Best(10, At(1, 7, 50, 4, "Trent Park"), At(2, 7, 50, 1, "Ash Wood")), Is.EqualTo(new[] { 2, 1 }));

    //  Putney Heath is beside both Richmond Park's network and Wimbledon Common's.
    [Test]
    public void A_place_beside_two_networks_is_offered_once() =>
        Assert.That(Best(10, At(1, 7, 50, 4, "Putney Heath"), At(2, 8, 40, 1, "Putney Heath")), Is.EqualTo(new[] { 2 }));

    [Test]
    public void Unnamed_car_parks_on_different_networks_are_both_offered() =>
        Assert.That(Best(10, At(1, 7, 50, 4), At(2, 8, 50, 1)), Is.EqualTo(new[] { 2, 1 }));

    [Test]
    public void Of_a_networks_unnamed_car_parks_one_is_offered() =>
        Assert.That(Best(10, At(1, 7, 50, 4), At(2, 7, 50, 1)), Is.EqualTo(new[] { 2 }));

    [Test]
    public void More_green_ground_comes_first() =>
        Assert.That(Best(10, At(1, 1, 12, 1), At(2, 2, 25, 5)), Is.EqualTo(new[] { 2, 1 }));

    [Test]
    public void Beyond_three_times_the_Distance_the_nearer_the_middle_comes_first() =>
        Assert.That(Best(10, At(1, 1, 300, 6), At(2, 2, 30, 2)), Is.EqualTo(new[] { 2, 1 }));

    [Test]
    public void Ten_are_offered_at_most() =>
        Assert.That(Best(10, [.. Enumerable.Range(1, 12).Select(i => At(i, i, 40, i))]), Has.Length.EqualTo(10));
}
