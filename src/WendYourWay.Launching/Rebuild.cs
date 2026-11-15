namespace WendYourWay.Launching;

/// <summary>A file the app is built from, and when it was last written.</summary>
public sealed record Source(string Path, DateTime WrittenUtc);

/// <summary>
/// Whether the app needs rebuilding before it opens (#142, T138; user chose "Rebuild first"): any
/// file it's built from written since it was built. What a build writes (bin, obj) and the launcher
/// itself don't count: the launcher can't rebuild itself while it runs.
/// </summary>
public static class Rebuild
{
    /// <summary>Folders whose files aren't the app's code: what builds write, and the launcher's own.</summary>
    private static readonly string[] NotTheApps = ["bin", "obj", "WendYourWay.Launcher", "WendYourWay.Launching", "node_modules", ".git"];

    private static readonly string[] CodeFolders = ["src", "db"];

    /// <summary>The kinds of file the app is built from.</summary>
    private static readonly string[] Kinds = [".cs", ".razor", ".cshtml", ".css", ".js", ".json", ".sql", ".csproj", ".props", ".targets", ".svg", ".html", ".png", ".ico"];

    /// <summary>Whether any of these was written after the build (none: never built).</summary>
    public static bool Needed(IEnumerable<Source> sources, DateTime? builtUtc) =>
        builtUtc is not { } built || sources.Any(s => Counts(s.Path) && s.WrittenUtc > built);

    /// <summary>The files under the repository's src and db folders, and its build settings, as the launcher finds them.</summary>
    public static IEnumerable<Source> Sources(string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        foreach (var folder in CodeFolders.Select(f => System.IO.Path.Combine(repositoryRoot, f)).Where(Directory.Exists))
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                yield return new Source(System.IO.Path.GetRelativePath(repositoryRoot, file), File.GetLastWriteTimeUtc(file));
        foreach (var file in Directory.EnumerateFiles(repositoryRoot, "Directory.*.props"))
            yield return new Source(System.IO.Path.GetFileName(file), File.GetLastWriteTimeUtc(file));
    }

    private static bool Counts(string path)
    {
        var parts = path.Split('\\', '/');
        return !parts.Any(p => NotTheApps.Contains(p, StringComparer.OrdinalIgnoreCase))
            && Kinds.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }
}
