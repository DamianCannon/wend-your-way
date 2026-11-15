-- An empty network_next built exactly as network is (PLAN T034), for a build to fill while the app
-- keeps reading network; NetworkBuild then swaps them and keeps the old one as network_prev.
-- network's tables are the pattern, so the migrations stay the one definition of the Network:
-- columns, generated columns and checks come by LIKE; defaults, keys and indexes are recreated
-- under their own names (LIKE would rename indexes, so a migration naming one would fail only on
-- a database that had swapped); each sequence starts afresh; storage settings (the fillfactor
-- that keeps map order) are copied.
-- plpgsql, not sql: it names network_next, which exists only during a build.
-- Repeatable: Migrations applies every script in db/functions after the numbered migrations, on
-- every run, so a function is changed by editing it here (create or replace). Schema changes
-- still go in numbered migrations.
create or replace function prepare_network_next()
returns void
language plpgsql
volatile
security invoker
set search_path = public, pg_temp
as $$
declare
  v_table record;
  v_column record;
  v_index record;
  v_sequence text;
begin
  -- The Network before last goes first, so a Great Britain build peaks at two, not three.
  drop schema if exists network_prev cascade;
  drop schema if exists network_next cascade;
  create schema network_next;

  for v_table in
    select c.relname, c.reloptions
      from pg_class c join pg_namespace n on n.oid = c.relnamespace
     where n.nspname = 'network' and c.relkind = 'r'
     order by c.relname
  loop
    execute format('create table network_next.%I (like network.%I including generated including constraints including storage including statistics)',
                   v_table.relname, v_table.relname);
    if v_table.reloptions is not null then
      execute format('alter table network_next.%I set (%s)', v_table.relname, array_to_string(v_table.reloptions, ','));
    end if;

    -- Defaults; a column numbered from a sequence gets its own, starting afresh.
    for v_column in
      select a.attname, pg_get_expr(d.adbin, d.adrelid) as expression, pg_get_serial_sequence(format('network.%I', v_table.relname), a.attname) as sequence
        from pg_attrdef d
        join pg_attribute a on a.attrelid = d.adrelid and a.attnum = d.adnum
       where d.adrelid = format('network.%I', v_table.relname)::regclass and a.attgenerated = ''
    loop
      if v_column.sequence is not null then
        v_sequence := split_part(v_column.sequence, '.', 2);
        execute format('create sequence network_next.%s owned by network_next.%I.%I', v_sequence, v_table.relname, v_column.attname);
        execute format('alter table network_next.%I alter column %I set default nextval(%L::regclass)',
                       v_table.relname, v_column.attname, 'network_next.' || v_sequence);
      else
        execute format('alter table network_next.%I alter column %I set default %s', v_table.relname, v_column.attname, v_column.expression);
      end if;
    end loop;

    -- Keys, under their own names (they bring their indexes).
    for v_index in
      select con.conname, pg_get_constraintdef(con.oid) as definition
        from pg_constraint con
       where con.conrelid = format('network.%I', v_table.relname)::regclass and con.contype in ('p', 'u', 'x')
    loop
      execute format('alter table network_next.%I add constraint %I %s', v_table.relname, v_index.conname, v_index.definition);
    end loop;

    -- Every other index, under its own name.
    for v_index in
      select i.indexdef
        from pg_indexes i
       where i.schemaname = 'network' and i.tablename = v_table.relname
         and not exists (select 1 from pg_constraint con
                          where con.conrelid = format('network.%I', v_table.relname)::regclass and con.conname = i.indexname)
    loop
      execute replace(v_index.indexdef, ' ON network.', ' ON network_next.');
    end loop;
  end loop;
end;
$$;
