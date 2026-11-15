# Route on OpenStreetMap; view in the user's OS Maps app via GPX

Loops are generated on a Network built from OpenStreetMap, with public right-of-way status checked against (and filled in from) council Definitive Map data where it is published. OS mapping is not used for routing: OS Explorer is only a picture, and OS vector data lacks the surfaces, alleys, footbridges and access detail the generator needs. The Basemap is kept separate from the Network. We do not license OS tiles: the app shows Loops on an OSM-based Basemap, and exports GPX so the user can open a Loop on OS Explorer in the OS Maps app they already pay for.

The Network covers all of England and Wales from the start (OSM and national traffic counts are cheap to hold nationally), because Chosen Outings from across the country are needed for their Way Choices. Definitive Map data is fetched one highway authority at a time, the first time a Request or an Outing touches that authority, rather than all at once. The source is rowmaps.com, which republishes open-licensed (mostly Open Government Licence) right-of-way data for about 149 authorities as per-authority GeoJSON, so no scraping or paid subscription is needed. We deliberately do not scrape the rights-of-way layer from OS Maps or similar apps.

## Consequences

- Network quality is bounded by OSM tagging in the user's area. Missing or wrong footpath, bridleway or surface tags directly produce bad Loops, which is why the Definitive Map cross-check exists.
- A GPX export is a first-class output, not an afterthought.
- There is no Definitive Map data for inner London (the boroughs were never required to keep one) or for several outer London boroughs, including Barnet, Enfield, Croydon, Merton and Richmond. Much of the user's home ground therefore relies on OSM alone.
- Published right-of-way data is a digitised copy, not the legal record itself; it is good enough for routing.
