using Bunit;
using WendYourWay.Web.Components.Pages;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The app explains itself (user, 2026-10-19): the parts, how they connect, what it's built on,
/// and a way back to the map.
/// </summary>
public class ReadingHowItWorks
{
    private BunitContext _context = null!;

    [SetUp]
    public void Arrange() => _context = new BunitContext();

    [TearDown]
    public void Dispose() => _context.Dispose();

    [Test]
    public void It_shows_how_the_parts_connect()
    {
        var page = _context.Render<HowItWorks>();
        Assert.That((page.Find("h1").TextContent, page.Find("img.diagram").GetAttribute("src")),
            Is.EqualTo(("How Wend Your Way works", "img/how-it-works.svg")));
    }

    [Test]
    public void It_names_what_WendYourWay_is_built_on() =>
        Assert.That(_context.Render<HowItWorks>().FindAll("table td:first-child").Select(td => td.TextContent),
            Does.Contain("PostgreSQL 17 + PostGIS 3.6").And.Contain("osm2pgsql 2.3").And.Contain("Leaflet 1.9 + Thunderforest"));

    [Test]
    public void It_leads_back_to_the_map() =>
        Assert.That(_context.Render<HowItWorks>().Find("a.back").GetAttribute("href"), Is.EqualTo("./"));
}
