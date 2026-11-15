using System.Globalization;
using WendYourWay.Core.Loops;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Outings;

/// <summary>
/// The app's stars for an Outing (PLAN T097, STORY-045): its track matched onto the Network and
/// scored as a Loop is, the score kept when it's imported and again after a rebuild, its stars the
/// fifth of the user's Outings it falls in. On Fixtures/richmond-park.osm.gz, with Runs written
/// here: round a 5 km Loop planned from Pembroke Lodge, the same 22 km north, off the Network, and an 8
/// km Gravel Ride from there.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ScoringOutings
{
    private static readonly LatLon PembrokeLodge = new(51.4436, -0.2963);

    // Round a Loop planned on the fixture, and the same well off it.
    private (double Lat, double Lon)[] RoundThePark = [];
    private (double Lat, double Lon)[] FarFromEveryWay = [];
    private (double Lat, double Lon)[] RideRound = [];
    private WendYourWay.Data.Planning.PlannedLoop _parkLoop = null!;
    private IReadOnlyList<(WendYourWay.Core.Network.SegmentKey Key, bool Forward)> _parkTraversals = [];

    private NpgsqlDataSource _dataSource = null!;
    private string _folder = null!;
    private int _written;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("richmond-park.osm.gz"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        var offered = await new WendYourWay.Data.Planning.LoopPlanner(new PostgisNetworkSlices(_dataSource), new MapGeometry(_dataSource), ScoringWeights.Default)
            .PlanAsync(PembrokeLodge, distanceKm: 5);
        var loop = offered[0];
        (_parkLoop, _parkTraversals) = (loop, offered.TraversalsOf(loop)!);
        RoundThePark = [.. loop.Line.Select(p => (p.Latitude, p.Longitude))];
        FarFromEveryWay = [.. loop.Line.Select(p => (p.Latitude + 0.2, p.Longitude))];
        var ride = (await new WendYourWay.Data.Planning.LoopPlanner(new PostgisNetworkSlices(_dataSource), new MapGeometry(_dataSource), ScoringWeights.Default)
            .PlanAsync(PembrokeLodge, distanceKm: 8, activity: Activity.GravelRide))[0];
        RideRound = [.. ride.Line.Select(p => (p.Latitude, p.Longitude))];
        _folder = Directory.CreateTempSubdirectory("scoring-outings").FullName;
    }

    [OneTimeTearDown]
    public void Dispose()
    {
        _dataSource.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [SetUp]
    public async Task NoOutings()
    {
        await using var command = _dataSource.CreateCommand("delete from app.outings; delete from app.saved_routes");
        await command.ExecuteNonQueryAsync();
    }

    // A recorded Run through these points, a point every 5 m or so, a second apart.
    private string Gpx(string name, (double Lat, double Lon)[] through, string type = "running")
    {
        var points = new List<(double Lat, double Lon)>();
        for (var i = 0; i + 1 < through.Length; i++)
        {
            var (a, b) = (through[i], through[i + 1]);
            var steps = Math.Max(1, (int)(Math.Abs(b.Lon - a.Lon) / 0.00007 + Math.Abs(b.Lat - a.Lat) / 0.000045));
            for (var s = 0; s < steps; s++) points.Add((a.Lat + ((b.Lat - a.Lat) * s / steps), a.Lon + ((b.Lon - a.Lon) * s / steps)));
        }
        points.Add(through[^1]);
        // A day apart, so no two are taken for one Outing recorded twice.
        var start = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero).AddDays(++_written);
        var trkpts = string.Concat(points.Select((p, i) => string.Format(CultureInfo.InvariantCulture,
            "<trkpt lat=\"{0:0.0000000}\" lon=\"{1:0.0000000}\"><time>{2:yyyy-MM-ddTHH:mm:ssZ}</time></trkpt>", p.Lat, p.Lon, start.AddSeconds(i))));
        var path = Path.Combine(_folder, name + ".gpx");
        File.WriteAllText(path, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx creator="t097-test" version="1.1" xmlns="http://www.topografix.com/GPX/1/1">
             <trk><name>{name}</name><type>{type}</type><trkseg>{trkpts}</trkseg></trk>
            </gpx>
            """);
        return path;
    }

    private async Task<(double? Score, double? MatchedShare)> Kept(string name)
    {
        await using var query = _dataSource.CreateCommand("select score, matched_share from app.outings where name = @name");
        query.Parameters.Add(new NpgsqlParameter("name", name));
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.IsDBNull(0) ? null : reader.GetFloat(0), reader.IsDBNull(1) ? null : reader.GetFloat(1));
    }

    // Its share off-road and climbing per km as kept (T104).
    private async Task<(double? OffRoad, double? Climb)> Shape(string name)
    {
        await using var query = _dataSource.CreateCommand("select off_road_share, climb_per_km from app.outings where name = @name");
        query.Parameters.Add(new NpgsqlParameter("name", name));
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.IsDBNull(0) ? null : reader.GetFloat(0), reader.IsDBNull(1) ? null : reader.GetFloat(1));
    }

    private async Task<int?> StarsListed(string name) => (await new PostgisOutingViews(_dataSource).ListAsync()).Single(o => o.Name == name).Stars;

    [Test]
    public async Task An_imported_Outing_is_scored_as_a_Loop_is()
    {
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Round the park", RoundThePark));
        var (score, share) = await Kept("Round the park");
        Assert.That((score > 0, share >= 0.8), Is.EqualTo((true, true)));
    }

    //  T104: beside its score, its share off-road (Richmond Park: much of it) and its climbing per
    //  km (none: this Network was built without terrain).
    [Test]
    public async Task Its_share_off_road_and_climbing_are_kept_with_its_score()
    {
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Round the park", RoundThePark));
        var (offRoad, climb) = await Shape("Round the park");
        Assert.That((offRoad > 0.2 && offRoad <= 1, climb), Is.EqualTo((true, (double?)0)));
    }

    [Test]
    public async Task Its_stars_are_listed_from_its_score_and_shape()
    {
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Round the park", RoundThePark));
        var (score, share) = await Kept("Round the park");
        var (offRoad, climb) = await Shape("Round the park");
        Assert.That(await StarsListed("Round the park"), Is.EqualTo(Stars.Of(Activity.Run, score!.Value, share!.Value, offRoad!.Value, climb!.Value)).And.Not.Null);
    }

    //  T113: its off-road share and its climbing each read as itself. The fixture has no climbing, so
    //  a shape is kept by hand that bands apart if the two are swapped: all off-road on the flat is 3
    //  stars (0.1 + 0.380), none off-road climbing 1 m per km would be 1 (0.1 + 0.0132).
    [Test]
    public async Task Its_off_road_share_and_climbing_are_each_listed_as_themselves()
    {
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Round the park", RoundThePark));
        await using (var keep = _dataSource.CreateCommand(
            "update app.outings set activity = 'run', score = 0.1, matched_share = 1, off_road_share = 1, climb_per_km = 0 where name = 'Round the park'"))
            await keep.ExecuteNonQueryAsync();
        Assert.That(await StarsListed("Round the park"), Is.EqualTo(3));
    }

    //  T115 (user: "maybe you could calculate the app ratings for the three saved routes?"): a Saved
    //  Route saved before the stars is scored by `wend outings score` as an Outing is; with no Activity
    //  kept, as a Run.
    [Test]
    public async Task A_Saved_Route_saved_before_the_stars_is_scored_as_a_Run()
    {
        var saved = new WendYourWay.Data.Planning.PostgisSavedRoutes(_dataSource);
        await saved.SaveAsync("Old park loop", _parkLoop, _parkTraversals);
        var count = await OutingScoring.ScoreSavedRoutesAsync(_dataSource);
        var listed = (await saved.ListAsync()).Single();
        await using var query = _dataSource.CreateCommand("select activity::text from app.saved_routes");
        Assert.That((count, listed.Scored, listed.Stars is not null, await query.ExecuteScalarAsync()), Is.EqualTo((1, true, true, (object)"run")));
    }

    //  Scored as an Outing of the same line is.
    [Test]
    public async Task Its_stars_are_those_of_an_Outing_on_the_same_line()
    {
        var saved = new WendYourWay.Data.Planning.PostgisSavedRoutes(_dataSource);
        await saved.SaveAsync("Old park loop", _parkLoop, _parkTraversals);
        await OutingScoring.ScoreSavedRoutesAsync(_dataSource);
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Round the park", RoundThePark));
        Assert.That((await saved.ListAsync()).Single().Stars, Is.EqualTo(await StarsListed("Round the park")));
    }

    //  One saved with its score (and its Activity, a ride's here) is left as it is.
    [Test]
    public async Task A_Saved_Route_with_a_score_is_left_as_it_is()
    {
        var saved = new WendYourWay.Data.Planning.PostgisSavedRoutes(_dataSource);
        await saved.SaveAsync("Ride loop", _parkLoop, _parkTraversals, new WendYourWay.Data.Planning.RouteScore(Activity.GravelRide, 0.05, 1));
        var count = await OutingScoring.ScoreSavedRoutesAsync(_dataSource);
        await using var query = _dataSource.CreateCommand("select activity::text || ' ' || score::text from app.saved_routes");
        Assert.That((count, await query.ExecuteScalarAsync()), Is.EqualTo((0, (object)"gravel_ride 0.05")));
    }

    //  A ride has no Activity of its own: it's scored as a Gravel Ride, the same track run scoring otherwise.
    [Test]
    public async Task A_ride_is_scored_as_a_Gravel_Ride()
    {
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Ride round", RideRound, "cycling"));
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Run round", RideRound));
        Assert.That((await Kept("Ride round")).Score, Is.Not.EqualTo((await Kept("Run round")).Score));
    }

    [Test]
    public async Task An_Outing_far_from_every_way_cant_be_rated()
    {
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Nowhere", FarFromEveryWay));
        Assert.That(((await Kept("Nowhere")).MatchedShare, await StarsListed("Nowhere")), Is.EqualTo(((double?)0, (int?)null)));
    }

    [Test]
    public async Task One_that_cant_be_rated_doesnt_stop_the_others_being_scored()
    {
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Nowhere", FarFromEveryWay));
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Round the park", RoundThePark));
        await Forget();
        var scored = await OutingScoring.ScoreAsync(_dataSource);
        Assert.That((scored.Scored, scored.Unrated, (await Kept("Round the park")).Score > 0), Is.EqualTo((2, 1, true)));
    }

    //  A track spread across more than the matcher reads at once (about 90 km) is refused by it.
    [Test]
    public async Task One_the_matcher_refuses_cant_be_rated_and_the_others_are_still_scored()
    {
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Far and wide", [(51.44, -0.30), (52.44, -0.30), (51.44, -0.30)]));
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Round the park", RoundThePark));
        await Forget();
        var scored = await OutingScoring.ScoreAsync(_dataSource);
        Assert.That((scored.Scored, scored.Unrated, (await Kept("Far and wide")).MatchedShare, (await Kept("Round the park")).Score > 0),
            Is.EqualTo((2, 1, (double?)0, true)));
    }

    // Every Outing's score set aside, as a rebuild leaves them.
    private async Task Forget()
    {
        await using var command = _dataSource.CreateCommand("update app.outings set score = null, matched_share = null");
        await command.ExecuteNonQueryAsync();
    }

    [Test]
    public async Task Scoring_again_scores_only_those_not_scored()
    {
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Round the park", RoundThePark));
        await using (var command = _dataSource.CreateCommand("update app.outings set score = 9.5"))
        {
            await command.ExecuteNonQueryAsync();
        }
        var scored = await OutingScoring.ScoreAsync(_dataSource);
        Assert.That((scored.Scored, (await Kept("Round the park")).Score), Is.EqualTo((0, (double?)9.5)));
    }

    [Test]
    public async Task Scoring_all_scores_every_Outing_again()
    {
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Round the park", RoundThePark));
        var before = await Kept("Round the park");
        await using (var command = _dataSource.CreateCommand("update app.outings set score = 9.5"))
        {
            await command.ExecuteNonQueryAsync();
        }
        var scored = await OutingScoring.ScoreAsync(_dataSource, all: true);
        Assert.That((scored.Scored, await Kept("Round the park")), Is.EqualTo((1, before)));
    }

    //  The rebuild clears them in its transaction; they're scored again after it commits.
    [Test]
    public async Task A_rebuild_leaves_every_Outing_to_be_scored_again()
    {
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Round the park", RoundThePark));
        await NetworkBuild.Build(_dataSource);
        Assert.That((await Kept("Round the park"), await Shape("Round the park")), Is.EqualTo((((double?)null, (double?)null), ((double?)null, (double?)null))));
    }

    [Test]
    public async Task Until_it_is_scored_it_is_listed_as_not_scored()
    {
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Round the park", RoundThePark));
        await Forget();
        Assert.That((await new PostgisOutingViews(_dataSource).ListAsync()).Single().Scored, Is.False);
    }

    [Test]
    public async Task Scored_it_is_listed_as_scored_even_when_it_cant_be_rated()
    {
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Nowhere", FarFromEveryWay));
        Assert.That((await new PostgisOutingViews(_dataSource).ListAsync()).Single().Scored, Is.True);
    }

    [Test]
    public async Task Until_it_is_scored_an_Outing_has_no_stars()
    {
        await OutingImporter.ImportGpxAsync(_dataSource, Gpx("Round the park", RoundThePark));
        await Forget();
        Assert.That(await StarsListed("Round the park"), Is.Null);
    }
}
