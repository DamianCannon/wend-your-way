using WendYourWay.Core.Loops;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Phone;

namespace WendYourWay.Web.Planning;

/// <summary>
/// What the Loop card shows (T124e): the Loops offered and the one shown, how a Request is getting
/// on, what it said, the phone's code, and what Undo, Redo and Save would do. Made by the planner's
/// session, in place of the seventeen values the card was given one by one; what the user does on
/// the card stays the card's callbacks.
/// </summary>
/// <param name="ShortestM">For Point-to-points, the shortest way, each route labelled against it; none for Loops.</param>
/// <param name="Place">The car park the Loops start from, named on the card opened; none elsewhere.</param>
public sealed record PagerView(
    IReadOnlyList<PlannedLoop> Loops, int Current, Activity Activity, double? ShortestM,
    bool Finding, PlanningProgress? Progress, bool Searching, bool NothingFound, string? Problem,
    bool PhoneAsked, PhoneLink? Phone, bool RouteHidden, bool CanUndo, bool CanRedo,
    string? NudgeNote, string SuggestedName, string? Place)
{
    /// <summary>Whether Explain? is on (T157): the route's notes on the map.</summary>
    public bool Explaining { get; init; }

    /// <summary>Whether the route shown may be wended (T194): one shown alone, not a variation already.</summary>
    public bool CanWendIt { get; init; }

    /// <summary>Whether Wend It! is finding the variations (T194).</summary>
    public bool Wending { get; init; }

    /// <summary>Before any Request: no Loops yet.</summary>
    public static PagerView None { get; } = new([], 0, Activity.Run, null, false, null, false, false, null, false, null, false, false, false, null, "", null);
}
