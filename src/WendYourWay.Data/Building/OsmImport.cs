using System.Diagnostics;
using Npgsql;

namespace WendYourWay.Data.Building;

/// <summary>
/// Loads an OSM extract (.osm.pbf or .osm XML) into the osm schema with osm2pgsql, using the
/// Wend Your Way flex style embedded in this assembly. Replaces whatever was loaded before.
/// </summary>
public static class OsmImport
{
    // Node locations osm2pgsql caches in memory; the rest it looks up in the database.
    private const string CacheMb = "400";

    // Over this, node positions go in a flat file rather than PostgreSQL (T035): a Great Britain
    // import with them in a 10 GB table crawled for nine hours on a few GB of free memory. The file
    // is sized by the highest node ID (about 105 GB for any real extract), so smaller ones don't.
    private const long FlatNodesOverBytes = 1L << 30;

    /// <summary>Whether an extract this size keeps its node positions in a flat file.</summary>
    public static bool UsesFlatNodes(long extractBytes) => extractBytes > FlatNodesOverBytes;

    /// <summary>Runs osm2pgsql and returns its output.</summary>
    /// <param name="cancellationToken">Stops osm2pgsql; the osm schema is then incomplete, and the Network untouched.</param>
    /// <param name="flatNodesFile">
    /// Where to keep node positions in a flat file, deleted afterwards; by default, next to an
    /// extract over 1 GB, and none for a smaller one.
    /// </param>
    /// <exception cref="InvalidOperationException">osm2pgsql failed; its output is in the message.</exception>
    /// <exception cref="OperationCanceledException">It was stopped.</exception>
    public static string Run(string osm2pgsqlExe, string connectionString, string extract, string? flatNodesFile = null, CancellationToken cancellationToken = default)
    {
        flatNodesFile ??= File.Exists(extract) && UsesFlatNodes(new FileInfo(extract).Length)
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(extract))!, "flat-nodes.bin")
            : null;
        cancellationToken.ThrowIfCancellationRequested();
        var styleFile = Path.Combine(Path.GetTempPath(), $"cutthrough-{Guid.NewGuid():N}.lua");
        try
        {
            using (var style = typeof(OsmImport).Assembly.GetManifestResourceStream("cutthrough.lua")
                ?? throw new InvalidOperationException("The osm2pgsql style isn't embedded in WendYourWay.Data."))
            using (var file = File.Create(styleFile))
            {
                style.CopyTo(file);
            }

            var start = new ProcessStartInfo(osm2pgsqlExe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            // Slim keeps the raw nodes and ways in the database while it works, not in memory:
            // Great Britain's would need several GB on a 16 GB laptop (T018). --drop removes
            // them afterwards; the flex tables keep everything the build needs.
            foreach (var argument in new[] { "--create", "--slim", "--drop", "--cache", CacheMb, "--output=flex", "--style", styleFile, "--database", ConnInfo(connectionString) })
                start.ArgumentList.Add(argument);
            if (flatNodesFile is not null)
            {
                start.ArgumentList.Add("--flat-nodes");
                start.ArgumentList.Add(flatNodesFile);
            }
            start.ArgumentList.Add(extract);
            // A password in the connection string (Docker's, T142) by the environment, never the command line.
            if (new NpgsqlConnectionStringBuilder(connectionString).Password is { Length: > 0 } password) start.Environment["PGPASSWORD"] = password;

            using var process = Process.Start(start) ?? throw new InvalidOperationException($"Couldn't start {osm2pgsqlExe}.");
            string output;
            using (cancellationToken.Register(() => process.Kill(entireProcessTree: true)))
            {
                var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
                var stdout = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                cancellationToken.ThrowIfCancellationRequested();
                output = stdout + stderr.Result;   // osm2pgsql logs progress on stderr
            }
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"osm2pgsql exited with {process.ExitCode}:{Environment.NewLine}{output}");
            return output;
        }
        finally
        {
            File.Delete(styleFile);
        }
    }

    // osm2pgsql takes a libpq conninfo string; the password from pgpass.conf, or PGPASSWORD above.
    private static string ConnInfo(string connectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        return $"host={b.Host} port={b.Port} dbname={b.Database} user={b.Username}";
    }
}
