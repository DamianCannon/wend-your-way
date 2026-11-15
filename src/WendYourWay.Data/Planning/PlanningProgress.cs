namespace WendYourWay.Data.Planning;

/// <summary>Where a Request has got (T054).</summary>
public enum PlanningStage
{
    /// <summary>Reading the map round the start: the Segments read, of how many.</summary>
    Reading,

    /// <summary>Trying Loops: how many tried, of how many it will try.</summary>
    Trying,

    /// <summary>Drawing the Loops offered for the map.</summary>
    Drawing,
}

/// <summary>How far a Request has got, reported a few times a second (T054).</summary>
/// <param name="Stage">What it's doing.</param>
/// <param name="Done">How much of it is done: Segments read, or Loops tried.</param>
/// <param name="Total">How much there is to do, once known.</param>
/// <param name="Left">About how long reading the map has left, from its rate so far, once there's a rate (none while trying Loops, whose rate swings).</param>
/// <param name="SoFar">The Loops offered from those found so far, or none yet.</param>
public sealed record PlanningProgress(
    PlanningStage Stage, int Done, int? Total, TimeSpan? Left, IOfferedLoops? SoFar);
