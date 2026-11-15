using WendYourWay.Cli;

namespace WendYourWay.Integration.Specs.Cli;

/// <summary>
/// `wend byways get` (#146, T142, STORY-046): the highway authorities' Definitive Map byways, for Trail
/// Rides, from rowmaps.com, as scripts/get-rights-of-way.ps1 got them, now on any machine (a Docker
/// container included): each authority's into a folder of its code; the boundaries and names of those
/// whose Definitive Map isn't open into without-data. Against a stand-in for rowmaps.com.
/// </summary>
[TestFixture]
public class GettingTheByways
{
    private StubSite _site = null!;
    private string _folder = null!;

    [SetUp]
    public void Arrange()
    {
        _site = new StubSite();
        _site.Pages["/jsons/"] = """<a href="AB/">AB/</a> <a href="CD/">CD/</a> <a href="../">..</a>""";
        _site.Pages["/jsons/AB/mutated4.json"] = """{"type":"FeatureCollection","features":[]}""";
        _site.Pages["/jsons/CD/mutated4.json"] = """{"type":"FeatureCollection","features":[{"x":1}]}""";
        _site.Pages["/missing/"] = "<table><tr><td>1</td><td>EF</td><td>East Fen District Council</td><td>KML giving the boundary</td></tr></table>";
        _site.Pages["/realbounds/EF.kml"] = "<kml>East Fen</kml>";
        _folder = Path.Combine(Directory.CreateTempSubdirectory("wend-byways-").FullName, "rights-of-way");
    }

    [TearDown]
    public void Dispose()
    {
        _site.Dispose();
        Directory.Delete(Path.GetDirectoryName(_folder)!, recursive: true);
    }

    private async Task<(int ExitCode, string Output)> Get()
    {
        using var output = new StringWriter();
        var exitCode = await CommandLine.Run(["byways", "get", "--folder", _folder, "--source", _site.Url, "--pause", "0"], output);
        return (exitCode, output.ToString());
    }

    [Test]
    public async Task Each_authoritys_byways_go_in_a_folder_of_its_code()
    {
        var (exitCode, output) = await Get();
        Assert.That((exitCode, File.ReadAllText(Path.Combine(_folder, "CD", "mutated4.json"))), Is.EqualTo((0, _site.Pages["/jsons/CD/mutated4.json"])), output);
    }

    [Test]
    public async Task Those_without_open_data_are_named_with_their_boundaries()
    {
        await Get();
        Assert.That((File.ReadAllText(Path.Combine(_folder, "without-data", "names.tsv")), File.Exists(Path.Combine(_folder, "without-data", "EF.kml"))),
            Is.EqualTo(("EF\tEast Fen District Council", true)));
    }

    [Test]
    public async Task Got_again_one_already_here_is_skipped()
    {
        await Get();
        _site.Asked.Clear();
        var (_, output) = await Get();
        Assert.That((_site.Asked.Contains("GET /jsons/AB/mutated4.json"), output), Is.EqualTo((false, output)));
        Assert.That(output, Does.Contain("2 authorities; 0 downloaded, the rest already here"));
    }

    [Test]
    public async Task A_page_changed_beyond_reading_is_said()
    {
        _site.Pages["/jsons/"] = "<p>We've moved.</p>";
        var (exitCode, output) = await Get();
        Assert.That((exitCode, output.Trim()), Is.EqualTo((1, $"No authorities found at {_site.Url}/jsons/: has the page changed?")));
    }
}
