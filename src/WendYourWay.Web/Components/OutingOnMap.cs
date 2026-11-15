using WendYourWay.Core.Network;
using WendYourWay.Data;
using WendYourWay.Data.Outings;

namespace WendYourWay.Web.Components;

/// <summary>An Outing as map.js draws it, with ground as the names the map colours by.</summary>
public sealed record OutingOnMap(IReadOnlyList<LatLon> Track, IReadOnlyList<SegmentOnMap> Covered, IReadOnlyList<GapOnMap> Gaps)
{
    public static OutingOnMap From(OutingView outing)
    {
        ArgumentNullException.ThrowIfNull(outing);
        return new OutingOnMap(
            outing.Track,
            [.. outing.Covered.Select(c => new SegmentOnMap(c.Line, c.Ground switch
            {
                GroundKind.OffRoad => "off_road",
                GroundKind.TrafficFree => "traffic_free",
                _ => "connector",
            }))],
            [.. outing.Gaps.Select((g, i) => new GapOnMap(g.Line, g.LengthM, i + 1))]);
    }
}

/// <summary>A covered Segment on the map: its line and its ground ("off_road", "traffic_free" or "connector").</summary>
public sealed record SegmentOnMap(IReadOnlyList<LatLon> Line, string Ground);

/// <summary>A gap on the map, numbered as the Outings panel lists it.</summary>
public sealed record GapOnMap(IReadOnlyList<LatLon> Line, double LengthM, int Number);
