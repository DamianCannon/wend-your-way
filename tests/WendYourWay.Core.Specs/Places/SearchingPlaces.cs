using WendYourWay.Core.Places;

namespace WendYourWay.Core.Specs.Places;

/// <summary>
/// Search by name and postcode (PLAN T064): what's typed made a key; postcodes told from names;
/// matches ranked whole name, then its start, then a later word's; then by kind; then nearest the
/// middle of the map. Eight shown.
/// </summary>
public class SearchingPlaces
{
    [TestCase("Box Hill", "box hill")]
    [TestCase("  Ranmore Common, The ", "ranmore common the")]
    [TestCase("Ynys Môn", "ynys mon")]
    [TestCase("St. Albans", "st albans")]
    [TestCase("King's Lynn", "kings lynn")]
    [TestCase("Bishop's-Stortford", "bishops stortford")]
    public void A_name_is_keyed_in_lower_case_without_accents_or_punctuation(string name, string key) =>
        Assert.That(PlaceSearch.Key(name), Is.EqualTo(key));

    [TestCase("SW1A 1AA", "sw1a1aa")]
    [TestCase("sw1a1aa", "sw1a1aa")]
    [TestCase("EN4", "en4")]
    [TestCase("en4 0", "en40")]
    [TestCase("N2", "n2")]
    [TestCase("rh5 6b", "rh56b")]
    public void A_postcode_is_keyed_without_its_space(string text, string key) =>
        Assert.That(PlaceSearch.PostcodeKey(text), Is.EqualTo(key));

    [TestCase("box")]
    [TestCase("ashridge")]
    [TestCase("st albans")]
    [TestCase("a")]
    public void A_name_isn_t_a_postcode(string text) =>
        Assert.That(PlaceSearch.PostcodeKey(text), Is.Null);

    [TestCase("City", PlaceKind.City)]
    [TestCase("Town", PlaceKind.Town)]
    [TestCase("Suburban Area", PlaceKind.Suburb)]
    [TestCase("Other Settlement", PlaceKind.Settlement)]
    [TestCase("Village", PlaceKind.Village)]
    [TestCase("Hamlet", PlaceKind.Hamlet)]
    [TestCase("Woodland Or Forest", PlaceKind.Wood)]
    [TestCase("Urban Greenspace", PlaceKind.Greenspace)]
    [TestCase("Other Landcover", PlaceKind.Landcover)]
    [TestCase("Hill Or Mountain", PlaceKind.Hill)]
    [TestCase("Hill Or Mountain Ranges", PlaceKind.Hill)]
    [TestCase("Valley", PlaceKind.Valley)]
    [TestCase("Inland Water", PlaceKind.Water)]
    [TestCase("Railway Station", PlaceKind.Station)]
    [TestCase("Postcode", PlaceKind.Postcode)]
    public void OS_Open_Names_kinds_kept(string localType, PlaceKind kind) =>
        Assert.That(PlaceSearch.KindOf(localType), Is.EqualTo(kind));

    [TestCase("Named Road")]
    [TestCase("Section Of Named Road")]
    [TestCase("Primary Education")]
    [TestCase("Further Education,Secondary Education")]
    [TestCase("Hospital")]
    [TestCase("Bay")]
    public void OS_Open_Names_kinds_left_out(string localType) =>
        Assert.That(PlaceSearch.KindOf(localType), Is.Null);

    [TestCase(PlaceKind.City, 12)]
    [TestCase(PlaceKind.Town, 13)]
    [TestCase(PlaceKind.Suburb, 14)]
    [TestCase(PlaceKind.Village, 14)]
    [TestCase(PlaceKind.Park, 14)]
    [TestCase(PlaceKind.District, 14)]
    [TestCase(PlaceKind.Hamlet, 15)]
    [TestCase(PlaceKind.Station, 15)]
    [TestCase(PlaceKind.Postcode, 16)]
    public void Each_kind_is_shown_at_its_own_zoom(PlaceKind kind, int zoom) =>
        Assert.That(PlaceSearch.ZoomFor(kind), Is.EqualTo(zoom));

    private static PlaceCandidate Place(int id, string name, PlaceKind kind = PlaceKind.Village, double fromMiddleKm = 10) =>
        new(id, PlaceSearch.Key(name), kind, fromMiddleKm * 1000);

    private static int[] Ranked(string typed, params PlaceCandidate[] candidates) =>
        [.. PlaceSearch.Rank(candidates, PlaceSearch.Key(typed)).Select(c => c.Id)];

    [Test]
    public void The_whole_name_comes_before_its_start() =>
        Assert.That(Ranked("box hill", Place(1, "Box Hill Wood", PlaceKind.City), Place(2, "Box Hill")), Is.EqualTo(new[] { 2, 1 }));

    [Test]
    public void Its_start_comes_before_a_later_word() =>
        Assert.That(Ranked("epping", Place(1, "North Epping", PlaceKind.City), Place(2, "Epping Forest", PlaceKind.Wood)), Is.EqualTo(new[] { 2, 1 }));

    [Test]
    public void A_name_that_doesn_t_match_is_left_out() =>
        Assert.That(Ranked("box", Place(1, "Ashbox"), Place(2, "Box")), Is.EqualTo(new[] { 2 }));

    [Test]
    public void Then_a_town_before_a_village_before_a_hamlet() =>
        Assert.That(Ranked("new", Place(1, "Newton", PlaceKind.Hamlet), Place(2, "Newport", PlaceKind.Town), Place(3, "Newland")),
            Is.EqualTo(new[] { 2, 3, 1 }));

    [Test]
    public void A_green_area_before_a_hamlet() =>
        Assert.That(Ranked("trent", Place(1, "Trent", PlaceKind.Hamlet), Place(2, "Trent", PlaceKind.Park)), Is.EqualTo(new[] { 2, 1 }));

    //  "richmond park" from north London went to a suburb of that name by Rotherham, 230 km off,
    //  over the park 15 km away (browser, 2026-11-03): a suburb, village, green area, hill, water
    //  or station of a name rank together, nearest first.
    [Test]
    public void A_park_near_comes_before_a_suburb_far_off() =>
        Assert.That(Ranked("richmond park", Place(1, "Richmond Park", PlaceKind.Suburb, 230), Place(2, "Richmond Park", PlaceKind.Park, 15)), Is.EqualTo(new[] { 2, 1 }));

    [Test]
    public void A_town_far_off_still_comes_before_a_village_near() =>
        Assert.That(Ranked("newport", Place(1, "Newport", PlaceKind.Village, 60), Place(2, "Newport", PlaceKind.City, 200)), Is.EqualTo(new[] { 2, 1 }));

    [Test]
    public void Then_the_nearest_the_middle_of_the_map() =>
        Assert.That(Ranked("newport", Place(1, "Newport", PlaceKind.Town, 200), Place(2, "Newport", PlaceKind.Town, 30)), Is.EqualTo(new[] { 2, 1 }));

    [Test]
    public void A_postcode_district_before_its_postcodes() =>
        Assert.That(Ranked("rh5", Place(1, "RH5 6BY", PlaceKind.Postcode, 1), Place(2, "RH5", PlaceKind.District, 5)), Is.EqualTo(new[] { 2, 1 }));

    [Test]
    public void Eight_are_shown_at_most() =>
        Assert.That(Ranked("a", [.. Enumerable.Range(1, 12).Select(i => Place(i, $"A{i}"))]), Has.Length.EqualTo(8));
}
