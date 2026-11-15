using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;

namespace WendYourWay.Core.Specs.Support;

/// <summary>
/// A deliberately naive Dijkstra: no heuristic, no priority queue, no cleverness. It is slow and
/// obviously correct, which makes it the oracle the real path finder is checked against.
/// </summary>
internal static class ReferenceSearch
{
    /// <summary>The cost of the cheapest Path, or null if the target can't be reached.</summary>
    public static double? CheapestCost(NetworkSlice slice, int from, int to, ICostModel cost)
    {
        var best = Enumerable.Repeat(double.PositiveInfinity, slice.NodeCount).ToArray();
        var done = new bool[slice.NodeCount];
        best[from] = 0;

        while (true)
        {
            var node = -1;
            for (var n = 0; n < slice.NodeCount; n++)
                if (!done[n] && !double.IsPositiveInfinity(best[n]) && (node < 0 || best[n] < best[node]))
                    node = n;
            if (node < 0) return null;
            if (node == to) return best[to];
            done[node] = true;

            foreach (var segment in slice.Segments)
            {
                if (segment.From == segment.To) continue;
                int next;
                bool forward;
                if (segment.From == node) { next = segment.To; forward = true; }
                else if (segment.To == node) { next = segment.From; forward = false; }
                else continue;
                best[next] = Math.Min(best[next], best[node] + cost.CostOf(segment, forward));
            }
        }
    }
}
