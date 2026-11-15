using System.Text.RegularExpressions;

namespace WendYourWay.Launching;

/// <summary>What answers on Wend Your Way's address: nothing, Wend Your Way, or something else.</summary>
public sealed record App(string Name)
{
    public static readonly App None = new("none");
    public static readonly App Ours = new("Wend Your Way");
    public static readonly App SomethingElse = new("something else");

    /// <summary>From the page the address served (none if it didn't answer): Wend Your Way's by its title.</summary>
    public static App Of(string? page) =>
        page is null ? None
        : Regex.IsMatch(page, @"<title>\s*Wend Your Way\b", RegexOptions.IgnoreCase) ? Ours
        : SomethingElse;
}

/// <summary>
/// What the launcher does when clicked (#142, T138, STORY-049): bring its window forward, start the
/// app, open a window onto one started some other way, or say why it can't.
/// </summary>
public sealed record Launch(string Name)
{
    public static readonly Launch BringToFront = new("bring to front");
    public static readonly Launch StartTheApp = new("start the app");
    public static readonly Launch OpenOntoIt = new("open onto it");
    public static readonly Launch PostgreSqlDown = new("PostgreSQL down");
    public static readonly Launch PortTaken = new("port taken");

    /// <summary>Wend Your Way's own address, as its appsettings.json has it.</summary>
    public static readonly Uri Usual = new("http://127.0.0.1:5018/");

    /// <summary>From whether the launcher's window is already open, what answers on the address, and whether PostgreSQL does.</summary>
    public static Launch Decide(bool ourWindowOpen, App answering, bool postgreSqlUp) =>
        ourWindowOpen ? BringToFront
        : answering == App.Ours ? OpenOntoIt
        : answering == App.SomethingElse ? PortTaken
        : !postgreSqlUp ? PostgreSqlDown
        : StartTheApp;

    /// <summary>The address to open: the one given to the app (--urls), else its own.</summary>
    public static Uri Address(IReadOnlyList<string> appArguments)
    {
        ArgumentNullException.ThrowIfNull(appArguments);
        for (var i = 0; i < appArguments.Count; i++)
        {
            if (appArguments[i] == "--urls" && i + 1 < appArguments.Count) return new Uri(appArguments[i + 1].Split(';')[0].TrimEnd('/') + "/");
            if (appArguments[i].StartsWith("--urls=", StringComparison.Ordinal)) return new Uri(appArguments[i]["--urls=".Length..].Split(';')[0].TrimEnd('/') + "/");
        }
        return Usual;
    }

    /// <summary>Why it can't start, said plainly (AC6).</summary>
    public static string Said(Launch why) =>
        why == PostgreSqlDown ? "PostgreSQL isn't running, so Wend Your Way can't start. Start it (the postgresql-17 service), then try again."
        : why == PortTaken ? "Something other than Wend Your Way is using its address (127.0.0.1:5018). Close it, then try again."
        : throw new ArgumentOutOfRangeException(nameof(why), why.Name, "Not a reason it can't start.");
}
