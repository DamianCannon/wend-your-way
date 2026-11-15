namespace WendYourWay.Integration.Specs;

/// <summary>External tools and fixture files the integration specs need.</summary>
internal static class Tools
{
    /// <summary>
    /// The osm2pgsql that scripts/setup.ps1 unpacks into tools/osm2pgsql, or the path in the
    /// WEND_OSM2PGSQL environment variable.
    /// </summary>
    public static string Osm2pgsql { get; } = Environment.GetEnvironmentVariable("WEND_OSM2PGSQL") ?? Find();

    private static string Find()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir is not null; dir = dir.Parent)
        {
            var tools = Path.Combine(dir.FullName, "tools", "osm2pgsql");
            if (!Directory.Exists(tools)) continue;
            var exe = Directory.EnumerateFiles(tools, "osm2pgsql.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe is not null) return exe;
        }
        throw new InvalidOperationException("osm2pgsql.exe not found under tools/osm2pgsql. Run scripts/setup.ps1.");
    }
}

/// <summary>Hand-written OSM fixtures, copied next to the test assembly.</summary>
internal static class Fixtures
{
    public static string Path(string name) =>
        System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
}
