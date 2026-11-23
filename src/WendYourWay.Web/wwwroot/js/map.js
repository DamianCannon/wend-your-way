// The planner's map: Leaflet over a choice of map tiles (MapView.razor drives it).
// Leaflet itself is loaded as a global (L) by App.razor. Positions arrive as { latitude, longitude }.

const london = [51.5080, -0.1281];

// Arrows and distance markers are spaced on the screen, not on the ground, so they stay
// readable at every zoom: an arrow about every 90 px, and a distance marker every 1, 2, 5 or
// 10 km, whichever first leaves 70 px between markers.
const arrowEveryPixels = 90;
const markerEveryPixels = 70;
const markerSteps = [1, 2, 5, 10, 20, 50];

let map;
let startMarker;
let finishMarker;
let loopLayer;
let routeShown = true;
let shownLoop;
let decorations;
let measured;
let outingLayer;
let placeMarker;
let startsLayer;
let marksLayer;
let carParks;
let owner;
let notesLayer;
let notes = [];

// The stylesheet's own colours (T165): the map draws in them, so the two never drift apart.
const colour = (name, fallback) => getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
const hiVis = colour("--hi-vis", "#e8590c");
const danger = colour("--danger", "#c92a2a");
// An Outing's covered Segments by ground (STORY-030). Hi-vis orange stays the Loop's own.
const groundColours = { off_road: colour("--ground-off-road", "#2e7d32"), traffic_free: colour("--ground-traffic-free", "#1f78c8"), connector: colour("--ground-connector", "#8a8f8c") };
const gapColour = colour("--map-gap", "#d62828");
const possibleGapColour = colour("--possible-gap", "#5b6760");
const gapDot = colour("--gap-dot", "#ffd400");
const gapDotRing = colour("--gap-dot-ring", "#1f2a24");
// A route's gaps (T088): a break in the orange, dotted in ink, so they never read as more route.
const routeGapInk = colour("--ink", "#1f2a24");

const point = p => [p.latitude, p.longitude];

// The map the user chose last, kept in this browser only; a blocked or empty store just means
// the first map.
const chosenMapKey = "cutthrough.map";
const chosenMap = () => { try { return localStorage.getItem(chosenMapKey); } catch { return null; } };
const rememberMap = name => { try { localStorage.setItem(chosenMapKey, name); } catch { /* not kept */ } };

// The Off-road networks overlay (T022b): tiles the app serves, drawn on canvases. Zoomed in, each
// green Segment coloured by its network's size; zoomed out, a dot for each network of 10 km or
// more. Bands: under 10 km, 10–30, 30–100, 100–300, over 300.
const offRoadName = "Off-road networks";
const offRoadColours = [
    colour("--off-road-1", "rgba(110, 120, 115, 0.45)"), colour("--off-road-2", "#9ccc65"), colour("--off-road-3", "#4caf50"),
    colour("--off-road-4", "#2e7d32"), colour("--off-road-5", "#0f4d24")];

const overlayKey = name => "cutthrough.overlay." + name;
const rememberedOverlay = (name, byDefault = false) => {
    try { const kept = localStorage.getItem(overlayKey(name)); return kept === null ? byDefault : kept === "on"; } catch { return byDefault; }
};
const rememberOverlay = (name, on) => { try { localStorage.setItem(overlayKey(name), on ? "on" : "off"); } catch { /* not kept */ } };

function offRoadNetworks() {
    const Layer = L.GridLayer.extend({
        createTile(coords, done) {
            const tile = document.createElement("canvas");
            tile.width = tile.height = 256;
            fetch(`tiles/offroad/${coords.z}/${coords.x}/${coords.y}`)
                .then(response => response.json())
                .then(data => { drawOffRoad(tile.getContext("2d"), data, coords.z); done(null, tile); })
                .catch(error => done(error, tile));
            return tile;
        }
    });
    return new Layer({ opacity: 0.85, zIndex: 3, maxZoom: 20 });
}

// Cafés and toilets (T067): an overlay, off at first and remembered, of those in view from zoom
// 13, read as the map stops moving; a cup for a café, "WC" for toilets, named on pointing.
const amenitiesName = "Cafés and toilets";
const carParksName = "Car parks";
const amenitiesFromZoom = 13;
const hotspotsName = "Gravel hotspots";
const cup = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 9h11v5a5 5 0 0 1-5 5h-1a5 5 0 0 1-5-5zM16 10h1.5a2.5 2.5 0 0 1 0 5H16M8 3v3M11 3v3"/></svg>';

function cafesAndToilets() {
    const layer = L.layerGroup();
    let reading = 0;
    layer.refresh = () => {
        const run = ++reading;
        if (!map.hasLayer(layer) || map.getZoom() < amenitiesFromZoom) { layer.clearLayers(); return; }
        const b = map.getBounds();
        fetch(`amenities?south=${b.getSouth()}&west=${b.getWest()}&north=${b.getNorth()}&east=${b.getEast()}`)
            .then(response => response.ok ? response.json() : [])
            .then(found => {
                if (run !== reading) return;
                layer.clearLayers();
                for (const a of found) {
                    const icon = L.divIcon({
                        className: `amenity-marker ${a.kind}`, iconSize: [20, 20],
                        html: a.kind === "cafe" ? `<span>${cup}</span>` : "<span>WC</span>"
                    });
                    L.marker([a.lat, a.lon], { icon, keyboard: false })
                        .bindTooltip(a.name ?? (a.kind === "cafe" ? "Café" : "Toilets"), { direction: "top", offset: [0, -10] })
                        .addTo(layer);
                }
            })
            .catch(() => { /* none shown */ });
    };
    return layer;
}

// Areas read for the view (T170: Open access land): off until turned on, and remembered; read as the map
// stops moving from each of its sources whose bounds the view is over, from that source's zoom in. A wash
// under the Loop, never in the way of a click.
const accessFill = colour("--access-land", "#e3b505");
const accessEdge = colour("--access-land-edge", "#b08900");

// Whether a source covers any of the view (none of its own bounds: anywhere).
const covers = (source, b) => !source.within
    || (b.getWest() < source.within[2] && b.getEast() > source.within[0] && b.getSouth() < source.within[3] && b.getNorth() > source.within[1]);

// An ArcGIS feature layer's areas for the view, simplified to about a pixel, a page at a time: a busy view
// (London's commons, zoomed out) holds more than one answer's 2,000.
async function arcgisAreas(source, b, zoom, stale) {
    const query = new URLSearchParams({
        where: "1=1", geometry: `${b.getWest()},${b.getSouth()},${b.getEast()},${b.getNorth()}`,
        geometryType: "esriGeometryEnvelope", inSR: "4326", spatialRel: "esriSpatialRelIntersects",
        outFields: "OBJECTID", outSR: "4326", maxAllowableOffset: String(360 / (256 * 2 ** zoom)),
        geometryPrecision: "6", orderByFields: "OBJECTID", f: "geojson"
    });
    const features = [];
    for (let pages = 0, offset = 0; pages < 5; pages++) {
        query.set("resultOffset", String(offset));
        const found = await fetch(`${source.query}?${query}`).then(response => response.ok ? response.json() : null);
        if (stale() || !found?.features) break;
        features.push(...found.features);
        if (!found.properties?.exceededTransferLimit) break;
        offset += found.features.length;
    }
    return features;
}

// A WFS layer's areas for the view, as GeoJSON in latitude and longitude: one answer, as a view from the
// source's zoom holds a few hundred at most (Wales's busiest, 451 at zoom 12).
async function wfsAreas(source, b) {
    const query = new URLSearchParams({
        service: "WFS", version: "2.0.0", request: "GetFeature", typeNames: source.layer,
        outputFormat: "application/json", srsName: "urn:ogc:def:crs:EPSG::4326", count: "2000",
        bbox: `${b.getSouth()},${b.getWest()},${b.getNorth()},${b.getEast()},urn:ogc:def:crs:EPSG::4326`
    });
    const found = await fetch(`${source.query}?${query}`).then(response => response.ok ? response.json() : null);
    return found?.features ?? [];
}

function areaOverlay(overlay) {
    const layer = L.geoJSON(null, {
        pane: "areas", interactive: false,
        style: { color: accessEdge, weight: 1, opacity: 0.8, fillColor: accessFill, fillOpacity: 0.22 }
    });
    // The zoom it's drawn from in this view: the deepest of the sources the view is over (T170: Wales's from
    // 12, England's from 11), so the key says to zoom in while any of them is waiting.
    layer.fromZoomHere = () => {
        const here = overlay.sources.filter(source => covers(source, map.getBounds())).map(source => source.fromZoom);
        return here.length ? Math.max(...here) : overlay.fromZoom;
    };
    let reading = 0;
    layer.refresh = () => {
        const run = ++reading;
        const stale = () => run !== reading;
        const zoom = map.getZoom(), b = map.getBounds();
        const sources = map.hasLayer(layer) ? overlay.sources.filter(source => zoom >= source.fromZoom && covers(source, b)) : [];
        if (!sources.length) { layer.clearLayers(); return; }
        Promise.all(sources.map(source => (source.layer ? wfsAreas(source, b) : arcgisAreas(source, b, zoom, stale)).catch(() => [])))
            .then(found => {
                if (stale()) return;
                layer.clearLayers();
                layer.addData({ type: "FeatureCollection", features: found.flat() });
            });
    };
    return layer;
}

// Lines from one layer of vector tiles (T173: OS paths), drawn bold by MapLibre inside Leaflet: off
// until turned on, and remembered; MapLibre is loaded the first time one is. Under the Loop, taking no
// clicks. Vector tiles are 512 pixels (MapLibre takes no others), so MapLibre's zoom is one less than
// Leaflet's: none are asked for before the overlay's zoom.
const maplibre = [
    ["https://unpkg.com/maplibre-gl@4.7.1/dist/maplibre-gl.css", "sha384-MinO0mNliZ3vwppuPOUnGa+iq619pfMhLVUXfC4LHwSCvF9H+6P/KO4Q7qBOYV5V"],
    ["https://unpkg.com/maplibre-gl@4.7.1/dist/maplibre-gl.js", "sha384-SYKAG6cglRMN0RVvhNeBY0r3FYKNOJtznwA0v7B5Vp9tr31xAHsZC0DqkQ/pZDmj"],
    ["https://unpkg.com/@maplibre/maplibre-gl-leaflet@0.0.22/leaflet-maplibre-gl.js", "sha384-4CB9Vtol9LN6lGgBCvmPLbUEZwilrqIvPieSRurgAXAB7FVJaLS9n8WyAIA5wjQ+"]
];
let maplibreLoading;
const loadMaplibre = () => maplibreLoading ??= maplibre.reduce((ready, [url, integrity]) => ready.then(() => new Promise((resolve, reject) => {
    const css = url.endsWith(".css");
    const element = document.createElement(css ? "link" : "script");
    if (css) { element.rel = "stylesheet"; element.href = url; } else element.src = url;
    element.integrity = integrity;
    element.crossOrigin = "anonymous";
    element.onload = resolve;
    element.onerror = reject;
    document.head.appendChild(element);
})), Promise.resolve()).catch(e => { maplibreLoading = undefined; throw e; });

const osPaths = colour("--os-paths", "#7b3fbf");

const VectorOverlayLayer = L.Layer.extend({
    initialize(overlay) {
        this._overlay = overlay;
        L.setOptions(this, { attribution: overlay.attribution });
    },
    onAdd(on) {
        const o = this._overlay;
        loadMaplibre().then(() => {
            if (!on.hasLayer(this) || this._drawn) return;
            const lines = { type: "line", source: "tiles", "source-layer": o.sourceLayer, layout: { "line-cap": "round", "line-join": "round" } };
            this._drawn = L.maplibreGL({
                pane: "areas", interactive: false, attributionControl: false,
                style: {
                    version: 8,
                    sources: { tiles: { type: "vector", tiles: [o.tiles], minzoom: o.fromZoom - 1, maxzoom: o.maxNativeZoom } },
                    layers: [
                        { ...lines, id: "edge", paint: { "line-color": "#ffffff", "line-width": 6, "line-opacity": 0.85 } },
                        { ...lines, id: "lines", paint: { "line-color": osPaths, "line-width": 3.5 } }
                    ]
                }
            }).addTo(on);
        }).catch(() => { /* not drawn: tried again when next turned on */ });
    },
    onRemove(on) {
        if (this._drawn) on.removeLayer(this._drawn);
        this._drawn = null;
    }
});

// What's this route? (T176): while an overlay naming its routes is on, resting the pointer near one
// shows a card of the routes there, from the map's own route relations, with links to look them up.
// It stays while the pointer is on the route or the card, so a link can be clicked; clicks on the
// map are untouched. Only where there's a pointer that hovers.
const escapeHtml = text => String(text).replace(/[&<>"']/g, c => `&#${c.charCodeAt(0)};`);
const routeKinds = { hiking: "Walking", foot: "Walking", walking: "Walking", running: "Running", bicycle: "Cycling", mtb: "MTB" };
const routeLevels = { i: "international", n: "national", r: "regional", l: "local" };

function routeCard(route) {
    const level = routeLevels[(route.network ?? "")[0]];
    const title = [route.ref, route.name].filter(Boolean).map(escapeHtml).join(" · ") || "Unnamed route";
    const links = [[route.waymarkedTrails, "Waymarked Trails"], [route.openStreetMap, "OpenStreetMap"], [route.website, "Website"]]
        .filter(([href]) => href)
        .map(([href, label]) => `<a href="${escapeHtml(href)}" target="_blank" rel="noopener noreferrer">${label}</a>`)
        .join(" · ");
    return `<li><strong>${title}</strong><span class="kind">${routeKinds[route.kind] ?? "Route"}${level ? `, ${level}` : ""}</span><span class="links">${links}</span></li>`;
}

let routeCardPopup;
function nameRoutesOnPointing(isOn) {
    if (!window.matchMedia?.("(hover: hover)").matches) return;
    const reach = 12;   // pixels from the pointer a route is looked for
    let resting, closing, asking = 0, overCard = false, askedAt = null;
    // A layer of its own, not the map's popup (T178): opening it closes no other popup, and a click on
    // the map is never taken as closing it.
    const card = L.popup({ closeButton: false, autoPan: false, autoClose: false, closeOnClick: false, className: "route-card", offset: [0, -4], maxWidth: 320 });
    routeCardPopup = card;
    const hide = () => { if (map.hasLayer(card)) map.removeLayer(card); };
    const close = () => { clearTimeout(closing); closing = setTimeout(() => { if (!overCard) hide(); }, 250); };
    // Another popup open (Avoid, a stop, a Blocked stretch): it's what the user is reading, so none is asked for.
    const otherPopupOpen = () => map._container.querySelector(".leaflet-popup:not(.route-card)") !== null;
    card.on("add", () => {
        const element = card.getElement();
        element.addEventListener("mouseenter", () => { overCard = true; clearTimeout(closing); });
        element.addEventListener("mouseleave", () => { overCard = false; close(); });
    });
    const ask = at => {
        const run = ++asking;
        const metresAPixel = 40075016.686 * Math.cos(at.lat * Math.PI / 180) / 2 ** (map.getZoom() + 8);
        const query = new URLSearchParams({ lat: at.lat.toFixed(6), lon: at.lng.toFixed(6), metres: (reach * metresAPixel).toFixed(1) });
        fetch(`signed-routes?${query}`)
            .then(response => response.ok ? response.json() : [])
            .then(routes => {
                if (run !== asking || !isOn() || otherPopupOpen()) return;
                if (!routes.length) { close(); return; }
                askedAt = map.latLngToContainerPoint(at);
                card.setLatLng(at).setContent(`<ul>${routes.map(routeCard).join("")}</ul>`);
                if (!map.hasLayer(card)) map.addLayer(card);
                clearTimeout(closing);
            })
            .catch(() => { /* none shown */ });
    };
    map.on("mousemove", e => {
        clearTimeout(resting);
        if (!isOn()) return;
        // Moved off the route the card is for: it closes unless the pointer reaches the card.
        if (map.hasLayer(card) && askedAt && askedAt.distanceTo(e.containerPoint) > reach * 2) close();
        // Resting on the card is reading it, not asking again there.
        if (overCard || otherPopupOpen()) return;
        resting = setTimeout(() => ask(e.latlng), 350);
    });
    map.on("mouseout", () => { clearTimeout(resting); asking++; close(); });
    map.on("movestart zoomstart", () => { clearTimeout(resting); asking++; overCard = false; hide(); });
}

// Gravel hotspots (T072): large Off-road networks a bike may use, each marked with its km and
// named on pointing; clicking one starts there, as a click on the map does, and finds its Loops
// (user, 2026-11-06: showing it alone looked like nothing happened).
function gravelHotspots() {
    const layer = L.layerGroup();
    const escape = text => text.replace(/[&<>"']/g, c => `&#${c.charCodeAt(0)};`);
    fetch("gravel-hotspots")
        .then(response => response.ok ? response.json() : [])
        .then(found => {
            for (const h of found) {
                const icon = L.divIcon({ className: "hotspot-marker", html: `<span>${h.km}</span>`, iconSize: null });
                L.marker([h.lat, h.lon], { icon, keyboard: false, zIndexOffset: -200 })
                    .bindTooltip(`${h.near ? `Near ${escape(h.near)} · ` : ""}${h.km} km of off-road riding`, { direction: "top", offset: [0, -12] })
                    .on("click", () => owner.invokeMethodAsync("MapClicked", h.lat, h.lon))
                    .addTo(layer);
            }
        })
        .catch(() => { /* none shown */ });
    return layer;
}

function drawOffRoad(context, data, zoom) {
    context.lineCap = context.lineJoin = "round";
    context.lineWidth = zoom >= 16 ? 3.5 : zoom >= 14 ? 2.5 : 1.8;
    for (const [band, geometry] of data.lines ?? []) {
        context.strokeStyle = offRoadColours[band];
        context.beginPath();
        const lines = geometry.type === "MultiLineString" ? geometry.coordinates : [geometry.coordinates];
        for (const line of lines)
            line.forEach(([x, y], i) => i === 0 ? context.moveTo(x, y) : context.lineTo(x, y));
        context.stroke();
    }
    for (const [x, y, km, band] of data.dots ?? []) {
        context.fillStyle = offRoadColours[band];
        context.globalAlpha = 0.7;
        context.beginPath();
        context.arc(x, y, Math.min(3 + Math.sqrt(km) / 2, 16), 0, 2 * Math.PI);
        context.fill();
        context.globalAlpha = 1;
    }
}

// The grids maps are cut on (T168). Every map but OS Explorer is on the web grid (EPSG:3857); OS
// Leisure, the Explorer-style 1:25 000, only on Britain's (EPSG:27700), so choosing it puts the live map
// on that grid, and choosing another puts it back. Britain's grid is given a zoom for every web zoom,
// each about the web zoom's ground resolution (OS's zoom z at the map's z + 7: its 7 m a pixel against
// the web grid's 5.9 at 52°N on zoom 14), so every zoom number in the app keeps its meaning.
const britishGrid = "EPSG:27700";
const osOrigin = [-238375, 1376256];
let britishCrs;
const crsFor = grid => grid === britishGrid
    ? britishCrs ??= new L.Proj.CRS(britishGrid,
        "+proj=tmerc +lat_0=49 +lon_0=-2 +k=0.9996012717 +x_0=400000 +y_0=-100000 +ellps=airy +towgs84=446.448,-125.157,542.06,0.15,0.247,0.842,-20.489 +units=m +no_defs",
        { origin: osOrigin, resolutions: Array.from({ length: 21 }, (_, z) => 896 / 2 ** (z - 7)), bounds: L.bounds([osOrigin[0], 0], [900000, osOrigin[1]]) })
    : L.CRS.EPSG3857;
const onBritishGrid = () => map.options.crs === britishCrs && britishCrs !== undefined;

// A map choosable under the Loop: added on a map on another grid, it moves the map to its own first,
// at the same centre and zoom, and the map goes no closer than it's readable (OS Explorer: 17).
// Before it's added (beforeAdd): once it listens to the map, the reset would reach it unready.
const GridTileLayer = L.TileLayer.extend({
    beforeAdd(on) {
        if (!useGrid(on, this.options.grid, this.options.maxZoom)) on.setMaxZoom(this.options.maxZoom);
    }
});

// The overlays cut on the web grid alone (Off-road networks, Signed trails, OS paths): off while the
// map is on Britain's grid, and back when it's not; their choice in the switcher stays as it was.
let webOnly = [];
let parked = [];
let switching = false;
let overlaysOffered = () => { };

// Moves the map to the grid, if it isn't on it already (false if it was), going no closer than maxZoom.
// Setting a live map's crs isn't Leaflet's documented API (1.9.4): recheck on upgrading (ADR 0008).
function useGrid(on, grid, maxZoom) {
    const crs = crsFor(grid);
    if (on.options.crs === crs) return false;
    const centre = on.getCenter();
    const zoom = Math.min(on.getZoom(), maxZoom);
    on.options.maxZoom = maxZoom;
    switching = true;
    try {
        if (grid === britishGrid)
            for (const layer of webOnly) if (on.hasLayer(layer)) { on.removeLayer(layer); parked.push(layer); }
        on.options.crs = crs;
        on.setView(centre, zoom, { reset: true });
        if (grid !== britishGrid) { parked.forEach(layer => on.addLayer(layer)); parked = []; }
    } finally {
        switching = false;
    }
    on.fire("zoomlevelschange");
    // After the switcher's click is done: offering the overlays again rebuilds its list, and a click
    // whose radio button has left the page isn't known as the switcher's any more, so the map takes it
    // as a click on the map, moving the start there.
    setTimeout(overlaysOffered);
    decorate();
    return true;
}

// An overlay turned on as the map opens: parked if it's web-grid only and the map is on Britain's.
function turnOn(layer) {
    if (webOnly.includes(layer) && onBritishGrid()) parked.push(layer);
    else layer.addTo(map);
}

// The key to the map (T177): told what's on screen, in its words, as that changes (once for a burst of
// changes), and whether it was left collapsed, remembered in this browser.
const keyCollapsedKey = "cutthrough.key.collapsed";
let keyedOverlays = {};
// The zoom each overlay is drawn from, if not every zoom (T180): on but further out, the key says to zoom in.
// Car parks: StartsAround.CarParkZoom in the app.
let keyedFrom = {};
let keyReport;
function reportKey() {
    if (!owner || keyReport) return;
    keyReport = setTimeout(() => {
        keyReport = undefined;
        const shown = [];
        if (startMarker && map.hasLayer(startMarker)) shown.push("start");
        if (finishMarker && map.hasLayer(finishMarker)) shown.push("finish");
        if (shownLoop && loopLayer && map.hasLayer(loopLayer)) {
            shown.push("loop");
            if (decorations && map.hasLayer(decorations)) shown.push("km");
            if (notesLayer?.getLayers().length) shown.push("notes");
            if (shownLoop.anchors?.length) shown.push("stops");
            if (shownLoop.avoids?.length) shown.push("avoids");
            if (shownLoop.mapGaps?.length) shown.push("map-gaps");
            if (shownLoop.possibleMapGaps?.length) shown.push("possible-gaps");
            if (shownLoop.gaps?.length) shown.push("not-on-map");
            if (shownLoop.customersOnly?.length) shown.push("customers");
        }
        if (marksLayer?.getLayers().length) shown.push("blocked");
        if (outingLayer) shown.push("outing");
        for (const [name, layer] of Object.entries(keyedOverlays)) if (map.hasLayer(layer)) shown.push(name);
        const from = name => typeof keyedFrom[name] === "function" ? keyedFrom[name]() : keyedFrom[name] ?? 0;
        const waiting = shown.filter(name => map.getZoom() < from(name));
        let collapsed = false;
        try { collapsed = localStorage.getItem(keyCollapsedKey) === "true"; } catch { /* open */ }
        owner.invokeMethodAsync("KeyShown", shown, waiting, collapsed);
    });
}

// How much of the map's right the key covers (T180), so a Loop or an Outing is fitted clear of it.
const keyWidth = () => Math.max(document.querySelector(".map-key")?.offsetWidth ?? 0, 30);

/** Keeps whether the key is collapsed (T177), for next time. */
export function rememberKeyCollapsed(collapsed) {
    try { localStorage.setItem(keyCollapsedKey, String(collapsed)); } catch { /* not kept */ }
}

// Until when a move is the map fitting a Loop or an Outing, not the user's.
let fittingUntil = 0;
let keyListener;

/**
 * Takes the map down as its page goes (T178): the module outlives the page (it's imported once), so
 * whatever it held of this map is let go, and a map made later starts afresh, its pins drawn on it.
 */
export function dispose() {
    if (keyListener) document.removeEventListener("keydown", keyListener);
    clearTimeout(keyReport);
    try { map?.remove(); } catch { /* its page has gone already */ }
    map = startMarker = finishMarker = loopLayer = shownLoop = decorations = measured = outingLayer = undefined;
    placeMarker = startsLayer = marksLayer = carParks = owner = notesLayer = routeCardPopup = keyListener = keyReport = undefined;
    routeShown = true;
    notes = [];
    webOnly = [];
    parked = [];
    switching = false;
    overlaysOffered = () => { };
    keyedOverlays = {};
    keyedFrom = {};
    fittingUntil = 0;
}
const fitting = () => { fittingUntil = Date.now() + 1000; };

function reportView() {
    const bounds = map.getBounds();
    owner.invokeMethodAsync("ViewChanged", bounds.getSouth(), bounds.getWest(), bounds.getNorth(), bounds.getEast(), map.getZoom());
}

/** Creates the map in `element`, centred on `centre` (or London), and reports clicks to `dotNet`. */
export function create(element, tiles, centre, dotNet) {
    dispose();
    // Every map the app offers, the one chosen last (or the first) on screen, and a switcher when
    // there's a choice. Each credits its maker while it's shown. The map opens on the chosen one's grid.
    // A map on Britain's grid only if its grid library loaded (T178): else a remembered OS Explorer would
    // leave the map blank on every visit.
    const usable = tiles.layers.filter(layer => layer.grid !== britishGrid || L.Proj?.CRS);
    const names = usable.map(layer => layer.name);
    const chosen = usable.find(layer => layer.name === chosenMap()) ?? usable[0];
    // No keyboard panning: the arrow keys page through the Loops (R10.5).
    map = L.map(element, { zoomControl: false, keyboard: false, crs: crsFor(chosen.grid) }).setView(centre ? point(centre) : london, 14);
    L.control.zoom({ position: "topright" }).addTo(map);
    L.control.scale({ position: "bottomright", imperial: false }).addTo(map);
    map.on("zoomend", drawNotes);
    const layers = {};
    for (const layer of usable)
        layers[layer.name] = new GridTileLayer(layer.url, {
            attribution: layer.attribution, maxZoom: layer.maxZoom, maxNativeZoom: layer.maxNativeZoom, subdomains: "abc",
            grid: layer.grid, minZoom: layer.minZoom ?? 0,
            // OS's zoom z on Britain's grid is the map's z + 7; OS Leisure covers Britain alone.
            ...(layer.grid === britishGrid ? { zoomOffset: -7, bounds: L.latLngBounds([49.388, -10.834], [62.266, 7.551]) } : {})
        });
    layers[chosen.name].addTo(map);
    // The Off-road networks (T022b), over whichever map, on if they were last time.
    const offRoad = offRoadNetworks();
    webOnly.push(offRoad);
    if (rememberedOverlay(offRoadName)) turnOn(offRoad);
    const amenities = cafesAndToilets();
    if (rememberedOverlay(amenitiesName)) amenities.addTo(map);
    // The Gravel hotspots (T072): on until turned off, all of them at every zoom.
    const hotspots = gravelHotspots();
    if (rememberedOverlay(hotspotsName, true)) hotspots.addTo(map);
    // The car parks around here (T070): on, as they always were, until turned off.
    carParks = L.layerGroup();
    if (rememberedOverlay(carParksName, true)) carParks.addTo(map);
    // The overlays from tiles elsewhere (T166: Signed trails), off until turned on, remembered.
    const tiledOverlays = {};
    for (const overlay of tiles.overlays ?? []) {
        const group = L.layerGroup(overlay.urls.map(url => L.tileLayer(url, {
            attribution: overlay.attribution, maxZoom: 20, maxNativeZoom: overlay.maxNativeZoom, zIndex: 2
        })));
        webOnly.push(group);
        if (rememberedOverlay(overlay.name)) turnOn(group);
        tiledOverlays[overlay.name] = group;
    }
    // Pointing at a signed route names it (T176), while an overlay of them is on.
    const naming = (tiles.overlays ?? []).filter(o => o.namesRoutes).map(o => tiledOverlays[o.name]);
    if (naming.length) nameRoutesOnPointing(() => naming.some(group => map.hasLayer(group)));
    // The areas read for the view (T170: Open access land), in a pane of their own over the map and
    // under the Loop, taking no clicks.
    const areasPane = map.createPane("areas");
    areasPane.style.zIndex = 350;
    areasPane.style.pointerEvents = "none";
    const areaOverlays = {};
    for (const overlay of tiles.areas ?? []) {
        const layer = areaOverlay(overlay);
        layer.options.attribution = overlay.attribution;
        if (rememberedOverlay(overlay.name)) layer.addTo(map);
        areaOverlays[overlay.name] = layer;
    }
    // The lines from vector tiles (T173: OS paths), in the same pane.
    const vectorOverlays = {};
    for (const overlay of tiles.vectors ?? []) {
        const layer = new VectorOverlayLayer(overlay);
        webOnly.push(layer);
        if (rememberedOverlay(overlay.name)) turnOn(layer);
        vectorOverlays[overlay.name] = layer;
    }
    const readForTheView = [amenities, ...Object.values(areaOverlays)];
    const overlays = { [offRoadName]: offRoad, ...tiledOverlays, ...areaOverlays, ...vectorOverlays, [amenitiesName]: amenities, [hotspotsName]: hotspots, [carParksName]: carParks };
    let offered = [];
    const switcher = L.control.layers(names.length > 1 ? layers : {}, {}, { position: "topright" }).addTo(map);
    // The overlays offered for the grid the map is on (T168): on Britain's, none cut on the web grid,
    // and a word under the list saying which are missing and why, so they're not looked for.
    const notOffered = L.DomUtil.create("div", "not-offered", switcher.getContainer().querySelector(".leaflet-control-layers-list"));
    const webOnlyNames = Object.keys(overlays).filter(name => webOnly.includes(overlays[name]));
    notOffered.textContent = `${webOnlyNames.join(", ")}: not drawn over OS Explorer, on Britain's own grid.`;
    overlaysOffered = () => {
        switching = true;
        offered.forEach(layer => switcher.removeLayer(layer));
        offered = [];
        for (const [name, layer] of Object.entries(overlays))
            if (!(webOnly.includes(layer) && onBritishGrid())) { switcher.addOverlay(layer, name); offered.push(layer); }
        switching = false;
        notOffered.hidden = !onBritishGrid() || webOnlyNames.length === 0;
        // Overlays put back on leaving Britain's grid come back unannounced (the switcher had let them
        // go), so the key is told afresh (T178).
        reportKey();
    };
    overlaysOffered();
    map.on("overlayadd", e => { if (readForTheView.includes(e.layer)) e.layer.refresh(); });
    map.on("moveend", () => readForTheView.forEach(layer => layer.refresh()));
    readForTheView.forEach(layer => layer.refresh());
    map.on("baselayerchange", e => rememberMap(e.name));
    map.on("overlayadd", e => { if (!switching) rememberOverlay(e.name, true); });
    map.on("overlayremove", e => { if (!switching) rememberOverlay(e.name, false); });
    map.attributionControl.setPrefix('<a href="https://leafletjs.com">Leaflet</a>');
    owner = dotNet;
    keyedOverlays = overlays;
    keyedFrom = { [amenitiesName]: amenitiesFromZoom, [carParksName]: 11 };
    for (const overlay of tiles.vectors ?? []) keyedFrom[overlay.name] = overlay.fromZoom;
    // An area overlay's zoom depends on where the view is (T170: Wales's from 12): asked each time.
    for (const [name, layer] of Object.entries(areaOverlays)) keyedFrom[name] = layer.fromZoomHere;
    map.on("overlayadd overlayremove moveend", reportKey);
    reportKey();
    // A click that closes a popup only closes it: the popups have no close button, so clicking
    // away is how they go, and that mustn't move the start (user, 2026-11-07).
    let popupClosedAt = 0;
    // Not the signed-trail card closing (T178): it's no popup the user clicked away.
    map.on("popupclose", e => { if (e.popup !== routeCardPopup) popupClosedAt = Date.now(); });
    map.on("click", e => {
        if (Date.now() - popupClosedAt < 300) return;
        dotNet.invokeMethodAsync("MapClicked", e.latlng.lat, e.latlng.lng);
    });
    // The part in view, as the user leaves it (T060): not when the map fits a Loop itself.
    let settling;
    map.on("moveend", () => {
        if (Date.now() < fittingUntil) return;
        clearTimeout(settling);
        settling = setTimeout(reportView, 250);
    });
    reportView();
    keyListener = e => {
        // Undo and Redo (T078), as everywhere else: Ctrl+Z, and Ctrl+Y or Ctrl+Shift+Z.
        if ((e.ctrlKey || e.metaKey) && !e.altKey && !typingIn(e.target)) {
            const key = e.key.toLowerCase();
            if (key === "z" || key === "y") {
                e.preventDefault();
                dotNet.invokeMethodAsync(key === "y" || e.shiftKey ? "RedoKey" : "UndoKey");
                return;
            }
        }
        const step = { ArrowLeft: -1, ArrowRight: 1 }[e.key];
        if (!step || e.altKey || e.ctrlKey || e.metaKey || typingIn(e.target)) return;
        e.preventDefault();
        dotNet.invokeMethodAsync("PageKey", step);
    };
    document.addEventListener("keydown", keyListener);
}

// A box the arrow keys move within: a text box, the Hills slider, a list.
function typingIn(target) {
    return target instanceof HTMLElement && (target.isContentEditable || ["INPUT", "SELECT", "TEXTAREA"].includes(target.tagName));
}

/** Takes the map to a place searched for (T064): a move like the user's, so the view is reported. */
export function goTo(place, zoom) {
    map.setView(point(place), zoom);
}

/** Marks where the next Request starts: dragged, the start moves there (T091). */
export function showStart(start) {
    const icon = L.divIcon({ className: "start-marker", iconSize: [22, 22], iconAnchor: [11, 11] });
    if (startMarker) startMarker.setLatLng(point(start));
    else startMarker = pin(point(start), icon, "Drag to move the start", "StartDragged");
    reportKey();
}

/** Marks where a Point-to-point ends (T091): dragged, the finish moves there. */
export function showFinish(finish) {
    const icon = L.divIcon({ className: "finish-marker", iconSize: [22, 22], iconAnchor: [11, 11] });
    if (finishMarker) finishMarker.setLatLng(point(finish));
    else finishMarker = removable(pin(point(finish), icon, "Drag to move the finish, or click to remove it", "FinishDragged"),
        "Remove the finish", () => owner.invokeMethodAsync("FinishRemoved"));
    reportKey();
}

/** Takes the finish off the map: a Loop again. */
export function clearFinish() {
    finishMarker?.remove();
    finishMarker = undefined;
    reportKey();
}

// A pin dragged to move what it marks; a click on it does nothing, never the map's beneath.
function pin(at, icon, title, dropped) {
    return L.marker(at, { icon, keyboard: false, draggable: true, title, bubblingMouseEvents: false })
        .on("dragend", e => {
            const to = e.target.getLatLng();
            owner.invokeMethodAsync(dropped, to.lat, to.lng);
        })
        .addTo(map);
}

/** Marks the places to start found (T025), each named with its drive; clicking one makes it the start. */
export function showStarts(starts, chosen) {
    clearStarts();
    startsLayer = carParks;
    starts.forEach((start, i) => {
        const icon = L.divIcon({ className: "trailhead-marker" + (i === chosen ? " chosen" : ""), html: "<span>P</span>", iconSize: [24, 24], iconAnchor: [12, 12] });
        L.marker(point(start.at), { icon, keyboard: false, title: start.label, zIndexOffset: i === chosen ? 500 : 0 })
            .bindTooltip(start.label, { direction: "top", offset: [0, -12] })
            .on("click", () => owner.invokeMethodAsync("StartClicked", i))
            .addTo(startsLayer);
    });
}

/** Takes the places to start off the map. */
export function clearStarts() {
    if (startsLayer) startsLayer.clearLayers();
    startsLayer = null;
}

/**
 * Draws a Loop: its line with white chevrons inside it showing the direction of travel, a marker
 * at each whole km, and its Anchors numbered in order.
 */
export function showLoop(loop, fit = true) {
    clearLoop();
    shownLoop = loop;
    const line = loop.line.map(point);
    loopLayer = L.layerGroup([
        L.polyline(line, { color: "#ffffff", weight: 12, opacity: 0.9, interactive: false }),
        L.polyline(line, { color: hiVis, weight: 8, opacity: 1, interactive: false }),
        // The Map gaps it crosses (#150, T146b): red and dashed, so the user checks each is passable.
        // Under the grab line, so Avoid this time and a Via work on a gap as on any way.
        ...(loop.mapGaps ?? []).flatMap(gap => {
            const gapLine = [point(gap.from), point(gap.to)];
            return [
                L.polyline(gapLine, { color: "#ffffff", weight: 10, opacity: 1, interactive: false }),
                L.polyline(gapLine, { color: gapColour, weight: 6, opacity: 1, dashArray: "6 5", interactive: false }),
                // A dot at its middle (T183), seen at any length: a 2 m gap is shorter than a dash.
                // Caution yellow ringed dark (user, 2026-11-23: "a different colour to stand out more"), not red beside the orange.
                L.circleMarker([(gap.from.latitude + gap.to.latitude) / 2, (gap.from.longitude + gap.to.longitude) / 2],
                    { radius: 6, color: gapDotRing, weight: 2.5, fillColor: gapDot, fillOpacity: 1, interactive: false }),
            ];
        }),
        // Wider than it looks, to grab (T076): dragged, a Via; clicked, Avoid this time. Never the map's
        // click beneath, which would move the start.
        nudgeable(L.polyline(line, { color: "#000", weight: 22, opacity: 0, bubblingMouseEvents: false, className: "loop-grab" })),
        // An imported route's gaps (T083), over the line: where the GPX left the map. A break in the
        // orange, white with an ink dotted line (T088): red read as more route. Clicked, nothing;
        // never the map beneath, which would move the start.
        ...(loop.gaps ?? []).flatMap(gap => {
            const gapLine = gap.line.map(point);
            return [
                L.polyline(gapLine, { color: "#ffffff", weight: 13, opacity: 1, interactive: false }),
                L.polyline(gapLine, { color: routeGapInk, weight: 4, opacity: 1, dashArray: "1 8", lineCap: "round", bubblingMouseEvents: false })
                    .bindTooltip(`Not on the map · ${Math.round(gap.lengthM)} m`, { sticky: true }),
            ];
        }),
        // The possible Map gaps near it (T146c), 5 m to 15 m, which no search uses: a straight red line, and
        // clicked, "Include this time", the Loop through it. Over the grab line, so a click finds them.
        ...(loop.possibleMapGaps ?? []).flatMap(gap => {
            const gapLine = [point(gap.from), point(gap.to)];
            const middle = [(gap.from.latitude + gap.to.latitude) / 2, (gap.from.longitude + gap.to.longitude) / 2];
            return [
                // Grey and dashed (T183): an option near the Loop, not part of it, unlike its red.
                L.polyline(gapLine, { color: "#ffffff", weight: 7, opacity: 0.9, interactive: false }),
                L.polyline(gapLine, { color: possibleGapColour, weight: 3, opacity: 1, dashArray: "4 4", interactive: false }),
                removable(L.polyline(gapLine, { color: "#000", weight: 18, opacity: 0, bubblingMouseEvents: false })
                    .bindTooltip(`A possible gap in the map · ${Math.round(gap.lengthM)} m`, { sticky: true }),
                    "Include this time", () => owner.invokeMethodAsync("GapIncluded", middle[0], middle[1])),
            ];
        }),
        // Where it runs on ways for customers only (T182): striped white over its orange, to check you may
        // pass. Taking no clicks, so a Nudge works there as anywhere.
        ...(loop.customersOnly ?? []).map(s => L.polyline(line.slice(s.from, s.to + 1), { color: "#ffffff", weight: 4, opacity: 1, dashArray: "6 8", lineCap: "butt", interactive: false })),
        // Its stops, numbered, and its Avoids, each "!": clicked, each can be removed (T079).
        ...loop.anchors.map((anchor, i) => removable(L.marker(point(anchor), {
            icon: L.divIcon({ className: "anchor-marker", html: String(i + 1), iconSize: [24, 24], iconAnchor: [12, 12] }),
            keyboard: false, title: "A stop: click to remove it",
        }), "Remove this stop", () => owner.invokeMethodAsync("StopRemoved", i))),
        ...(loop.avoids ?? []).map((avoid, i) => removable(L.marker(point(avoid), {
            icon: L.divIcon({ className: "avoid-marker", html: "!", iconSize: [22, 22], iconAnchor: [11, 11] }),
            keyboard: false, title: "Avoided: click to allow it again",
        }), "Allow this again", () => owner.invokeMethodAsync("AvoidRemoved", i))),
    ]);
    if (routeShown) loopLayer.addTo(map);
    measured = measure(line);
    map.on("zoomend", decorate);

    // The Loop starts where the clicked start snapped to.
    showStart(loop.start);
    // Fitted only when it's a different route; a change to the one shown stays where the map is (T087).
    if (fit) {
        fitting();
        map.fitBounds(L.latLngBounds(line), { paddingTopLeft: [visibleLeft() + 40, 40], paddingBottomRight: [keyWidth() + 30, 110] });
    }
    decorate();
    reportKey();
}

/**
 * Puts the Loop on the map, or takes it off, its chevrons and km markers with it, to see the ground
 * under it (T071); a Loop shown later follows. The start stays.
 */
export function showRoute(shown) {
    routeShown = shown;
    if (loopLayer) shown ? loopLayer.addTo(map) : loopLayer.remove();
    drawNotes();
    if (shown) decorate();
    else if (decorations) decorations.remove();
    reportKey();
}

// The route's line taking Nudges (T076): dragged from where it's grabbed to where it's dropped, the
// map held still meanwhile, a dashed line following the pointer; or clicked without moving, a
// popup offering "Avoid this time" there.
function nudgeable(line) {
    let dragged = false;
    line.on("mousedown", e => {
        if (e.originalEvent.button !== 0) return;
        L.DomEvent.stop(e);
        const from = e.latlng;
        const start = e.containerPoint;
        let ghost = null;
        map.dragging.disable();
        const move = event => {
            const at = map.mouseEventToContainerPoint(event);
            if (!ghost && at.distanceTo(start) < 6) return;
            const to = map.containerPointToLatLng(at);
            if (ghost) ghost.setLatLngs([from, to]);
            else ghost = L.polyline([from, to], { color: hiVis, weight: 4, dashArray: "6 8", interactive: false }).addTo(map);
        };
        const up = event => {
            document.removeEventListener("mousemove", move);
            document.removeEventListener("mouseup", up);
            map.dragging.enable();
            if (ghost) {
                dragged = true;
                ghost.remove();
                // Down on the line, up on the map: the browser clicks the map where it's dropped,
                // which would move the start. That one click is swallowed.
                const swallow = click => { click.stopPropagation(); click.preventDefault(); };
                window.addEventListener("click", swallow, { capture: true, once: true });
                setTimeout(() => { window.removeEventListener("click", swallow, { capture: true }); dragged = false; }, 400);
                const to = map.mouseEventToLatLng(event);
                owner.invokeMethodAsync("RouteDragged", from.lat, from.lng, to.lat, to.lng);
            }
        };
        document.addEventListener("mousemove", move);
        document.addEventListener("mouseup", up);
    });
    // On the click, not the mouse coming up: Leaflet closes popups as a click begins.
    line.on("click", e => {
        L.DomEvent.stop(e);
        if (dragged) dragged = false;
        else offerAvoid(e.latlng);
    });
    return line;
}

function offerAvoid(at) {
    const content = L.DomUtil.create("div", "route-popup");
    const button = L.DomUtil.create("button", "avoid-this", content);
    button.type = "button";
    button.textContent = "Avoid this time";
    // The map itself fixed where it's fixed best (T077): OSM's own editor, the way under the route
    // here selected, or its note form; Wend Your Way sends OSM nothing.
    const way = wayAt(at);
    const place = `19/${at.lat.toFixed(6)}/${at.lng.toFixed(6)}`;
    const link = (text, href) => {
        const a = L.DomUtil.create("a", "osm-link", content);
        a.textContent = text;
        a.href = href;
        a.target = "_blank";
        a.rel = "noopener noreferrer";
        return a;
    };
    // Blocked for good (T082): no Loop uses it from now on.
    const block = L.DomUtil.create("button", "block-this", content);
    block.type = "button";
    block.textContent = "Never use this";
    L.DomEvent.on(block, "click", event => {
        L.DomEvent.stop(event);
        map.closePopup();
        owner.invokeMethodAsync("BlockClicked", at.lat, at.lng);
    });
    const fix = link("Fix the map here", `https://www.openstreetmap.org/edit?editor=id${way ? `&way=${way}` : ""}#map=${place}`);
    link("Report a problem here", `https://www.openstreetmap.org/note/new#map=${place}`);
    const popup = L.popup({ className: "nudge-popup", offset: [0, -6], closeButton: false }).setLatLng(at).setContent(content).openOn(map);
    // An OSM edit reaches Wend Your Way only at the next rebuild: until then, "Never use this".
    L.DomEvent.on(fix, "click", () => {
        if (content.querySelector(".fix-hint")) return;
        L.DomUtil.create("p", "fix-hint", content).textContent =
            "Your edit reaches Wend Your Way at the next rebuild. Until then, \u201cNever use this\u201d keeps it off your Loops.";
        block.classList.add("suggested");
        popup.update();
    });
    L.DomEvent.on(button, "click", event => {
        L.DomEvent.stop(event);
        map.closePopup(popup);
        owner.invokeMethodAsync("AvoidClicked", at.lat, at.lng);
    });
}

// A stop or an Avoid's marker, clicked: a popup with the one thing it offers. Never the map's
// click beneath, which would move the start.
function removable(marker, label, remove) {
    marker.on("click", e => {
        L.DomEvent.stop(e);
        const button = L.DomUtil.create("button", "avoid-this");
        button.type = "button";
        button.textContent = label;
        const popup = L.popup({ className: "nudge-popup", offset: [0, -10], closeButton: false }).setLatLng(e.latlng).setContent(button).openOn(map);
        L.DomEvent.on(button, "click", event => {
            L.DomEvent.stop(event);
            map.closePopup(popup);
            remove();
        });
    });
    return marker;
}

/**
 * Draws the Blocked Segments (T082) in red, dashed, over the map and under the route; clicked,
 * each offers "Unblock" and OSM's note form there.
 */
export function showMarks(marks) {
    if (marksLayer) marksLayer.remove();
    marksLayer = L.layerGroup(marks.map(mark => {
        // Dotted (T180): a gap in the map is dashed red too.
        const line = L.polyline(mark.line.map(point), { color: danger, weight: 6, dashArray: "1 9", lineCap: "round", opacity: 0.9, bubblingMouseEvents: false, className: "blocked" });
        line.on("click", e => {
            L.DomEvent.stop(e);
            const content = L.DomUtil.create("div", "route-popup");
            L.DomUtil.create("strong", "blocked-title", content).textContent = "Blocked";
            const unblock = L.DomUtil.create("button", "avoid-this", content);
            unblock.type = "button";
            unblock.textContent = "Unblock";
            L.DomEvent.on(unblock, "click", event => {
                L.DomEvent.stop(event);
                map.closePopup();
                owner.invokeMethodAsync("UnblockClicked", mark.id);
            });
            const a = L.DomUtil.create("a", "osm-link", content);
            a.textContent = "Report a problem here";
            a.href = `https://www.openstreetmap.org/note/new#map=19/${e.latlng.lat.toFixed(6)}/${e.latlng.lng.toFixed(6)}`;
            a.target = "_blank";
            a.rel = "noopener noreferrer";
            L.popup({ className: "nudge-popup", offset: [0, -6], closeButton: false }).setLatLng(e.latlng).setContent(content).openOn(map);
        });
        return line;
    })).addTo(map);
    reportKey();
}

// The OSM way under the piece of the Loop's line nearest a place, if it's known (0 on Open ground).
function wayAt(at) {
    if (!shownLoop || !shownLoop.ways || shownLoop.ways.length !== shownLoop.line.length) return 0;
    const p = map.latLngToLayerPoint(at);
    let [best, bestPx] = [1, Infinity];
    for (let i = 1; i < shownLoop.line.length; i++) {
        const a = map.latLngToLayerPoint(point(shownLoop.line[i - 1]));
        const b = map.latLngToLayerPoint(point(shownLoop.line[i]));
        const px = L.LineUtil.pointToSegmentDistance(p, a, b);
        if (px < bestPx) [best, bestPx] = [i, px];
    }
    return shownLoop.ways[best];
}

/** Marks a place on the Loop, pointed at on its elevation profile. */
export function markPlace(place) {
    if (placeMarker) placeMarker.setLatLng(point(place));
    else placeMarker = L.circleMarker(point(place), {
        radius: 7, color: "#ffffff", weight: 3, fillColor: hiVis, fillOpacity: 1, interactive: false,
    }).addTo(map);
}

/** Takes the place marked off the map. */
export function clearPlace() {
    if (placeMarker) placeMarker.remove();
    placeMarker = undefined;
}

/** Takes the last Loop off the map. */
/**
 * Explain? (T157, #161): the route's notes, each a label where it is: its Crossings and Obstacles,
 * its stretches of road, its longest green stretch. Each said from its zoom in, so zoomed out the map
 * isn't buried; none while the route is hidden. None given takes them off.
 */
export function showNotes(shown) {
    notes = shown ?? [];
    drawNotes();
}

function drawNotes() {
    if (notesLayer) notesLayer.remove();
    notesLayer = undefined;
    reportKey();
    if (!map || notes.length === 0 || !routeShown) return;
    const escape = text => text.replace(/[&<>"']/g, c => `&#${c.charCodeAt(0)};`);
    const zoom = map.getZoom();
    notesLayer = L.layerGroup(notes.filter(n => zoom >= n.fromZoom).map(n => L.marker(point(n.at), {
        icon: L.divIcon({ className: `note-marker ${n.kind}`, html: `<span>${escape(n.words)}</span>`, iconSize: null }),
        keyboard: false, interactive: false, zIndexOffset: 400
    }))).addTo(map);
    reportKey();
}

export function clearLoop() {
    clearPlace();
    notes = [];
    drawNotes();
    map.off("zoomend", decorate);
    if (decorations) decorations.remove();
    if (loopLayer) loopLayer.remove();
    loopLayer = decorations = measured = undefined;
    reportKey();
}

// The line's points with the distance travelled to each, so positions along it are cheap.
function measure(line) {
    const points = line.map(p => L.latLng(p));
    const along = [0];
    for (let i = 1; i < points.length; i++) along.push(along[i - 1] + points[i - 1].distanceTo(points[i]));
    return { points, along, length: along[along.length - 1] };
}

// Where the Loop is, and which way it's heading, after `metres` of it.
function pointAt(metres) {
    const { points, along } = measured;
    let i = 1;
    while (i < points.length - 1 && along[i] < metres) i++;
    const a = points[i - 1];
    const b = points[i];
    const t = along[i] > along[i - 1] ? (metres - along[i - 1]) / (along[i] - along[i - 1]) : 0;
    return { at: L.latLng(a.lat + ((b.lat - a.lat) * t), a.lng + ((b.lng - a.lng) * t)), heading: bearing(a, b) };
}

// Redraws the chevrons and distance markers for the current zoom.
function decorate() {
    if (!measured) return;
    if (decorations) decorations.remove();
    if (!routeShown) return;
    // Measured on the map, so on any grid (T168): 100 px across its middle.
    const middle = map.getSize().divideBy(2);
    const metresPerPixel = map.distance(map.containerPointToLatLng(middle), map.containerPointToLatLng(middle.add([100, 0]))) / 100;

    const layers = [];
    // No chevron under a mark on the line (user, 2026-11-23: one showing through a stop "looks odd"):
    // its stops, Avoids, start, finish and the dots on its gaps.
    const marks = [shownLoop?.start, ...(shownLoop?.anchors ?? []), ...(shownLoop?.avoids ?? []),
        ...(shownLoop?.mapGaps ?? []).map(g => ({ latitude: (g.from.latitude + g.to.latitude) / 2, longitude: (g.from.longitude + g.to.longitude) / 2 }))]
        .filter(Boolean).map(m => map.latLngToLayerPoint(point(m)));
    if (finishMarker) marks.push(map.latLngToLayerPoint(finishMarker.getLatLng()));
    const clearOfMarks = at => { const p = map.latLngToLayerPoint(at); return marks.every(m => m.distanceTo(p) > 16); };
    const arrowEvery = arrowEveryPixels * metresPerPixel;
    for (let d = arrowEvery / 2; d < measured.length; d += arrowEvery) {
        const { at, heading } = pointAt(d);
        if (!clearOfMarks(at)) continue;
        layers.push(L.marker(at, {
            icon: L.divIcon({
                className: "direction-arrow",
                html: `<svg viewBox="0 0 12 12" style="transform: rotate(${heading}deg)"><path d="M2.5 7.75 6 4.25l3.5 3.5"/></svg>`,
                iconSize: [12, 12], iconAnchor: [6, 6],
            }),
            keyboard: false, interactive: false,
        }));
    }

    // No marker so near the finish that it sits on the start.
    const stepKm = markerSteps.find(km => km * 1000 / metresPerPixel >= markerEveryPixels) ?? markerSteps[markerSteps.length - 1];
    for (let km = stepKm; km * 1000 < measured.length - 300; km += stepKm) {
        layers.push(L.marker(pointAt(km * 1000).at, {
            icon: L.divIcon({ className: "distance-marker", html: `${km} km`, iconSize: null }),
            keyboard: false, interactive: false,
        }));
    }
    decorations = L.layerGroup(layers).addTo(map);
}

function bearing(a, b) {
    const toRadians = Math.PI / 180;
    const y = Math.sin((b.lng - a.lng) * toRadians) * Math.cos(b.lat * toRadians);
    const x = (Math.cos(a.lat * toRadians) * Math.sin(b.lat * toRadians))
        - (Math.sin(a.lat * toRadians) * Math.cos(b.lat * toRadians) * Math.cos((b.lng - a.lng) * toRadians));
    return Math.atan2(y, x) / toRadians;
}

// How much of the map's left edge the rail and panel cover.
function visibleLeft() {
    const panel = document.querySelector(".panel");
    const rail = document.querySelector(".rail");
    return (rail ? rail.offsetWidth : 0) + (panel ? panel.offsetWidth : 0);
}

/**
 * Draws an Outing (STORY-030): the Segments it covered, wide and coloured by ground, its GPS
 * track thin and dark over them, and its gaps in red, each labelled with its number and length.
 */
export function showOuting(outing) {
    clearOuting();
    const track = outing.track.map(point);
    outingLayer = L.layerGroup([
        ...outing.covered.map(segment => L.polyline(segment.line.map(point), {
            color: groundColours[segment.ground] ?? groundColours.connector, weight: 8, opacity: 0.75, interactive: false,
        })),
        L.polyline(track, { color: routeGapInk, weight: 2, opacity: 0.9, interactive: false }),
        ...outing.gaps.flatMap(gap => {
            const line = gap.line.map(point);
            return [
                L.polyline(line, { color: "#ffffff", weight: 9, opacity: 0.9, interactive: false }),
                L.polyline(line, { color: gapColour, weight: 5, opacity: 1 })
                    .bindTooltip(`Gap ${gap.number} · ${Math.round(gap.lengthM)} m`, { sticky: true }),
            ];
        }),
    ]).addTo(map);
    fitting();
    map.fitBounds(L.latLngBounds(track), { paddingTopLeft: [visibleLeft() + 40, 40], paddingBottomRight: [keyWidth() + 30, 40] });
    reportKey();
}

/** Takes the last Outing off the map. */
export function clearOuting() {
    if (outingLayer) outingLayer.remove();
    outingLayer = undefined;
    reportKey();
}
