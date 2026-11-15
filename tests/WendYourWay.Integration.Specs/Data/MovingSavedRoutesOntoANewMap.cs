using System.IO.Compression;
using System.Text.RegularExpressions;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Building;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// A Saved Route moved onto the new map (PLAN T136, STORY-016; user: "I will want to be able to
/// nudge these in due course"): its saved line matched onto the Network again; moved if the new
/// route follows the same line, its name and the user's stars kept; refused, kept as it was, and
/// said where. On Fixtures/richmond-park.osm.gz: a route whose Segment's way was split elsewhere
/// (as T103 makes one), then the map rebuilt without one of the route's ways.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MovingSavedRoutesOntoANewMap
{
    private static readonly LatLon PembrokeLodge = new(51.4436, -0.2963);

    private PlannedLoop _planned = null!;
    private RouteMove _split = null!;
    private SavedRouteSummary _splitAfter = null!;
    private SavedRoute _splitOpened = null!;
    private bool _splitReopens;
    private RouteMove _unchanged = null!;
    private RouteMove _gone = null!;
    private SavedRouteSummary _goneAfter = null!;
    private SavedRoute _goneOpened = null!;
    private (int Moved, int Kept) _all;
    private string _byHand = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        var richmond = Fixtures.Path("richmond-park.osm.gz");
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, richmond);
        await using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(dataSource);
        var planner = new LoopPlanner(new PostgisNetworkSlices(dataSource), new MapGeometry(dataSource), ScoringWeights.Default, matching: new RouteMatching(dataSource));
        var offered = await planner.PlanAsync(PembrokeLodge, distanceKm: 10);
        _planned = offered[0];
        var traversals = offered.TraversalsOf(_planned)!;
        var saved = new PostgisSavedRoutes(dataSource);
        var moves = new SavedRouteMoves(saved, planner, dataSource);

        var whole = await saved.SaveAsync("Park loop", _planned, traversals);
        _unchanged = await moves.MoveAsync(whole);

        // A way of it split elsewhere since it was saved: a Segment with the same start, another end.
        var split = await saved.SaveAsync("Park loop, split", _planned,
            [.. traversals.Select((t, i) => i == traversals.Count / 3 ? (t.Key with { ToNodeId = t.Key.ToNodeId + 1 }, t.Forward) : t)]);
        await saved.RateAsync(split, 4);
        _split = await moves.MoveAsync(split);
        _splitAfter = (await saved.ListAsync()).Single(r => r.Id == split);
        _splitOpened = (await saved.OpenAsync(split))!;
        _splitReopens = (await planner.ReopenAsync(_splitOpened.Loop, await saved.TraversalsAsync(split))).Count > 0;
        await saved.DeleteAsync(split);

        // `wend routes move`, by hand.
        var again = await saved.SaveAsync("Park loop, split again", _planned,
            [.. traversals.Select((t, i) => i == traversals.Count / 4 ? (t.Key with { ToNodeId = t.Key.ToNodeId + 1 }, t.Forward) : t)]);
        using (var output = new StringWriter())
        {
            await WendYourWay.Cli.CommandLine.Run(["routes", "move", "--database", TestDatabase.ConnectionString], output);
            _byHand = output.ToString();
        }
        await saved.DeleteAsync(again);

        // Rebuilt without one of its ways.
        var gone = traversals[traversals.Count / 2].Key.OsmWayId;
        var without = Path.Combine(Directory.CreateTempSubdirectory("cutthrough-moved-map-").FullName, "richmond-park-less-one.osm");
        await using (var file = new GZipStream(File.OpenRead(richmond), CompressionMode.Decompress))
        using (var reader = new StreamReader(file))
        {
            var osm = await reader.ReadToEndAsync();
            await File.WriteAllTextAsync(without, Regex.Replace(osm, $"<way id=\"{gone}\"[^>]*>.*?</way>", "", RegexOptions.Singleline));
        }
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, without);
        await NetworkBuild.Build(dataSource);
        _gone = await moves.MoveAsync(whole);
        _goneAfter = (await saved.ListAsync()).Single(r => r.Id == whole);
        _goneOpened = (await saved.OpenAsync(whole))!;
        _all = await moves.MoveChangedAsync();
        Directory.Delete(Path.GetDirectoryName(without)!, recursive: true);
    }

    [Test]
    public void One_the_map_hasn_t_changed_under_is_left_as_it_is() => Assert.That(_unchanged.Outcome, Is.EqualTo(RouteMoveOutcome.Unchanged));

    //  Its paths all still there, a way split: moved.
    [Test]
    public void A_way_of_it_split_elsewhere_it_s_moved() => Assert.That((_split.Outcome, _splitAfter.MapChanged), Is.EqualTo((RouteMoveOutcome.Moved, false)));

    [Test]
    public void Moved_it_keeps_its_name_and_the_user_s_stars() => Assert.That((_splitAfter.Name, _splitAfter.UserStars), Is.EqualTo(("Park loop, split", (int?)4)));

    [Test]
    public void Moved_it_follows_the_same_line() =>
        Assert.That(_splitOpened.Loop.Breakdown.LengthM, Is.EqualTo(_planned.Breakdown.LengthM).Within(2).Percent);

    [Test]
    public void Moved_it_has_the_app_s_score_on_the_new_map() => Assert.That(_splitAfter.Scored, Is.True);

    //  What the user wants it for: "I will want to be able to nudge these".
    [Test]
    public void Moved_it_opens_again_to_be_nudged() => Assert.That(_splitReopens, Is.True);

    //  A path of it gone: kept as it was, and said where.
    [Test]
    public void A_way_of_it_gone_it_s_kept_as_it_was() =>
        Assert.That((_gone.Outcome, _goneAfter.MapChanged, _goneOpened.Loop.Line), Is.EqualTo((RouteMoveOutcome.Kept, true, _planned.Line)));

    [Test]
    public void Kept_it_says_from_where_it_no_longer_follows_the_map() =>
        Assert.That(_gone.Said, Does.Match(@"^From \d+\.\d km it no longer follows the map\.$"));

    [Test]
    public void By_hand_it_says_each_and_how_many() =>
        Assert.That(_byHand, Does.Contain("  Park loop, split again: moved onto the map as it is").And.Contain("1 moved onto it, 0 kept as they were."));

    [Test]
    public void All_those_changed_are_tried_and_counted() => Assert.That(_all, Is.EqualTo((0, 1)));
}
