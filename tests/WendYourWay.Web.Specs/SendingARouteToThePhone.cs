using System.Net;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Phone;

namespace WendYourWay.Web.Specs;

/// <summary>
/// A route to the phone (PLAN T099; user, 2026-11-10: "QR code over home Wi-Fi"): the app stays
/// the laptop's, and a listener of its own serves one file, at a random link, once, for ten
/// minutes. Here it listens on loopback only: no spec ever listens on the home network.
/// </summary>
[NonParallelizable]
public class SendingARouteToThePhone
{
    private static readonly GpxFile Gpx = new("wend-your-way-9.4km.gpx", "<gpx>a made-up route</gpx>");

    private PhoneShare _share = null!;
    private HttpClient _http = null!;

    [SetUp]
    public void Arrange()
    {
        _share = new PhoneShare(() => IPAddress.Loopback);
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    [TearDown]
    public async Task Dispose()
    {
        _http.Dispose();
        await _share.DisposeAsync();
    }

    private async Task<HttpStatusCode?> StatusOf(Uri url)
    {
        try
        {
            using var response = await _http.GetAsync(url);
            return response.StatusCode;
        }
        catch (HttpRequestException)
        {
            return null;   // no one listening: as good as not found
        }
    }

    [Test]
    public async Task Its_link_downloads_the_route()
    {
        var link = (await _share.ShareAsync(Gpx))!;
        using var response = await _http.GetAsync(link.Url);
        Assert.That((response.StatusCode, await response.Content.ReadAsStringAsync()), Is.EqualTo((HttpStatusCode.OK, Gpx.Content)));
    }

    //  As the phone asks for it, by the laptop's address on the Wi-Fi, not the app's own names
    //  (the app allows only localhost and 127.0.0.1: the user's phone was turned away, 2026-11-10).
    [Test]
    public async Task It_answers_to_the_home_network_s_address()
    {
        var link = (await _share.ShareAsync(Gpx))!;
        using var request = new HttpRequestMessage(HttpMethod.Get, link.Url);
        request.Headers.Host = $"192.168.1.77:{link.Url.Port}";
        using var response = await _http.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task It_downloads_as_a_GPX_file_by_its_name()
    {
        var link = (await _share.ShareAsync(Gpx))!;
        using var response = await _http.GetAsync(link.Url);
        Assert.That((response.Content.Headers.ContentType?.MediaType, response.Content.Headers.ContentDisposition?.DispositionType, response.Content.Headers.ContentDisposition?.FileName?.Trim('"')),
            Is.EqualTo(("application/gpx+xml", "attachment", Gpx.FileName)));
    }

    [Test]
    public async Task It_is_kept_by_nothing_on_the_way_and_says_nothing_of_itself()
    {
        var link = (await _share.ShareAsync(Gpx))!;
        using var response = await _http.GetAsync(link.Url);
        Assert.That((response.Headers.CacheControl?.NoStore, response.Headers.Contains("X-Content-Type-Options"), response.Headers.Contains("Server")), Is.EqualTo((true, true, false)));
    }

    [Test]
    public async Task It_downloads_once()
    {
        var link = (await _share.ShareAsync(Gpx))!;
        (await _http.GetAsync(link.Url)).Dispose();
        Assert.That(await StatusOf(link.Url), Is.Not.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task A_wrong_token_is_not_found()
    {
        var link = (await _share.ShareAsync(Gpx))!;
        var wrong = new Uri(link.Url, "/AAAAAAAAAAAAAAAAAAAAAA.gpx");
        Assert.That(await StatusOf(wrong), Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Nothing_else_is_there()
    {
        var link = (await _share.ShareAsync(Gpx))!;
        Assert.That(await StatusOf(new Uri(link.Url, "/")), Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task A_wrong_token_doesnt_use_up_the_link()
    {
        var link = (await _share.ShareAsync(Gpx))!;
        await StatusOf(new Uri(link.Url, "/AAAAAAAAAAAAAAAAAAAAAA.gpx"));
        Assert.That(await StatusOf(link.Url), Is.EqualTo(HttpStatusCode.OK));
    }

    //  128 random bits, 22 characters of URL-safe base 64: never the same twice.
    [Test]
    public async Task Its_token_is_128_random_bits()
    {
        var first = (await _share.ShareAsync(Gpx))!.Url.AbsolutePath.Trim('/').Replace(".gpx", "", StringComparison.Ordinal);
        var second = (await _share.ShareAsync(Gpx))!.Url.AbsolutePath.Trim('/').Replace(".gpx", "", StringComparison.Ordinal);
        Assert.That((first.Length, Convert.FromBase64String(first.Replace('-', '+').Replace('_', '/') + "==").Length, first == second), Is.EqualTo((22, 16, false)));
    }

    [Test]
    public async Task A_new_share_ends_the_one_before()
    {
        var before = (await _share.ShareAsync(Gpx))!;
        await _share.ShareAsync(Gpx);
        Assert.That(await StatusOf(before.Url), Is.Not.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task It_lasts_as_long_as_it_says()
    {
        await using var brief = new PhoneShare(() => IPAddress.Loopback, TimeSpan.FromMilliseconds(300));
        var link = (await brief.ShareAsync(Gpx))!;
        await Task.Delay(1500);
        Assert.That(await StatusOf(link.Url), Is.Not.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task It_says_how_long_it_lasts()
    {
        var link = (await _share.ShareAsync(Gpx))!;
        Assert.That(link.Until - DateTimeOffset.Now, Is.EqualTo(TimeSpan.FromMinutes(10)).Within(TimeSpan.FromSeconds(30)));
    }

    [Test]
    public async Task With_no_home_network_it_shares_nothing()
    {
        await using var nowhere = new PhoneShare(() => null);
        Assert.That(await nowhere.ShareAsync(Gpx), Is.Null);
    }

    [Test]
    public async Task Closed_with_the_app_its_link_is_gone()
    {
        var link = (await _share.ShareAsync(Gpx))!;
        await _share.DisposeAsync();
        Assert.That(await StatusOf(link.Url), Is.Not.EqualTo(HttpStatusCode.OK));
    }
}

/// <summary>
/// Which of the laptop's addresses the phone can reach (T099): an adapter that's up, not a virtual
/// one (Hyper-V, WSL, Docker), with a default gateway, at a private address; never a public one.
/// </summary>
public class ChoosingTheHomeNetworkAddress
{
    private static NetworkCard Card(string name, string address, bool up = true, bool isVirtual = false, bool gateway = true) =>
        new(name, up, isVirtual, gateway, [IPAddress.Parse(address)]);

    [Test]
    public void The_Wi_Fi_adapter_s_private_address_is_chosen() =>
        Assert.That(LanAddress.Choose([Card("Hyper-V", "172.20.16.1", isVirtual: true, gateway: false), Card("Wi-Fi", "192.168.1.77")]), Is.EqualTo(IPAddress.Parse("192.168.1.77")));

    [TestCase("10.0.0.5")]
    [TestCase("172.16.0.5")]
    [TestCase("172.31.255.5")]
    [TestCase("192.168.0.5")]
    public void Any_private_range_will_do(string address) =>
        Assert.That(LanAddress.Choose([Card("Ethernet", address)]), Is.EqualTo(IPAddress.Parse(address)));

    [TestCase("81.2.69.160")]
    [TestCase("172.32.0.5")]
    [TestCase("169.254.10.10")]
    [TestCase("127.0.0.1")]
    public void A_public_link_local_or_loopback_address_never_is(string address) =>
        Assert.That(LanAddress.Choose([Card("Ethernet", address)]), Is.Null);

    [Test]
    public void A_virtual_adapter_isn_t_chosen() =>
        Assert.That(LanAddress.Choose([Card("vEthernet (WSL)", "172.25.0.1", isVirtual: true)]), Is.Null);

    [Test]
    public void One_that_s_down_isn_t() =>
        Assert.That(LanAddress.Choose([Card("Wi-Fi", "192.168.1.77", up: false)]), Is.Null);

    [Test]
    public void One_with_no_way_out_isn_t() =>
        Assert.That(LanAddress.Choose([Card("Ethernet", "192.168.56.1", gateway: false)]), Is.Null);
}
