# LIMS_AJT_NK_CallbackWorker

Worker Service สำหรับเฝ้าดูโฟลเดอร์แล้วส่ง `POST /input_ocr` ตามรอบเวลา

รองรับการรันเป็น Windows Service, UNC shared path, ตรวจว่าไฟล์เขียนเสร็จแล้ว,
retry แบบ exponential backoff และ recovery งานที่ค้างสถานะ `sending`

## Folder Structure

```text
1_Inbound/     # วางไฟล์เข้ามาที่นี่
2_Processing/  # ไฟล์ที่กำลังประมวลผล
3_Success/     # ยิง API ผ่านแล้ว
4_Error/       # ยิง API ไม่ผ่าน หรือไฟล์พัง
```

## Configuration

Worker อ่านค่าจากตาราง `t_interface_lims_ocr_config_api`

ถ้าตารางยังไม่มีข้อมูล ระบบจะ seed ค่าเริ่มต้นให้อัตโนมัติ:

- `input_ocr_url = http://localhost:5117/input_ocr`
- `callback_url = http://localhost:5117/api/call_back`
- โฟลเดอร์ `1_Inbound`, `2_Processing`, `3_Success`, `4_Error`
- `interval_seconds = 30`
- `file_stable_seconds = 5`
- `max_send_attempts = 3`
- `retry_delay_seconds = 5`
- `request_timeout_seconds = 60`

- `input_ocr_url`: ปลายทาง `input_ocr`
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

Worker จะสแกนไฟล์ `.pdf` ใน `1_Inbound` แล้ว move ไป `2_Processing` ก่อนส่ง payload `input_ocr`

สถานะหลักใน `t_interface_lims_ocr_log`:

- `sending`: claim ไฟล์แล้วและกำลังส่ง; เมื่อ restart worker จะนำกลับมาส่งต่อ
- `submitted`: OCR รับ request แล้ว กำลังรอ callback
- `send_error`: ส่งไม่สำเร็จหลัง retry และย้ายไป Error
- `completed_success`: callback และ interface สำเร็จ ย้ายไป Success
- `completed_error`: callback/interface ไม่สำเร็จ ย้ายไป Error
