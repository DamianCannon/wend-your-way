using WendYourWay.Data;
using WendYourWay.Data.Outings;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Outings;

/// <summary>
/// The user's own stars for an Outing (PLAN T097, STORY-045 AC1): one to five, kept with it, and
/// listed beside the app's. On Fixtures/same-ground.osm with the invented Run of
/// Fixtures/pavement-and-park.gpx.
/// </summary>
[TestFixture]
[NonParallelizable]
public class RatingOutings
{
    private NpgsqlDataSource _dataSource = null!;
    private PostgisOutingViews _outings = null!;
    private int _id;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        await OutingImporter.ImportGpxAsync(_dataSource, Fixtures.Path("pavement-and-park.gpx"));
        _outings = new PostgisOutingViews(_dataSource);
        _id = (await _outings.ListAsync()).Single().Id;
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [SetUp]
    public async Task Unrated() => await _outings.RateAsync(_id, null);

    private async Task<int?> Listed() => (await _outings.ListAsync()).Single().UserStars;

    [Test]
    public async Task Not_rated_it_has_none_of_the_user_s_stars() =>
        Assert.That(await Listed(), Is.Null);

    [Test]
    public async Task Its_stars_are_kept_and_listed()
    {
        await _outings.RateAsync(_id, 4);
        Assert.That(await Listed(), Is.EqualTo(4));
    }

    [Test]
    public async Task Rated_again_it_keeps_the_new_stars()
    {
        await _outings.RateAsync(_id, 4);
        await _outings.RateAsync(_id, 2);
        Assert.That(await Listed(), Is.EqualTo(2));
    }

    [Test]
    public async Task Its_stars_can_be_taken_away()
    {
        await _outings.RateAsync(_id, 4);
        await _outings.RateAsync(_id, null);
        Assert.That(await Listed(), Is.Null);
    }

    [TestCase(0)]
    [TestCase(6)]
    public void Stars_are_one_to_five(int stars) =>
        Assert.That(async () => await _outings.RateAsync(_id, stars), Throws.TypeOf<ArgumentOutOfRangeException>());

    //  The user's data outlives the map: a rebuild leaves their stars as they were.
    [Test]
    public async Task Its_stars_outlive_a_rebuild()
    {
        await _outings.RateAsync(_id, 5);
        await NetworkBuild.Build(_dataSource);
        Assert.That(await Listed(), Is.EqualTo(5));
    }
}
