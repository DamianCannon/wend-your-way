-- Writes the routable ways of a square of the built Network back out as OSM XML, for an
-- integration-spec fixture (PLAN T013). The machine has no osmium to cut a .pbf, and osm2pgsql
-- reads XML, so this round-trips what the import kept: every routable way whole, with its tags,
-- and every node it uses. Nodes come before ways, each sorted by ID, as osm2pgsql requires.
--
-- Usage (see scripts/cut-fixture.ps1):
--   psql -At -v lat=51.4436 -v lon=-0.2963 -v half_m=3500 -f scripts/cut-fixture.sql
--
-- Map data © OpenStreetMap contributors, ODbL: the output carries the same credit.

with box as (
  select ST_Transform(ST_Expand(ST_Transform(ST_SetSRID(ST_MakePoint(:lon, :lat), 4326), 27700), :half_m), 4326) as g
), ways as (
  select w.way_id, w.tags, w.nodes, w.geom
    from osm.ways w, box
   where ST_Intersects(w.geom, box.g)
     and ST_NPoints(w.geom) = cardinality(w.nodes)
), nodes as (
  select distinct on (w.nodes[d.path[1]])
         w.nodes[d.path[1]] as id, ST_Y(d.geom) as lat, ST_X(d.geom) as lon
    from ways w
   cross join lateral ST_DumpPoints(w.geom) d
   order by w.nodes[d.path[1]]
)
select '<?xml version="1.0" encoding="UTF-8"?>'
union all select '<!-- Cut by scripts/cut-fixture.sql from a Geofabrik extract. Map data © OpenStreetMap contributors, ODbL. -->'
union all select '<osm version="0.6" generator="Wend Your Way scripts/cut-fixture.sql">'
union all (select xmlelement(name node, xmlattributes(id as id, round(lat::numeric, 7) as lat, round(lon::numeric, 7) as lon))::text
             from nodes order by id)
union all (select xmlelement(name way, xmlattributes(way_id as id),
                    (select xmlagg(xmlelement(name nd, xmlattributes(n as ref)) order by i) from unnest(nodes) with ordinality u(n, i)),
                    (select xmlagg(xmlelement(name tag, xmlattributes(key as k, value as v)) order by key) from jsonb_each_text(tags)))::text
             from ways order by way_id)
union all select '</osm>';
