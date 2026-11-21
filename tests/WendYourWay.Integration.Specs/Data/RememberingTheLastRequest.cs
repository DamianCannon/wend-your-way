using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// The last Request's settings are kept in app.settings (PLAN T056, R1.1a): saved, loaded back, and a
/// start at home kept as home, with no position.
/// </summary>
[TestFixture]
[NonParallelizable]
public class RememberingTheLastRequest
{
    private static readonly LatLon KenwoodHouse = new(51.5712, -0.1677);

    private NpgsqlDataSource _dataSource = null!;
    private PostgisLastRequest _last = null!;

    [SetUp]
    public void Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        _last = new PostgisLastRequest(_dataSource);
    }

    [TearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public async Task Before_any_Request_there_is_nothing_to_restore() =>
        Assert.That(await _last.LoadAsync(), Is.Null);

    [Test]
    public async Task Saved_settings_load_back_as_they_were()
    {
        var settings = new RequestSettings(KenwoodHouse, DistanceKm: 12, HillsLean: 0.5);
        await _last.SaveAsync(settings);
        Assert.That(await _last.LoadAsync(), Is.EqualTo(settings));
    }

    [Test]
    public async Task Only_the_last_settings_are_kept()
    {
        await _last.SaveAsync(new RequestSettings(KenwoodHouse, 12, 0.5));
        await _last.SaveAsync(new RequestSettings(null, 8, -1));
        Assert.That(await _last.LoadAsync(), Is.EqualTo(new RequestSettings(null, 8, -1)));
    }

    //  T091: a Point-to-point's finish and its Distance, or none.
    [Test]
    public async Task A_finish_is_kept_too()
    {
        var settings = new RequestSettings(null, 10, 0) { Finish = new LatLon(51.4436, -0.2963), FinishDistanceKm = 12 };
        await _last.SaveAsync(settings);
        var loaded = (await _last.LoadAsync())!;
        Assert.That((loaded.Finish, loaded.FinishDistanceKm), Is.EqualTo(((LatLon?)new LatLon(51.4436, -0.2963), (double?)12)));
    }

    [Test]
    public async Task The_Off_road_Lean_is_kept_too()
    {
        await _last.SaveAsync(new RequestSettings(null, 12, 0.5, OffRoadLean: -0.5));
        Assert.That((await _last.LoadAsync())!.OffRoadLean, Is.EqualTo(-0.5));
    }

    //  T157 and T158: Explain? as last set, and the first-Loop tour answered, so it's offered once.
    [Test]
    public async Task Explain_and_the_tour_answered_are_kept_too()
    {
        await _last.SaveAsync(new RequestSettings(null, 12, 0.5) { Explain = true, TourOffered = true });
        var loaded = (await _last.LoadAsync())!;
        Assert.That((loaded.Explain, loaded.TourOffered), Is.EqualTo((true, true)));
    }

    //  Kept by T056, before there was an Off-road Lean.
    [Test]
    public async Task Settings_kept_before_the_Off_road_Lean_load_with_it_at_its_centre()
    {
        await using (var command = _dataSource.CreateCommand("""insert into app.settings (last_request) values ('{"start": null, "hillsLean": 0.5, "distanceKm": 12}')"""))
            await command.ExecuteNonQueryAsync();
        Assert.That(await _last.LoadAsync(), Is.EqualTo(new RequestSettings(null, 12, 0.5, 0)));
    }

    //  Home's position is never written anywhere but the user's secrets.
    [Test]
    public async Task A_start_at_home_is_saved_as_home_with_no_position()
    {
        await _last.SaveAsync(new RequestSettings(null, 12, 0));
        await using var query = _dataSource.CreateCommand("select last_request::text from app.settings");
        Assert.That((string)(await query.ExecuteScalarAsync())!, Does.Not.Contain("atitude").And.Not.Contain("ongitude"));
    }
}
