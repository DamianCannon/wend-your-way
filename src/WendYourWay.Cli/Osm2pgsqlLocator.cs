namespace WendYourWay.Cli;

/// <summary>
/// Finds the osm2pgsql that scripts/setup.ps1 unpacks into tools/osm2pgsql, searching up from
/// the running program, or uses the WEND_OSM2PGSQL environment variable.
/// </summary>
internal static class Osm2pgsqlLocator
{
    public static string? Find()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("WEND_OSM2PGSQL");
        if (!string.IsNullOrEmpty(fromEnvironment)) return fromEnvironment;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var tools = Path.Combine(dir.FullName, "tools", "osm2pgsql");
            if (!Directory.Exists(tools)) continue;
            var exe = Directory.EnumerateFiles(tools, "osm2pgsql.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe is not null) return exe;
        }
        return null;
    }
}
