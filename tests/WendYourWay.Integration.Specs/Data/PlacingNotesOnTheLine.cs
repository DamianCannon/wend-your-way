using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// A Loop's notes (T157, #161) put on its line as the map draws it: each at its Traversal, where it
/// starts, halfway along, or where it ends. A crossing link isn't on the map, so its note is where the
/// way before it ends: the junction it crosses at.
/// </summary>
public class PlacingNotesOnTheLine
{
    //  Two Traversals on the map: the first from W to C, the second from C to E by way of N.
    private static readonly LatLon W = new(51.5, -0.12), C = new(51.5, -0.10), N = new(51.51, -0.10), E = new(51.51, -0.084);
    private static readonly LatLon[] Line = [W, C, N, E];
    private static readonly int[] Of = [0, 0, 1, 1];

    private static LatLon At(LoopNote note, params int?[] onTheMap) => RouteNotes.Place(Line, Of, onTheMap, [note]).Single().At;

    [Test]
    public void At_its_end_it_s_its_Traversal_s_last_point() =>
        Assert.That(At(new LoopNote(NoteKind.Obstacle, 0, NotePlace.End), 0, 1), Is.EqualTo(C));

    [Test]
    public void At_its_start_it_s_where_the_Traversal_before_ends() =>
        Assert.That(At(new LoopNote(NoteKind.Obstacle, 1, NotePlace.Start), 0, 1), Is.EqualTo(C));

    //  C to N to E: halfway along is N, the corner, as both legs are the same length.
    [Test]
    public void Halfway_along_it_s_halfway_along_its_line()
    {
        var at = At(new LoopNote(NoteKind.Road, 1, NotePlace.Middle), 0, 1);
        Assert.That(at.MetresTo(N), Is.LessThan(30));
    }

    //  Traversal 1 of the Loop is a crossing link; the map has the Loop's 0 and 2 as its 0 and 1.
    [Test]
    public void A_crossing_link_s_note_is_at_the_junction_it_crosses() =>
        Assert.That(At(new LoopNote(NoteKind.Crossing, 1, NotePlace.Middle), 0, null, 1), Is.EqualTo(C));

    [Test]
    public void It_keeps_what_the_note_says()
    {
        var note = new LoopNote(NoteKind.Road, 0, NotePlace.Middle) { LengthM = 450, MainRoadM = 120, Pavement = true };
        Assert.That(RouteNotes.Place(Line, Of, [0, 1], [note]).Single().Note, Is.EqualTo(note));
    }
}
