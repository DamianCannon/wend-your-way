using Bunit;
using WendYourWay.Core.Loops;
using WendYourWay.Core.Places;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Web.Components;
using WendYourWay.Web.Components.Pages;
using WendYourWay.Data.Planning;
using WendYourWay.Data.Places;
using WendYourWay.Web.Planning;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace WendYourWay.Web.Specs;

/// <summary>The planner page (T119, from AskingForALoop): exporting a route: GPX, TCX for a watch, and to the phone by a QR code, named after the route.</summary>
public class ExportingARoute : PlannerPageSpecs
{
    // The name Save offers the Loop shown (T112: what it's exported as), the save box closed again.
    private static string OfferedName(IRenderedComponent<Planner> screen)
    {
        screen.Find(".pager .save").Click();
        var name = screen.Find(".pager .save-name").GetAttribute("value")!;
        screen.Find(".pager form.saving .cancel").Click();
        return name;
    }

    //  T100: the exported route's turns, from its Segments, in the GPX and in a TCX for a watch.
    private static readonly RouteTurn[] SomeTurns = [new(ParliamentHill, 2100, WendYourWay.Core.Loops.TurnKind.Left, "Left onto the footpath")];

    //  T139 (user: "when generating gpx/tcx files or creating the QR code there's a short delay with
    //  no feedback"): the button pressed says so at once, and none of the three can be pressed again
    //  till it's done.
    private static string Bar(IRenderedComponent<Planner> screen) =>
        string.Join(" | ", screen.FindAll(".pager .bar button.phone, .pager .bar button.export").Select(b => $"{b.TextContent.Trim()}{(b.GetAttribute("aria-disabled") == "true" ? " (off)" : "")}"));

    [TestCase("button.export:not(.export-tcx)", "QR (off) | Preparing… (off) | TCX (off)")]
    [TestCase("button.export-tcx", "QR (off) | GPX (off) | Preparing… (off)")]
    [TestCase("button.phone", "Preparing… (off) | GPX (off) | TCX (off)")]
    public void While_it_s_made_the_button_says_so(string button, string bar)
    {
        var screen = OfferThree();
        _turns.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        screen.Find($".pager {button}").Click();
        screen.WaitForAssertion(() => Assert.That(Bar(screen), Is.EqualTo(bar)));
    }

    [Test]
    public void Made_the_buttons_are_as_they_were()
    {
        var screen = OfferThree();
        _turns.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        screen.Find(".pager button.phone").Click();
        screen.WaitForAssertion(() => Assert.That(Bar(screen), Does.Contain("Preparing…")));
        _turns.Hold.SetResult();
        screen.WaitForAssertion(() => Assert.That(Bar(screen), Is.EqualTo("QR | GPX | TCX")));
    }

    //  From the review (T139): GPX and TCX clear too; a second press while one's made starts
    //  nothing; a screen reader is told.
    [TestCase("button.export:not(.export-tcx)")]
    [TestCase("button.export-tcx")]
    public void A_download_made_the_buttons_are_as_they_were(string button)
    {
        _context.JSInterop.SetupModule("./js/download.js").Mode = JSRuntimeMode.Loose;
        var screen = OfferThree();
        _turns.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        screen.Find($".pager {button}").Click();
        screen.WaitForAssertion(() => Assert.That(Bar(screen), Does.Contain("Preparing…")));
        _turns.Hold.SetResult();
        screen.WaitForAssertion(() => Assert.That(Bar(screen), Is.EqualTo("QR | GPX | TCX")));
    }

    [Test]
    public void A_second_press_while_one_is_made_starts_nothing()
    {
        var screen = OfferThree();
        _turns.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        screen.Find(".pager button.phone").Click();
        screen.WaitForAssertion(() => Assert.That(Bar(screen), Does.Contain("Preparing…")));
        screen.Find(".pager button.export-tcx").Click();
        _turns.Hold.SetResult();
        screen.WaitForAssertion(() => Assert.That(Bar(screen), Is.EqualTo("QR | GPX | TCX")));
        Assert.That(_turns.Asked, Has.Count.EqualTo(1));
    }

    [Test]
    public void A_screen_reader_is_told()
    {
        var screen = OfferThree();
        _turns.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        screen.Find(".pager button.export:not(.export-tcx)").Click();
        screen.WaitForAssertion(() => Assert.That(screen.Find(".pager [role=status]").TextContent, Is.EqualTo("Preparing the GPX…")));
    }

    //  T099: "To phone" on the Loop card sends the Loop shown, as GPX, to the phone: a QR code of
    //  its link, the link written beneath, how long it lasts and where it's sent from.
    [Test]
    public void To_phone_sends_the_Loop_shown_as_GPX()
    {
        var screen = OfferThree();
        screen.Find(".pager .page[aria-label='Next Loop']").Click();
        screen.Find(".pager button.phone").Click();
        screen.WaitForAssertion(() => Assert.That(_phone.Shared, Is.EqualTo(new[] { LoopGpx.Of(Second, name: OfferedName(screen)) })));
    }

    [Test]
    public void To_phone_shows_a_QR_code_of_its_link()
    {
        var screen = OfferThree();
        screen.Find(".pager button.phone").Click();
        var phone = screen.WaitForElement(".pager .to-phone");
        //  An image, not markup in the card: the card's own icon styles mustn't touch its squares.
        Assert.That((phone.QuerySelector("img.qr")?.GetAttribute("src")?.StartsWith("data:image/png;base64,", StringComparison.Ordinal), phone.QuerySelector(".link")!.TextContent.Trim()),
            Is.EqualTo(((bool?)true, FakePhoneShare.Url.ToString())));
    }

    //  User, 2026-11-10: "have a countdown from ten minutes with a label saying Scan link valid for xx:xx".
    [Test]
    public void To_phone_counts_down_how_long_its_link_lasts()
    {
        var screen = OfferThree();
        screen.Find(".pager button.phone").Click();
        Assert.That(screen.WaitForElement(".pager .to-phone .terms").TextContent.Trim(), Does.Match(@"^Scan link valid for (10:00|09:5\d)$"));
    }

    [Test]
    public void A_link_run_out_says_so()
    {
        _phone.Lasts = TimeSpan.FromMinutes(-1);
        var screen = OfferThree();
        screen.Find(".pager button.phone").Click();
        Assert.That(screen.WaitForElement(".pager .to-phone .terms").TextContent.Trim(), Is.EqualTo("Link expired: press QR again"));
    }

    //  Like Export GPX's, with QR in place of GPX (user, 2026-11-10: "phone icon is massive").
    [Test]
    public void The_button_says_QR() =>
        Assert.That(OfferThree().Find(".pager button.phone").TextContent.Trim(), Is.EqualTo("QR"));

    [Test]
    public void With_no_home_network_To_phone_says_so()
    {
        _phone.NoNetwork = true;
        var screen = OfferThree();
        screen.Find(".pager button.phone").Click();
        Assert.That(screen.WaitForElement(".pager .to-phone").TextContent, Does.Contain("Wi-Fi").And.Not.Contain("http://"));
    }

    [Test]
    public void Another_Loop_shown_puts_the_code_away()
    {
        var screen = OfferThree();
        screen.Find(".pager button.phone").Click();
        screen.WaitForElement(".pager .to-phone");
        screen.Find(".pager .page[aria-label='Next Loop']").Click();
        screen.WaitForAssertion(() => Assert.That(screen.FindAll(".pager .to-phone"), Is.Empty));
    }

    [Test]
    public void With_no_words_the_card_says_none()
    {
        var screen = OfferThree();
        screen.Find(".pager .expand").Click();
        Assert.That(screen.FindAll(".pager .story"), Is.Empty);
    }

    [Test]
    public void Export_GPX_puts_the_Loop_s_turns_in()
    {
        var download = _context.JSInterop.SetupModule("./js/download.js");
        download.Mode = JSRuntimeMode.Loose;
        _turns.Turns = SomeTurns;
        var screen = OfferThree();
        screen.WaitForElement(".pager button.export").Click();
        screen.WaitForAssertion(() => Assert.That(download.Invocations["saveFile"].Single().Arguments[2], Is.EqualTo(LoopGpx.Of(Planned, SomeTurns, OfferedName(screen)).Content)));
        Assert.That(_turns.Asked.Single(), Is.EqualTo(FakeOffer.Traversals));
    }

    //  In the bar beside QR and GPX, another download (user, 2026-11-10): the card needn't be opened.
    [Test]
    public void TCX_for_a_watch_exports_the_Loop_as_a_course_with_its_turns()
    {
        var download = _context.JSInterop.SetupModule("./js/download.js");
        download.Mode = JSRuntimeMode.Loose;
        _turns.Turns = SomeTurns;
        var screen = OfferThree();
        screen.WaitForElement(".pager button.export-tcx").Click();
        var tcx = LoopTcx.Of(Planned, SomeTurns, OfferedName(screen));
        screen.WaitForAssertion(() => Assert.That(download.Invocations["saveFile"].Single().Arguments, Is.EqualTo(new object?[] { tcx.FileName, "application/vnd.garmin.tcx+xml", tcx.Content })));
    }

    [Test]
    public void To_phone_sends_the_Loop_s_turns_too()
    {
        _turns.Turns = SomeTurns;
        var screen = OfferThree();
        screen.Find(".pager button.phone").Click();
        screen.WaitForAssertion(() => Assert.That(_phone.Shared, Is.EqualTo(new[] { LoopGpx.Of(Planned, SomeTurns, OfferedName(screen)) })));
    }

    //  T112 (user: "Its own name, else place"): exported by the name Save offers, a file named from it.
    [Test]
    public void Export_GPX_names_a_new_Loop_by_its_place_and_length()
    {
        var download = _context.JSInterop.SetupModule("./js/download.js");
        download.Mode = JSRuntimeMode.Loose;
        var screen = OfferThree();
        var offered = OfferedName(screen);
        screen.Find(".pager button.export").Click();
        screen.WaitForAssertion(() => Assert.That((offered.EndsWith(" · 9.7 km", StringComparison.Ordinal), download.Invocations["saveFile"].Single().Arguments[0]),
            Is.EqualTo((true, (object?)ExportNames.FileName(offered, "gpx")))));
    }

    [Test]
    public void Saved_by_a_name_it_is_exported_by_it()
    {
        var download = _context.JSInterop.SetupModule("./js/download.js");
        download.Mode = JSRuntimeMode.Loose;
        var screen = OfferThree();
        SaveAs(screen, "Heath wiggle");
        screen.WaitForAssertion(() => Assert.That(_saved.Kept, Has.Count.EqualTo(1)));
        screen.Find(".pager button.export").Click();
        screen.WaitForAssertion(() => Assert.That(download.Invocations["saveFile"].Single().Arguments[0], Is.EqualTo("heath-wiggle.gpx")));
    }

    [Test]
    public void The_name_saved_by_doesnt_follow_to_another_Loop()
    {
        var download = _context.JSInterop.SetupModule("./js/download.js");
        download.Mode = JSRuntimeMode.Loose;
        var screen = OfferThree();
        SaveAs(screen, "Heath wiggle");
        screen.WaitForAssertion(() => Assert.That(_saved.Kept, Has.Count.EqualTo(1)));
        screen.Find(".pager .page[aria-label='Next Loop']").Click();
        screen.Find(".pager button.export").Click();
        screen.WaitForAssertion(() => Assert.That(download.Invocations["saveFile"].Single().Arguments[0], Is.Not.EqualTo("heath-wiggle.gpx")));
    }

    [Test]
    public void A_Saved_Route_opened_is_exported_by_its_name()
    {
        var download = _context.JSInterop.SetupModule("./js/download.js");
        download.Mode = JSRuntimeMode.Loose;
        _saved.SaveAsync("Sunday hills", Planned, FakeOffer.Traversals).GetAwaiter().GetResult();
        var screen = OfferThree();
        OpenSavedRoutes(screen);
        screen.WaitForElement(".saved-routes li .name").Click();
        screen.WaitForElement(".pager button.export").Click();
        screen.WaitForAssertion(() => Assert.That(download.Invocations["saveFile"].Single().Arguments[0], Is.EqualTo("sunday-hills.gpx")));
    }

    [Test]
    public void Export_GPX_exports_the_Loop_shown()   // T052
    {
        var download = _context.JSInterop.SetupModule("./js/download.js");
        download.Mode = JSRuntimeMode.Loose;
        var screen = OfferThree();
        screen.Find(".pager .page[aria-label='Next Loop']").Click();
        screen.WaitForElement(".pager button.export").Click();

        var gpx = LoopGpx.Of(Second, name: OfferedName(screen));
        screen.WaitForAssertion(() => Assert.That(download.Invocations["saveFile"].Single().Arguments[2], Is.EqualTo(gpx.Content)));
    }

    [Test]
    public void Export_GPX_hands_the_browser_the_Loop_as_a_GPX_file()   // STORY-029
    {
        var download = _context.JSInterop.SetupModule("./js/download.js");
        download.Mode = JSRuntimeMode.Loose;
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForElement(".pager button.export").Click();

        var gpx = LoopGpx.Of(Planned, name: OfferedName(screen));
        screen.WaitForAssertion(() => Assert.That(download.Invocations["saveFile"].Single().Arguments,
            Is.EqualTo(new object[] { gpx.FileName, "application/gpx+xml", gpx.Content })));
    }

    [Test]
    public void Before_a_Loop_there_is_nothing_to_export() =>
        Assert.That(Open(KenwoodHouse).FindAll(".pager button.export"), Is.Empty);
}
