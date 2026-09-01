# LIMS_AJT_NK_API

Repository นี้แยกโครงสร้างชัดเจนระหว่าง API และ Worker Service แล้ว

## Project Structure

```text
src/
  LIMS_AJT_NK_API/             # ASP.NET Core API
  LIMS_AJT_NK_CallbackWorker/  # Worker Service สำหรับ call callback API
```

## Run API

```powershell
dotnet run --project .\src\LIMS_AJT_NK_API\LIMS_AJT_NK_API.csproj
```

## Run Worker

```powershell
dotnet run --project .\src\LIMS_AJT_NK_CallbackWorker\LIMS_AJT_NK_CallbackWorker.csproj
```

## Build

```powershell
dotnet build .\src\LIMS_AJT_NK_API\LIMS_AJT_NK_API.csproj
dotnet build .\src\LIMS_AJT_NK_CallbackWorker\LIMS_AJT_NK_CallbackWorker.csproj
```

## API Callback Endpoint

- Endpoint: `POST /api/call_back`
- บันทึกลงฐานข้อมูล SQL Server ที่ตาราง:
  - `t_interface_lims_ocr_callback`
  - `t_interface_lims_ocr_result`
  - `t_interface_lims_ocr_result_item`
  - `t_lims_documents` เมื่อกำหนด Host document directory แล้ว

รายละเอียดการจับคู่และ response: [OCR Callback QAQC](docs/OCR_CALLBACK_QAQC.md)

## Test

```powershell
dotnet test .\tests\LIMS_AJT_NK_API.Tests\LIMS_AJT_NK_API.Tests.csproj
```

ก่อน deploy callback contract รุ่นที่รองรับ `file_id` ให้รัน migration
`database\20260818_align_ocr_callback_payload_schema.sql` กับฐานข้อมูลเป้าหมาย

ก่อน deploy callback รุ่นที่ป้องกันการประมวลผลซ้ำ ให้รัน migration
`database\20260825_add_ocr_callback_idempotency.sql` และหยุด API callback รุ่นเก่า
ทุก instance ก่อนเปิดรุ่นใหม่ เพื่อไม่ให้รุ่นเก่าแทรก callback ที่ไม่มี
`idempotency_key`

ก่อน deploy contract ที่ตรงกับไฟล์ผล OCR ให้รัน migration
`database\20260901_support_ocr_file_result_contract.sql` เพื่อเก็บ `summary`,
result `status` และ `document_classification`

ก่อนเปิดใช้ Worker เส้น `input_ocr_file` ให้รัน migration
`database\20260901_add_input_ocr_file_worker_route.sql` แล้วตั้งค่า
`submission_mode = 'file'` และ `input_ocr_file_url` ใน config row ที่ใช้งาน

จากนั้นกำหนดตำแหน่ง `_Documents` จริงบน Host ให้ config row ที่ใช้งาน เช่น:

```sql
DECLARE @config_id UNIQUEIDENTIFIER =
(
    SELECT TOP (1) config_id
    FROM dbo.t_interface_lims_ocr_config_api
    ORDER BY create_date DESC
);

UPDATE dbo.t_interface_lims_ocr_config_api
SET document_host_directory = N'D:\LIMS\WMS_NEW\_Documents',
    document_web_path = N'../_Documents',
    document_group = N'QC_COA'
WHERE config_id = @config_id;
```

Service account ของ API ต้องมีสิทธิ์อ่านไฟล์ใน `processing_directory` และเขียนไฟล์
ใน `document_host_directory` โดย `document_group` เลือกได้ระหว่างกลุ่มที่ระบบ LIMS
ใช้งาน เช่น `QC_COA` หรือ `QA_COA`

การตั้ง shared path, retry และติดตั้ง Worker เป็น Windows Service ดูที่
[Callback Worker](src/LIMS_AJT_NK_CallbackWorker/README.md)
