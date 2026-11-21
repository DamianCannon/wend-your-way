namespace WendYourWay.Launching;

/// <summary>
/// The practice map from its own shortcut (T161, #165; `--practice`): the app told it's the practice map,
/// at an address of its own, built first by `wend practice` if it's needed; once, by a name of its own,
/// so it opens beside the usual window rather than bringing that forward.
/// </summary>
public static class Practice
{
    /// <summary>The practice map's own address, beside the app's usual one.</summary>
    public const string Address = "http://127.0.0.1:5020";

    /// <summary>What the launcher is given to open the practice map.</summary>
    public const string Flag = "--practice";

    public static IReadOnlyList<string> AppArguments(IReadOnlyList<string> appArguments)
    {
        ArgumentNullException.ThrowIfNull(appArguments);
        var given = appArguments.Any(a => a == "--urls" || a.StartsWith("--urls=", StringComparison.Ordinal));
        return [.. appArguments, "--Practice=true", .. given ? Array.Empty<string>() : ["--urls", Address]];
    }

    public static IReadOnlyList<string> WendArguments(IReadOnlyList<string> appArguments)
    {
        ArgumentNullException.ThrowIfNull(appArguments);
        const string Database = "--ConnectionStrings:WendYourWay=";
        return appArguments.FirstOrDefault(a => a.StartsWith(Database, StringComparison.Ordinal)) is { } given
            ? ["practice", "--database", given[Database.Length..]]
            : ["practice"];
    }

    /// <summary>The name the launcher is opened once by.</summary>
    public static string Once(bool practising) => practising ? @"Local\WendYourWay.Launcher.Practice" : @"Local\WendYourWay.Launcher";
}
