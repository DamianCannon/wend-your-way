using WendYourWay.Core.Scoring;

namespace WendYourWay.Data.Planning;

/// <summary>A Loop's note (T157) and where it is on the map.</summary>
public sealed record RouteNote(LatLon At, LoopNote Note);

/// <summary>
/// A Loop's notes put on its line as the map draws it (T157, #161): each at its Traversal, where it
/// starts, halfway along, or where it ends.
/// </summary>
public static class RouteNotes
{
    /// <summary>
    /// The notes on <paramref name="line"/>, whose points each came from the Traversal on the map
    /// given by <paramref name="of"/>. <paramref name="onTheMap"/> is each of the Loop's Traversals'
    /// index on the map, or null for a crossing link, which the map leaves out: its note is where the
    /// way before it ends, the junction it crosses at.
    /// </summary>
    public static IReadOnlyList<RouteNote> Place(IReadOnlyList<LatLon> line, IReadOnlyList<int> of, IReadOnlyList<int?> onTheMap, IReadOnlyList<LoopNote> notes)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(of);
        ArgumentNullException.ThrowIfNull(onTheMap);
        ArgumentNullException.ThrowIfNull(notes);
        var placed = new List<RouteNote>(notes.Count);
        foreach (var note in notes)
        {
            if (OnTheMap(note, onTheMap) is not var (traversal, place)) continue;
            var last = LastOf(of, traversal);
            if (last < 0) continue;
            // A Traversal's line starts where the one before ends: its own first point is left out, as given once.
            var first = traversal == 0 ? 0 : Math.Max(0, LastOf(of, traversal - 1));
            placed.Add(new RouteNote(place switch
            {
                NotePlace.Start => line[first],
                NotePlace.End => line[last],
                _ => Along(line, first, last, note.Along),
            }, note));
        }
        return placed;
    }

    // The note's Traversal on the map, and where on it; a crossing link's at the end of the one before (or the start of the one after).
    private static (int Traversal, NotePlace Place)? OnTheMap(LoopNote note, IReadOnlyList<int?> onTheMap)
    {
        if (note.Traversal < 0 || note.Traversal >= onTheMap.Count) return null;
        if (onTheMap[note.Traversal] is { } traversal) return (traversal, note.Place);
        for (var t = note.Traversal - 1; t >= 0; t--)
        {
            if (onTheMap[t] is { } before) return (before, NotePlace.End);
        }
        for (var t = note.Traversal + 1; t < onTheMap.Count; t++)
        {
            if (onTheMap[t] is { } after) return (after, NotePlace.Start);
        }
        return null;
    }

    private static int LastOf(IReadOnlyList<int> of, int traversal)
    {
        for (var i = of.Count - 1; i >= 0; i--)
        {
            if (of[i] == traversal) return i;
        }
        return -1;
    }

    // This share of the way along the line from one point to another, between the points either side.
    private static LatLon Along(IReadOnlyList<LatLon> line, int first, int last, double share)
    {
        var length = 0.0;
        for (var i = first; i < last; i++) length += line[i].MetresTo(line[i + 1]);
        var along = 0.0;
        for (var i = first; i < last; i++)
        {
            var step = line[i].MetresTo(line[i + 1]);
            if (along + step >= length * share && step > 0)
            {
                var part = ((length * share) - along) / step;
                return new LatLon(line[i].Latitude + (part * (line[i + 1].Latitude - line[i].Latitude)),
                    line[i].Longitude + (part * (line[i + 1].Longitude - line[i].Longitude)));
            }
            along += step;
        }
        return line[first];
    }
}
