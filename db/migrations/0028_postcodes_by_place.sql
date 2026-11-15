-- 0028: the postcode nearest a start clicked on the map (PLAN T064), shown in the search box.
create index ix_names_postcode_geom on places.names using gist (geom) where kind = 'Postcode';
