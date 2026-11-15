using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Saved Routes (PLAN T080, STORY-015, R5.1) in app.saved_routes: saved with a name, listed newest
/// first, opened as they were, renamed and deleted.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SavingRoutes
{
    private static readonly LatLon KenwoodHouse = new(51.5712, -0.1677);

    private static readonly PlannedLoop Loop = new(
        [KenwoodHouse, new LatLon(51.5600, -0.1600), KenwoodHouse], KenwoodHouse, [new LatLon(51.5600, -0.1600)],
        new Breakdown(9_400, 60, 30, 900, 120, Obstacles: [new ObstacleCount(ObstacleKind.Gate, 2)]),
        [new ProfileSample(0, 120, KenwoodHouse), new ProfileSample(9_400, 120, KenwoodHouse)])
    {
        Ways = [0, 11, 12],
        Avoids = [new LatLon(51.5650, -0.1650)],
    };

    private static readonly (SegmentKey Key, bool Forward)[] Traversals = [(new SegmentKey(11, 1, 2), true), (new SegmentKey(12, 2, 1), false)];

    private NpgsqlDataSource _dataSource = null!;
    private PostgisSavedRoutes _saved = null!;

    [SetUp]
    public void Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        _saved = new PostgisSavedRoutes(_dataSource);
    }

    [TearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public async Task None_are_saved_at_first() =>
        Assert.That(await _saved.ListAsync(), Is.Empty);

    //  Opened, it's the Loop as saved: its line, stops, Avoids, Breakdown, profile and ways.
    [Test]
    public async Task A_route_opens_as_it_was_saved()
    {
        var id = await _saved.SaveAsync("Heath wiggle", Loop, Traversals);
        var opened = (await _saved.OpenAsync(id))!;
        Assert.That(opened.Name, Is.EqualTo("Heath wiggle"));
        Assert.That((opened.Loop.Line, opened.Loop.Anchors, opened.Loop.Avoids, opened.Loop.Ways), Is.EqualTo((Loop.Line, Loop.Anchors, Loop.Avoids, Loop.Ways)));
        Assert.That((opened.Loop.Breakdown.LengthM, opened.Loop.Breakdown.Obstacles, opened.Loop.Profile.Count), Is.EqualTo((9_400.0, Loop.Breakdown.Obstacles, 2)));
    }

    //  T097 (STORY-045 AC1): the user's own stars for a Saved Route, kept with it and listed.
    [Test]
    public async Task Its_stars_are_kept_and_listed()
    {
        var id = await _saved.SaveAsync("Heath wiggle", Loop, Traversals);
        await _saved.RateAsync(id, 3);
        Assert.That((await _saved.ListAsync()).Single().UserStars, Is.EqualTo(3));
    }

    [Test]
    public async Task Its_stars_can_be_taken_away()
    {
        var id = await _saved.SaveAsync("Heath wiggle", Loop, Traversals);
        await _saved.RateAsync(id, 3);
        await _saved.RateAsync(id, null);
        Assert.That((await _saved.ListAsync()).Single().UserStars, Is.Null);
    }

    [Test]
    public async Task A_Saved_Route_s_stars_are_one_to_five()
    {
        var id = await _saved.SaveAsync("Heath wiggle", Loop, Traversals);
        Assert.That(async () => await _saved.RateAsync(id, 6), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    //  T097 (STORY-045 AC2): the app's stars for a Saved Route, from the score kept when it was saved.
    [Test]
    public async Task Its_app_stars_come_from_the_score_it_was_saved_with()
    {
        // A Run's (T108): 0.3 + 0.380 x 0.6 off-road + 0.0132 x 14 m a km = 0.713, 4 stars.
        await _saved.SaveAsync("Heath wiggle", Loop, Traversals, new RouteScore(Activity.Run, 0.3, 1, OffRoadShare: 0.6, ClimbPerKm: 14));
        Assert.That((await _saved.ListAsync()).Single().Stars, Is.EqualTo(4));
    }

    //  T114: by the cuts as last calibrated, kept with the user's data: 0.713 is 2 stars cut at 0.7 and up.
    [Test]
    public async Task Its_app_stars_are_cut_as_last_calibrated()
    {
        await using (var keep = _dataSource.CreateCommand("""
            insert into app.settings (star_cuts) values ('{"run":[0.7,0.8,0.9,1.0],"ride":[1,2,3,4]}')
            on conflict (id) do update set star_cuts = excluded.star_cuts
            """))
            await keep.ExecuteNonQueryAsync();
        await _saved.SaveAsync("Heath wiggle", Loop, Traversals, new RouteScore(Activity.Run, 0.3, 1, OffRoadShare: 0.6, ClimbPerKm: 14));
        Assert.That((await _saved.ListAsync()).Single().Stars, Is.EqualTo(2));
    }

    //  T104: its share off-road and climbing per km kept with its score.
    [Test]
    public async Task Its_share_off_road_and_climbing_are_kept_with_its_score()
    {
        var id = await _saved.SaveAsync("Heath wiggle", Loop, Traversals, new RouteScore(Activity.Run, 0.3, 1, OffRoadShare: 0.6, ClimbPerKm: 14));
        await using var query = _dataSource.CreateCommand($"select off_road_share, climb_per_km from app.saved_routes where id = {id}");
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        Assert.That((reader.GetFloat(0), reader.GetFloat(1)), Is.EqualTo((0.6f, 14f)));
    }

    [Test]
    public async Task A_ride_s_app_stars_are_a_Gravel_Ride_s()
    {
        await _saved.SaveAsync("Heath ride", Loop, Traversals, new RouteScore(Activity.GravelRide, 0.3, 1));
        Assert.That((await _saved.ListAsync()).Single().Stars, Is.EqualTo(5));
    }

    [Test]
    public async Task Saved_with_no_score_it_has_no_app_stars_and_says_it_wasnt_scored()
    {
        await _saved.SaveAsync("Heath wiggle", Loop, Traversals);
        var listed = (await _saved.ListAsync()).Single();
        Assert.That((listed.Stars, listed.Scored), Is.EqualTo(((int?)null, false)));
    }

    [Test]
    public async Task Saved_with_a_score_it_says_it_was_scored()
    {
        await _saved.SaveAsync("Friend's", Loop, Traversals, new RouteScore(Activity.Run, 0.3, 0.7));
        Assert.That((await _saved.ListAsync()).Single().Scored, Is.True);
    }

    [Test]
    public async Task One_mostly_off_the_map_has_no_app_stars()
    {
        await _saved.SaveAsync("Friend's", Loop, Traversals, new RouteScore(Activity.Run, 0.3, 0.7));
        Assert.That((await _saved.ListAsync()).Single().Stars, Is.Null);
    }

    //  T096: offered in another direction is of the Request that offered it, not of the route kept.
    [Test]
    public async Task A_route_offered_in_another_direction_opens_as_any_other()
    {
        var opened = (await _saved.OpenAsync(await _saved.SaveAsync("Other way", Loop with { InAnotherDirection = true }, Traversals)))!;
        Assert.That(opened.Loop.InAnotherDirection, Is.False);
    }

    //  An imported route's gaps (T083) are kept with it.
    [Test]
    public async Task An_imported_route_opens_with_its_gaps()
    {
        var gapped = Loop with { Gaps = [new RouteGap([KenwoodHouse, new LatLon(51.5650, -0.1650)], 640)] };
        var opened = (await _saved.OpenAsync(await _saved.SaveAsync("Friend's", gapped, Traversals)))!;
        Assert.That(opened.Loop.Gaps.Select(g => (g.Line.ToArray(), g.LengthM)), Is.EqualTo(gapped.Gaps.Select(g => (g.Line.ToArray(), g.LengthM))));
    }

    [Test]
    public async Task Its_Segments_are_kept_by_their_OSM_identity() =>
        Assert.That(await _saved.TraversalsAsync(await _saved.SaveAsync("Heath wiggle", Loop, Traversals)), Is.EqualTo(Traversals));

    [Test]
    public async Task They_re_listed_newest_first_with_their_km()
    {
        await _saved.SaveAsync("First", Loop, Traversals);
        await _saved.SaveAsync("Second", Loop, Traversals);
        Assert.That((await _saved.ListAsync()).Select(r => (r.Name, r.LengthKm)), Is.EqualTo(new[] { ("Second", 9.4), ("First", 9.4) }));
    }

    [Test]
    public async Task A_route_can_be_renamed()
    {
        var id = await _saved.SaveAsync("Heath wiggle", Loop, Traversals);
        await _saved.RenameAsync(id, "Thames Path out and back");
        Assert.That((await _saved.OpenAsync(id))!.Name, Is.EqualTo("Thames Path out and back"));
    }

    [Test]
    public async Task A_route_can_be_deleted()
    {
        var id = await _saved.SaveAsync("Heath wiggle", Loop, Traversals);
        await _saved.DeleteAsync(id);
        Assert.That((await _saved.ListAsync(), await _saved.OpenAsync(id)), Is.EqualTo((Array.Empty<SavedRouteSummary>(), (SavedRoute?)null)).Using<(IReadOnlyList<SavedRouteSummary>, SavedRoute?)>(
            (a, b) => a.Item1.Count == 0 && a.Item2 is null));
    }

    //  A name is something to call it by.
    [Test]
    public void A_route_needs_a_name() =>
        Assert.That(async () => await _saved.SaveAsync("  ", Loop, Traversals), Throws.ArgumentException);
}
