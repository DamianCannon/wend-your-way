using WendYourWay.Core.Network;
using WendYourWay.Data;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// The build's Off-road networks (PLAN T022) are the ones the generator finds in a slice: on the
/// real London Network, 3 km round Kenwood House, every network wholly inside the slice is split
/// the same way. Needs `wend network offroad` run on London first.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class FindingOffRoadNetworksOnLondon
{
    private static readonly NodePoint KenwoodHouse = new(527_100, 187_300);
    private const double RadiusM = 3000;

    private NpgsqlDataSource _dataSource = null!;
    private NetworkSlice _slice = null!;
    private Dictionary<SegmentKey, int> _built = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        _dataSource = await RealNetwork.OpenOrIgnore(RealNetwork.London);
        _slice = await new PostgisNetworkSlices(_dataSource).AroundAsync(KenwoodHouse, RadiusM);
        _built = [];
        await using var query = _dataSource.CreateCommand("""
            select s.osm_way_id, s.from_node_id, s.to_node_id, o.network_id
              from network.segment_offroad o
              join network.segments s on s.id = o.segment_id
              join network.offroad_networks n on n.id = o.network_id
             where ST_Within(n.extent, ST_Buffer(ST_SetSRID(ST_MakePoint(@x, @y), 27700), @inside))
            """);
        query.Parameters.Add(new NpgsqlParameter("x", KenwoodHouse.X));
        query.Parameters.Add(new NpgsqlParameter("y", KenwoodHouse.Y));
        // A box within a circle a little smaller than the slice's, so every Segment is in the slice.
        query.Parameters.Add(new NpgsqlParameter("inside", RadiusM - 50));
        await using var reader = await query.ExecuteReaderAsync();
        while (await reader.ReadAsync()) _built[new SegmentKey(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2))] = reader.GetInt32(3);
        if (_built.Count == 0) Assert.Ignore("No Off-road networks on this Network: run `wend network offroad` first.");
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource?.Dispose();

    [Test]
    public void Then_every_network_inside_the_slice_is_split_as_the_slice_splits_it()
    {
        var inSlice = _slice.OffRoadNetworks;
        var green = _slice.Segments.Where(s => _built.ContainsKey(s.Key)).ToList();
        //  Each built network is one network in the slice, and no two share one.
        var sliceOf = green.GroupBy(s => _built[s.Key]).Select(g => g.Select(s => inSlice.NetworkAt(s.From)).Distinct().ToList()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(green, Is.Not.Empty);
            Assert.That(sliceOf, Is.All.Count.EqualTo(1));
            Assert.That(sliceOf.Select(n => n[0]), Is.Unique);
        });
    }

    [Test]
    public void Then_each_measures_what_the_slice_measures()
    {
        var inSlice = _slice.OffRoadNetworks;
        var metres = _slice.Segments.Where(s => _built.ContainsKey(s.Key)).GroupBy(s => _built[s.Key])
            .Select(g => (Built: g.Sum(s => s.LengthM), Slice: inSlice.MetresAt(g.First().From)));
        Assert.That(metres, Is.All.Matches<(double Built, double Slice)>(m => Math.Abs(m.Built - m.Slice) < 0.01));
    }
}
