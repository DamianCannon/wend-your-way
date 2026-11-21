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

    /// <summary>
    /// The app's arguments for the practice map: told so, at its own address in place of any given, so it
    /// runs beside the app on your own map (T163), which keeps that one.
    /// </summary>
    public static IReadOnlyList<string> AppArguments(IReadOnlyList<string> appArguments)
    {
        ArgumentNullException.ThrowIfNull(appArguments);
        var kept = new List<string>();
        for (var i = 0; i < appArguments.Count; i++)
        {
            if (appArguments[i] == "--urls") i++;
            else if (!appArguments[i].StartsWith("--urls=", StringComparison.Ordinal)) kept.Add(appArguments[i]);
        }
        return [.. kept, "--Practice=true", "--urls", Address];
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

/// <summary>
/// The page asking the launcher's window to switch the map (T163, #167; user: "a button in the help panel
/// that switches to the practice map (and back again)"): {"switchTo":"practice"} or {"switchTo":"own"}.
/// </summary>
public static class MapSwitch
{
    public const string ToPractice = "practice";

    public const string ToOwn = "own";

    /// <summary>Where it asks to go, or null if it's not a switch (or not one this knows).</summary>
    public static string? Parse(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(message);
            return json.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && json.RootElement.TryGetProperty("switchTo", out var to) && to.ValueKind == System.Text.Json.JsonValueKind.String
                && to.GetString() is ToPractice or ToOwn ? to.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
