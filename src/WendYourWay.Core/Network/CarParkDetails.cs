using System.Globalization;

namespace WendYourWay.Core.Network;

/// <summary>What a car park costs, as its fee tag says (PLAN T066).</summary>
public enum CarParkFee
{
    Free,
    Donation,
    /// <summary>Paid: "yes", or the hours or price of the fee.</summary>
    Pay,
}

/// <summary>
/// What's known of a Trailhead beyond where it is (PLAN T066): its fee, spaces, its own opening
/// hours as tagged, and whether toilets, a café and a pub are within 300 m. Only what's known is
/// said: closing times are tagged on few car parks, and none is guessed.
/// </summary>
public sealed record CarParkDetails(CarParkFee? Fee = null, int? Capacity = null, string? Hours = null, bool Toilets = false, bool Cafe = false, bool Pub = false)
{
    /// <summary>How near toilets, a café or a pub must be.</summary>
    public const double NearbyM = 300;

    /// <summary>The fee a fee tag says: "no" is free, "donation" a donation, anything else paid; none if untagged.</summary>
    public static CarParkFee? FeeOf(string? tag) => tag switch
    {
        null or "" => null,
        "no" => CarParkFee.Free,
        "donation" => CarParkFee.Donation,
        _ => CarParkFee.Pay,
    };

    /// <summary>The spaces a capacity tag says, if it's a number.</summary>
    public static int? CapacityOf(string? tag) =>
        int.TryParse(tag, NumberStyles.None, CultureInfo.InvariantCulture, out var spaces) ? spaces : null;

    /// <summary>What's known, in a line: "Free · 250 spaces · Open 08:00-20:00 · Toilets · Café · Pub".</summary>
    public string Says => string.Join(" · ", Parts());

    private IEnumerable<string> Parts()
    {
        if (Fee is { } fee) yield return fee switch { CarParkFee.Free => "Free", CarParkFee.Donation => "Donation", _ => "Pay to park" };
        if (Capacity is { } spaces) yield return string.Create(CultureInfo.InvariantCulture, $"{spaces} {(spaces == 1 ? "space" : "spaces")}");
        if (Hours is { Length: > 0 } hours) yield return $"Open {hours}";
        if (Toilets) yield return "Toilets";
        if (Cafe) yield return "Café";
        if (Pub) yield return "Pub";
    }
}
