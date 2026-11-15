using Microsoft.Extensions.Configuration;

namespace WendYourWay.Web.Components;

/// <summary>One map the user can choose under the Loop: its tiles, its credit and how far it zooms.</summary>
/// <param name="Name">What the map switcher calls it.</param>
/// <param name="Url">Leaflet's tile URL template.</param>
/// <param name="Attribution">The credit shown while it's on screen.</param>
/// <param name="MaxNativeZoom">The deepest zoom the provider draws; Leaflet enlarges beyond it.</param>
public sealed record TileLayer(string Name, string Url, string Attribution, int MaxNativeZoom);

/// <summary>
/// The maps the user can choose between under the Loop, first shown first: different ones suit
/// different Activities (user, 2026-10-19). Only pictures: routing, scoring and GPX never use them.
/// Keys live in `dotnet user-secrets` (Thunderforest:ApiKey, OsMaps:ApiKey), never in git; a map
/// whose key isn't set is left out.
/// </summary>
public sealed record MapTiles(IReadOnlyList<TileLayer> Layers)
{
    // A Loop is OpenStreetMap data whatever the map under it, so every map credits it (R8.3).
    private const string OpenStreetMapCredit =
        "Data © <a href=\"https://www.openstreetmap.org/copyright\">OpenStreetMap contributors</a>";

    /// <summary>One map, for tests and the simplest setups.</summary>
    public static MapTiles Of(string url, string attribution) => new([new TileLayer("Map", url, attribution, 19)]);

    public static MapTiles From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var thunderforest = configuration["Thunderforest:ApiKey"];
        var os = configuration["OsMaps:ApiKey"];
        var layers = new List<TileLayer>();

        if (!string.IsNullOrEmpty(thunderforest))
        {
            layers.Add(new TileLayer("Thunderforest Outdoors",
                $"https://tile.thunderforest.com/outdoors/{{z}}/{{x}}/{{y}}{{r}}.png?apikey={Uri.EscapeDataString(thunderforest)}",
                $"Maps © <a href=\"https://www.thunderforest.com\">Thunderforest</a>, {OpenStreetMapCredit}", 22));
        }
        if (!string.IsNullOrEmpty(os))
        {
            // Ordnance Survey's free (OpenData) tiles stop at zoom 16; deeper ones are Premium.
            layers.Add(new TileLayer("OS Outdoor",
                $"https://api.os.uk/maps/raster/v1/zxy/Outdoor_3857/{{z}}/{{x}}/{{y}}.png?key={Uri.EscapeDataString(os)}",
                $"Contains OS data © Crown copyright and database rights {DateTime.UtcNow.Year}. Loops: {OpenStreetMapCredit}", 16));
        }
        if (!string.IsNullOrEmpty(thunderforest))
        {
            layers.Add(new TileLayer("Thunderforest OpenCycleMap",
                $"https://tile.thunderforest.com/cycle/{{z}}/{{x}}/{{y}}{{r}}.png?apikey={Uri.EscapeDataString(thunderforest)}",
                $"Maps © <a href=\"https://www.thunderforest.com\">Thunderforest</a>, {OpenStreetMapCredit}", 22));
        }
        layers.Add(new TileLayer("CyclOSM",
            "https://{s}.tile-cyclosm.openstreetmap.fr/cyclosm/{z}/{x}/{y}.png",
            $"<a href=\"https://www.cyclosm.org\">CyclOSM</a>, {OpenStreetMapCredit}", 20));
        layers.Add(new TileLayer("OpenTopoMap",
            "https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png",
            $"Map style © <a href=\"https://opentopomap.org\">OpenTopoMap</a> (CC-BY-SA), SRTM, {OpenStreetMapCredit}", 17));
        return new MapTiles(layers);
    }
}
