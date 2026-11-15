-- osm2pgsql flex style for Wend Your Way (PLAN T004, T030, T035).
--
-- Everything the plan needs from OpenStreetMap, kept in one go since each change costs a Great
-- Britain re-import (ARCHITECTURE, "The osm2pgsql style, designed in one go"):
--   osm.ways        every way a Run, Ride or drive could use, with its tags, its node IDs in
--                   order and its geometry; split into Segments and classified afterwards in SQL
--                   (build_segments(), classify_ground(), run_access()), next to the data
--   osm.areas       green and open land, water, cemeteries, car parks and pedestrian squares
--   osm.points      gates, stiles and other barriers, crossings, viewpoints, water, cafés, parking
--   osm.routes      named walking and cycling routes, and osm.route_ways their ways in order
--   osm.lines       barriers, railways above ground and waterways: what Open ground mustn't
--                   cross, and rivers and canals mapped only as lines (T048)
--   osm.countries   England, Wales and Scotland, for the England and Wales clip
-- Each keeps the object's tags whole, so a later rule can read a tag without another import.
--
-- Run with:  osm2pgsql --create --output=flex --style cutthrough.lua -d "<conninfo>" <file>

local routable = {
    motorway = true, motorway_link = true,
    trunk = true, trunk_link = true, primary = true, primary_link = true,
    secondary = true, secondary_link = true, tertiary = true, tertiary_link = true,
    unclassified = true, residential = true, living_street = true, service = true, road = true,
    footway = true, path = true, pedestrian = true, track = true, steps = true,
    cycleway = true, bridleway = true, byway = true,
}

-- Walkways drawn as areas, walked along their edge as other routers do.
local walked_areas = { pedestrian = true, footway = true }

local ways = osm2pgsql.define_way_table('ways', {
    { column = 'tags', type = 'jsonb', not_null = true },
    -- Node IDs in order. build_segments() relies on the geometry having exactly one vertex
    -- per entry here; it checks, and counts any way where that doesn't hold.
    { column = 'nodes', sql_type = 'int8[]', not_null = true },
    { column = 'geom', type = 'linestring', projection = 4326, not_null = true },
}, { schema = 'osm' })

-- Land kept as areas, by key and value.
local area_kinds = {
    leisure = { park = true, garden = true, nature_reserve = true, common = true, recreation_ground = true,
                pitch = true, golf_course = true },
    landuse = { forest = true, grass = true, meadow = true, recreation_ground = true, village_green = true,
                cemetery = true, allotments = true, orchard = true, military = true },
    natural = { wood = true, heath = true, grassland = true, scrub = true, moor = true, fell = true,
                wetland = true, beach = true, water = true },
    waterway = { riverbank = true },
    amenity = { grave_yard = true, parking = true },
}

local areas = osm2pgsql.define_area_table('areas', {
    { column = 'tags', type = 'jsonb', not_null = true },
    { column = 'geom', type = 'multipolygon', projection = 4326, not_null = true },
}, { schema = 'osm' })

local function is_kept_area(tags)
    for key, values in pairs(area_kinds) do
        if values[tags[key]] then return true end
    end
    return tags.area == 'yes' and walked_areas[tags.highway] == true
end

-- Points kept, by key and value (true for any value).
local point_kinds = {
    barrier = true,
    crossing = true,
    highway = { crossing = true, traffic_signals = true },
    railway = { level_crossing = true, crossing = true },
    tourism = { viewpoint = true, attraction = true, picnic_site = true },
    historic = true,
    natural = { peak = true },
    amenity = { drinking_water = true, toilets = true, cafe = true, pub = true, parking = true,
                parking_entrance = true },
}

local points = osm2pgsql.define_node_table('points', {
    { column = 'tags', type = 'jsonb', not_null = true },
    { column = 'geom', type = 'point', projection = 4326, not_null = true },
}, { schema = 'osm' })

-- Lines kept, by key and value (T048): barriers, railways above ground, waterways.
local line_kinds = {
    barrier = { fence = true, wall = true, hedge = true, city_wall = true, retaining_wall = true },
    railway = { rail = true, light_rail = true, narrow_gauge = true },
    waterway = { river = true, canal = true, stream = true, ditch = true, drain = true },
}

local lines = osm2pgsql.define_way_table('lines', {
    { column = 'tags', type = 'jsonb', not_null = true },
    { column = 'geom', type = 'linestring', projection = 4326, not_null = true },
}, { schema = 'osm' })

local function is_kept_line(tags)
    if tags.tunnel and tags.tunnel ~= 'no' then return false end
    for key, values in pairs(line_kinds) do
        if values[tags[key]] then return true end
    end
    return false
end

function osm2pgsql.process_node(object)
    for key, values in pairs(point_kinds) do
        local value = object.tags[key]
        if value and (values == true or values[value]) then
            points:insert({ tags = object.tags, geom = object:as_point() })
            return
        end
    end
end

local function insert_way(object)
    -- osm2pgsql's linestring collapses a node listed twice in a row; do the same to the IDs so
    -- they stay one-to-one with the vertices. (It also drops missing nodes and collapses
    -- different nodes at the same spot, which can't be seen from here: build_segments()
    -- detects those ways and counts them.)
    local ids, previous = {}, nil
    for _, id in ipairs(object.nodes) do
        if id ~= previous then
            ids[#ids + 1] = id
            previous = id
        end
    end

    ways:insert({
        tags = object.tags,
        nodes = '{' .. table.concat(ids, ',') .. '}',
        geom = object:as_linestring(),
    })
end

function osm2pgsql.process_way(object)
    local tags = object.tags
    if object.is_closed and is_kept_area(tags) then
        areas:insert({ tags = tags, geom = object:as_polygon() })
    end
    if is_kept_line(tags) then
        lines:insert({ tags = tags, geom = object:as_linestring() })
    end

    local highway = tags.highway
    if not highway or not routable[highway] then return end
    -- A way drawn as an area is routable only if it's a walkway, along its outline.
    if tags.area == 'yes' and not walked_areas[highway] then return end
    insert_way(object)
end

-- The countries of Great Britain, from OSM's own boundary relations, so build_segments() can
-- clip the Network to England and Wales (T030). A regional extract (London, say) carries these
-- relations without most of their member ways; osm2pgsql can't assemble those, and the geometry
-- comes out null.
local country_codes = { ['GB-ENG'] = true, ['GB-WLS'] = true, ['GB-SCT'] = true }

local countries = osm2pgsql.define_relation_table('countries', {
    { column = 'iso', type = 'text', not_null = true },
    { column = 'geom', type = 'multipolygon', projection = 4326 },
}, { schema = 'osm' })

-- Named walking and cycling routes, for Corridors (T037).
local route_kinds = { hiking = true, foot = true, walking = true, running = true, bicycle = true, mtb = true }

local routes = osm2pgsql.define_relation_table('routes', {
    { column = 'tags', type = 'jsonb', not_null = true },
}, { schema = 'osm' })

local route_ways = osm2pgsql.define_table({
    name = 'route_ways',
    schema = 'osm',
    ids = { type = 'relation', id_column = 'relation_id' },
    columns = {
        { column = 'sequence', type = 'int', not_null = true },
        { column = 'way_id', type = 'int8', not_null = true },
        { column = 'role', type = 'text' },
    },
})

function osm2pgsql.process_relation(object)
    local tags = object.tags

    if tags.type == 'boundary' and tags.boundary == 'administrative' and tags.admin_level == '4'
        and country_codes[tags['ISO3166-2']] then
        countries:insert({ iso = tags['ISO3166-2'], geom = object:as_multipolygon() })
        return
    end

    if tags.type == 'multipolygon' and is_kept_area(tags) then
        areas:insert({ tags = tags, geom = object:as_multipolygon() })
        return
    end

    if tags.type == 'route' and route_kinds[tags.route] then
        routes:insert({ tags = tags })
        local sequence = 0
        for _, member in ipairs(object.members) do
            if member.type == 'w' then
                sequence = sequence + 1
                route_ways:insert({ sequence = sequence, way_id = member.ref, role = member.role })
            end
        end
    end
end
