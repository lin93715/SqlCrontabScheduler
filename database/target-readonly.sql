-- Apply to a dedicated reporting database as its trusted administrator.
\set ON_ERROR_STOP on
\getenv reader_password SCHEDULER_READER_PASSWORD
BEGIN;
CREATE ROLE scheduler_query_reader LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD :'reader_password';
SELECT format('REVOKE CONNECT, TEMPORARY ON DATABASE %I FROM PUBLIC', current_database()) \gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO scheduler_query_reader', current_database()) \gexec
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA report;
CREATE SCHEMA scheduler_guard;
REVOKE ALL ON SCHEMA scheduler_guard, report FROM PUBLIC;
GRANT USAGE ON SCHEMA scheduler_guard, report TO scheduler_query_reader;
ALTER ROLE scheduler_query_reader SET default_transaction_read_only = on;
ALTER ROLE scheduler_query_reader SET statement_timeout = '30s';
ALTER ROLE scheduler_query_reader SET idle_in_transaction_session_timeout = '30s';
ALTER ROLE scheduler_query_reader SET search_path = pg_catalog, report, pg_temp;

CREATE FUNCTION scheduler_guard.count_read_rows(query_text text, max_rows integer)
RETURNS integer
LANGUAGE plpgsql
SECURITY INVOKER
SET search_path = pg_catalog, report, pg_temp
AS $body$
DECLARE results refcursor;
DECLARE scanned bigint;
BEGIN
    OPEN results NO SCROLL FOR EXECUTE query_text;
    MOVE FORWARD least(greatest(max_rows, 1), 100001) FROM results;
    GET DIAGNOSTICS scanned = ROW_COUNT;
    CLOSE results;
    RETURN scanned::integer;
END
$body$;
REVOKE ALL ON FUNCTION scheduler_guard.count_read_rows(text, integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION scheduler_guard.count_read_rows(text, integer) TO scheduler_query_reader;

-- Limit side-effecting public built-ins in this dedicated database.
SELECT format('REVOKE EXECUTE ON FUNCTION %s FROM PUBLIC', p.oid::regprocedure)
FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname = 'pg_catalog'
  AND (p.proname LIKE 'pg%advisory%' OR p.proname IN ('pg_notify', 'set_config'))
\gexec
ALTER DEFAULT PRIVILEGES REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;
COMMIT;
