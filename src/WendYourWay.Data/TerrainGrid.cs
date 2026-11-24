using System.Globalization;
using WendYourWay.Core.Network;

namespace WendYourWay.Data;

/// <summary>
/// The ground's height from OS Terrain 50 (PLAN T049): a folder of its ASCII grid tiles, 50 m cells
/// in British National Grid, each tile within one 10 km square. Tiles are found by their headers,
/// not their names, and read only when a height in them is asked for. Where there's no tile (the
/// sea, which OS leaves out), the ground is at 0 m.
/// </summary>
public sealed class TerrainGrid
{
    private const int CellM = 50;
    private const int SquareM = 10_000;
    // Tiles kept read at once: a build walks the Segments square by square, so it needs few.
    private const int TilesKept = 256;

    private readonly Dictionary<(int, int), string> _files = [];
    private readonly Dictionary<(int, int), Tile?> _tiles = [];

    private sealed record Tile(int Column0, int Row0, int Columns, int Rows, float[] Heights);

    public TerrainGrid(string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"No OS Terrain 50 tiles at {Path.GetFullPath(folder)}.");
        Folder = folder;
        foreach (var file in Directory.EnumerateFiles(folder, "*.asc"))
        {
            var header = Header(File.ReadLines(file).Take(5));
            _files[((int)(header["xllcorner"] / SquareM), (int)(header["yllcorner"] / SquareM))] = file;
        }
    }

    /// <summary>The folder of tiles read (T171: which release they are is said there).</summary>
    public string Folder { get; }

    /// <summary>How many tiles the folder holds.</summary>
    public int Tiles => _files.Count;

    /// <summary>The height at <paramref name="point"/>, bilinear between the four nearest cell centres.</summary>
    public double Height(NodePoint point)
    {
        double x = (point.X / CellM) - 0.5, y = (point.Y / CellM) - 0.5;
        int column = (int)Math.Floor(x), row = (int)Math.Floor(y);
        double across = x - column, up = y - row;
        return (Cell(column, row) * (1 - across) * (1 - up)) + (Cell(column + 1, row) * across * (1 - up))
            + (Cell(column, row + 1) * (1 - across) * up) + (Cell(column + 1, row + 1) * across * up);
    }

    // A cell by its column east and row north of the grid's origin.
    private double Cell(int column, int row)
    {
        var tile = TileAt((column * CellM / SquareM, row * CellM / SquareM));
        if (tile is null) return 0;
        int c = column - tile.Column0, r = row - tile.Row0;
        if (c < 0 || c >= tile.Columns || r < 0 || r >= tile.Rows) return 0;
        // The file's first row is the tile's top (north) edge.
        return tile.Heights[((tile.Rows - 1 - r) * tile.Columns) + c];
    }

    private Tile? TileAt((int, int) square)
    {
        if (_tiles.TryGetValue(square, out var tile)) return tile;
        if (_tiles.Count >= TilesKept) _tiles.Clear();
        return _tiles[square] = _files.TryGetValue(square, out var file) ? Read(file) : null;
    }

    private static Tile Read(string file)
    {
        using var lines = File.ReadLines(file).GetEnumerator();
        var headerLines = new List<string>();
        for (var i = 0; i < 5 && lines.MoveNext(); i++) headerLines.Add(lines.Current);
        var header = Header(headerLines);
        if (header["cellsize"] != CellM) throw new InvalidDataException($"{file}: cells of {header["cellsize"]} m, not {CellM} m.");
        int columns = (int)header["ncols"], rows = (int)header["nrows"];
        var heights = new float[columns * rows];
        var k = 0;
        while (lines.MoveNext())
        {
            foreach (var value in lines.Current.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                heights[k++] = float.Parse(value, CultureInfo.InvariantCulture);
            }
        }
        if (k != heights.Length) throw new InvalidDataException($"{file}: {k} heights, not {heights.Length}.");
        return new Tile((int)(header["xllcorner"] / CellM), (int)(header["yllcorner"] / CellM), columns, rows, heights);
    }

    private static Dictionary<string, double> Header(IEnumerable<string> lines) =>
        lines.Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(p => p[0].ToLowerInvariant(), p => double.Parse(p[1], CultureInfo.InvariantCulture), StringComparer.Ordinal);
}
