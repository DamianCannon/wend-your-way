namespace WendYourWay.Data.Planning;

/// <summary>A Nudge of a Loop offered (T076; SPEC R9.3): a Via or an Avoid, where the user put it on the map.</summary>
public abstract record LoopNudge;

/// <summary>The route grabbed at <paramref name="Grabbed"/> and dropped at <paramref name="To"/>: go through there.</summary>
public sealed record ViaNudge(LatLon Grabbed, LatLon To) : LoopNudge;

/// <summary>"Avoid this time", clicked on the route at <paramref name="At"/>: not the Segment there.</summary>
public sealed record AvoidNudge(LatLon At) : LoopNudge;

/// <summary>A stop removed (T079): <paramref name="Stop"/> is its place among the Loop's Anchors and Vias.</summary>
public sealed record RemoveStopNudge(int Stop) : LoopNudge;

/// <summary>An Avoid removed (T079): <paramref name="Avoid"/> is its place in <see cref="PlannedLoop.Avoids"/>.</summary>
public sealed record RemoveAvoidNudge(int Avoid) : LoopNudge;

/// <summary>
/// A Loop nudged, and how far it is from the Request's length window: 0 within it, more than 0 by
/// how many km longer, less than 0 by how many shorter.
/// </summary>
public sealed record NudgedLoop(PlannedLoop Loop, double KmOutsideWindow);
