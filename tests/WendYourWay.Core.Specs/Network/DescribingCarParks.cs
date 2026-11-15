using WendYourWay.Core.Network;

namespace WendYourWay.Core.Specs.Network;

/// <summary>
/// Car park details (PLAN T066): its fee, spaces, hours as tagged, and toilets, a café and a pub
/// within 300 m; only what's known is said, and nothing is guessed.
/// </summary>
public class DescribingCarParks
{
    [TestCase("no", CarParkFee.Free)]
    [TestCase("yes", CarParkFee.Pay)]
    [TestCase("donation", CarParkFee.Donation)]
    [TestCase("Mo-Su 08:00-20:00", CarParkFee.Pay)]
    [TestCase("£5 per day", CarParkFee.Pay)]
    public void A_fee_tagged_is_free_a_donation_or_pay(string tag, CarParkFee fee) =>
        Assert.That(CarParkDetails.FeeOf(tag), Is.EqualTo(fee));

    [TestCase(null)]
    [TestCase("")]
    public void No_fee_tagged_is_unknown(string? tag) =>
        Assert.That(CarParkDetails.FeeOf(tag), Is.Null);

    [TestCase("250", 250)]
    [TestCase("about 40", null)]
    [TestCase(null, null)]
    public void Only_a_number_is_a_capacity(string? tag, int? spaces) =>
        Assert.That(CarParkDetails.CapacityOf(tag), Is.EqualTo(spaces));

    [Test]
    public void Everything_known_is_said_in_order() =>
        Assert.That(new CarParkDetails(CarParkFee.Free, 250, "08:00-20:00", Toilets: true, Cafe: true, Pub: true).Says,
            Is.EqualTo("Free · 250 spaces · Open 08:00-20:00 · Toilets · Café · Pub"));

    [Test]
    public void Nothing_known_says_nothing() =>
        Assert.That(new CarParkDetails().Says, Is.Empty);

    [TestCase(CarParkFee.Pay, "Pay to park")]
    [TestCase(CarParkFee.Donation, "Donation")]
    public void A_fee_says_what_it_is(CarParkFee fee, string says) =>
        Assert.That(new CarParkDetails(fee).Says, Is.EqualTo(says));

    [Test]
    public void One_space_is_one_space() =>
        Assert.That(new CarParkDetails(Capacity: 1).Says, Is.EqualTo("1 space"));

    [Test]
    public void Hours_always_open_say_so() =>
        Assert.That(new CarParkDetails(Hours: "24/7").Says, Is.EqualTo("Open 24/7"));
}
