using WendYourWay.Core.Loops;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// The stars' cut points worked out again from the user's Outings as they are (PLAN T114; user chose
/// "Same rule, recomputed"): of the values sorted, the one at floor(q × n); a Run's ranking value at
/// the 10th, 30th, 50th and 70th percentiles, a ride's score at the 20th, 40th, 60th and 80th. An
/// Activity with fewer than 20 keeps the cuts it has.
/// </summary>
public class CalibratingStars
{
    private static double[] Hundred() => [.. Enumerable.Range(0, 100).Reverse().Select(i => (double)i)];

    [Test]
    public void A_Runs_cuts_are_its_10th_30th_50th_and_70th_percentiles() =>
        Assert.That(StarCuts.Calibrate(Hundred(), Hundred(), StarCuts.InCode).Run, Is.EqualTo(new double[] { 10, 30, 50, 70 }));

    [Test]
    public void A_rides_cuts_are_its_fifths() =>
        Assert.That(StarCuts.Calibrate(Hundred(), Hundred(), StarCuts.InCode).Ride, Is.EqualTo(new double[] { 20, 40, 60, 80 }));

    //  Twenty is enough: floor(0.1 × 20) is the third value.
    [Test]
    public void Twenty_are_enough() =>
        Assert.That(StarCuts.Calibrate([.. Enumerable.Range(0, 20).Select(i => (double)i)], [], StarCuts.InCode).Run, Is.EqualTo(new double[] { 2, 6, 10, 14 }));

    //  Of 25, the 10th percentile falls between the third value and the fourth (2.5): the third.
    [Test]
    public void Between_two_values_it_takes_the_one_below() =>
        Assert.That(StarCuts.Calibrate([.. Enumerable.Range(0, 25).Select(i => (double)i)], [], StarCuts.InCode).Run, Is.EqualTo(new double[] { 2, 7, 12, 17 }));

    //  Of 90, the 70th percentile is the value at 63 exactly, not 62 (0.7 × 90 in doubles is 62.99…).
    [Test]
    public void Ninety_are_cut_where_their_percentiles_fall_exactly() =>
        Assert.That(StarCuts.Calibrate([.. Enumerable.Range(0, 90).Select(i => (double)i)], [], StarCuts.InCode).Run, Is.EqualTo(new double[] { 9, 27, 45, 63 }));

    [Test]
    public void Fewer_than_twenty_Runs_keep_the_Runs_cuts_it_has()
    {
        var kept = new StarCuts([1, 2, 3, 4], [5, 6, 7, 8]);
        Assert.That(StarCuts.Calibrate([.. Enumerable.Range(0, 19).Select(i => (double)i)], Hundred(), kept), Is.EqualTo(new StarCuts([1, 2, 3, 4], [20, 40, 60, 80])).Using(new CutsAlike()));
    }

    [Test]
    public void Fewer_than_twenty_rides_keep_the_rides_cuts_it_has()
    {
        var kept = new StarCuts([1, 2, 3, 4], [5, 6, 7, 8]);
        Assert.That(StarCuts.Calibrate(Hundred(), [.. Enumerable.Range(0, 19).Select(i => (double)i)], kept), Is.EqualTo(new StarCuts([10, 30, 50, 70], [5, 6, 7, 8])).Using(new CutsAlike()));
    }

    //  The ranking value a Run is cut by: its score + 0.380 × its off-road share + 0.0132 × its climbing per km.
    [Test]
    public void A_Runs_ranking_value_is_its_score_with_its_shape_weighed_in() =>
        Assert.That(Stars.RankingOf(0.1, offRoadShare: 0.5, climbPerKm: 10), Is.EqualTo(0.1 + 0.19 + 0.132).Within(1e-12));

    [Test]
    public void A_Run_gets_its_stars_from_the_cuts_given() =>
        Assert.That(Stars.Of(Activity.Run, 0.35, 1, 0, 0, new StarCuts([0.1, 0.2, 0.3, 0.4], [9, 9, 9, 9])), Is.EqualTo(4));

    [Test]
    public void A_ride_gets_its_stars_from_the_cuts_given() =>
        Assert.That(Stars.Of(Activity.GravelRide, 0.35, 1, null, null, new StarCuts([9, 9, 9, 9], [0.1, 0.2, 0.3, 0.4])), Is.EqualTo(4));

    //  Cuts kept that aren't four rising numbers are ignored for the ones in code.
    [Test]
    public void Usable_kept_cuts_are_used() =>
        Assert.That(StarCuts.OrInCode([0.1, 0.2, 0.2, 0.4], [1, 2, 3, 4]), Is.EqualTo(new StarCuts([0.1, 0.2, 0.2, 0.4], [1, 2, 3, 4])).Using(new CutsAlike()));

    private static readonly object[] Unusable =
    [
        new object?[] { null },
        new object?[] { new double[] { 0.1, 0.2, 0.3 } },
        new object?[] { new double[] { 0.1, 0.2, 0.3, 0.4, 0.5 } },
        new object?[] { new double[] { 0.1, 0.3, 0.2, 0.4 } },
        new object?[] { new double[] { 0.1, 0.2, double.NaN, 0.4 } },
        new object?[] { new double[] { 0.1, 0.2, 0.3, double.PositiveInfinity } },
    ];

    [TestCaseSource(nameof(Unusable))]
    public void Unusable_Run_cuts_are_the_ones_in_code(double[]? run) =>
        Assert.That(StarCuts.OrInCode(run, [1, 2, 3, 4]).Run, Is.EqualTo(StarCuts.InCode.Run));

    [TestCaseSource(nameof(Unusable))]
    public void Unusable_ride_cuts_are_the_ones_in_code(double[]? ride) =>
        Assert.That(StarCuts.OrInCode([1, 2, 3, 4], ride).Ride, Is.EqualTo(StarCuts.InCode.Ride));

    [Test]
    public void The_cuts_in_code_are_T108s_and_T098s() =>
        Assert.That((StarCuts.InCode.Run, StarCuts.InCode.Ride),
            Is.EqualTo((new[] { 0.1910, 0.4008, 0.5575, 0.7172 }, new[] { 0.0059, 0.0198, 0.0457, 0.0845 })));

    private sealed class CutsAlike : IEqualityComparer<StarCuts>
    {
        public bool Equals(StarCuts? x, StarCuts? y) => x!.Run.SequenceEqual(y!.Run) && x.Ride.SequenceEqual(y.Ride);
        public int GetHashCode(StarCuts obj) => 0;
    }
}
