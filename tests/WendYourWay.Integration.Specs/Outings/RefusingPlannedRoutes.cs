using WendYourWay.Data;
using WendYourWay.Data.Outings;

namespace WendYourWay.Integration.Specs.Outings;

/// <summary>
/// A planned route (Trail Router's, say) isn't an Outing: importing one would count ground the
/// user never ran. A recording has a time on every point; a planned route has few or none.
/// </summary>
[TestFixture]
[NonParallelizable]
public class RefusingPlannedRoutes
{
    private string _folder = null!;

    [OneTimeSetUp]
    public void Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        _folder = Directory.CreateTempSubdirectory("cutthrough-routes-").FullName;
    }

    [OneTimeTearDown]
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    //  The invented Run, with its times kept on only every fourth point, or on none.
    private string PlannedRoute(string name, int keepEvery)
    {
        var lines = File.ReadAllLines(Fixtures.Path("pavement-and-park.gpx"));
        var point = 0;
        var kept = lines.Select(line =>
        {
            if (!line.Contains("<trkpt", StringComparison.Ordinal)) return line;
            return point++ % keepEvery == 0 ? line : System.Text.RegularExpressions.Regex.Replace(line, "<time>[^<]*</time>", "");
        });
        var path = Path.Combine(_folder, name);
        File.WriteAllLines(path, kept);
        return path;
    }

    [TestCase(4, Description = "a time on one point in four, as Trail Router's files have")]
    [TestCase(int.MaxValue, Description = "a time on the first point only")]
    public void A_file_whose_points_mostly_have_no_time_is_refused_as_a_planned_route(int keepEvery)
    {
        using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        Assert.That(
            async () => await OutingImporter.ImportGpxAsync(dataSource, PlannedRoute($"route-{keepEvery}.gpx", keepEvery)),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("planned route"));
    }
}
