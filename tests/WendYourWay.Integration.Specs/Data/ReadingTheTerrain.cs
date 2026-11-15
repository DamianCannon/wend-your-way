using WendYourWay.Core.Network;
using WendYourWay.Data;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// OS Terrain 50 (PLAN T049): heights from its ASCII grid tiles, on Fixtures/terrain's 1 km tile,
/// where the ground rises 1 m in every 10 m eastwards from 100 m at its west edge (away from its
/// valley).
/// </summary>
public class ReadingTheTerrain
{
    private static readonly TerrainGrid Grid = new(Fixtures.Path("terrain"));

    [Test]
    public void Then_a_cell_centre_has_its_cells_height() =>
        Assert.That(Grid.Height(new NodePoint(517_025, 151_025)), Is.EqualTo(102.5).Within(1e-4));

    [Test]
    public void Then_between_cell_centres_the_height_is_in_between() =>
        Assert.That(Grid.Height(new NodePoint(517_040, 151_025)), Is.EqualTo(104).Within(1e-4));

    //  OS leaves the sea out: no tile.
    [Test]
    public void Then_where_there_is_no_tile_the_ground_is_at_sea_level() =>
        Assert.That(Grid.Height(new NodePoint(600_000, 151_025)), Is.Zero);
}
