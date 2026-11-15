using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Planning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace WendYourWay.Web.Specs;

/// <summary>What the app sends a browser that opens it.</summary>
public class ServingThePlanner
{
    private WebApplicationFactory<Program> _app = null!;
    private string _page = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        //  "Home" is a public landmark here, overriding whatever the user's own secrets say.
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Home:Latitude"] = "51.5712",
                ["Home:Longitude"] = "-0.1677",
                //  The app migrates its database as it starts (T056): only ever the test one here.
                ["ConnectionStrings:WendYourWay"] = "Host=localhost;Port=5432;Database=cutthrough_test;Username=cutthrough",
            })));
        using var client = _app.CreateClient();
        _page = await client.GetStringAsync(new Uri("/", UriKind.Relative));
    }

    [OneTimeTearDown]
    public void Dispose() => _app.Dispose();

    [Test]
    public void Then_the_planner_opens_on_a_map() =>
        Assert.That(_page, Does.Contain("class=\"map\""));

    //  Only this laptop can reach the app unless the user opts in to their LAN.
    [Test]
    public void Then_it_listens_only_on_this_laptop()
    {
        var urls = _app.Services.GetRequiredService<IConfiguration>()["Urls"];
        Assert.That(urls, Is.Not.Null.And.Not.Empty);
        Assert.That(urls!.Split(';').Select(u => IPAddress.Parse(new Uri(u).Host)), Has.All.Matches<IPAddress>(a => a is not null && IPAddress.IsLoopback(a)));
    }

    //  T022b: the Off-road networks as map tiles, for the part of the map in view.
    [Test]
    public async Task Then_it_serves_the_Off_road_networks_as_tiles()
    {
        using var client = _app.CreateClient();
        using var response = await client.GetAsync(new Uri("/tiles/offroad/14/8184/5443", UriKind.Relative));
        Assert.That((response.StatusCode, response.Content.Headers.ContentType?.MediaType, await response.Content.ReadAsStringAsync()),
            Is.EqualTo((HttpStatusCode.OK, "application/json", "{\"lines\":[]}")));
    }

    //  T067: the cafés and toilets in view, for the overlay; none in the test database's map data.
    [Test]
    public async Task Then_it_serves_the_cafes_and_toilets_in_view()
    {
        using var client = _app.CreateClient();
        using var response = await client.GetAsync(new Uri("/amenities?south=51.425&west=-0.32&north=51.465&east=-0.23", UriKind.Relative));
        Assert.That((response.StatusCode, response.Content.Headers.ContentType?.MediaType, await response.Content.ReadAsStringAsync()),
            Is.EqualTo((HttpStatusCode.OK, "application/json", "[]")));
    }

    [TestCase("south=51.465&west=-0.32&north=51.425&east=-0.23", Description = "south of north")]
    [TestCase("south=51&west=-1&north=52&east=0", Description = "wider than zoom 11")]
    public async Task Then_a_view_that_s_wrong_or_too_large_is_refused(string query)
    {
        using var client = _app.CreateClient();
        using var response = await client.GetAsync(new Uri("/amenities?" + query, UriKind.Relative));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Then_a_tile_that_doesn_t_exist_is_not_found()
    {
        using var client = _app.CreateClient();
        using var response = await client.GetAsync(new Uri("/tiles/offroad/3/8/0", UriKind.Relative));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    //  A web page elsewhere can't reach the app by pointing its own host name at this laptop
    //  (DNS rebinding): requests for any host but this one are refused.
    [Test]
    public async Task Then_requests_for_another_host_are_refused()
    {
        using var client = _app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/", UriKind.Relative));
        request.Headers.Host = "attacker.example";
        using var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    //  Compared as a yes/no, so a failure can never print anyone's real home.
    [Test]
    public void Then_home_comes_from_configuration() =>
        Assert.That(_app.Services.GetRequiredService<PlannerSettings>().Home?.Latitude == 51.5712, Is.True,
            "home wasn't the configured landmark");

    //  Home reaches the browser only through calls to the map, never written into the page.
    [Test]
    public void Then_the_page_never_carries_home_in_its_markup() =>
        Assert.That(_page, Does.Not.Contain("51.571").And.Not.Contain("0.167"));

    //  If the connection to the app breaks, the page says so rather than freezing silently.
    [Test]
    public void Then_the_page_can_say_when_the_app_has_stopped_responding() =>
        Assert.That(_page, Does.Contain("id=\"blazor-error-ui\""));

    //  The explanation page, and its diagram from the app's own files.
    [Test]
    public async Task Then_it_serves_how_WendYourWay_works_with_its_diagram()
    {
        using var client = _app.CreateClient();
        var page = await client.GetStringAsync(new Uri("/how-it-works", UriKind.Relative));
        using var diagram = await client.GetAsync(new Uri("/img/how-it-works.svg", UriKind.Relative));
        Assert.That((page.Contains("How Wend Your Way works", StringComparison.Ordinal), diagram.StatusCode, diagram.Content.Headers.ContentType?.MediaType),
            Is.EqualTo((true, HttpStatusCode.OK, "image/svg+xml")));
    }
}
