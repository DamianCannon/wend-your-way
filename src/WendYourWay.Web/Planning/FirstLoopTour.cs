namespace WendYourWay.Web.Planning;

/// <summary>Where the first-Loop tour (T158) has got to: offered, one of its six steps, or done.</summary>
public enum TourStep { Offered, Start, Go, Page, Breakdown, Nudge, Keep, Done }

/// <summary>What the user has done that a step of the tour may be waiting for.</summary>
internal enum TourMove { StartChosen, GoPressed, Paged, BreakdownOpened, Nudged, Kept }

/// <summary>
/// The first-Loop tour (T158, #162): someone new plans their first Loop one step at a time, each
/// step moved on by doing it, not by reading it (user: "some sort of learning mode"). Offered once;
/// taken again from Help. No part of the planner's history.
/// </summary>
public sealed class FirstLoopTour
{
    /// <summary>How many steps it has, from the start to keeping the Loop.</summary>
    public const int Steps = 6;

    /// <summary>Where it's got to, or null if there's no tour.</summary>
    internal TourStep? Step { get; private set; }

    internal void Offer() => Step = TourStep.Offered;

    internal void Begin() => Step = TourStep.Start;

    internal void End() => Step = null;

    /// <summary>The step left undone (one that can't be done: one Loop found, say), and the next.</summary>
    internal void Skip()
    {
        if (Step is { } step and > TourStep.Offered and < TourStep.Done) Step = step + 1;
    }

    /// <summary>Moves on if this is what the step was waiting for; whether it did.</summary>
    internal bool Did(TourMove move)
    {
        var next = (Step, move) switch
        {
            (TourStep.Start, TourMove.StartChosen) => TourStep.Go,
            // A start finds Loops at once, so Go comes after them; pressed first, both are done.
            (TourStep.Start or TourStep.Go, TourMove.GoPressed) => TourStep.Page,
            (TourStep.Page, TourMove.Paged) => TourStep.Breakdown,
            (TourStep.Breakdown, TourMove.BreakdownOpened) => TourStep.Nudge,
            (TourStep.Nudge, TourMove.Nudged) => TourStep.Keep,
            (TourStep.Keep, TourMove.Kept) => TourStep.Done,
            _ => (TourStep?)null,
        };
        if (next is null) return false;
        Step = next;
        return true;
    }

    /// <summary>What the card over the map says, or null if there's no tour.</summary>
    internal TourView? View => Step is { } step ? new TourView(step, (int)step, Words(step), Here(step)) : null;

    private static string Words(TourStep step) => step switch
    {
        TourStep.Offered => "New here? Plan your first loop with a short tour, one step at a time.",
        TourStep.Start => "Click the map where you'd like to start, choose a car park, or search in the Start box. Loops are found from there straight away.",
        TourStep.Go => "Loops are on their way. Change the distance, then press Go to find loops that long instead.",
        TourStep.Page => "Up to five loops come, best first. Page through them with ‹ and › on the card at the bottom.",
        TourStep.Breakdown => "Open the card with its arrow to see what this loop adds up to: its climbing, how much is off-road, the roads it crosses. Explain? shows where they are on the map.",
        TourStep.Nudge => "Not keen on a bit of it? Drag the route to where it should go, or click it and choose Avoid this time. Undo puts it back.",
        TourStep.Keep => "Like it? Save it with the bookmark, export a GPX for OS Maps or a TCX for a watch, or press QR to send it to your phone.",
        _ => "That's your first loop. Take the tour again from Help any time.",
    };

    // Which control the step is about, for the page to make it stand out.
    private static string? Here(TourStep step) => step switch
    {
        TourStep.Start => "start",
        TourStep.Go => "go",
        TourStep.Page => "page",
        TourStep.Breakdown => "breakdown",
        TourStep.Nudge => "nudge",
        TourStep.Keep => "keep",
        _ => null,
    };
}

/// <summary>The tour as the card shows it: its step, the step's number (1 to 6), what it says, and what it's about.</summary>
public sealed record TourView(TourStep Step, int Number, string Words, string? Here);
