\set ON_ERROR_STOP on
BEGIN;
CREATE TABLE report.daily_totals (
    report_date date PRIMARY KEY,
    total_orders integer NOT NULL
);
INSERT INTO report.daily_totals VALUES
    (CURRENT_DATE - 1, 14), (CURRENT_DATE, 21);
GRANT SELECT ON report.daily_totals TO scheduler_query_reader;
COMMIT;
