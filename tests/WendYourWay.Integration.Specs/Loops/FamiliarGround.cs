using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// Before a Novelty Lean (PLAN T059): the ground the user's Outings of an Activity covered, by
/// same-ground group (R4.3), and how much of a Loop runs on it. Read from app.outing_coverage, so
/// only ever summed, never printed by place.
/// </summary>
internal static class FamiliarGround
{
    /// <summary>The Segments the user's runs, walks and hikes covered.</summary>
    public static async Task<HashSet<SegmentKey>> RunAsync(NpgsqlDataSource network)
    {
        var keys = new HashSet<SegmentKey>();
        await using var command = network.CreateCommand(
            "select distinct c.osm_way_id, c.from_node_id, c.to_node_id from app.outing_coverage c join app.outings o on o.id = c.outing_id where o.activity = 'run'");
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) keys.Add(new SegmentKey(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)));
        return keys;
    }

    /// <summary>The share of the Loop's length on ground any of these Segments lies on.</summary>
    public static double Share(NetworkSlice slice, Loop loop, IReadOnlySet<SegmentKey> covered)
    {
        var groups = new HashSet<int>();
        foreach (var segment in slice.Segments)
        {
            if (covered.Contains(segment.Key)) segment.Cover(groups);
        }
        double on = 0, length = 0;
        foreach (var traversal in loop.Traversals)
        {
            var segment = slice.Segments[traversal.Segment];
            on += segment.RetracedMetres(groups);
            length += segment.LengthM;
        }
        return length > 0 ? on / length : 0;
    }
}
