# LIMS_AJT_NK_CallbackWorker

Worker Service สำหรับเฝ้าดูโฟลเดอร์แล้วส่ง `POST /input_ocr` ตามรอบเวลา

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

- `input_ocr_url`: ปลายทาง `input_ocr`
- `callback_url`: callback URL ที่ส่งใน payload
- `inbound_directory`: โฟลเดอร์รับไฟล์
- `processing_directory`: โฟลเดอร์ระหว่างประมวลผล
- `success_directory`: โฟลเดอร์ไฟล์ที่ส่ง API ผ่าน
- `error_directory`: โฟลเดอร์ไฟล์ที่ส่ง API ไม่ผ่าน
- `flow_id`: ถ้าปล่อยว่าง worker จะสร้างใหม่ต่อไฟล์
- `interval_seconds`: ความถี่ในการสแกนโฟลเดอร์

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

## Payload ที่ส่ง

Worker จะสแกนไฟล์ `.pdf` ใน `1_Inbound` แล้ว move ไป `2_Processing` ก่อนส่ง payload `input_ocr`
