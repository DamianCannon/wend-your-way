using Microsoft.Extensions.Configuration;

namespace WendYourWay.Web.Components;

/// <summary>One map the user can choose under the Loop: its tiles, its credit and how far it zooms.</summary>
/// <param name="Name">What the map switcher calls it.</param>
/// <param name="Url">Leaflet's tile URL template.</param>
/// <param name="Attribution">The credit shown while it's on screen.</param>
/// <param name="MaxNativeZoom">The deepest zoom the provider draws; Leaflet enlarges beyond it.</param>
public sealed record TileLayer(string Name, string Url, string Attribution, int MaxNativeZoom)
{
    /// <summary>
    /// The grid its tiles are cut on: null for the web grid (EPSG:3857) every other map uses; "EPSG:27700"
    /// for Britain's grid (T168: OS Explorer). The map's zoom numbers stay the web grid's either way.
    /// </summary>
    public string? Grid { get; init; }

    /// <summary>The farthest-out zoom it's drawn at (null: every zoom).</summary>
    public int? MinZoom { get; init; }

    /// <summary>The closest zoom the map goes to while it's chosen: past it, too enlarged to read.</summary>
    public int MaxZoom { get; init; } = 20;
}

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

/// <summary>Areas drawn over whichever map is chosen, read for the view as GeoJSON (T170): one switch, from one or more sources.</summary>
/// <param name="Name">What the map switcher calls it.</param>
/// <param name="Sources">Where they're read from.</param>
/// <param name="Attribution">The credit shown while it's on.</param>
public sealed record AreaOverlay(string Name, IReadOnlyList<AreaSource> Sources, string Attribution)
{
    /// <summary>The zoom it's offered from: the shallowest of its sources'.</summary>
    public int FromZoom => Sources.Min(s => s.FromZoom);
}

/// <summary>One place areas are read from (T170): an ArcGIS feature layer's query, or a WFS's <see cref="Layer"/>.</summary>
/// <param name="Query">The address asked.</param>
/// <param name="FromZoom">The zoom they're read from: further out, a view holds too much.</param>
public sealed record AreaSource(string Query, int FromZoom)
{
    /// <summary>The WFS feature type read, as GeoJSON in latitude and longitude; none for an ArcGIS feature layer.</summary>
    public string? Layer { get; init; }

    /// <summary>The bounds it covers (west, south, east, north): not read for a view outside them. None: anywhere.</summary>
    public double[]? Within { get; init; }
}

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

    // Natural Resources Wales's access land (T170): open country, common land, other dedicated land.
    private static readonly string[] WalesAccessLayers = ["NRW_OPEN_COUNTRY_2014", "NRW_COMMON_LAND_2014", "NRW_OTHER_DEDICATED_LAND"];

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
        if (!string.IsNullOrEmpty(os) && premium)
        {
            // OS Leisure, the Explorer-style 1:25 000 (its zooms 7–9; its 5–6 premium too), on
            // Britain's grid only (T168). Its zoom z is drawn at the map's zoom z + 7, about the same ground
            // resolution (7 m a pixel against the web grid's 5.9 at 52°N), so its 9 is the map's 16, enlarged once.
            layers.Add(new TileLayer("OS Explorer",
                $"https://api.os.uk/maps/raster/v1/zxy/Leisure_27700/{{z}}/{{x}}/{{y}}.png?key={Uri.EscapeDataString(os)}",
                $"Contains OS data © Crown copyright and database rights {DateTime.UtcNow.Year}. Loops: {OpenStreetMapCredit}", 16)
            { Grid = "EPSG:27700", MinZoom = 7, MaxZoom = 17 });
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
        // Open access land (T170, #174), under the OGL: in England, Natural England's CRoW access layer; in
        // Wales, Natural Resources Wales's open country, common land and other dedicated land, from DataMapWales's
        // WFS. Its shapes aren't simplified (a view over the Beacons is about 8 MB at zoom 11, 2.4 MB at 12), so
        // from 12, and only for a view over Wales. Dedicated woodlands left out: a 1947 forestry scheme, not
        // access land, naming landowners.
        const string dataMapWales = "https://datamap.gov.wales/geoserver/ows";
        double[] wales = [-5.35, 51.35, -2.65, 53.45];
        var openAccess = new AreaOverlay("Open access land",
            [
                new AreaSource("https://services.arcgis.com/JJzESW51TqeY9uat/arcgis/rest/services/CRoW_Act_2000_Access_Layer/FeatureServer/0/query", 11),
                .. WalesAccessLayers
                    .Select(layer => new AreaSource(dataMapWales, 12) { Layer = "inspire-nrw:" + layer, Within = wales })
            ],
            "Open access land © Natural England (Open Government Licence). Contains Natural Resources Wales information © Natural Resources Wales " +
            "and Database Right. All rights Reserved. Contains Ordnance Survey Data. Ordnance Survey Licence number AC0000849444. " +
            "Contains Ordnance Survey data © Crown copyright and database right");
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
