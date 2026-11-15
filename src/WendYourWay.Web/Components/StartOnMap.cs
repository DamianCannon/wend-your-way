using WendYourWay.Data;

namespace WendYourWay.Web.Components;

/// <summary>A place to start as the map marks it (T025): where, and what its marker says.</summary>
public sealed record StartOnMap(LatLon At, string Label);
