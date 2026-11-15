using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using WendYourWay.Cli;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Building;
using WendYourWay.Data.Outings;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Cli;

/// <summary>
/// `wend network back` (#135, T131, STORY-048): back to the map before, if a new one turns out bad,
/// in one swap; the Outings covered and scored again on it, Saved Routes the map changed under
/// moved onto it, the stars calibrated. Done again, it's forward again. On
/// Fixtures/richmond-park.osm.gz: built once, then again without one of a saved route's ways.
/// </summary>
[TestFixture]
[NonParallelizable]
public class GoingBackToTheMapBefore
{
    private static readonly LatLon PembrokeLodge = new(51.4436, -0.2963);

    private (int ExitCode, string Output) _none;
    private (string Kind, string Outcome, string? Message) _noneJob;
    private MapBefore? _noneFound;
    private long _firstSegments;
    private long _secondSegments;
    private long _firstCovered;
    private MapBefore _before = null!;
    private (int ExitCode, string Output) _back;
    private (string Kind, string Outcome, string? Message) _backJob;
    private long _backSegments;
    private long _backCovered;
    private long _backUnscored;
    private float _firstScore;
    private float _secondScore;
    private float _backScore;
    private bool _wholeFlaggedAfter;
    private bool _wholeFlaggedBefore;
    private long _forwardSegments;
    private (int ExitCode, string Output) _forward;

    private static async Task<(int ExitCode, string Output)> Ct(params string[] args)
    {
        using var output = new StringWriter();
        var exitCode = await CommandLine.Run(args, output);
        return (exitCode, output.ToString());
    }

    private static Task<(int ExitCode, string Output)> Back() => Ct("network", "back", "--database", TestDatabase.ConnectionString);

    private static async Task<T> Scalar<T>(string sql)
    {
        await using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await using var command = dataSource.CreateCommand(sql);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<(string Kind, string Outcome, string? Message)> LastJob()
    {
        await using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await using var query = dataSource.CreateCommand("select kind, outcome, message from app.map_updates order by id desc limit 1");
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private static Task<float> Score() => Scalar<float>("select score from app.outings");

    private static Task<long> Segments() => Scalar<long>("select count(*) from network.segments");

    // An Outing's coverage that's on the map in use.
    private static Task<long> Covered() => Scalar<long>("""
        select count(*) from app.outing_coverage c
         where exists (select 1 from network.segments s where s.osm_way_id = c.osm_way_id and s.from_node_id = c.from_node_id and s.to_node_id = c.to_node_id)
        """);

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        var richmond = Fixtures.Path("richmond-park.osm.gz");
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, richmond);
        await using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(dataSource);

        // Built once: the empty Network the migrations made is no map to go back to.
        _noneFound = await NetworkBefore.FindAsync(dataSource);
        _none = await Back();
        _noneJob = await LastJob();

        var planner = new LoopPlanner(new PostgisNetworkSlices(dataSource), new MapGeometry(dataSource), ScoringWeights.Default, matching: new RouteMatching(dataSource));
        var offered = await planner.PlanAsync(PembrokeLodge, distanceKm: 10);
        var planned = offered[0];
        var traversals = offered.TraversalsOf(planned)!;
        var saved = new PostgisSavedRoutes(dataSource);
        var whole = await saved.SaveAsync("Park loop", planned, traversals);

        // The Loop run, as an Outing.
        var gpx = Path.Combine(Directory.CreateTempSubdirectory("cutthrough-back-").FullName, "park-loop.gpx");
        var start = new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);
        await File.WriteAllTextAsync(gpx, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="spec" xmlns="http://www.topografix.com/GPX/1/1"><trk><name>Park loop run</name><type>running</type><trkseg>
            {string.Join("\n", planned.Line.Select((p, i) => string.Create(CultureInfo.InvariantCulture,
                $"<trkpt lat=\"{p.Latitude}\" lon=\"{p.Longitude}\"><time>{start.AddSeconds(i * 8):yyyy-MM-ddTHH:mm:ssZ}</time></trkpt>")))}
            </trkseg></trk></gpx>
            """);
        await OutingImporter.ImportGpxAsync(dataSource, gpx);
        await OutingScoring.ScoreAsync(dataSource);
        (_firstSegments, _firstCovered, _firstScore) = (await Segments(), await Covered(), await Score());

        // Built again without one of the route's ways: the route flagged.
        var gone = traversals[traversals.Count / 2].Key.OsmWayId;
        var without = Path.Combine(Path.GetDirectoryName(gpx)!, "richmond-park-less-one.osm");
        await using (var file = new GZipStream(File.OpenRead(richmond), CompressionMode.Decompress))
        using (var reader = new StreamReader(file))
            await File.WriteAllTextAsync(without, Regex.Replace(await reader.ReadToEndAsync(), $"<way id=\"{gone}\"[^>]*>.*?</way>", "", RegexOptions.Singleline));
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, without);
        await NetworkBuild.Build(dataSource);
        await OutingScoring.ScoreAsync(dataSource);
        (_secondSegments, _secondScore) = (await Segments(), await Score());
        _wholeFlaggedBefore = (await saved.ListAsync()).Single(r => r.Id == whole).MapChanged;
        _before = (await NetworkBefore.FindAsync(dataSource))!;

        // Back.
        _back = await Back();
        _backJob = await LastJob();
        (_backSegments, _backCovered) = (await Segments(), await Covered());
        _backUnscored = await Scalar<long>("select count(*) from app.outings where score is null");
        _backScore = await Score();
        _wholeFlaggedAfter = (await saved.ListAsync()).Single(r => r.Id == whole).MapChanged;

        // And forward again.
        _forward = await Back();
        _forwardSegments = await Segments();
        Directory.Delete(Path.GetDirectoryName(gpx)!, recursive: true);
    }

    [Test]
    public void Built_once_there_is_no_map_before() => Assert.That(_noneFound, Is.Null);

    [Test]
    public void With_no_map_before_it_is_refused_and_says_so() =>
        Assert.That((_none.ExitCode, _noneJob.Kind, _noneJob.Outcome, _noneJob.Message),
            Is.EqualTo((1, JobKind.Back, "failed", "There's no map before this one to go back to.")), _none.Output);

    [Test]
    public void Built_again_the_one_before_is_found_and_fits() => Assert.That(_before.Fits, Is.True);

    [Test]
    public void Gone_back_the_map_is_the_one_before() =>
        Assert.That((_back.ExitCode, _backSegments), Is.EqualTo((0, _firstSegments)), _back.Output);

    [Test]
    public void It_is_a_job_that_says_where_it_went() =>
        Assert.That((_backJob.Kind, _backJob.Outcome, _backJob.Message), Is.EqualTo((JobKind.Back, "finished", _backJob.Message)), _back.Output);

    [Test]
    public void It_says_which_map() =>
        Assert.That(_backJob.Message, Does.Match(@"^Gone (back|forward) to the map (of \d{1,2} \w{3} \d{4}|before this one)\.$"));

    [Test]
    public void The_Outings_are_covered_again_on_it() => Assert.That(_backCovered, Is.EqualTo(_firstCovered).And.GreaterThan(0));

    [Test]
    public void The_Outings_are_scored_again_on_it() => Assert.That(_backUnscored, Is.Zero);

    //  Its score on the map before, not the newer map's kept.
    [Test]
    public void An_Outing_scores_as_it_did_on_that_map() =>
        Assert.That((_backScore, _secondScore != _firstScore), Is.EqualTo((_firstScore, true)));

    //  The route was flagged on the newer map, which lacks one of its ways; on the one before, it isn't.
    [Test]
    public void A_Saved_Route_flagged_on_the_newer_map_isnt_on_this_one() =>
        Assert.That((_wholeFlaggedBefore, _wholeFlaggedAfter), Is.EqualTo((true, false)));

    [Test]
    public void Saved_Routes_are_moved_onto_it_as_after_an_update() => Assert.That(_back.Output, Does.Contain("Saved Route"));

    [Test]
    public void Done_again_it_is_forward_again() =>
        Assert.That((_forward.ExitCode, _forwardSegments), Is.EqualTo((0, _secondSegments)), _forward.Output);

    [Test]
    public async Task Another_job_running_it_waits_its_turn()
    {
        var segments = await Segments();
        await using var held = await UpdateLock.TryTakeAsync(TestDatabase.ConnectionString);
        var (exitCode, output) = await Back();
        Assert.That((exitCode, output.Contains("already running", StringComparison.Ordinal), await Segments()), Is.EqualTo((1, true, segments)), output);
    }

    //  A migration since that changed the Network's tables: the map before can't be read here.
    [Test]
    public async Task Of_an_older_make_it_is_refused()
    {
        var segments = await Segments();
        await Scalar<object>("alter table network.segments add column made_since int; select 1");
        try
        {
            var (exitCode, output) = await Back();
            Assert.That((exitCode, await Segments(), (await LastJob()).Message),
                Is.EqualTo((1, segments, "The map before this one was made by an older Wend Your Way, so it can't be gone back to.")), output);
        }
        finally
        {
            await Scalar<object>("alter table network.segments drop column made_since; select 1");
        }
    }

    //  The map's date: an update before the last go-back no longer dates it, so gone back to an
    //  undated map, Update is offered again.
    [Test]
    public async Task An_update_before_the_last_go_back_no_longer_dates_the_map()
    {
        await Scalar<object>("""
            insert into app.map_updates (kind, stage, outcome, source, source_at, finished_at) values ('update', 'backing up', 'finished', 'spec', '2026-11-09T21:22:17Z', now());
            insert into app.map_updates (kind, stage, outcome, source, finished_at) values ('back', 'backing up', 'finished', 'network_prev', now());
            select 1
            """);
        await using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        var gone = await new Jobs(dataSource).NewestSourceAtAsync();
        await Scalar<object>("insert into app.map_updates (kind, stage, outcome, source, source_at, finished_at) values ('update', 'backing up', 'finished', 'spec', '2026-11-10T21:22:17Z', now()); select 1");
        Assert.That((gone, await new Jobs(dataSource).NewestSourceAtAsync()),
            Is.EqualTo(((DateTimeOffset?)null, (DateTimeOffset?)new DateTimeOffset(2026, 11, 10, 21, 22, 17, TimeSpan.Zero))));
    }

    //  Stopped after its swap: the next finishes it, rather than swapping again.
    [Test]
    public async Task Stopped_after_its_swap_the_next_finishes_it_rather_than_swap_again()
    {
        var segments = await Segments();
        await Scalar<object>("""
            insert into app.map_updates (kind, stage, source, started_at, heartbeat_at) values ('back', 'swapped', 'network_prev', now() - interval '1 hour', now() - interval '1 hour')
            returning id
            """);
        var (exitCode, output) = await Back();
        Assert.That((exitCode, await Segments(), (await LastJob()).Message), Is.EqualTo((0, segments, "Finished going to the map that was swapped in.")), output);
    }
}
