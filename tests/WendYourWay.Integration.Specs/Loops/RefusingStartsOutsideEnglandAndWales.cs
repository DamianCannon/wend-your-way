using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// STORY-009 AC3, R1.2 (PLAN T030): a Request starting outside England and Wales is refused with
/// a message saying so, and no Loop is generated. On Fixtures/border.osm.
/// </summary>
[TestFixture]
[NonParallelizable]
public class RefusingStartsOutsideEnglandAndWales
{
    private static readonly LatLon InScotland = new(55.38, -2.50);
    private static readonly LatLon InEngland = new(55.32, -2.50);

    private NpgsqlDataSource _dataSource = null!;
    private LoopPlanner _planner = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("border.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        _planner = new LoopPlanner(new PostgisNetworkSlices(_dataSource), new MapGeometry(_dataSource), ScoringWeights.Default);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public void Then_a_start_in_Scotland_is_refused_with_a_message_saying_so() =>
        Assert.That(async () => await _planner.PlanAsync(InScotland, distanceKm: 5),
            Throws.TypeOf<OutsideEnglandAndWalesException>().With.Message.Contains("outside England and Wales"));

    //  The fixture is too small for a Loop anywhere: what matters here is that England isn't refused.
    [Test]
    public void Then_a_start_in_England_isn_t_refused() =>
        Assert.That(async () => await _planner.PlanAsync(InEngland, distanceKm: 5), Throws.Nothing);
}
