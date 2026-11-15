using WendYourWay.Core.Network;

namespace WendYourWay.Core.Loops;

/// <summary>
/// What the user asks for: a Run or a Gravel Ride (<paramref name="Activity"/>, T068) of about
/// <paramref name="DistanceKm"/> from <paramref name="Start"/> (British National Grid), leaning to
/// fewer or more hills by <paramref name="HillsLean"/> (T050) and to tarmac or trails by
/// <paramref name="OffRoadLean"/> (T058). Other Leans and Heading come later.
/// </summary>
public sealed record Request(NodePoint Start, double DistanceKm, double HillsLean = 0, double OffRoadLean = 0, Activity Activity = Activity.Run)
{
    /// <summary>The shortest Distance a Request may ask for.</summary>
    public const double MinDistanceKm = 1;

    /// <summary>
    /// The longest Distance a Request may ask for: a longer one would load a slice the size of a
    /// county onto a 16 GB laptop.
    /// </summary>
    public const double MaxDistanceKm = 100;

    /// <summary>How much shorter than the Distance an offered Loop may be (R1.3).</summary>
    public const double WindowKm = 1;

    /// <summary>How much longer than the Distance an offered Loop may be, at least (R1.3): 10% more for the river is worth it (user, 2026-10-24).</summary>
    public const double LongerShare = 0.15;

    /// <summary>How far from the clicked point the start may snap: farther, and no Loop is made.</summary>
    public const double SnapMarginM = 500;

    public double DistanceKm { get; } = DistanceKm is >= MinDistanceKm and <= MaxDistanceKm
        ? DistanceKm
        : throw new ArgumentOutOfRangeException(nameof(DistanceKm), DistanceKm, $"A Distance must be from {MinDistanceKm} to {MaxDistanceKm} km.");

    /// <summary>From −1 (fewer hills) to +1 (more); 0 leans neither way.</summary>
    public double HillsLean { get; } = HillsLean is >= -1 and <= 1
        ? HillsLean
        : throw new ArgumentOutOfRangeException(nameof(HillsLean), HillsLean, "A Hills Lean must be from -1 to 1.");

    /// <summary>From −1 (tarmac) to +1 (trails); 0 leans neither way.</summary>
    public double OffRoadLean { get; } = OffRoadLean is >= -1 and <= 1
        ? OffRoadLean
        : throw new ArgumentOutOfRangeException(nameof(OffRoadLean), OffRoadLean, "An Off-road Lean must be from -1 to 1.");

    /// <summary>All its Leans together.</summary>
    public Leans Leans => new(HillsLean, OffRoadLean);

    /// <summary>
    /// The same Request at <paramref name="leans"/> (T053b): its start, Distance and Activity kept.
    /// </summary>
    public Request At(Leans leans)
    {
        ArgumentNullException.ThrowIfNull(leans);
        return new Request(Start, DistanceKm, leans.Hills, leans.OffRoad, Activity);
    }

    /// <summary>The shortest Loop offered (R1.3).</summary>
    public double ShortestKm => DistanceKm - WindowKm;

    /// <summary>The longest Loop offered (R1.3): 15% over the Distance, or 1 km if that's more.</summary>
    public double LongestKm => DistanceKm + Math.Max(WindowKm, LongerShare * DistanceKm);

    /// <summary>
    /// How much of the Network around the start a Request needs: no closed Loop reaches farther
    /// from its start than half its length, and the longest allowed is <see cref="LongestKm"/>.
    /// From <see cref="LongFromKm"/>, <see cref="LongReach"/> of it: no Loop offered at 40 or 60 km
    /// reached farther than 0.34 (Great Britain, T068), and a 100 km slice of half its length held
    /// 2.5 million Segments round London.
    /// </summary>
    public double SliceRadiusM => (LongestKm * 1000 * (DistanceKm >= LongFromKm ? LongReach : 0.5)) + SnapMarginM;

    /// <summary>The Distance from which a slice reaches <see cref="LongReach"/> of the longest Loop, not half.</summary>
    public const double LongFromKm = 30;

    /// <summary>How far a long Loop's slice reaches, as a share of the longest Loop.</summary>
    public const double LongReach = 0.4;
}
