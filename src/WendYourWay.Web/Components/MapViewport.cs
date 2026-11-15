using WendYourWay.Data.Planning;

namespace WendYourWay.Web.Components;

/// <summary>The part of the map in view, and its zoom (T060).</summary>
public sealed record MapViewport(MapBounds Bounds, int Zoom);
