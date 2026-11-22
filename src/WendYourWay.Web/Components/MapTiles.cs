using Microsoft.Extensions.Configuration;

namespace WendYourWay.Web.Components;

/// <summary>One map the user can choose under the Loop: its tiles, its credit and how far it zooms.</summary>
/// <param name="Name">What the map switcher calls it.</param>
/// <param name="Url">Leaflet's tile URL template.</param>
/// <param name="Attribution">The credit shown while it's on screen.</param>
/// <param name="MaxNativeZoom">The deepest zoom the provider draws; Leaflet enlarges beyond it.</param>
public sealed record TileLayer(string Name, string Url, string Attribution, int MaxNativeZoom);

/// <summary>A see-through map drawn over whichever is chosen, turned on and off (T166).</summary>
/// <param name="Name">What the map switcher calls it.</param>
/// <param name="Urls">Leaflet's tile URL templates, drawn one over another.</param>
/// <param name="Attribution">The credit shown while it's on.</param>
/// <param name="MaxNativeZoom">The deepest zoom the provider draws; Leaflet enlarges beyond it.</param>
public sealed record TileOverlay(string Name, IReadOnlyList<string> Urls, string Attribution, int MaxNativeZoom)
{
    /// <summary>Whether pointing at one of its routes names it, from the map's own route relations (T176).</summary>
    public bool NamesRoutes { get; init; }
}

/// <summary>Areas drawn over whichever map is chosen, read for the view as GeoJSON from an ArcGIS feature service (T170).</summary>
/// <param name="Name">What the map switcher calls it.</param>
/// <param name="Query">The feature layer's query address.</param>
/// <param name="Attribution">The credit shown while it's on.</param>
/// <param name="FromZoom">The zoom they're drawn from: further out, a view holds too many.</param>
public sealed record AreaOverlay(string Name, string Query, string Attribution, int FromZoom);

/// <summary>Lines drawn bold over whichever map is chosen, from one layer of vector tiles (T173).</summary>
/// <param name="Name">What the map switcher calls it.</param>
/// <param name="Tiles">The vector tiles' URL template.</param>
/// <param name="SourceLayer">The tiles' layer drawn.</param>
/// <param name="Attribution">The credit shown while it's on.</param>
/// <param name="FromZoom">The map's zoom it's drawn from: 512-pixel tiles fill the map one zoom deeper than their own.</param>
/// <param name="MaxNativeZoom">The deepest of the tiles' own zooms; drawn larger beyond it.</param>
public sealed record VectorOverlay(string Name, string Tiles, string SourceLayer, string Attribution, int FromZoom, int MaxNativeZoom);

/// <summary>
/// The maps the user can choose between under the Loop, first shown first: different ones suit
/// different Activities (user, 2026-10-19). Only pictures: routing, scoring and GPX never use them.
/// Keys live in `dotnet user-secrets` (Thunderforest:ApiKey, OsMaps:ApiKey, and OsMaps:Premium), never in git; a map
/// whose key isn't set is left out.
/// </summary>
public sealed record MapTiles(IReadOnlyList<TileLayer> Layers)
{
    /// <summary>The overlays offered from tiles elsewhere, off until turned on (T166).</summary>
    public IReadOnlyList<TileOverlay> Overlays { get; init; } = [];

    /// <summary>The areas offered, off until turned on (T170).</summary>
    public IReadOnlyList<AreaOverlay> Areas { get; init; } = [];

    /// <summary>The overlays offered from vector tiles, off until turned on (T173).</summary>
    public IReadOnlyList<VectorOverlay> Vectors { get; init; } = [];

    // Waymarked Trails' maps: signed walking, cycling and mountain-bike routes.
    private static readonly string[] TrailKinds = ["hiking", "cycling", "mtb"];

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
        var premium = string.Equals(configuration["OsMaps:Premium"], "true", StringComparison.OrdinalIgnoreCase);
        var layers = new List<TileLayer>();

        if (!string.IsNullOrEmpty(thunderforest))
        {
            layers.Add(new TileLayer("Thunderforest Outdoors",
                $"https://tile.thunderforest.com/outdoors/{{z}}/{{x}}/{{y}}{{r}}.png?apikey={Uri.EscapeDataString(thunderforest)}",
                $"Maps © <a href=\"https://www.thunderforest.com\">Thunderforest</a>, {OpenStreetMapCredit}", 22));
        }
        if (!string.IsNullOrEmpty(os))
        {
            // Ordnance Survey's free (OpenData) plan serves tiles to zoom 16; on its Premium plan (OsMaps:Premium),
            // 17–20 too, within the free monthly allowance (T167). Leaflet enlarges past the deepest.
            var deepest = premium ? 20 : 16;
            layers.Add(new TileLayer("OS Outdoor",
                $"https://api.os.uk/maps/raster/v1/zxy/Outdoor_3857/{{z}}/{{x}}/{{y}}.png?key={Uri.EscapeDataString(os)}",
                $"Contains OS data © Crown copyright and database rights {DateTime.UtcNow.Year}. Loops: {OpenStreetMapCredit}", deepest));
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
        // Aerial photos (T166, #170): whether a path is a real track, gravel or overgrown. Esri, no key.
        layers.Add(new TileLayer("Aerial photos",
            "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
            $"Photos © <a href=\"https://www.esri.com\">Esri</a>, Maxar, Earthstar Geographics and the GIS User Community. Loops: {OpenStreetMapCredit}", 19));
        // Signed walking, cycling and MTB routes (T166): Waymarked Trails, no key.
        var signedTrails = new TileOverlay("Signed trails",
            TrailKinds.Select(kind => $"https://tile.waymarkedtrails.org/{kind}/{{z}}/{{x}}/{{y}}.png").ToList(),
            $"Trails © <a href=\"https://waymarkedtrails.org\">Waymarked Trails</a> (CC BY-SA), {OpenStreetMapCredit}", 18) { NamesRoutes = true };
        // Open access land in England (T170, #174): Natural England's CRoW access layer, under the OGL.
        var openAccess = new AreaOverlay("Open access land",
            "https://services.arcgis.com/JJzESW51TqeY9uat/arcgis/rest/services/CRoW_Act_2000_Access_Layer/FeatureServer/0/query",
            "Open access land © Natural England (Open Government Licence). Contains Ordnance Survey data © Crown copyright and database right", 11);
        // OS's paths (T173, #177), to see those OpenStreetMap lacks: OS NGD's vector tiles carry them
        // only from their zoom 16, which is premium, so on the Premium plan alone; at 512 pixels a
        // tile fills the map at a zoom one deeper than its own, so they show from the map's zoom 17.
        var vectors = new List<VectorOverlay>();
        if (!string.IsNullOrEmpty(os) && premium)
        {
            vectors.Add(new VectorOverlay("OS paths",
                $"https://api.os.uk/maps/vector/ngd/ota/v1/collections/ngd-base/tiles/3857/{{z}}/{{y}}/{{x}}?key={Uri.EscapeDataString(os)}",
                "trn_ntwk_pathlink", $"Paths: contains OS data © Crown copyright and database rights {DateTime.UtcNow.Year}", 17, 19));
        }
        return new MapTiles(layers) { Overlays = [signedTrails], Areas = [openAccess], Vectors = vectors };
    }
}
