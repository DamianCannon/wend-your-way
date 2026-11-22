using Bunit;
using WendYourWay.Web.Components;
using WendYourWay.Web.Components.Pages;
using WendYourWay.Web.Planning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The map services' API keys in the Admin panel (T169, #173; user: "we need a task for the admin panel
/// where users can manage their API keys in order to know which ones are required and for them to be
/// added, changed and deleted"; kept in the database, the user's choice). A saved key wins over one set
/// outside the app (user-secrets, .env); none is ever shown whole.
/// </summary>
public class ManagingApiKeys
{
    private PlannerScreen _screen = null!;

    [SetUp]
    public void Arrange() => _screen = new PlannerScreen(new FakePlanner());

    [TearDown]
    public void Dispose() => _screen.Dispose();

    private IRenderedComponent<Planner> OpenAdmin()
    {
        var page = _screen.Open();
        page.Find(".rail button[aria-label='Admin']").Click();
        page.WaitForElement(".admin .keys .key");
        return page;
    }

    private static string Said(IRenderedComponent<Planner> page, string key) =>
        page.Find($".admin .keys .key[data-key='{key}'] .state").TextContent.Trim();

    private static void Enter(IRenderedComponent<Planner> page, string key, string value)
    {
        page.Find($".keys .key[data-key='{key}'] button.change").Click();
        page.Find($".keys .key[data-key='{key}'] input.entry").Change(value);
        page.Find($".keys .key[data-key='{key}'] button.save").Click();
    }

    [Test]
    public void Every_key_is_listed_with_whether_its_needed() =>
        Assert.That(OpenAdmin().FindAll(".keys .key").Select(k => (k.QuerySelector(".label")!.TextContent.Trim(), k.QuerySelector(".need")!.TextContent.Trim())),
            Is.EqualTo(new[] { ("Thunderforest", "Recommended"), ("Ordnance Survey", "Optional") }));

    [Test]
    public void Each_says_what_it_gives_and_where_to_get_one()
    {
        var os = OpenAdmin().Find(".keys .key[data-key='OsMaps:ApiKey']");
        Assert.That((os.QuerySelector(".gives")!.TextContent.Contains("OS Outdoor", StringComparison.Ordinal), os.QuerySelector(".gives a")!.GetAttribute("href")),
            Is.EqualTo((true, "https://osdatahub.os.uk/")));
    }

    [Test]
    public void One_not_set_says_so() =>
        Assert.That(Said(OpenAdmin(), "OsMaps:ApiKey"), Is.EqualTo("Not set."));

    [Test]
    public void One_set_outside_the_app_says_so_and_shows_only_its_end()
    {
        _screen.KeysOutside["Thunderforest:ApiKey"] = "tf-outside-key-0123abcd";
        var page = OpenAdmin();
        Assert.That((Said(page, "Thunderforest:ApiKey"), page.Markup.Contains("tf-outside-key", StringComparison.Ordinal)),
            Is.EqualTo(("Set outside the app (user-secrets or .env), ending abcd.", false)));
    }

    [Test]
    public void Added_it_is_saved_and_shown_only_by_its_end()
    {
        var page = OpenAdmin();
        Enter(page, "OsMaps:ApiKey", "os-test-key-98765wxyz");
        page.WaitForAssertion(() => Assert.That((_screen.KeyStore.Keys["OsMaps:ApiKey"], Said(page, "OsMaps:ApiKey"), page.Markup.Contains("os-test-key", StringComparison.Ordinal)),
            Is.EqualTo(("os-test-key-98765wxyz", "Saved here, ending wxyz.", false))));
    }

    //  The saved key never goes back to the page, even into the box that changes it.
    [Test]
    public void Changing_it_starts_from_an_empty_hidden_box()
    {
        _screen.KeyStore.Keys["OsMaps:ApiKey"] = "os-test-key-98765wxyz";
        var page = OpenAdmin();
        page.Find(".keys .key[data-key='OsMaps:ApiKey'] button.change").Click();
        var entry = page.Find(".keys .key[data-key='OsMaps:ApiKey'] input.entry");
        Assert.That((entry.GetAttribute("type"), entry.GetAttribute("value") ?? ""), Is.EqualTo(("password", "")));
    }

    [Test]
    public void Changed_the_new_one_is_saved()
    {
        _screen.KeyStore.Keys["OsMaps:ApiKey"] = "os-test-key-98765wxyz";
        var page = OpenAdmin();
        Enter(page, "OsMaps:ApiKey", "os-new-key-1234qrst");
        page.WaitForAssertion(() => Assert.That(_screen.KeyStore.Keys["OsMaps:ApiKey"], Is.EqualTo("os-new-key-1234qrst")));
    }

    [Test]
    public void An_empty_one_isnt_saved()
    {
        var page = OpenAdmin();
        Enter(page, "OsMaps:ApiKey", "   ");
        Assert.That((_screen.KeyStore.Keys.ContainsKey("OsMaps:ApiKey"), page.Find(".keys .key[data-key='OsMaps:ApiKey'] .problem").TextContent.Trim()),
            Is.EqualTo((false, "Paste the key first.")));
    }

    [Test]
    public void Deleting_asks_first_and_then_it_is_gone()
    {
        _screen.KeyStore.Keys["OsMaps:ApiKey"] = "os-test-key-98765wxyz";
        var page = OpenAdmin();
        page.Find(".keys .key[data-key='OsMaps:ApiKey'] button.delete").Click();
        var asked = _screen.KeyStore.Keys.ContainsKey("OsMaps:ApiKey");
        page.Find(".keys .key[data-key='OsMaps:ApiKey'] .confirm button.yes").Click();
        page.WaitForAssertion(() => Assert.That((asked, _screen.KeyStore.Keys.ContainsKey("OsMaps:ApiKey"), Said(page, "OsMaps:ApiKey")),
            Is.EqualTo((true, false, "Not set."))));
    }

    //  Only a key saved here can be deleted here; one set outside the app is then the one in use.
    [Test]
    public void Deleted_where_one_is_set_outside_that_one_is_used_and_said()
    {
        _screen.KeysOutside["OsMaps:ApiKey"] = "os-outside-key-5555mnop";
        _screen.KeyStore.Keys["OsMaps:ApiKey"] = "os-test-key-98765wxyz";
        var page = OpenAdmin();
        page.Find(".keys .key[data-key='OsMaps:ApiKey'] button.delete").Click();
        page.Find(".keys .key[data-key='OsMaps:ApiKey'] .confirm button.yes").Click();
        page.WaitForAssertion(() => Assert.That(Said(page, "OsMaps:ApiKey"), Is.EqualTo("Set outside the app (user-secrets or .env), ending mnop.")));
    }

    [Test]
    public void One_set_outside_the_app_has_nothing_here_to_delete()
    {
        _screen.KeysOutside["OsMaps:ApiKey"] = "os-outside-key-5555mnop";
        Assert.That(OpenAdmin().FindAll(".keys .key[data-key='OsMaps:ApiKey'] button.delete"), Is.Empty);
    }

    //  OS's plan decides how far OS Outdoor zooms (T167): with the OS key.
    [Test]
    public void OS_s_Premium_plan_is_ticked_with_its_key()
    {
        _screen.KeyStore.Keys["OsMaps:ApiKey"] = "os-test-key-98765wxyz";
        var page = OpenAdmin();
        page.Find(".keys .key[data-key='OsMaps:ApiKey'] input.premium").Change(true);
        page.WaitForAssertion(() => Assert.That(_screen.KeyStore.Keys.GetValueOrDefault("OsMaps:Premium"), Is.EqualTo("true")));
    }

    //  Unticked it's saved as false, so a Premium setting outside the app is turned off too.
    [Test]
    public void Unticked_it_is_off_whatever_is_set_outside()
    {
        _screen.KeysOutside["OsMaps:Premium"] = "true";
        _screen.KeyStore.Keys["OsMaps:ApiKey"] = "os-test-key-98765wxyz";
        var page = OpenAdmin();
        var ticked = page.Find(".keys .key[data-key='OsMaps:ApiKey'] input.premium").HasAttribute("checked");
        page.Find(".keys .key[data-key='OsMaps:ApiKey'] input.premium").Change(false);
        page.WaitForAssertion(() => Assert.That((ticked, _screen.KeyStore.Keys.GetValueOrDefault("OsMaps:Premium")), Is.EqualTo((true, "false"))));
    }

    //  The maps come and go without a restart: a reload of the page draws them.
    [Test]
    public void Saved_the_map_can_be_reloaded_from_here()
    {
        var page = OpenAdmin();
        Enter(page, "OsMaps:ApiKey", "os-test-key-98765wxyz");
        page.WaitForAssertion(() => Assert.That(page.Find(".keys .changed button.reload").TextContent.Trim(), Is.EqualTo("Reload the map")));
    }

    //  Database changes held back as the app started (T149), or no database: the keys outside still work.
    [Test]
    public void When_keys_cant_be_kept_it_says_so_and_nothing_can_be_changed()
    {
        _screen.KeyStore.Broken = true;
        var page = OpenAdmin();
        Assert.That((page.Find(".keys .cant").TextContent.Trim(), page.FindAll(".keys button.change").All(b => b.HasAttribute("disabled"))),
            Is.EqualTo(("Keys can't be saved here until the database is brought up to date. Any set outside the app still work.", true)));
    }
}

/// <summary>The maps drawn with the keys as saved (T169): a saved one wins over one set outside the app.</summary>
public class DrawingTheMapsWithSavedKeys
{
    private static async Task<MapTiles> TilesWith(Dictionary<string, string?> outside, Dictionary<string, string> saved)
    {
        var store = new FakeKeyStore();
        foreach (var (name, value) in saved) store.Keys[name] = value;
        var keys = new MapKeys(store, new ConfigurationBuilder().AddInMemoryCollection(outside).Build(), NullLogger<MapKeys>.Instance);
        await keys.LoadAsync();
        return MapTiles.From(keys.WithSaved());
    }

    [Test]
    public async Task A_saved_key_brings_its_map() =>
        Assert.That((await TilesWith([], new() { ["OsMaps:ApiKey"] = "os-saved" })).Layers.Select(l => l.Name), Does.Contain("OS Outdoor"));

    [Test]
    public async Task A_saved_key_wins_over_one_set_outside() =>
        Assert.That((await TilesWith(new() { ["OsMaps:ApiKey"] = "os-outside" }, new() { ["OsMaps:ApiKey"] = "os-saved" })).Layers.Single(l => l.Name == "OS Outdoor").Url,
            Does.EndWith("key=os-saved"));

    [Test]
    public async Task With_none_saved_those_set_outside_are_used() =>
        Assert.That((await TilesWith(new() { ["OsMaps:ApiKey"] = "os-outside" }, [])).Layers.Single(l => l.Name == "OS Outdoor").Url,
            Does.EndWith("key=os-outside"));
}

/// <summary>The keys kept in memory, as a spec sets them; broken, as if the database couldn't be read.</summary>
internal sealed class FakeKeyStore : IKeyStore
{
    public Dictionary<string, string> Keys { get; } = new(StringComparer.Ordinal);

    public bool Broken { get; set; }

    public Task<IReadOnlyDictionary<string, string>> AllAsync(CancellationToken cancellationToken = default) =>
        Broken ? throw new InvalidOperationException("relation app.api_keys does not exist") : Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Keys));

    public Task SaveAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        Keys[name] = value.Trim();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        Keys.Remove(name);
        return Task.CompletedTask;
    }
}
