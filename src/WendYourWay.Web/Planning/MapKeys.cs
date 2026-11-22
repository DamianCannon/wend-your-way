using System.Collections.Immutable;
using WendYourWay.Data;

namespace WendYourWay.Web.Planning;

/// <summary>An API key Wend Your Way can use (T169): the setting it stands for, and what to say about it.</summary>
/// <param name="Name">Its configuration setting, as in user-secrets or .env (`OsMaps:ApiKey`).</param>
/// <param name="Label">Whose key it is.</param>
/// <param name="Need">Recommended or Optional: none is needed for the app to work.</param>
/// <param name="Gives">What it brings.</param>
/// <param name="Where">Where one is got.</param>
/// <param name="WhereSaid">That place, as said.</param>
/// <param name="Plan">The setting for the provider's paid plan, ticked or not, if it has one.</param>
public sealed record KnownKey(string Name, string Label, string Need, string Gives, Uri Where, string WhereSaid, string? Plan = null)
{
    /// <summary>Every key Wend Your Way can use, as the Admin panel lists them.</summary>
    public static IReadOnlyList<KnownKey> All { get; } =
    [
        new("Thunderforest:ApiKey", "Thunderforest", "Recommended",
            "Thunderforest Outdoors and OpenCycleMap, the first maps in the switcher.",
            new Uri("https://www.thunderforest.com/pricing/"), "a free hobby key from Thunderforest"),
        new("OsMaps:ApiKey", "Ordnance Survey", "Optional",
            "OS Outdoor, Ordnance Survey's own map.",
            new Uri("https://osdatahub.os.uk/"), "a key from a project with the OS Maps API on the OS Data Hub (its free plan will do)",
            Plan: "OsMaps:Premium"),
    ];
}

/// <summary>Where a key in use comes from (T169).</summary>
public enum KeyFrom
{
    /// <summary>Not set anywhere.</summary>
    Nowhere,

    /// <summary>Saved in the Admin panel, in the database.</summary>
    Saved,

    /// <summary>Set outside the app: user-secrets, .env or the environment.</summary>
    Outside,
}

/// <summary>A key as it is now: where it's from, and only the end of it (T169).</summary>
/// <param name="Ending">The last four characters, the most of a key ever shown; none when not set.</param>
/// <param name="OnPlan">Whether its provider's paid plan is ticked, for a key that has one.</param>
/// <param name="AlsoOutside">Whether one is set outside the app too, used if the saved one is deleted.</param>
public sealed record KeyState(KnownKey Key, KeyFrom From, string? Ending, bool OnPlan, bool AlsoOutside);

/// <summary>Where saved keys are kept: the database's app.api_keys (T169), or a spec's own.</summary>
public interface IKeyStore
{
    Task<IReadOnlyDictionary<string, string>> AllAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(string name, string value, CancellationToken cancellationToken = default);

    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>The keys in the user's own database, whichever map is open (T169): the practice one is emptied each time.</summary>
public sealed class DatabaseKeyStore(ApiKeys keys) : IKeyStore
{
    public Task<IReadOnlyDictionary<string, string>> AllAsync(CancellationToken cancellationToken = default) => keys.AllAsync(cancellationToken);

    public Task SaveAsync(string name, string value, CancellationToken cancellationToken = default) => keys.SaveAsync(name, value, cancellationToken);

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default) => keys.DeleteAsync(name, cancellationToken);
}

/// <summary>
/// The map services' API keys (T169, #173; user: "manage their API keys in order to know which ones are
/// required and for them to be added, changed and deleted"): saved in the database, the user's choice, and
/// winning over those set outside the app. Read as the app starts and as the Admin panel opens; if they
/// can't be (database changes held back, T149, or no database), those outside are used and none is saved.
/// </summary>
public sealed partial class MapKeys(IKeyStore store, IConfiguration configuration, ILogger<MapKeys> logger)
{
    private ImmutableDictionary<string, string> _saved = ImmutableDictionary<string, string>.Empty;

    /// <summary>Whether keys can be saved here: the database's keys were read.</summary>
    public bool CanSave { get; private set; }

    /// <summary>Reads the saved keys again; if they can't be read, none, and nothing can be saved.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _saved = (await store.AllAsync(cancellationToken)).ToImmutableDictionary(StringComparer.Ordinal);
            CanSave = true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Never the keys themselves: only why they couldn't be read.
            CantRead(logger, e.GetType().Name);
            _saved = ImmutableDictionary<string, string>.Empty;
            CanSave = false;
        }
    }

    /// <summary>Every key Wend Your Way can use, as it is now.</summary>
    public IReadOnlyList<KeyState> States() => [.. KnownKey.All.Select(StateOf)];

    private KeyState StateOf(KnownKey key)
    {
        var (from, value) = _saved.TryGetValue(key.Name, out var saved) ? (KeyFrom.Saved, saved)
            : configuration[key.Name] is { Length: > 0 } outside ? (KeyFrom.Outside, outside)
            : (KeyFrom.Nowhere, null);
        var onPlan = key.Plan is { } plan && string.Equals(Value(plan), "true", StringComparison.OrdinalIgnoreCase);
        return new KeyState(key, from, value is null ? null : value[^Math.Min(4, value.Length)..], onPlan, configuration[key.Name] is { Length: > 0 });
    }

    // The setting in use: the saved one, else the one outside.
    private string? Value(string name) => _saved.TryGetValue(name, out var saved) ? saved : configuration[name];

    /// <summary>Saves a key, or changes it; why not, if it can't be.</summary>
    public async Task<string?> SaveAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Paste the key first.";
        return await ChangingAsync(() => store.SaveAsync(name, value.Trim(), cancellationToken), cancellationToken);
    }

    /// <summary>Ticks or unticks a provider's paid plan: saved either way, so it wins over one set outside.</summary>
    public Task<string?> SetPlanAsync(string plan, bool on, CancellationToken cancellationToken = default) =>
        ChangingAsync(() => store.SaveAsync(plan, on ? "true" : "false", cancellationToken), cancellationToken);

    /// <summary>Deletes a saved key: one set outside the app, if there is one, is used again.</summary>
    public Task<string?> DeleteAsync(string name, CancellationToken cancellationToken = default) =>
        ChangingAsync(() => store.DeleteAsync(name, cancellationToken), cancellationToken);

    private async Task<string?> ChangingAsync(Func<Task> change, CancellationToken cancellationToken)
    {
        if (!CanSave) return CantSave;
        try
        {
            await change();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            CantChange(logger, e.GetType().Name);
            return "Couldn't save it. The app's log says why.";
        }
        await LoadAsync(cancellationToken);
        return null;
    }

    /// <summary>What's said when keys can't be saved.</summary>
    public const string CantSave = "Keys can't be saved here until the database is brought up to date. Any set outside the app still work.";

    /// <summary>
    /// The keys in use, saved ones over those outside: what the maps are drawn with. Their values alone,
    /// not the app's configuration wrapped, so one made for each page holds nothing of it once done with.
    /// </summary>
    public IConfiguration WithSaved() =>
        new ConfigurationBuilder().AddInMemoryCollection(KnownKey.All.SelectMany(k => k.Plan is { } plan ? [k.Name, plan] : new[] { k.Name })
            .Select(name => KeyValuePair.Create(name, Value(name)))).Build();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The saved API keys couldn't be read ({Problem}): those set outside the app are used.")]
    private static partial void CantRead(ILogger logger, string problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An API key couldn't be saved or deleted ({Problem}).")]
    private static partial void CantChange(ILogger logger, string problem);
}
