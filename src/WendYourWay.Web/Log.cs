namespace WendYourWay.Web;

/// <summary>The app's own log messages.</summary>
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message =
        "No Thunderforest API key: its maps (Outdoors, OpenCycleMap) are left out. Set one with " +
        "`dotnet user-secrets set Thunderforest:ApiKey <key> --project src/WendYourWay.Web`.")]
    public static partial void NoTileKey(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message =
        "No OS Terrain 50 tiles in data/terrain50/asc: Loops come without an elevation profile. " +
        "Set Terrain:Folder to where they are.")]
    public static partial void NoTerrain(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't plan a Loop.")]
    public static partial void PlanningFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't load the Outings.")]
    public static partial void OutingsFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't keep or restore the last Request's settings.")]
    public static partial void SettingsNotKept(ILogger logger, Exception exception);

    // Never what was typed: a postcode typed may be home's (T064).
    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't search for a place.")]
    public static partial void SearchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Backup}")]
    public static partial void BackedUp(ILogger logger, string backup);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrated the database: {Scripts}.")]
    public static partial void Migrated(ILogger logger, IReadOnlyList<string> scripts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A map update, build or other job is running: the database's changes ({Scripts}) and the start-up backup wait till the app next starts after it.")]
    public static partial void HeldBack(ILogger logger, IReadOnlyList<string> scripts);

    [LoggerMessage(Level = LogLevel.Information, Message = "Couldn't ask Geofabrik how new its map is.")]
    public static partial void MapUpdateUnasked(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't read or start the map's update.")]
    public static partial void MapUpdateFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't read Wend Your Way's status or start a job from the Admin panel.")]
    public static partial void AdminFailed(ILogger logger, Exception exception);
}
