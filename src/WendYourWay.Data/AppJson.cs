using System.Text.Json;

namespace WendYourWay.Data;

/// <summary>
/// How the app's own JSON is written and read: the last Request, and a Saved Route's Loop, which
/// scoring reads back (T121: one set of options, where three had to agree). A backup's manifest has
/// its own, for people to read.
/// </summary>
public static class AppJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
