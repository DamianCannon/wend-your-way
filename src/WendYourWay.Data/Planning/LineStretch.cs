namespace WendYourWay.Data.Planning;

/// <summary>A stretch of a route's line, by the indexes of its first and last points (T182).</summary>
public sealed record LineStretch(int From, int To)
{
    /// <summary>
    /// The stretches traced from the given Traversals: <paramref name="of"/> says which Traversal each point
    /// ends a piece of, so a stretch runs from the point before its first piece to its last piece's end.
    /// </summary>
    public static IReadOnlyList<LineStretch> Where(IReadOnlyList<int> of, IReadOnlyCollection<int> traversals)
    {
        ArgumentNullException.ThrowIfNull(of);
        ArgumentNullException.ThrowIfNull(traversals);
        var stretches = new List<LineStretch>();
        for (var i = 0; i < of.Count; i++)
        {
            if (!traversals.Contains(of[i])) continue;
            var from = Math.Max(i - 1, 0);
            if (stretches.Count > 0 && stretches[^1].To == from) stretches[^1] = stretches[^1] with { To = i };
            else if (stretches.Count > 0 && stretches[^1].To == i) continue;
            else stretches.Add(new LineStretch(from, i));
        }
        return stretches;
    }
}
