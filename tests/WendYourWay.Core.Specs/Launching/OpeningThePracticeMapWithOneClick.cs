using WendYourWay.Launching;

namespace WendYourWay.Core.Specs.Launching;

/// <summary>
/// The practice map from its own shortcut (T161, #165; user chose a second launcher shortcut): what
/// the launcher gives the app and `wend` for it, so it opens beside the usual window, not onto it.
/// </summary>
public class OpeningThePracticeMapWithOneClick
{
    [Test]
    public void The_app_is_told_it_s_the_practice_map_at_an_address_of_its_own() =>
        Assert.That(Practice.AppArguments([]), Is.EqualTo(new[] { "--Practice=true", "--urls", "http://127.0.0.1:5020" }));

    //  Beside the app on your own map (T163): an address given by hand is that one's, not the practice map's.
    [TestCase("--urls", "http://127.0.0.1:5019")]
    [TestCase("--urls=http://127.0.0.1:5019")]
    public void An_address_given_by_hand_is_the_app_s_own_not_the_practice_map_s(params string[] given) =>
        Assert.That(Practice.AppArguments([.. given, "--Home:Latitude=51.5"]), Is.EqualTo(new[] { "--Home:Latitude=51.5", "--Practice=true", "--urls", "http://127.0.0.1:5020" }));

    [Test]
    public void Its_address_is_the_one_it_s_opened_at() =>
        Assert.That(Launch.Address(Practice.AppArguments([])), Is.EqualTo(new Uri("http://127.0.0.1:5020/")));

    //  Built first if it's needed, by `wend practice`, beside the database the app is given, if any.
    [Test]
    public void It_s_built_first_beside_the_usual_database() =>
        Assert.That(Practice.WendArguments([]), Is.EqualTo(new[] { "practice" }));

    [Test]
    public void Or_beside_the_database_given_by_hand() =>
        Assert.That(Practice.WendArguments(["--urls", "http://127.0.0.1:5021", "--ConnectionStrings:WendYourWay=Host=localhost;Database=cutthrough_london"]),
            Is.EqualTo(new[] { "practice", "--database", "Host=localhost;Database=cutthrough_london" }));

    //  Its own window, beside the usual one: clicked again, it's the practice window brought forward.
    [Test]
    public void It_s_opened_once_by_a_name_of_its_own() =>
        Assert.That(Practice.Once(practising: true), Is.Not.EqualTo(Practice.Once(practising: false)));
}

/// <summary>The page asking the window to switch the map (T163, #167): what the launcher takes from it.</summary>
public class SwitchingTheMapInTheWindow
{
    [TestCase("""{"switchTo":"practice"}""", "practice")]
    [TestCase("""{"switchTo":"own"}""", "own")]
    public void A_switch_says_where_to(string message, string to) =>
        Assert.That(MapSwitch.Parse(message), Is.EqualTo(to));

    [TestCase("""{"switchTo":"somewhere"}""")]
    [TestCase("""{"other":"practice"}""")]
    [TestCase("""["practice"]""")]
    [TestCase("""{"switchTo":1}""")]
    [TestCase("practice")]
    [TestCase("")]
    [TestCase(null)]
    public void Anything_else_isn_t_one(string? message) =>
        Assert.That(MapSwitch.Parse(message), Is.Null);
}
