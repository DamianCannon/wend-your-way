using System.IO.Compression;
using WendYourWay.Core.Network;
using WendYourWay.Core.Places;
using Npgsql;
using NpgsqlTypes;

namespace WendYourWay.Data.Places;

/// <summary>What loading the places did (PLAN T064).</summary>
/// <param name="Places">OS Open Names' places and green areas, a Welsh name counted on its own.</param>
/// <param name="Postcodes">Its postcodes.</param>
/// <param name="Districts">The postcode districts made from them.</param>
/// <param name="GreenAreas">The map's own named green areas OS Open Names hasn't.</param>
public sealed record PlacesLoaded(long Places, long Postcodes, long Districts, long GreenAreas);

/// <summary>A release of OS Open Names (T171): OS's version, "2026-10", and the MD5 of the zip it came in.</summary>
public sealed record PlacesRelease(string Version, string Md5);

/// <summary>The release of OS Open Names loaded (T171), and when.</summary>
public sealed record PlacesLoadedRelease(PlacesRelease Release, DateTimeOffset LoadedAt);

/// <summary>
/// Loads the places to search for (PLAN T064), in place of any loaded before, in one transaction:
/// OS Open Names' entries for England and Wales, the postcode districts made from its postcodes,
/// and the map's own named green areas of <see cref="GreenAreaM2"/> or more, unless OS Open Names
/// has one of the same name within <see cref="SameNameM"/>.
/// </summary>
public static class PlacesImport
{
    /// <summary>The smallest of the map's named green areas kept: 2 hectares.</summary>
    public const double GreenAreaM2 = 20_000;

    /// <summary>How near another of the same name a green area is the same place.</summary>
    public const double SameNameM = 1000;

    /// <summary>Loads OS Open Names' CSV files, unpacked by hand: which release they are isn't known.</summary>
    public static Task<PlacesLoaded> LoadAsync(NpgsqlDataSource dataSource, IReadOnlyList<string> files, Action<string>? reading = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        return LoadAsync(dataSource, files.Select(file => (file, OpenNames.Read(file))), null, reading, cancellationToken);
    }

    /// <summary>Loads OS Open Names straight from OS's zip (T171), kept as <paramref name="release"/>.</summary>
    public static async Task<PlacesLoaded> LoadZipAsync(NpgsqlDataSource dataSource, string zip, PlacesRelease release, Action<string>? reading = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        using var archive = ZipFile.OpenRead(zip);
        return await LoadAsync(dataSource, OpenNames.Files(archive).Select(file => (file.FullName, OpenNames.Read(file))), release, reading, cancellationToken);
    }

    /// <summary>The release of OS Open Names loaded, and when; null if they were loaded by hand, or never.</summary>
    public static async Task<PlacesLoadedRelease?> ReleaseAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        await using var command = dataSource.CreateCommand("select version, md5, loaded_at from places.release limit 1");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new PlacesLoadedRelease(new PlacesRelease(reader.GetString(0), reader.GetString(1)), reader.GetFieldValue<DateTimeOffset>(2))
            : null;
    }

    private static async Task<PlacesLoaded> LoadAsync(NpgsqlDataSource dataSource, IEnumerable<(string Name, IEnumerable<OpenNamesEntry> Entries)> files,
        PlacesRelease? release, Action<string>? reading, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, transaction, "truncate places.names restart identity", cancellationToken);

        long places = 0, postcodes = 0;
        await using (var names = await connection.BeginBinaryImportAsync(
            "copy places.names (name, kind, context, key, geom) from stdin (format binary)", cancellationToken))
        {
            foreach (var (file, entries) in files)
            {
                reading?.Invoke(file);
                foreach (var entry in entries)
                {
                    if (entry.Kind == PlaceKind.Postcode)
                    {
                        await WriteAsync(names, entry.Name, entry.Kind, entry.Context, PlaceSearch.PostcodeKey(entry.Name) ?? PlaceSearch.Key(entry.Name), entry.At, cancellationToken);
                        postcodes++;
                    }
                    else
                    {
                        await WriteAsync(names, entry.Name, entry.Kind, entry.Context, PlaceSearch.Key(entry.Name), entry.At, cancellationToken);
                        places++;
                    }
                }
            }
            await names.CompleteAsync(cancellationToken);
        }

        // Each district in the middle of its postcodes, named for the place most of them are in.
        var districts = await ExecuteAsync(connection, transaction, """
            insert into places.names (name, kind, context, key, geom)
            select split_part(name, ' ', 1), 'District', mode() within group (order by context),
                   lower(split_part(name, ' ', 1)), ST_Centroid(ST_Collect(geom))
              from places.names
             where kind = 'Postcode'
             group by split_part(name, ' ', 1)
            """, cancellationToken);

        var green = await GreenAreasAsync(connection, transaction, cancellationToken);
        await ExecuteAsync(connection, transaction, "analyze places.names", cancellationToken);
        // Which release they are (T171): none, loaded by hand.
        await ExecuteAsync(connection, transaction, "delete from places.release", cancellationToken);
        if (release is not null)
        {
            await using var said = new NpgsqlCommand("insert into places.release (version, md5) values (@version, @md5)", connection, transaction);
            said.Parameters.AddWithValue("version", release.Version);
            said.Parameters.AddWithValue("md5", release.Md5);
            await said.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new PlacesLoaded(places, postcodes, districts, green);
    }

    // The map's named parks, commons, nature reserves, woods, forests and heaths, which OS Open
    // Names mostly hasn't: each named for the borough or county of the OS place nearest it, and one
    // of a name within SameNameM, as a forest drawn in parts is one place.
    private static async Task<long> GreenAreasAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using (var exists = new NpgsqlCommand("select to_regclass('osm.areas') is not null", connection, transaction))
        {
            if (await exists.ExecuteScalarAsync(cancellationToken) is not true) return 0;
        }

        var areas = new List<(string Name, NodePoint At)>();
        await using (var command = new NpgsqlCommand("""
            select a.tags->>'name', ST_X(p), ST_Y(p)
              from osm.areas a
             cross join lateral (select ST_Transform(a.geom, 27700) as g) t
             cross join lateral (select ST_PointOnSurface(t.g) as p) s
             where a.tags ? 'name'
               and (a.tags->>'leisure' in ('park', 'common', 'nature_reserve') or a.tags->>'landuse' = 'forest'
                    or a.tags->>'natural' in ('wood', 'heath'))
               and ST_Area(t.g) >= @area
            """, connection, transaction) { CommandTimeout = 0 })
        {
            command.Parameters.Add(new NpgsqlParameter("area", GreenAreaM2));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) areas.Add((reader.GetString(0), new NodePoint(reader.GetDouble(1), reader.GetDouble(2))));
        }

        await ExecuteAsync(connection, transaction, "create temporary table green (name text, key text, geom geometry(Point, 27700)) on commit drop", cancellationToken);
        await using (var writer = await connection.BeginBinaryImportAsync("copy green (name, key, geom) from stdin (format binary)", cancellationToken))
        {
            foreach (var (name, at) in areas)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(name, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(PlaceSearch.Key(name), NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(Point(at), NpgsqlDbType.Bytea, cancellationToken);
            }
            await writer.CompleteAsync(cancellationToken);
        }

        var command2 = $"""
            insert into places.names (name, kind, context, key, geom)
            select g.name, 'Park',
                   (select n.context from places.names n where n.kind not in ('Postcode', 'District') order by n.geom <-> g.geom limit 1),
                   g.key, g.geom
              from green g
             where not exists (select 1 from places.names n where n.kind not in ('Postcode', 'District') and n.key = g.key and ST_DWithin(n.geom, g.geom, {SameNameM}))
            """;
        await ExecuteAsync(connection, transaction, command2, cancellationToken);
        await ExecuteAsync(connection, transaction, $"""
            delete from places.names later
             using places.names earlier
             where later.kind = 'Park' and earlier.kind = 'Park' and later.key = earlier.key and earlier.id < later.id
               and ST_DWithin(earlier.geom, later.geom, {SameNameM})
            """, cancellationToken);
        await using var count = new NpgsqlCommand("select count(*) from places.names where kind = 'Park'", connection, transaction);
        return (long)(await count.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task WriteAsync(NpgsqlBinaryImporter writer, string name, PlaceKind kind, string? context, string key, NodePoint at, CancellationToken cancellationToken)
    {
        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(name, NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(kind.ToString(), NpgsqlDbType.Text, cancellationToken);
        if (context is null) await writer.WriteNullAsync(cancellationToken);
        else await writer.WriteAsync(context, NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(key, NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(Point(at), NpgsqlDbType.Bytea, cancellationToken);
    }

    private static async Task<long> ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // A point in EWKB with British National Grid's SRID, as COPY's binary format takes a geometry.
    private static byte[] Point(NodePoint at)
    {
        var bytes = new byte[25];
        bytes[0] = 1;   // little-endian
        BitConverter.TryWriteBytes(bytes.AsSpan(1), 0x20000001u);   // a point with an SRID
        BitConverter.TryWriteBytes(bytes.AsSpan(5), 27700);
        BitConverter.TryWriteBytes(bytes.AsSpan(9), at.X);
        BitConverter.TryWriteBytes(bytes.AsSpan(17), at.Y);
        return bytes;
    }
}
