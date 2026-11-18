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

// An Outing's covered Segments by ground (STORY-030). Hi-vis orange stays the Loop's own.
const groundColours = { off_road: "#2e7d32", traffic_free: "#1f78c8", connector: "#8a8f8c" };
const gapColour = "#d62828";
// A route's gaps (T088): a break in the orange, dotted in ink, so they never read as more route.
const routeGapInk = "#1f2a24";

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
const offRoadColours = ["rgba(110, 120, 115, 0.45)", "#9ccc65", "#4caf50", "#2e7d32", "#0f4d24"];

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

// Until when a move is the map fitting a Loop or an Outing, not the user's.
let fittingUntil = 0;
const fitting = () => { fittingUntil = Date.now() + 1000; };

function reportView() {
    const bounds = map.getBounds();
    owner.invokeMethodAsync("ViewChanged", bounds.getSouth(), bounds.getWest(), bounds.getNorth(), bounds.getEast(), map.getZoom());
}

/** Creates the map in `element`, centred on `centre` (or London), and reports clicks to `dotNet`. */
export function create(element, tiles, centre, dotNet) {
    // No keyboard panning: the arrow keys page through the Loops (R10.5).
    map = L.map(element, { zoomControl: false, keyboard: false }).setView(centre ? point(centre) : london, 14);
    L.control.zoom({ position: "topright" }).addTo(map);
    L.control.scale({ position: "bottomright", imperial: false }).addTo(map);
    // Every map the app offers, the one chosen last (or the first) on screen, and a switcher when
    // there's a choice. Each credits its maker while it's shown.
    const layers = {};
    for (const layer of tiles.layers)
        layers[layer.name] = L.tileLayer(layer.url, {
            attribution: layer.attribution, maxZoom: 20, maxNativeZoom: layer.maxNativeZoom, subdomains: "abc"
        });
    const names = Object.keys(layers);
    layers[names.includes(chosenMap()) ? chosenMap() : names[0]].addTo(map);
    // The Off-road networks (T022b), over whichever map, on if they were last time.
    const offRoad = offRoadNetworks();
    if (rememberedOverlay(offRoadName)) offRoad.addTo(map);
    const amenities = cafesAndToilets();
    if (rememberedOverlay(amenitiesName)) amenities.addTo(map);
    // The Gravel hotspots (T072): on until turned off, all of them at every zoom.
    const hotspots = gravelHotspots();
    if (rememberedOverlay(hotspotsName, true)) hotspots.addTo(map);
    // The car parks around here (T070): on, as they always were, until turned off.
    carParks = L.layerGroup();
    if (rememberedOverlay(carParksName, true)) carParks.addTo(map);
    L.control.layers(names.length > 1 ? layers : {}, { [offRoadName]: offRoad, [amenitiesName]: amenities, [hotspotsName]: hotspots, [carParksName]: carParks }, { position: "topright" }).addTo(map);
    map.on("overlayadd", e => { if (e.layer === amenities) amenities.refresh(); });
    map.on("moveend", () => amenities.refresh());
    amenities.refresh();
    map.on("baselayerchange", e => rememberMap(e.name));
    map.on("overlayadd", e => rememberOverlay(e.name, true));
    map.on("overlayremove", e => rememberOverlay(e.name, false));
    map.attributionControl.setPrefix('<a href="https://leafletjs.com">Leaflet</a>');
    owner = dotNet;
    // A click that closes a popup only closes it: the popups have no close button, so clicking
    // away is how they go, and that mustn't move the start (user, 2026-11-07).
    let popupClosedAt = 0;
    map.on("popupclose", () => { popupClosedAt = Date.now(); });
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
    document.addEventListener("keydown", e => {
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
    });
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
}

/** Marks where a Point-to-point ends (T091): dragged, the finish moves there. */
export function showFinish(finish) {
    const icon = L.divIcon({ className: "finish-marker", iconSize: [22, 22], iconAnchor: [11, 11] });
    if (finishMarker) finishMarker.setLatLng(point(finish));
    else finishMarker = removable(pin(point(finish), icon, "Drag to move the finish, or click to remove it", "FinishDragged"),
        "Remove the finish", () => owner.invokeMethodAsync("FinishRemoved"));
}

/** Takes the finish off the map: a Loop again. */
export function clearFinish() {
    finishMarker?.remove();
    finishMarker = undefined;
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
        L.polyline(line, { color: "#e8590c", weight: 8, opacity: 1, interactive: false }),
        // The Map gaps it crosses (#150, T146b): red and dashed, so the user checks each is passable.
        // Under the grab line, so Avoid this time and a Via work on a gap as on any way.
        ...(loop.mapGaps ?? []).flatMap(gap => {
            const gapLine = [point(gap.from), point(gap.to)];
            return [
                L.polyline(gapLine, { color: "#ffffff", weight: 10, opacity: 1, interactive: false }),
                L.polyline(gapLine, { color: gapColour, weight: 6, opacity: 1, dashArray: "6 5", interactive: false }),
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
                L.polyline(gapLine, { color: "#ffffff", weight: 7, opacity: 0.9, interactive: false }),
                L.polyline(gapLine, { color: gapColour, weight: 3, opacity: 1, interactive: false }),
                removable(L.polyline(gapLine, { color: "#000", weight: 18, opacity: 0, bubblingMouseEvents: false })
                    .bindTooltip(`A possible gap in the map · ${Math.round(gap.lengthM)} m`, { sticky: true }),
                    "Include this time", () => owner.invokeMethodAsync("GapIncluded", middle[0], middle[1])),
            ];
        }),
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
        map.fitBounds(L.latLngBounds(line), { paddingTopLeft: [visibleLeft() + 40, 40], paddingBottomRight: [60, 110] });
    }
    decorate();
}

/**
 * Puts the Loop on the map, or takes it off, its chevrons and km markers with it, to see the ground
 * under it (T071); a Loop shown later follows. The start stays.
 */
export function showRoute(shown) {
    routeShown = shown;
    if (loopLayer) shown ? loopLayer.addTo(map) : loopLayer.remove();
    if (shown) decorate();
    else if (decorations) decorations.remove();
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
            else ghost = L.polyline([from, to], { color: "#e8590c", weight: 4, dashArray: "6 8", interactive: false }).addTo(map);
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
        const line = L.polyline(mark.line.map(point), { color: "#c92a2a", weight: 6, dashArray: "6 6", opacity: 0.9, bubblingMouseEvents: false, className: "blocked" });
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
        radius: 7, color: "#ffffff", weight: 3, fillColor: "#e8590c", fillOpacity: 1, interactive: false,
    }).addTo(map);
}

/** Takes the place marked off the map. */
export function clearPlace() {
    if (placeMarker) placeMarker.remove();
    placeMarker = undefined;
}

/** Takes the last Loop off the map. */
export function clearLoop() {
    clearPlace();
    map.off("zoomend", decorate);
    if (decorations) decorations.remove();
    if (loopLayer) loopLayer.remove();
    loopLayer = decorations = measured = undefined;
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
    const metresPerPixel = 40075016.686 * Math.cos(map.getCenter().lat * Math.PI / 180) / (256 * 2 ** map.getZoom());

    const layers = [];
    const arrowEvery = arrowEveryPixels * metresPerPixel;
    for (let d = arrowEvery / 2; d < measured.length; d += arrowEvery) {
        const { at, heading } = pointAt(d);
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
        L.polyline(track, { color: "#1f2a24", weight: 2, opacity: 0.9, interactive: false }),
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
    map.fitBounds(L.latLngBounds(track), { paddingTopLeft: [visibleLeft() + 40, 40], paddingBottomRight: [40, 40] });
}

/** Takes the last Outing off the map. */
export function clearOuting() {
    if (outingLayer) outingLayer.remove();
    outingLayer = undefined;
}
