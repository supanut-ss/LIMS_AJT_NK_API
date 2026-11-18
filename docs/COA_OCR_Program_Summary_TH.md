# COA OCR – เอกสารสรุปโปรแกรม (สำหรับจัดทำคู่มือและปรับปรุง Requirement)

แหล่งข้อมูล: โค้ดและเอกสารใน `LIMS_AJT_NK_API` (API + Worker) และ
`LIMS_NK/COA/LIMS_AJT_NK` (เว็บแอป WMS_NEW) ทุกหัวข้ออ่านมาจากโค้ดโดยตรง
รายการที่ทำเครื่องหมาย **[ต้องยืนยัน]** คือสิ่งที่ตรวจจากโค้ดอย่างเดียวไม่ได้

## 1. ภาพรวมระบบ

COA OCR ใช้อ่านเอกสาร Certificate of Analysis (COA) แบบ PDF จาก Supplier โดยอัตโนมัติ
แล้วบันทึกผลทดสอบลงใน QAQC Parameter Test ของ LIMS

| ส่วนประกอบ | เทคโนโลยี | หน้าที่ |
|---|---|---|
| **CallbackWorker** (`LIMS_AJT_NK_CallbackWorker`) | .NET Worker / Windows Service | เฝ้าโฟลเดอร์ Inbound ส่ง PDF ไป Aji OCR และส่งไฟล์ XLSX master data |
| **API** (`LIMS_AJT_NK_API`) | ASP.NET Core (.NET 10) | รับ callback, จับคู่ inbound, อัปเดต QAQC, เก็บไฟล์ COA, เป็น proxy ของ get_result / feedback |
| **หน้า Review COA** (`WMS_NEW/OCR/ReviewCoa.aspx`) | ASP.NET WebForms | ผู้ใช้ตรวจ/แก้ผล OCR และส่ง feedback ไป Aji |
| **Aji OCR API** (ระบบภายนอก Ajinomoto) | REST | `input_ocr`, `update_master_data`, `get_result_ocr`, `feedback` |
| **SQL Server** | ตาราง `t_interface_lims_ocr_*` | Config, Log, Callback, Result, Result Item |

## 2. Flow การทำงานตั้งแต่ต้นจนจบ

```
1. ผู้ใช้/ระบบวาง PDF       -> 1_Inbound
2. Worker (ทุก N วินาที)    -> ย้ายไป 2_Processing, POST /aji/input_ocr (multipart, Bearer)
3. Aji OCR ประมวลผลเสร็จ   -> POST /api/call_back (API ของเรา)
4. API บันทึก raw callback  -> จับคู่ inbound -> อัปเดต QAQC parameter -> copy PDF ไป _Documents
5. Worker ปิดงาน            -> ย้ายไฟล์ไป 3_Success หรือ 4_Error
6. ผู้ใช้เปิด Review COA    -> ตรวจ/แก้ไข -> กด "Feedback" -> POST /aji/feedback
```

### 2.1 Worker (ประมวลผลโฟลเดอร์)

โฟลเดอร์: `1_Inbound` → `2_Processing` → `3_Success` / `4_Error` / โฟลเดอร์ invalid-type

- `.pdf` → ส่ง OCR (`submission_mode = file` แบบ multipart หรือ `path` แบบ JSON เดิม)
- `.xlsx` → `POST /aji/update_master_data` (master data ไม่มี callback เสร็จทันทีเมื่อสำเร็จ)
- นามสกุลอื่น → ย้ายไปโฟลเดอร์ invalid-type
- ไฟล์ต้องนิ่งตามเวลา `file_stable_seconds` ก่อนจึงจะถูก claim
- Retry แบบ exponential backoff เมื่อ timeout / HTTP 408, 429, 5xx (`max_send_attempts`, `retry_delay_seconds`)
- เมื่อ restart งานที่ค้างสถานะ `sending` จะถูกกู้คืนโดยใช้ route/URL ที่บันทึกไว้ใน log
- Worker **ไม่ poll** ผลลัพธ์ งานสถานะ `submitted` จะค้างใน `2_Processing` จนกว่า Aji จะเรียก `/api/call_back`
- สถานะ log (`t_interface_lims_ocr_log.work_status`): `sending`, `submitted`, `send_error`, `completed_success`, `completed_error`
- **คอลัมน์ที่อยู่ไฟล์:** `file_path` คือ path ตอนสร้างงาน (`2_Processing`) และ **ไม่ถูกอัปเดต** เมื่อ callback ปิดงาน ที่อยู่หลังย้าย (`3_Success` / `4_Error`) เก็บใน `final_path`
- Aji อาจคืน sub-job (`jobs[].job_task_id` เช่น `<id>_1`) Worker จะเก็บไว้เพื่อจับคู่กับ callback

### 2.2 Callback และ QAQC Interface (API)

Endpoint `POST /api/call_back` บันทึก raw callback ก่อน แล้วประมวลผลเฉพาะหน้าที่
`tracking_status = ReadyToCheck` (หน้าอื่นจะเป็น `ignored`)

**การจับคู่ inbound** (trim, ไม่สนตัวพิมพ์ ต้องพบเพียง 1 รายการ):

| ฟิลด์ OCR | ฟิลด์ LIMS |
|---|---|
| `product_name` | `t_lims_inbound_receive.item_rm_name` |
| `Supplier_name` | `t_lims_inbound_receive.supplier_name` |
| `Internal_lot` (ใช้ตัวนี้เท่านั้น ไม่ใช้ `lot_number` จาก OCR) | `t_lims_inbound_receive.lot_number` |
| `mfg_date` | `t_lims_inbound_receive.mfg_date` |

จากนั้นต้องพบ `t_lims_qaqc_scan` ของใบรับนั้นเพียง 1 รายการ

**ลำดับการอัปเดตแต่ละ parameter:**
`parameter_name ของ OCR` → `t_lims_coa_parameter_mapping` (active) → `t_lims_coa_parameter` (active) → `t_lims_manage_coa_parameter` (active) → `t_lims_qaqc_coa_parameter_test` (ตั้งค่า `Result`) + บันทึก audit ใน `t_lims_qaqc_coa_parameter_transaction` (`log_type = "OCR Callback"`, ผู้ใช้ `api`)

กฎ: ผลต้องไม่ว่างและยาวไม่เกิน 50 ตัวอักษร ทุกขั้นการ lookup ต้องพบ active row เพียง 1 แถว

**เหตุผลที่ข้าม/ไม่สำเร็จ** (แสดงในผลตอบกลับรายหน้า/รายรายการ):
`product_name_missing`, `supplier_name_missing`, `internal_lot_missing`, `mfg_date_missing`,
`inbound_not_found`, `inbound_ambiguous`, `qaqc_scan_not_found`, `qaqc_scan_ambiguous`,
`parameter_items_missing`, `result_missing`, `result_too_long`, `mapping_not_found`,
`mapping_ambiguous`, `coa_parameter_not_found`, `coa_parameter_ambiguous`,
`parameter_test_not_found`, `parameter_test_ambiguous`, `tracking_status_not_ready`

**ค่าสถานะ:**
- รายการ: `updated`, `already_processed`, `skipped`
- หน้า / callback `interface_status`: `completed` (อัปเดตครบ), `partial` (บางส่วน), `not_matched` (ไม่มีเลย)

### 2.3 Idempotency

- Key = `job_task_id` ที่ trim แล้ว (แยกตัวพิมพ์เล็ก/ใหญ่) และ hash ของ payload ที่ normalize แล้ว
- key เดิม + hash เดิม → HTTP 200 คืนผลเดิม (`is_duplicate = true`) ไม่มี side effect
- key เดิม + hash ต่าง → HTTP 409 หากต้องส่งข้อมูลที่แก้แล้วให้ใช้ `job_task_id` ใหม่
- เกิด error ระหว่าง interface → HTTP 500 callback คงสถานะ `pending` ส่ง payload เดิมซ้ำเพื่อ retry ได้
- Audit ใช้ `tran_id = item_id` จึงมี primary key ป้องกันการสร้างซ้ำ
- ใช้ได้เฉพาะ callback ที่สร้างหลัง deploy migration idempotency (ข้อมูลเก่าไม่ถูก backfill)

### 2.4 การเก็บไฟล์ COA

เมื่อจับคู่ inbound ได้ API จะหา PDF ต้นฉบับจาก log ของ `input_ocr` (ด้วย `job_task_id`)
แล้ว copy ไปที่ `{document_host_directory}/{document_group}` (ค่าเริ่มต้น `QC_COA` เลือก `QA_COA` ได้)
และเพิ่มแถวใน `t_lims_documents` (`pk_id = receipt_detail_id`, `udf1..3 = job_task_id, callback_id, file_id/tracking_id`)

- ไม่ได้ตั้ง `document_host_directory` → parameter ยังอัปเดตตามปกติ แต่ `document_status = not_configured`
- หา PDF ต้นฉบับจาก log ของ `input_ocr` ด้วย `job_task_id`: ดู `final_path` ก่อน แล้วค่อย `file_path` ใช้ไฟล์ที่มีอยู่จริง สำคัญกับ Remapping และ callback ซ้ำ เพราะ Worker ย้าย PDF ออกจาก `2_Processing` ไปแล้ว (แก้เมื่อ 2026-11-18 ก่อนหน้านั้นอ่านแค่ `file_path` จึง copy ไม่ได้)
- ตั้งแล้วแต่ไม่พบไฟล์ต้นฉบับ/copy ไม่ได้ → rollback การ interface ทั้งหมด ตอบ HTTP 500 (หน้า Review COA แสดงคำเตือน "Remapping failed")
- ใบรับ + group + job เดิมที่เก็บแล้ว → `already_exists` (ไม่สร้างซ้ำ)

### 2.5 หน้า Review COA (WMS_NEW)

- เมนู: กลุ่ม **OCR** → **Review COA** (`~/OCR/ReviewCoa.aspx`) สร้างด้วย `Insert_Menu_COA_OCR.sql` บนฐาน Security และต้องกำหนดสิทธิ์ตามกลุ่มผู้ใช้ที่หน้า *Authen/AssignMenu*
- Grid แสดง `t_interface_lims_ocr_result` เรียงใหม่สุดก่อน ฟิลเตอร์ได้: Tracking No, Document Type, Product, Supplier, Lot, Tracking Status
- คอลัมน์: Tracking No, Document Type/Classification, Product (+Origin), Supplier (+Origin), Lot, Internal Lot, Mfg/Expiry Date, Quantity, UOM, Tracking Status, Status, Interfaced, File ID, Create By/Date
- **แถวสีแดง** เมื่อฟิลด์ใดฟิลด์หนึ่งว่าง: document_type, product_name, origin_product_name, internal_lot, quantity, supplier_name, origin_supplier_name, lot_number, mfg_date, expiry_date
- Popup แก้ไข: ฟิลด์ header แก้ได้ (อ่านอย่างเดียว: tracking, status, interfaced, file id, ข้อมูลผู้สร้าง) แท็บ **Parameters** แสดงรายการ (seq, parameter, result, UOM, Interfaced) และแก้ไขผ่าน popup ช่องที่ว่างแสดงสีแดง
- ปุ่ม **Feedback**: อ่าน config ล่าสุด (`is_enabled`, `feedback_url`, `request_timeout_seconds`) สร้าง body ที่แก้แล้ว (วันที่รูปแบบ `yyyy-MM-dd`) แล้วส่ง `POST feedback` โดย `job_task_id = tracking_id` แสดงข้อความสำเร็จหรือแจ้งเตือน ไม่ส่งหาก config ไม่มี/ปิดใช้งาน/ไม่ถูกต้อง

## 3. API Endpoint อื่น ๆ

| Endpoint | หน้าที่ |
|---|---|
| `GET /api/test` | ทดสอบการเชื่อมต่อ |
| `GET /api/health` | ตรวจ DB, version, uptime (503 ถ้า DB ล่ม) |
| `POST /api/call_back` | รับผล OCR (ดูข้อ 2.2) |
| `POST /api/aji/get_result_ocr` | Proxy ไป Aji (Bearer) body `{job_task_id}` |
| `POST /api/aji/feedback` | Proxy ไป Aji body `{job_task_id, ocr_result}` |
| `POST /callback_test` | เก็บ raw JSON ใด ๆ ลง `t_interface_lims_ocr_log` เพื่อทดสอบ |

Validation ของ callback (HTTP 400): ต้องมี `job_task_id`; ต้องมี `ocr_result`; แต่ละหน้าต้องมี `page_id > 0` หรือ `file_id`; ต้องมี `tracking_status`; ถ้าส่ง `body_item` ต้องมี `parameter_name`

## 4. Configuration (`t_interface_lims_ocr_config_api` ใช้แถวล่าสุด)

`is_enabled`, `submission_mode` (`file`/`path`), `input_ocr_url`, `input_ocr_file_url`, `input_ocr_file_field_name` (`files`),
`input_ocr_bearer_token`, `update_master_url`, `get_result_ocr_url`, `feedback_url`, `callback_url` (ต้องเป็น URL ที่ Aji เรียกถึงได้ ไม่ใช่ localhost),
`flow_id`, path โฟลเดอร์ทั้ง 4 + invalid-type (ใช้ UNC ไม่ใช้ mapped drive), `document_host_directory`, `document_web_path`, `document_group`,
`interval_seconds` (30), `file_stable_seconds` (5), `max_send_attempts` (3), `retry_delay_seconds` (5), `request_timeout_seconds` (60)
เว็บแอปอ่านค่า `AJINOMOTO_API_*` ใน `Web.config` ด้วย ห้าม commit token จริง

## 5. Checklist การ Deploy

1. รัน DB migration ตามลำดับ: `20260721_*` (config, log) → `20260818_align_ocr_callback_payload_schema` → `20260825_add_ocr_callback_idempotency` (หยุด API รุ่นเก่าทุก instance ก่อน) → `20260901_support_ocr_file_result_contract` → `20260901_add_input_ocr_file_worker_route` → `20260902_align_aji_input_ocr_v5` ฝั่งเว็บ: `Database/Migrations/20261103_CreateOcrTables.sql`
2. กรอก config แถวที่ใช้งาน (URL, token, flow_id, callback URL, โฟลเดอร์, document directory)
3. รัน `Insert_Menu_COA_OCR.sql` บนฐาน Security และกำหนดเมนูให้กลุ่มผู้ใช้
4. ติดตั้ง Worker เป็น Windows Service ด้วย domain account ที่มีสิทธิ์ Read/Write/Modify ทุกโฟลเดอร์ ส่วน account ของ API ต้องอ่าน `processing_directory` และเขียน `document_host_directory` ได้

## 6. จุดที่ต้องตรวจเมื่อปรับ Requirement / คู่มือ

- ประมวลผลเฉพาะหน้า `ReadyToCheck` หน้า `processing`/`fail` ถูกข้าม
- ผลที่ยาวเกิน 50 ตัวอักษรจะถูกข้าม (`result_too_long`)
- การจับคู่ต้องตรงเป๊ะ (trim, ไม่สนตัวพิมพ์) ถ้า product/supplier/lot/mfg date ไม่ตรงจะไม่อัปเดต และกรณีพบหลายรายการระบบไม่เดา
- Parameter ต้องมี mapping OCR→COA ที่ active ใน `t_lims_coa_parameter_mapping` มิฉะนั้นได้ `mapping_not_found` ผู้ใช้ต้องดูแล master data นี้ **[ต้องยืนยัน]** ว่าดูแลผ่านหน้าจอใด
- ข้อมูลที่แก้ไขแล้วต้องส่งใหม่ด้วย `job_task_id` ใหม่ ถ้าใช้ id เดิมแต่เนื้อหาต่างจะได้ 409
- การแก้ใน Review COA อัปเดตเฉพาะตาราง OCR result และส่ง feedback ไป Aji **ไม่ได้** สั่ง interface เข้า QAQC ใหม่ **[ต้องยืนยัน]** ว่าต้องมี re-interface หลังแก้หรือไม่
- Feedback จากหน้าเว็บส่ง `tracking_id` เป็น `job_task_id` ขณะที่ callback ใช้ `job_task_id` (อาจเป็น sub-job เช่น `<id>_1`) **[ต้องยืนยัน]** ว่าเป็น id ที่ Aji ต้องการ
- รูปแบบวันที่: callback ใช้ `DD/MM/YYYY` ส่วน feedback จากเว็บใช้ `yyyy-MM-dd` **[ต้องยืนยัน]** ว่า Aji รับได้ทั้งสองแบบ
- Review COA ไม่มีปุ่มลบหรือเพิ่มแถวใหม่ (ดู/แก้ไข/ส่ง feedback เท่านั้น)
- กลุ่มเอกสาร (`document_group`) กำหนดว่าหน้าไหนจะเห็นไฟล์: แท็บ Upload File กรองตาม `pk_id` และ `document_group` (หน้า `QCCoa` ใช้ `QC_COA`, หน้า `QACoa` / `QAQCCoa` ใช้ `QA_COA`) ให้ตั้ง `document_group` ใน config ให้ตรงกับหน้าที่ผู้ใช้เปิด ไฟล์ที่เก็บคนละกลุ่มจะไม่ขึ้นรายการ **[ต้องยืนยัน]** กลุ่มของ `QAQCCoa` (ยืนยันแล้วบน DEV เท่านั้น: `QA_COA` แสดงไฟล์ได้)
- ไฟล์ค้างใน `2_Processing` สถานะ `submitted` หมายถึง callback ไม่เคยมา (ไม่มี polling สำรอง)
- Quantity เช่น `94000 kg` ถูกแยกเป็นจำนวนและหน่วยโดย API

## 7. สถานะการตรวจสอบ

- เนื้อหามาจากการอ่านซอร์สและเอกสารเท่านั้น **ไม่ได้รันโปรแกรม** และ **ไม่ได้รันเทสต์** ในการทำสรุปนี้
- รายการ "requirement ที่แก้ไข" (เทียบกับของเดิม) ไม่มีอยู่ใน repo ข้อ 6 จึงสรุปได้แค่พฤติกรรมปัจจุบัน หากมีเอกสาร requirement เดิม จะจัดทำตารางเทียบส่วนที่เปลี่ยนให้ได้
