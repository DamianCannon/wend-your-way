using WendYourWay.Core.Network;

namespace WendYourWay.Core.Routing;

/// <summary>
/// What travelling a Segment costs for one Request. Routing and scoring share it (ADR 0006).
/// </summary>
public interface ICostModel
{
    /// <summary>
    /// The lowest cost per metre any Segment can have under this model. A* relies on it: every
    /// cost must be at least <c>FloorPerMetre × LengthM</c>, and the floor must be above zero.
    /// </summary>
    double FloorPerMetre { get; }

    /// <summary>
    /// The cost of travelling <paramref name="segment"/> in the given direction, or
    /// <see cref="double.PositiveInfinity"/> if it can't be used at all.
    /// </summary>
    double CostOf(Segment segment, bool forward);
}
