\set ON_ERROR_STOP on
INSERT INTO scheduler_db_connection (id, name, secret_key, enabled, admin_only)
VALUES ('68b42a04-85ac-42e6-b34e-09f585419ff9', '示範報表資料庫', 'DemoReports', true, false)
ON CONFLICT (secret_key) DO NOTHING;
