using System.Globalization;
using WendYourWay.Data.Planning;

namespace WendYourWay.Web.Planning;

/// <summary>
/// What the Loop card says after a Nudge (T076, T079; moved out of the page, T123): why one couldn't
/// be made, and how far one made is from the Request's length window.
/// </summary>
public static class NudgeNotes
{
    /// <summary>A Nudge with no way to make it: the Loop is as it was.</summary>
    public static string Failed(LoopNudge nudge) => nudge switch
    {
        AvoidNudge => "No way round there: the Loop is as it was",
        RemoveStopNudge or RemoveAvoidNudge => "No way without it: the Loop is as it was",
        _ => "No way there: the Loop is as it was",
    };

    /// <summary>
    /// A Nudge made, <paramref name="kmOutsideWindow"/> km outside the length window (more than 0
    /// longer, less than 0 shorter): by how much; within it, nothing.
    /// </summary>
    public static string? Made(double kmOutsideWindow) => kmOutsideWindow switch
    {
        > 0 => string.Create(CultureInfo.InvariantCulture, $"{kmOutsideWindow:0.0} km longer than you asked"),
        < 0 => string.Create(CultureInfo.InvariantCulture, $"{-kmOutsideWindow:0.0} km shorter than you asked"),
        _ => null,
    };
}
