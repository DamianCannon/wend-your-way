using WendYourWay.Core.Network;
using WendYourWay.Data;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Reading a slice says how far it has got (PLAN T054): the Segments read so far and, once counted
/// alongside, how many there are. On the real London Network, 3 km round Kenwood House.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class ReadingASlice
{
    private static readonly NodePoint KenwoodHouse = new(527_100, 187_300);

    private NpgsqlDataSource _dataSource = null!;
    private readonly List<SliceReading> _seen = [];
    private NetworkSlice _slice = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        _dataSource = await RealNetwork.OpenOrIgnore();
        _slice = await new PostgisNetworkSlices(_dataSource).AroundAsync(KenwoodHouse, 3000, reading: _seen.Add);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource?.Dispose();

    [Test]
    public void Then_it_reports_as_it_reads() =>
        Assert.That(_seen.Count(r => r.Read < _slice.Segments.Count), Is.GreaterThan(1));

    [Test]
    public void Then_the_Segments_read_only_rise() =>
        Assert.That(_seen.Select(r => r.Read), Is.Ordered.Ascending);

    [Test]
    public void Then_the_total_it_reports_is_the_Segments_in_the_slice() =>
        Assert.That(_seen.Select(r => r.Total).OfType<int>(), Is.Not.Empty.And.All.EqualTo(_slice.Segments.Count));

    [Test]
    public void Then_it_ends_having_read_them_all() =>
        Assert.That(_seen[^1], Is.EqualTo(new SliceReading(_slice.Segments.Count, _slice.Segments.Count)));
}
