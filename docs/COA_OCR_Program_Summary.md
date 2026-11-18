# COA OCR – Program Summary (for Manual and Requirement Update)

Source of truth: code and docs in `LIMS_AJT_NK_API` (API + Worker) and
`LIMS_NK/COA/LIMS_AJT_NK` (WMS_NEW web app). Everything below was read from the
code; items marked **[Confirm]** could not be verified from code alone.

## 1. System overview

COA OCR automates reading Certificate of Analysis (COA) PDFs from suppliers and
writing the test results into the LIMS QAQC parameter tests.

| Component | Tech | Role |
|---|---|---|
| **CallbackWorker** (`LIMS_AJT_NK_CallbackWorker`) | .NET Worker / Windows Service | Watches the inbound folder, sends PDF to Aji OCR, sends XLSX master data |
| **API** (`LIMS_AJT_NK_API`) | ASP.NET Core (.NET 10) | Receives OCR callback, matches inbound, updates QAQC parameters, stores COA file, proxies get_result / feedback |
| **Review COA page** (`WMS_NEW/OCR/ReviewCoa.aspx`) | ASP.NET WebForms | Users review/edit OCR results and send feedback to Aji |
| **Aji OCR API** (external, Ajinomotto) | REST | `input_ocr`, `update_master_data`, `get_result_ocr`, `feedback` |
| **SQL Server** | `t_interface_lims_ocr_*` tables | Config, log, callback, result, result item |

## 2. End-to-end flow

```
1. User/system drops PDF  -> 1_Inbound
2. Worker (every N sec)   -> moves to 2_Processing, POST /aji/input_ocr (multipart, Bearer)
3. Aji OCR processes      -> POST /api/call_back (our API)
4. API saves raw callback -> match inbound -> update QAQC parameter tests -> copy COA PDF to _Documents
5. Worker finalizes       -> file moved to 3_Success or 4_Error
6. User opens Review COA  -> checks/edits result -> "Feedback" -> POST /aji/feedback
```

### 2.1 Worker (folder processing)

Folders: `1_Inbound` → `2_Processing` → `3_Success` / `4_Error` / invalid-type folder.

- `.pdf` → OCR (`submission_mode = file` multipart, or `path` legacy JSON).
- `.xlsx` → `POST /aji/update_master_data` (master data; no callback, completes immediately on success).
- Any other extension → moved to the invalid-type folder.
- File must be stable (`file_stable_seconds`) before it is claimed.
- Retry with exponential backoff on timeout / HTTP 408, 429, 5xx (`max_send_attempts`, `retry_delay_seconds`).
- On restart, jobs stuck in `sending` are recovered using the route/URL saved in the log.
- The worker does **not** poll for results. A `submitted` job stays in `2_Processing` until Aji calls `/api/call_back`.
- Log status (`t_interface_lims_ocr_log.work_status`): `sending`, `submitted`, `send_error`, `completed_success`, `completed_error`.
- **File location columns:** `file_path` is the path when the job was created (`2_Processing`) and is **not updated** when the callback finalizes the job. The moved location (`3_Success` / `4_Error`) is stored in `final_path`.
- Aji may return sub-jobs (`jobs[].job_task_id`, e.g. `<id>_1`); the worker stores them to match callbacks.

### 2.2 Callback and QAQC interface (API)

Endpoint `POST /api/call_back` saves the raw callback first, then processes only
pages with `tracking_status = ReadyToCheck` (other pages → `ignored`).

**Inbound matching** (trim, case-insensitive; must find exactly one):

| OCR field | LIMS field |
|---|---|
| `product_name` | `t_lims_inbound_receive.item_rm_name` |
| `Supplier_name` | `t_lims_inbound_receive.supplier_name` |
| `Internal_lot` (only; OCR `lot_number` is not used) | `t_lims_inbound_receive.lot_number` |
| `mfg_date` | `t_lims_inbound_receive.mfg_date` |

Then exactly one `t_lims_qaqc_scan` must exist for the receipt.

**Per-parameter update chain:**
`OCR parameter_name` → active `t_lims_coa_parameter_mapping` → active `t_lims_coa_parameter` → active `t_lims_manage_coa_parameter` → `t_lims_qaqc_coa_parameter_test` (set `Result`) + audit row in `t_lims_qaqc_coa_parameter_transaction` (`log_type = "OCR Callback"`, user `api`).

Rules: result must not be empty and max 50 characters; each lookup must return exactly one active row.

**Skip / failure reasons** (shown per page/item in the response):
`product_name_missing`, `supplier_name_missing`, `internal_lot_missing`, `mfg_date_missing`,
`inbound_not_found`, `inbound_ambiguous`, `qaqc_scan_not_found`, `qaqc_scan_ambiguous`,
`parameter_items_missing`, `result_missing`, `result_too_long`, `mapping_not_found`,
`mapping_ambiguous`, `coa_parameter_not_found`, `coa_parameter_ambiguous`,
`parameter_test_not_found`, `parameter_test_ambiguous`, `tracking_status_not_ready`.

**Status values:**
- Item: `updated`, `already_processed`, `skipped`
- Page / callback `interface_status`: `completed` (all updated), `partial` (some), `not_matched` (none)

### 2.3 Idempotency

- Key = trimmed `job_task_id` (case-sensitive). Hash of the normalized payload.
- Same key + same hash → HTTP 200 replay (`is_duplicate = true`), no side effects.
- Same key + different hash → HTTP 409. To resubmit corrected data, use a new `job_task_id`.
- Error during interface → HTTP 500, callback stays `pending`, re-sending the same payload retries it.
- Audit row `tran_id = item_id`, so the primary key prevents duplicate audit rows.
- Applies only to callbacks created after the idempotency migration (old rows are not back-filled).

### 2.4 COA file storage

After inbound match, the API finds the source PDF through the `input_ocr` log (by `job_task_id`),
copies it to `{document_host_directory}/{document_group}` (default group `QC_COA`; `QA_COA` possible)
and inserts `t_lims_documents` (`pk_id = receipt_detail_id`, `udf1..3 = job_task_id, callback_id, file_id/tracking_id`).

- `document_host_directory` not set → parameters still update, `document_status = not_configured`.
- The source PDF is looked up in the `input_ocr` log by `job_task_id`: `final_path` first, then `file_path`, whichever file exists. This matters for Remapping and repeated callbacks, because the worker has already moved the PDF out of `2_Processing` (fixed 2026-11-18; before that only `file_path` was read and the copy failed).
- Directory set but source file missing/copy fails → whole interface rolls back, HTTP 500 (Review COA shows a "Remapping failed" warning).
- Same receipt + group + job already stored → `already_exists` (no duplicate).

### 2.5 Review COA page (WMS_NEW)

- Menu: group **OCR** → **Review COA** (`~/OCR/ReviewCoa.aspx`). Menu created by `Insert_Menu_COA_OCR.sql` in the Security DB; must also be granted per user group in *Authen/AssignMenu*.
- Grid lists `t_interface_lims_ocr_result` (newest first). Filters: Tracking No, Document Type, Product, Supplier, Lot, Tracking Status.
- Columns: Tracking No, Document Type/Classification, Product (+Origin), Supplier (+Origin), Lot, Internal Lot, Mfg/Expiry Date, Quantity, UOM, Tracking Status, Status, Interfaced, File ID, Create By/Date.
- **Rows shown in red** when any of these is empty: document_type, product_name, origin_product_name, internal_lot, quantity, supplier_name, origin_supplier_name, lot_number, mfg_date, expiry_date.
- Edit popup: header fields editable (read-only: tracking, status, interfaced, file id, create info); tab **Parameters** lists items (seq, parameter, result, UOM, Interfaced) with edit popup; empty cells shown in red.
- **Feedback** button: reads the latest config (`is_enabled`, `feedback_url`, `request_timeout_seconds`), builds the corrected body (dates as `yyyy-MM-dd`) and sends `POST feedback` with `job_task_id = tracking_id`. Shows success or warning message. Not sent if config missing/disabled/invalid.

## 3. Other API endpoints

| Endpoint | Purpose |
|---|---|
| `GET /api/test` | Connectivity test |
| `GET /api/health` | DB connection, version, uptime (503 if DB down) |
| `POST /api/call_back` | Receive OCR result (see 2.2) |
| `POST /api/aji/get_result_ocr` | Proxy to Aji (Bearer), body `{job_task_id}` |
| `POST /api/aji/feedback` | Proxy to Aji, body `{job_task_id, ocr_result}` |
| `POST /callback_test` | Store any raw JSON in `t_interface_lims_ocr_log` for testing |

Callback validation (HTTP 400): `job_task_id` required; `ocr_result` required; each page needs `page_id > 0` or `file_id`; `tracking_status` required; `body_item[].parameter_name` required when items are sent.

## 4. Configuration (`t_interface_lims_ocr_config_api`, latest row is used)

`is_enabled`, `submission_mode` (`file`/`path`), `input_ocr_url`, `input_ocr_file_url`, `input_ocr_file_field_name` (`files`),
`input_ocr_bearer_token`, `update_master_url`, `get_result_ocr_url`, `feedback_url`, `callback_url` (must be reachable by Aji, not localhost),
`flow_id`, four folder paths + invalid-type folder (use UNC, not mapped drives), `document_host_directory`, `document_web_path`, `document_group`,
`interval_seconds` (30), `file_stable_seconds` (5), `max_send_attempts` (3), `retry_delay_seconds` (5), `request_timeout_seconds` (60).
Web app also reads `AJINOMOTO_API_*` keys in `Web.config`. Never commit real tokens.

## 5. Deployment checklist

1. Run DB migrations in order: `20260721_*` (config, log) → `20260818_align_ocr_callback_payload_schema` → `20260825_add_ocr_callback_idempotency` (stop old API instances first) → `20260901_support_ocr_file_result_contract` → `20260901_add_input_ocr_file_worker_route` → `20260902_align_aji_input_ocr_v5`. Web side: `Database/Migrations/20261103_CreateOcrTables.sql`.
2. Fill the active config row (URLs, token, flow_id, callback URL, folders, document directory).
3. Run `Insert_Menu_COA_OCR.sql` on the Security DB and assign the menu to user groups.
4. Install the worker as a Windows Service with a domain account that has Read/Write/Modify on all folders; API account needs read on `processing_directory` and write on `document_host_directory`.

## 6. Points to check when updating requirements / manual

- Only `ReadyToCheck` pages are interfaced; `processing`/`fail` pages are ignored.
- Result longer than 50 characters is skipped (`result_too_long`).
- Matching is exact (trimmed, case-insensitive); product/supplier/lot/mfg date mismatch means no update. Ambiguous matches are never guessed.
- Parameters need an active OCR→COA mapping in `t_lims_coa_parameter_mapping`, otherwise `mapping_not_found`. Users must maintain this master data. **[Confirm]** which screen maintains it.
- Corrected data must be re-sent with a new `job_task_id`; the same id with different content returns 409.
- Review COA edits update only the OCR result tables and the feedback to Aji. They do **not** re-run the QAQC interface. **[Confirm]** whether re-interface after edit is a requirement.
- Feedback from the page uses `tracking_id` as `job_task_id`, whereas the callback uses `job_task_id` (possibly a sub-job id such as `<id>_1`). **[Confirm]** this is the id Aji expects.
- Date format: callback `DD/MM/YYYY`; feedback from the web page `yyyy-MM-dd`. **[Confirm]** Aji accepts both.
- Review COA has no delete or new-row actions (view/edit/feedback only).
- The document group decides which page lists the file: the Upload File tab filters by `pk_id` and `document_group` (`QCCoa` page uses `QC_COA`, `QACoa` / `QAQCCoa` page uses `QA_COA`). Set `document_group` in the config to match the page users open; a file stored under the other group will not be listed. **[Confirm]** `QAQCCoa` group (verified on DEV only: `QA_COA` showed the file).
- Files left in `2_Processing` with status `submitted` mean the callback never arrived (no polling fallback).
- Quantity text such as `94000 kg` is split into quantity and UOM by the API.

## 7. Verification status

- Content comes from reading source and docs only. The program was **not run** and the tests were **not executed** for this summary.
- The two lists of "revised requirements" (what changed versus the original) are not in the repo; section 6 lists current behavior only. Provide the original requirement document to produce a diff.
