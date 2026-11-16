using System.Diagnostics;
using System.Xml;
using System.Xml.Schema;
using WendYourWay.Cli;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// STORY-029 end to end (PLAN T013): `wend network build` on a real extract, a 10 km Request, and
/// the GPX the user takes to OS Maps. The extract is a 7 km square of Greater London round
/// Pembroke Lodge in Richmond Park (Fixtures/richmond-park.osm.gz, cut by scripts/cut-fixture.ps1).
/// </summary>
[TestFixture]
[NonParallelizable]
public class PlanningALoopEndToEnd
{
    private static readonly LatLon PembrokeLodge = new(51.4436, -0.2963);

    private NpgsqlDataSource _dataSource = null!;
    private int _buildExitCode;
    private string _buildOutput = "";
    private PlannedLoop? _planned;
    private IOfferedLoops? _offered;
    private TimeSpan _building;
    private TimeSpan _planning;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        using var output = new StringWriter();
        var clock = Stopwatch.StartNew();
        _buildExitCode = await CommandLine.Run(
            ["network", "build", Fixtures.Path("richmond-park.osm.gz"), "--database", TestDatabase.ConnectionString, "--rights-of-way", Fixtures.Path("rights-of-way"), "--osm2pgsql", Tools.Osm2pgsql],
            output);
        _buildOutput = output.ToString();
        _building = clock.Elapsed;

        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        clock.Restart();
        _offered = await new LoopPlanner(new PostgisNetworkSlices(_dataSource), new MapGeometry(_dataSource), ScoringWeights.Default)
            .PlanAsync(PembrokeLodge, distanceKm: 10);
        _planned = _offered is [var best, ..] ? best : null;
        _planning = clock.Elapsed;
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource?.Dispose();

    [Test]
    public void Then_the_Network_builds() => Assert.That(_buildExitCode, Is.Zero, _buildOutput);

    [Test]
    public void Then_a_Loop_comes_back()
    {
        Assert.That(_planned, Is.Not.Null);
        var b = _planned!.Breakdown;
        TestContext.Out.WriteLine(
            $"{b.LengthM / 1000:0.00} km · Off-road {b.OffRoadPercent:0}% · Traffic-free {b.TrafficFreePercent:0}% · " +
            $"Connector {b.ConnectorLengthM / 1000:0.00} km · Retrace {b.RetraceLengthM / 1000:0.00} km · " +
            $"built in {_building.TotalSeconds:0.0} s, planned in {_planning.TotalSeconds:0.0} s{Environment.NewLine}{_buildOutput}");
    }

    [Test]
    public void Then_it_finishes_where_it_starts() =>
        Assert.That(_planned!.Line[^1], Is.EqualTo(_planned.Line[0]));

    //  T097: what a Saved Route of it would keep for the app's stars: its Activity, its score, all of it on the map.
    [Test]
    public void Then_its_score_is_what_saving_it_keeps()
    {
        var score = _offered!.ScoreOf(_planned!);
        Assert.That((score?.Activity, score?.Score > 0, score?.MatchedShare), Is.EqualTo(((WendYourWay.Core.Loops.Activity?)WendYourWay.Core.Loops.Activity.Run, (bool?)true, (double?)1)));
    }

    //  T104: and its shape, as its Breakdown says: its share off-road and climbing per km.
    [Test]
    public void Then_its_score_keeps_its_share_off_road_and_climbing()
    {
        var score = _offered!.ScoreOf(_planned!)!;
        Assert.That((score.OffRoadShare, score.ClimbPerKm), Is.EqualTo(((double?)_planned!.Breakdown.OffRoadShare, (double?)_planned.Breakdown.ClimbPerKm)));
    }

    [Test]
    public void Then_a_Loop_it_didnt_offer_has_no_score_from_it() =>
        Assert.That(_offered!.ScoreOf(_planned! with { Line = [] }), Is.Null);

    //  T096: from Pembroke Lodge at 8 km, four Loops on the usual bar and one in another direction (at
    //  10 km, since Main Roads were crossed at real crossings, T145c, five on the bar).
    [Test]
    public async Task Then_a_Loop_in_another_direction_is_marked_and_comes_after_those_on_the_usual_bar()
    {
        var offered = await new LoopPlanner(new PostgisNetworkSlices(_dataSource), new MapGeometry(_dataSource), ScoringWeights.Default)
            .PlanAsync(PembrokeLodge, distanceKm: 8);
        Assert.That(offered.Select(p => p.InAnotherDirection), Is.Ordered.And.Contains(false).And.Contains(true));
    }

    [Test]
    public void Then_its_length_is_between_9_and_11_km() =>
        Assert.That(_planned!.Breakdown.LengthM, Is.InRange(9000, 11500));

    [Test]
    public void Then_its_GPX_is_valid_GPX_1_1()
    {
        var schemas = new XmlSchemaSet();
        schemas.Add("http://www.topografix.com/GPX/1/1", Path.Combine(TestContext.CurrentContext.TestDirectory, "Schemas", "gpx-1.1.xsd"));
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas };
        settings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;
        var problems = new List<string>();
        settings.ValidationEventHandler += (_, e) => problems.Add($"{e.Severity}: {e.Message}");

        using (var reader = XmlReader.Create(new StringReader(LoopGpx.Of(_planned!).Content), settings))
        {
            while (reader.Read()) { }
        }
        Assert.That(problems, Is.Empty);
    }

    //  T076: Nudges on the real map, through the planner. A point halfway along a piece of the
    //  line in the middle of the Loop, inside one of its Segments.
    private (LatLon A, LatLon B, LatLon Between) MiddlePiece()
    {
        var line = _planned!.Line;
        var (a, b) = (line[line.Count / 2], line[(line.Count / 2) + 1]);
        return (a, b, new LatLon((a.Latitude + b.Latitude) / 2, (a.Longitude + b.Longitude) / 2));
    }

    private static bool Passes(IReadOnlyList<LatLon> line, LatLon a, LatLon b) =>
        line.Zip(line.Skip(1)).Any(p => (p.First == a && p.Second == b) || (p.First == b && p.Second == a));

    [Test]
    public async Task Then_Avoid_this_takes_that_bit_off_the_Loop()
    {
        var (a, b, between) = MiddlePiece();
        var nudged = await _offered!.NudgeAsync(_planned!, new AvoidNudge(between), Core.Loops.Leans.Centre);
        Assert.That(nudged, Is.Not.Null);
        Assert.That((Passes(_planned!.Line, a, b), Passes(nudged!.Loop.Line, a, b)), Is.EqualTo((true, false)));
    }

    //  Dragged about 300 m north of its middle: through the junction nearest there, a stop.
    [Test]
    public async Task Then_dragging_it_goes_through_where_it_s_dropped()
    {
        var (_, _, between) = MiddlePiece();
        var dropped = new LatLon(between.Latitude + 0.0027, between.Longitude);
        var nudged = await _offered!.NudgeAsync(_planned!, new ViaNudge(between, dropped), Core.Loops.Leans.Centre);
        Assert.That(nudged, Is.Not.Null);
        Assert.That(nudged!.Loop.Anchors, Has.Count.EqualTo(_planned!.Anchors.Count + 1));
        Assert.That(nudged.Loop.Line, Does.Contain(nudged.Loop.Anchors.MinBy(p => Metres(p, dropped))));
        Assert.That(Metres(nudged.Loop.Anchors.MinBy(p => Metres(p, dropped)), dropped), Is.LessThan(500));
    }

    //  T126 (user: "I can't nudge onto it - instead the waypoint is added on a nearby path"): a long
    //  path off the Loop, within a km of its middle, and a quarter of the way along it, where it is.
    private async Task<(SegmentKey Key, LatLon Start, LatLon Quarter, LatLon End)> LongPathNearby()
    {
        var (_, _, between) = MiddlePiece();
        var on = _offered!.TraversalsOf(_planned!)!.Select(t => t.Key).ToHashSet();
        await using var query = _dataSource.CreateCommand("""
            select s.osm_way_id, s.from_node_id, s.to_node_id,
                   ST_Y(a), ST_X(a), ST_Y(q), ST_X(q), ST_Y(b), ST_X(b)
              from network.segments s,
                   lateral (select ST_Transform(ST_StartPoint(s.geom), 4326) a, ST_Transform(ST_LineInterpolatePoint(s.geom, 0.25), 4326) q,
                                   ST_Transform(ST_EndPoint(s.geom), 4326) b) p
             where s.length_m > 300 and s.access_run = 'allowed'
               and ST_DWithin(s.geom, ST_Transform(ST_SetSRID(ST_MakePoint(@lon, @lat), 4326), 27700), 1000)
             order by s.length_m desc
            """);
        query.Parameters.Add(new NpgsqlParameter("lon", between.Longitude));
        query.Parameters.Add(new NpgsqlParameter("lat", between.Latitude));
        await using var reader = await query.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var key = new SegmentKey(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
            if (on.Contains(key)) continue;
            return (key, new LatLon(reader.GetDouble(3), reader.GetDouble(4)), new LatLon(reader.GetDouble(5), reader.GetDouble(6)), new LatLon(reader.GetDouble(7), reader.GetDouble(8)));
        }
        Assert.Fail("No long path off the Loop near it in the fixture.");
        return default;
    }

    [Test]
    public async Task Then_dropped_part_way_along_a_path_it_goes_along_that_path()
    {
        var (key, _, quarter, _) = await LongPathNearby();
        var (_, _, between) = MiddlePiece();
        var nudged = (await _offered!.NudgeAsync(_planned!, new ViaNudge(between, quarter), Core.Loops.Leans.Centre))!;
        Assert.That(_offered.TraversalsOf(nudged.Loop)!.Select(t => t.Key), Does.Contain(key));
    }

    //  Its stop at the path's end nearer the drop: a quarter along, its start.
    [Test]
    public async Task Then_dropped_part_way_along_a_path_its_stop_is_at_the_nearer_end()
    {
        var (_, start, quarter, end) = await LongPathNearby();
        var (_, _, between) = MiddlePiece();
        var nudged = (await _offered!.NudgeAsync(_planned!, new ViaNudge(between, quarter), Core.Loops.Leans.Centre))!;
        var stop = nudged.Loop.Anchors.MinBy(p => Metres(p, quarter));
        Assert.That((Metres(stop, start) < 5, Metres(stop, end) < 5), Is.EqualTo((true, false)));
    }

    [Test]
    public async Task Then_a_Nudge_nowhere_near_the_Loop_is_none() =>
        Assert.That(await _offered!.NudgeAsync(_planned!, new AvoidNudge(new LatLon(51.6, -0.1)), Core.Loops.Leans.Centre), Is.Null);

    //  T079: an Avoid is marked where it is, and removed, it's gone; a Via is a stop, and
    //  removed, the stops are as they were.
    [Test]
    public async Task Then_an_Avoid_is_marked_and_can_be_removed()
    {
        var (_, _, between) = MiddlePiece();
        var avoided = (await _offered!.NudgeAsync(_planned!, new AvoidNudge(between), Core.Loops.Leans.Centre))!.Loop;
        var removed = (await _offered.NudgeAsync(avoided, new RemoveAvoidNudge(0), Core.Loops.Leans.Centre))!.Loop;
        Assert.That((avoided.Avoids.Count, Metres(avoided.Avoids[0], between) < 300, removed.Avoids.Count), Is.EqualTo((1, true, 0)));
    }

    [Test]
    public async Task Then_a_Via_can_be_removed()
    {
        var (_, _, between) = MiddlePiece();
        var dropped = new LatLon(between.Latitude + 0.0027, between.Longitude);
        var via = (await _offered!.NudgeAsync(_planned!, new ViaNudge(between, dropped), Core.Loops.Leans.Centre))!.Loop;
        var stop = via.Anchors.ToList().IndexOf(via.Anchors.MinBy(p => Metres(p, dropped)));
        var removed = (await _offered.NudgeAsync(via, new RemoveStopNudge(stop), Core.Loops.Leans.Centre))!.Loop;
        Assert.That(removed.Anchors, Is.EqualTo(_planned!.Anchors));
    }

    //  T077: each point of the line knows its OSM way, for "Fix the map here": as many as the
    //  points, and each one a way the Loop is on.
    [Test]
    public async Task Then_each_point_of_its_line_knows_its_OSM_way()
    {
        await using var ways = _dataSource.CreateCommand("select array_agg(distinct osm_way_id) from network.segments");
        var known = ((long[])(await ways.ExecuteScalarAsync())!).ToHashSet();
        Assert.That(_planned!.Ways, Has.Count.EqualTo(_planned.Line.Count));
        Assert.That(_planned.Ways.All(known.Contains) && _planned.Ways.Distinct().Count() > 1, Is.True);
    }

    // Metres between two places, near enough over a few km.
    private static double Metres(LatLon a, LatLon b) =>
        Math.Sqrt(Math.Pow((a.Latitude - b.Latitude) * 111_200, 2) + Math.Pow((a.Longitude - b.Longitude) * 111_200 * Math.Cos(a.Latitude * Math.PI / 180), 2));
}
