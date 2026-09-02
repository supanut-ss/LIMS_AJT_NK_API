# LIMS_AJT_NK_CallbackWorker

Worker Service สำหรับเฝ้าดูโฟลเดอร์แล้วส่ง OCR ได้ 2 เส้นทางตาม config:
`POST /input_ocr` แบบ JSON path เดิม หรือ `POST /aji/input_ocr` แบบ multipart PDF ตาม Aji API v5
และส่งไฟล์ master data ไป `POST /aji/update_master_data` ตาม Aji API v5

รองรับการรันเป็น Windows Service, UNC shared path, ตรวจว่าไฟล์เขียนเสร็จแล้ว,
retry แบบ exponential backoff และ recovery งานที่ค้างสถานะ `sending`

## Folder Structure

```text
1_Inbound/     # วาง PDF สำหรับ OCR หรือ XLSX สำหรับ update master ที่นี่
2_Processing/  # ไฟล์ที่กำลังประมวลผล
3_Success/     # ยิง API ผ่านแล้ว
4_Error/       # ยิง API ไม่ผ่าน หรือไฟล์พัง
```

## Configuration

Worker อ่านค่าจากตาราง `t_interface_lims_ocr_config_api`

ถ้าตารางยังไม่มีข้อมูล ระบบจะ seed ค่าเริ่มต้นให้อัตโนมัติ:

- `input_ocr_url = http://localhost:5117/input_ocr`
- `input_ocr_file_url = http://dev-hippo.ztrus.net:6206/aji/input_ocr`
- `submission_mode = file`
- `input_ocr_file_field_name = files`
- `input_ocr_bearer_token = NULL` (ต้องกำหนด token จริงก่อนเริ่ม Worker)
- `update_master_url = http://dev-hippo.ztrus.net:6206/aji/update_master_data`
- `get_result_ocr_url = http://dev-hippo.ztrus.net:6206/aji/get_result_ocr`
- `feedback_url = http://dev-hippo.ztrus.net:6206/aji/feedback`
- `callback_url = NULL` (ต้องกำหนด URL ของ Callback Service ที่ Aji API เรียกถึงได้)
- โฟลเดอร์ `1_Inbound`, `2_Processing`, `3_Success`, `4_Error`
- `interval_seconds = 30`
- `file_stable_seconds = 5`
- `max_send_attempts = 3`
- `retry_delay_seconds = 5`
- `request_timeout_seconds = 60`

- `input_ocr_url`: ปลายทาง `input_ocr`
- `input_ocr_file_url`: ปลายทาง multipart เช่น `/aji/input_ocr`
- `submission_mode`: `path` สำหรับ JSON เดิม หรือ `file` สำหรับ multipart PDF
- `input_ocr_file_field_name`: ชื่อ multipart field ของ PDF ค่าเริ่มต้น `files`
- `input_ocr_bearer_token`: Bearer token สำหรับ Aji API; ระบบจะไม่เขียนค่านี้ลง request log
- `update_master_url`: ปลายทางอัปโหลด master data; ปล่อยว่างเพื่อปิดการสแกน XLSX
- `get_result_ocr_url`: ปลายทาง query ผล OCR; ปล่อยว่างเพื่อปิด fallback polling
- `feedback_url`: ปลายทางส่งข้อมูลที่ human ตรวจแก้ ใช้โดย `POST /api/aji/feedback`
- `callback_url`: callback URL ที่ส่งใน payload
- `inbound_directory`: โฟลเดอร์รับไฟล์
- `processing_directory`: โฟลเดอร์ระหว่างประมวลผล
- `success_directory`: โฟลเดอร์ไฟล์ที่ส่ง API ผ่าน
- `error_directory`: โฟลเดอร์ไฟล์ที่ส่ง API ไม่ผ่าน
- `flow_id`: ถ้าปล่อยว่าง worker จะสร้างใหม่ต่อไฟล์
- `interval_seconds`: ความถี่ในการสแกนโฟลเดอร์
- `file_stable_seconds`: อายุขั้นต่ำหลังแก้ไขไฟล์ ก่อน worker จะ claim ไฟล์
- `max_send_attempts`: จำนวนครั้งสูงสุดที่เรียก OCR ต่อรอบ
- `retry_delay_seconds`: ระยะเริ่มต้นของ exponential backoff
- `request_timeout_seconds`: timeout ของ OCR request ต่อครั้ง

สำหรับ shared path ให้ใช้ UNC path ไม่ใช้ mapped drive:

```sql
DECLARE @config_id UNIQUEIDENTIFIER =
(
    SELECT TOP (1) config_id
    FROM dbo.t_interface_lims_ocr_config_api
    ORDER BY create_date DESC
);

UPDATE dbo.t_interface_lims_ocr_config_api
SET inbound_directory = N'\\fileserver\LIMS_OCR\1_Inbound',
    processing_directory = N'\\fileserver\LIMS_OCR\2_Processing',
    success_directory = N'\\fileserver\LIMS_OCR\3_Success',
    error_directory = N'\\fileserver\LIMS_OCR\4_Error',
    interval_seconds = 30,
    file_stable_seconds = 5,
    max_send_attempts = 3,
    retry_delay_seconds = 5,
    request_timeout_seconds = 60
WHERE config_id = @config_id;
```

เปิดใช้เส้น upload PDF v5 โดยรัน migration
`database\20260901_add_input_ocr_file_worker_route.sql` และ
`database\20260902_align_aji_input_ocr_v5.sql` ตามลำดับ แล้วตั้งค่า:

```sql
UPDATE dbo.t_interface_lims_ocr_config_api
SET submission_mode = N'file',
    input_ocr_file_url = N'http://dev-hippo.ztrus.net:6206/aji/input_ocr',
    input_ocr_file_field_name = N'files',
    input_ocr_bearer_token = N'<REAL_BEARER_TOKEN>',
    update_master_url = N'http://dev-hippo.ztrus.net:6206/aji/update_master_data',
    get_result_ocr_url = N'http://dev-hippo.ztrus.net:6206/aji/get_result_ocr',
    feedback_url = N'http://dev-hippo.ztrus.net:6206/aji/feedback',
    flow_id = N'6a5efa97abaf97614454562a',
    callback_url = N'https://<LIMS-HOST>/api/call_back'
WHERE config_id = @config_id;
```

ห้ามเก็บ token จริงไว้ใน source control และ `callback_url` ต้องเป็น URL ที่ Aji API
เรียกกลับถึงได้ ไม่ใช่ `localhost` ของเครื่อง Aji

เปลี่ยนกลับเส้นเดิมได้ด้วย `submission_mode = N'path'` งานสถานะ `sending`
จะ recover ด้วย route และ URL ที่บันทึกไว้ใน log ตอนสร้างงาน ไม่เปลี่ยนตาม config ใหม่

Windows Service account ต้องมี Share permission และ NTFS permission แบบ
Read/Write/Modify ในทั้งสี่ directory

## Run

```powershell
# run from repo root
dotnet run --project .\src\LIMS_AJT_NK_CallbackWorker\LIMS_AJT_NK_CallbackWorker.csproj

# or run from worker folder
dotnet run --project .\LIMS_AJT_NK_CallbackWorker.csproj
```

## Build

```powershell
# build from repo root
dotnet build .\src\LIMS_AJT_NK_CallbackWorker\LIMS_AJT_NK_CallbackWorker.csproj

# or build from worker folder
dotnet build .\LIMS_AJT_NK_CallbackWorker.csproj
```

## Install as Windows Service

Publish จาก repo root:

```powershell
dotnet publish .\src\LIMS_AJT_NK_CallbackWorker\LIMS_AJT_NK_CallbackWorker.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained false `
  --output C:\Services\LIMS-OCR-Worker
```

เปิด PowerShell แบบ Administrator แล้วสร้าง service:

```powershell
sc.exe create LimsAjtNkOcrWorker `
  binPath= '"C:\Services\LIMS-OCR-Worker\LIMS_AJT_NK_CallbackWorker.exe"' `
  start= auto `
  DisplayName= "LIMS AJT NK OCR Worker"

sc.exe failure LimsAjtNkOcrWorker `
  reset= 86400 `
  actions= restart/60000/restart/60000/restart/60000
```

จากนั้นเปิด `services.msc` ตั้งค่าแท็บ **Log On** ให้ใช้ Domain Service Account
ที่เข้าถึง UNC path ได้ แล้วจึงสั่ง:

```powershell
sc.exe start LimsAjtNkOcrWorker
```

อย่าใช้ Local System หาก shared path ต้องยืนยันตัวตนกับ file server

## Payload ที่ส่ง

Worker จะสแกนไฟล์ `.pdf` ใน `1_Inbound` แล้ว move ไป `2_Processing` ก่อนส่ง
ตาม `submission_mode`; เส้น `file` ส่ง multipart fields `flow_id`, `job_task_id`,
`callback_url` และ PDF field ตาม `input_ocr_file_field_name` พร้อม Bearer token
เมื่อ Aji API คืน `jobs[].job_task_id` เช่น `<submitted-id>_1` Worker จะบันทึก ID
ดังกล่าวเพื่อจับคู่กับ `POST /api/call_back`

ไฟล์ `.xlsx` ใน `1_Inbound` จะถูกส่งไป `update_master_url` ด้วย multipart fields
`flow_id` และ `files` โดยเส้นนี้ไม่ส่ง Authorization ตาม contract v5 เมื่อ response
เป็น HTTP 2xx และ JSON มี `status: "success"` ไฟล์จะย้ายไป `3_Success`; กรณีอื่น
จะ retry เฉพาะ timeout, HTTP 408/429/5xx แล้วจึงย้ายไป `4_Error`

สถานะหลักใน `t_interface_lims_ocr_log`:

- `sending`: claim ไฟล์แล้วและกำลังส่ง; เมื่อ restart worker จะนำกลับมาส่งต่อ
- `submitted`: OCR รับ request แล้ว กำลังรอ callback
- `send_error`: ส่งไม่สำเร็จหลัง retry และย้ายไป Error
- `completed_success`: callback และ interface สำเร็จ ย้ายไป Success
- `completed_error`: callback/interface ไม่สำเร็จ ย้ายไป Error

สำหรับ `update_master_data` จะเปลี่ยนจาก `sending` เป็น `completed_success` ทันทีเมื่อ
API สำเร็จ เพราะเส้นนี้ไม่มี callback

## Get result, feedback และ callback test

Worker จะ poll `get_result_ocr_url` สำหรับงาน OCR สถานะ `submitted` ที่ยังไม่มี
callback โดยส่ง Bearer token เดียวกับ `input_ocr`. เมื่อ `summary.total > 0` และ
`summary.processing = 0` จะ forward JSON เดิมไป `callback_url`. ถ้า push callback
มาถึงก่อน Worker จะไม่ poll ซ้ำ

API ของเรามี proxy สำหรับเรียก Aji โดยไม่ต้องกระจาย URL/config ไปยัง frontend:

- `POST /api/aji/get_result_ocr` body `{ "job_task_id": "..." }`
- `POST /api/aji/feedback` body `{ "job_task_id": "...", "ocr_result": {...} }`
- `POST /callback_test` รับ JSON ใด ๆ และเก็บ raw payload ใน `t_interface_lims_ocr_log`

เส้น `get_result_ocr` ใช้ Bearer token ตาม contract v5 ส่วน `feedback` และ
`callback_test` ไม่ใช้ Authorization
