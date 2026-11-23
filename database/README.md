# PostgreSQL 配置

本機使用 PostgreSQL 17。範例在同一個 PostgreSQL instance 內建立兩個資料庫，分別使用獨立非超級使用者帳號。服務端的連線設定由開發 User Secrets 或部署環境的祕密儲存注入。

| 資料庫 | 帳號 | 用途 |
| --- | --- | --- |
| `scheduler` | `scheduler_metadata` | API、Worker、EF migration、Quartz AdoJobStore |
| `scheduler_reports` | `scheduler_query_reader` | Worker 查詢隔離的報表資料 |

## 依序初始化

`bootstrap-metadata.sql` 需用具建立資料庫及角色權限的 PostgreSQL 帳號，並從環境變數 `SCHEDULER_METADATA_PASSWORD` 讀取新角色密碼。建立 `scheduler_reports` 後，在該資料庫以管理帳號執行 `target-readonly.sql`，其新角色密碼來自 `SCHEDULER_READER_PASSWORD`。本機示範可再套用 `demo-data.sql`。

`quartz-postgres.sql` 取自 Quartz.NET **v3.22.11** 的官方 PostgreSQL schema。已將檔內 `DropDb` 預設值設為 0。第一次初始化 `scheduler` 時以 `scheduler_metadata` 執行一次，後續版本升級依 Quartz 官方相容性及 schema 變更處理。 [官方 schema](https://github.com/quartznet/quartznet/blob/v3.22.11/database/tables/tables_postgres.sql)

EF Core migration 由 API startup project 提供設定，遷移以 `Scheduler.Infrastructure` 為目標。遷移完成後，`register-demo-connection.sql` 可登錄示範連線。正式環境由管理員依同一欄位定義登錄連線，`secret_key` 對應 Worker 專屬的 `TargetConnections:<key>`。使用 `enabled=false` 撤銷後續執行，已開始的執行會受自身逾時與取消機制控制。

## 執行目標的權限

- 目標使用隔離的報表資料庫或唯讀複本。憑證角色無資料庫物件所有權、管理權、其他角色會員資格及 TEMP 權限。僅賦予必要 schema 的 USAGE，以及允許的表或檢視的 SELECT。腳本會撤銷指定內建函式的 PUBLIC 執行權，影響範圍為執行腳本的資料庫。
- `scheduler_guard.count_read_rows` 以呼叫者的權限開啟單一 SQL 游標，僅計算有限的筆數。查詢結果的欄位值留在目標資料庫。Worker 在伺服器唯讀交易中執行，檢查登入角色、TEMP 及交易狀態，並設定 statement timeout。Guard 管理權限應僅授予信任的部署人員。
- 檢查可由查詢角色執行的例程與可讀的檢視，特別是 extensions、外部資料包裝器、跨資料庫函式、SECURITY DEFINER 函式及自訂型別。提供所需的報表資料及必要函式，避免讓查詢角色取得對外副作用或跨資料集權限。為每位使用者隔離資料時可配置獨立連線或受控報表檢視。
- 在共用 PostgreSQL cluster 部署多個報表庫時，將腳本中的角色名稱改為各資料庫獨立且唯一的帳號，並檢查 cluster 其他資料庫的 CONNECT 與 pg_hba 規則。資料庫遠端連線使用驗證伺服器身分的 TLS，例如 Npgsql `SSL Mode=VerifyFull`，並配置受信任根憑證。
- Worker 的 metadata 憑證與目標憑證分開配置，部署時以網路規則限制 Worker 可到達的 PostgreSQL 主機。目標資料庫耗用可另外以 PostgreSQL role、連線池或資料庫層的資源規則限制。

## 資料表與保留

| 資料表 | 索引與保留用途 |
| --- | --- |
| `scheduler_job` | UUID PK，依 owner、刪除狀態與更新時間存取，連線 FK 使用 Restrict |
| `scheduler_execution` | UUID PK，Job FK，依排程時間、狀態、起訖時間查詢，預設保存已完成紀錄 90 天 |
| `scheduler_db_connection` | UUID PK，顯示名稱和祕密鍵皆唯一 |
| `scheduler_audit_log` | UUID PK，Job FK，依 Job、操作時間查詢，預設保存 365 天 |
| `scheduler_manual_request` | UUID PK，Job FK，每個 Job 最多一筆待處理請求，已完成請求預設保存 90 天 |
| `qrtz_*` | Quartz 官方 schema，保留既有 triggers、jobs 及鎖定狀態 |

時間使用 `timestamptz`，服務寫入 UTC。Worker 每小時清理到期資料。軟刪除的排程會在同步刪除 Quartz Job 且執行、請求、稽核資料均到期後移除。正式部署先依需求配置備份與稽核保留，調整 `Retention__ExecutionDays` 和 `Retention__AuditDays`。
