# OS Explorer switches the live map to Britain's grid, keeping web zoom numbers

Ordnance Survey serves its Leisure map (the Explorer-style 1:25 000) only on British National Grid (`Leisure_27700`), while every other map in the planner is on the web grid (EPSG:3857). When OS Explorer is chosen, the live Leaflet map moves to Britain's grid **in place**: `map.options.crs` is set, then `setView(centre, zoom, { reset: true })`. Choosing any other map moves it back. Paths, pins, areas and the Loop are all lat/lng layers, so they re-project themselves on the reset.

Britain's grid is given **one zoom per web zoom**: resolution `896 / 2^(z − 7)` m a pixel, from OS's origin (−238375, 1376256). The tile layer uses `zoomOffset: −7`, so the map's zoom z asks OS for its zoom z − 7.
- At the same number, Explorer is about 0.2–0.4 of a zoom further out than the web grid (7 m a pixel at 14, against 5.9 at 52°N).
- Every zoom threshold in the app keeps its meaning without translation: cafés from 13, car parks from 11, search zooms, note zooms, and the zoom sent with the view.

The map goes no closer than zoom 17 while Explorer is chosen (OS's deepest, 9, is the map's 16).

## Considered options

- **Recreate the map on the other grid.** Rejected after the spike (T168): every pane, layer, popup and listener would need rebuilding, about a day more, and in-place switching worked with 0 px error over repeated switches.
- **Warp OS's tiles onto the web grid in the app.** Rejected: an image library, a tile proxy and cache, rotated and blurred lettering, and a question over OS's terms.
- **Britain's grid with OS's own zoom numbers.** Rejected: every zoom number in the app, in C# and JS, would need translating.

## Consequences

- **Setting a live map's `crs` isn't Leaflet's documented API.** It works on 1.9.4. Recheck on any Leaflet upgrade (2.0 changes internals); the fallback is recreating the map.
- **Off-road networks, Signed trails and OS paths are cut on the web grid** (OS paths' MapLibre renders Mercator only). While Explorer is chosen they're parked and taken out of the switcher, which says why. The remembered choices are kept, and the overlays come back with any other map.
- **Chevrons and km markers** are spaced by measuring on the map, not by the web grid's formula.
- **The switcher's list is rebuilt after its click is done.** Rebuilt during the click, the clicked radio button leaves the page, and Leaflet takes the click as one on the map, moving the start.
- **`getBounds()` on the rotated grid** under-covers the view's corners slightly (about 0.2° of grid convergence in mid-England, up to 2–3° at the edges). Left as it is.
