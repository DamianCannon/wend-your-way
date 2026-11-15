using WendYourWay.Data;

namespace WendYourWay.Web.Components;

/// <summary>A Mark drawn on the map (T082): its id, to unblock it, and its Segment's line.</summary>
public sealed record MarkOnMap(int Id, IReadOnlyList<LatLon> Line);
