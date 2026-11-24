using System.IO.Compression;
using WendYourWay.Core.Network;
using WendYourWay.Data;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// OS Terrain 50's release unpacked for the build (T171, #175): its Great Britain zip holds a zip for each
/// 10 km square (`data/tq/tq15_OST50GRID_20260529.zip`), each with the square's ASCII grid tile and more. The
/// tiles come out, with which release they are.
/// </summary>
public class UnpackingTheHeights
{
    private string _folder = "";
    private string _tiles = "";

    [SetUp]
    public async Task Unpack()
    {
        _folder = Directory.CreateTempSubdirectory("wend-heights-").FullName;
        var zip = Path.Combine(_folder, "terr50_gagg_gb.zip");
        var tile = Directory.GetFiles(Fixtures.Path("terrain"), "*.asc").Single();
        await using (var outer = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            await using var square = outer.CreateEntry("data/tq/tq15_OST50GRID_20260529.zip").Open();
            using var inner = new ZipArchive(square, ZipArchiveMode.Create);
            inner.CreateEntryFromFile(tile, "TQ15.asc");
            await using (var gml = inner.CreateEntry("TQ15.gml").Open()) gml.WriteByte(0);
        }
        _tiles = Path.Combine(_folder, "asc");
        await TerrainRelease.UnpackAsync(zip, _tiles, new TerrainRelease("2026-07", "feba9008db55cdcb5c5e5b7896e3428b"));
    }

    [TearDown]
    public void Clear() => Directory.Delete(_folder, recursive: true);

    [Test]
    public void Then_each_squares_tile_is_read_as_heights() =>
        Assert.That(new TerrainGrid(_tiles).Height(new NodePoint(517_025, 151_025)), Is.EqualTo(102.5).Within(1e-4));

    [Test]
    public void Then_only_the_tiles_are_kept() =>
        Assert.That(Directory.GetFiles(_tiles, "*.asc").Select(Path.GetFileName), Is.EqualTo(new[] { "TQ15.asc" }));

    [Test]
    public void Then_the_release_is_said_beside_them() =>
        Assert.That(TerrainRelease.In(_tiles), Is.EqualTo(new TerrainRelease("2026-07", "feba9008db55cdcb5c5e5b7896e3428b")));

    [Test]
    public void A_folder_unpacked_by_hand_says_no_release() =>
        Assert.That(TerrainRelease.In(Fixtures.Path("terrain")), Is.Null);
}
