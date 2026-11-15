using System.IO.Compression;
using System.Text.RegularExpressions;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// A Saved Route whose map has changed (PLAN T103, STORY-016): after a rebuild, one with a Segment
/// no longer on the Network says so in Saved Routes, its line kept as it was saved; one rebuilt from
/// the same data says nothing. On Fixtures/richmond-park.osm.gz, rebuilt as it is, then without one
/// of the route's ways.
/// </summary>
[TestFixture]
[NonParallelizable]
public class FlaggingAChangedMap
{
    private static readonly LatLon PembrokeLodge = new(51.4436, -0.2963);

    private PlannedLoop _planned = null!;
    private bool _justSaved;
    private bool _sameData;
    private bool _wayGone;
    private bool _wayResplit;
    private PlannedLoop _openedAfter = null!;

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
        var id = await saved.SaveAsync("Park loop", _planned, traversals);
        _justSaved = (await saved.ListAsync()).Single().MapChanged;

        // AC1: rebuilt from the same data.
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, richmond);
        await NetworkBuild.Build(dataSource);
        _sameData = (await saved.ListAsync()).Single().MapChanged;

        // A way still there but split elsewhere: a Segment of it with the same start, another end.
        var split = traversals.Select((t, i) => i == traversals.Count / 3 ? (t.Key with { ToNodeId = t.Key.ToNodeId + 1 }, t.Forward) : t).ToArray();
        var resplit = await saved.SaveAsync("Park loop, split", _planned, split);
        _wayResplit = (await saved.ListAsync()).Single(r => r.Id == resplit).MapChanged;
        await saved.DeleteAsync(resplit);

        // AC2: rebuilt from data without one of its ways.
        var gone = traversals[traversals.Count / 2].Key.OsmWayId;
        var without = Path.Combine(Directory.CreateTempSubdirectory("cutthrough-changed-map-").FullName, "richmond-park-less-one.osm");
        await using (var file = new GZipStream(File.OpenRead(richmond), CompressionMode.Decompress))
        using (var reader = new StreamReader(file))
        {
            var osm = await reader.ReadToEndAsync();
            var less = Regex.Replace(osm, $"<way id=\"{gone}\"[^>]*>.*?</way>", "", RegexOptions.Singleline);
            Assert.That(less, Has.Length.LessThan(osm.Length), "the way is cut out");
            await File.WriteAllTextAsync(without, less);
        }
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, without);
        await NetworkBuild.Build(dataSource);
        _wayGone = (await saved.ListAsync()).Single().MapChanged;
        _openedAfter = (await saved.OpenAsync(id))!.Loop;
        Directory.Delete(Path.GetDirectoryName(without)!, recursive: true);
    }

    [Test]
    public void Just_saved_it_says_nothing() => Assert.That(_justSaved, Is.False);

    //  AC1: rebuilt from the same data, its Segments are all found again.
    [Test]
    public void Rebuilt_from_the_same_data_it_says_nothing() => Assert.That(_sameData, Is.False);

    //  AC2: a way of it gone, it says so.
    [Test]
    public void A_way_of_it_gone_it_says_the_map_has_changed() => Assert.That(_wayGone, Is.True);

    //  A Segment is its way between two nodes: the way split elsewhere is a changed map too.
    [Test]
    public void A_way_of_it_split_elsewhere_it_says_the_map_has_changed() => Assert.That(_wayResplit, Is.True);

    //  AC2: and its line is kept as it was saved.
    [Test]
    public void Its_line_is_kept_as_it_was_saved() => Assert.That(_openedAfter.Line, Is.EqualTo(_planned.Line));
}
