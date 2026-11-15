using WendYourWay.Core.Places;
using WendYourWay.Data;
using WendYourWay.Data.Places;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// Search by name and postcode (PLAN T064) on the real places: OS Open Names for England and Wales
/// and the map's own green areas. Needs `wend places import` run first. The map's middle is Kenwood
/// House, a public place; only public places and postcodes are searched for.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class SearchingPlacesOnTheRealMap
{
    private static readonly LatLon KenwoodHouse = new(51.5712, -0.1677);

    private NpgsqlDataSource _dataSource = null!;
    private PostgisPlaceSearch _search = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        _dataSource = await RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.London);
        _search = new PostgisPlaceSearch(_dataSource);
        //  Warm, as the app is after its first search: the first two-letter search after the
        //  database opens reads its pages from disk (125–150 ms; 3 ms after).
        await _search.SearchAsync("warm", KenwoodHouse);
        await _search.SearchAsync("wa", KenwoodHouse);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource?.Dispose();

    [TestCase("Trent Park")]
    [TestCase("Box Hill")]
    [TestCase("Ashridge")]
    [TestCase("SW1A 1AA")]
    [TestCase("EN4")]
    public async Task Then_each_comes_first(string name) =>
        Assert.That((await _search.SearchAsync(name.ToLowerInvariant(), KenwoodHouse))[0].Name, Is.EqualTo(name));

    [TestCase("tr")]
    [TestCase("tre")]
    [TestCase("forest")]
    [TestCase("ashridge")]
    [TestCase("sw1a 1aa")]
    [TestCase("n2")]
    public async Task Then_it_answers_within_100_ms(string text)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var found = await _search.SearchAsync(text, KenwoodHouse);
        var took = clock.Elapsed;
        Assert.Multiple(() =>
        {
            Assert.That(took, Is.LessThan(TimeSpan.FromMilliseconds(100)));
            Assert.That(found, Is.Not.Empty);
        });
    }

    [Test]
    public async Task Then_a_postcode_is_shown_close_in() =>
        Assert.That((await _search.SearchAsync("sw1a 1aa", KenwoodHouse))[0].Zoom, Is.EqualTo(PlaceSearch.ZoomFor(PlaceKind.Postcode)));
}
