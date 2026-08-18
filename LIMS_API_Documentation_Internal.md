# LIMS AJT NK API — เอกสารสรุป API ภายในระบบ

เอกสารฉบับนี้สรุปวิธีการเรียกใช้และโครงสร้างข้อมูลของ API ทั้ง 3 เส้นที่ถูกพัฒนาขึ้นภายในระบบ **LIMS_AJT_NK_API** (ฝั่งเรา)

**Base URL:** `http://<your-host>:<port>` (เช่น Development: `http://localhost:5117`)

---

## 1. GET `/api/test`
ใช้สำหรับตรวจสอบการเชื่อมต่อกับระบบ API (Connectivity Test / Ping)

### Request
* **Method:** `GET`
* **Path:** `/api/test`
* **Headers:** `Accept: application/json`

### Response
#### ✅ Success — `200 OK`
```json
{
    "status": "success",
    "message": "API is reachable",
    "data": {
        "server_time": "2026-06-12T09:30:20.1234567+07:00"
    },
    "errors": null
}
```

---

## 2. GET `/api/health`
ใช้สำหรับทำ Health Check ของระบบ ตรวจสอบการเชื่อมต่อของฐานข้อมูล (Database Connection) และดูค่า Uptime/Version

### Request
* **Method:** `GET`
* **Path:** `/api/health`
* **Headers:** `Accept: application/json`

### Response
#### ✅ Healthy — `200 OK`
เกิดขึ้นเมื่อระบบทำงานปกติและสามารถเชื่อมต่อฐานข้อมูลได้สำเร็จ
```json
{
    "status": "healthy",
    "message": "Service is running and database is connected",
    "data": {
        "version": "1.0.0",
        "uptime": "0d 01h 23m 45s",
        "uptime_seconds": 5025.1234,
        "db_status": "connected",
        "db_error": null,
        "server_time_utc": "2026-06-12T02:30:20Z"
    },
    "errors": null
}
```

#### ❌ Degraded/Unhealthy — `503 Service Unavailable`
เกิดขึ้นเมื่อระบบไม่สามารถเชื่อมต่อฐานข้อมูลได้
```json
{
    "status": "unhealthy",
    "message": "Service is degraded",
    "data": {
        "version": "1.0.0",
        "uptime": "0d 01h 23m 45s",
        "uptime_seconds": 5025.1234,
        "db_status": "disconnected",
        "db_error": "Connection timeout to SQL Server",
        "server_time_utc": "2026-06-12T02:30:20Z"
    },
    "errors": [
        {
            "field": "database",
            "message": "Connection timeout to SQL Server"
        }
    ]
}
```

---

## 3. POST `/api/call_back`
ใช้เป็น Endpoint รับผลการประมวลผล OCR ที่ระบบภายนอกส่งกลับมา (Callback) เพื่อทำการบันทึกข้อมูลเข้าฐานข้อมูล LIMS

### Request
* **Method:** `POST`
* **Path:** `/api/call_back`
* **Headers:** `Content-Type: application/json`

#### 📋 Request Body JSON Schema
| Field | Type | Required | Description |
|---|---|---|---|
| `job_task_id` | string | ✅ | ID ของงาน OCR (ห้ามเป็นค่าว่าง) |
| `ocr_result` | array | ✅ | รายการข้อมูลผลลัพธ์ของแต่ละหน้า (ห้ามว่าง) |
| `ocr_result[].page_id` | int | ⚠️ | หมายเลขหน้า (ต้องมากกว่า 0 เมื่อไม่มี `file_id`) |
| `ocr_result[].file_id` | string | ⚠️ | ID ไฟล์จาก OCR (ต้องส่ง `page_id` หรือ `file_id` อย่างน้อยหนึ่งค่า) |
| `ocr_result[].tracking_id` | string | ❌ | ID สำหรับติดตามสถานะหน้านั้น |
| `ocr_result[].tracking_status` | string | ✅ | สถานะประมวลผลของหน้า (ห้ามเป็นค่าว่าง เช่น `ReadyToCheck`, `SUCCESS`) |
| `ocr_result[].body_json` | object | ❌ | ข้อมูลค่าที่สแกนได้ในหน้านั้น (กรณีไม่มีข้อมูล/สแกนไม่สำเร็จสามารถเว้นว่างได้) |
| `ocr_result[].body_json.product_name` | string | ❌ | ชื่อสินค้าที่อ่านได้ |
| `ocr_result[].body_json.document_type` | string | ❌ | ประเภทของเอกสาร (เช่น COA) |
| `ocr_result[].body_json.Supplier_name` | string | ❌ | ชื่อ/รหัสผู้จัดส่ง (Supplier) |
| `ocr_result[].body_json.lot_number` | string | ❌ | หมายเลขลอต; ถ้าว่างระบบจะใช้ `Internal_lot` แทน |
| `ocr_result[].body_json.origin_supplier_name` | string | ❌ | ชื่อผู้จัดส่งต้นทางที่ปรากฏบนหน้าเอกสาร |
| `ocr_result[].body_json.origin_product_name` | string | ❌ | ชื่อสินค้าต้นทางที่ปรากฏบนหน้าเอกสาร |
| `ocr_result[].body_json.expiry_date` | string | ❌ | วันหมดอายุของสินค้า (แนะนำรูปแบบ: `DD/MM/YYYY`) |
| `ocr_result[].body_json.mfg_date` | string | ❌ | วันที่ผลิตสินค้า (แนะนำรูปแบบ: `DD/MM/YYYY`) |
| `ocr_result[].body_json.Internal_lot` | string | ❌ | รหัส Internal Lot ในระบบ |
| `ocr_result[].body_json.quantity` | string | ❌ | จำนวนและหน่วย เช่น `94000 kg` โดยระบบจะแยกเป็น quantity และ quantity_uom |
| `ocr_result[].body_json.body_item` | array | ❌ | รายการผลทดสอบที่พบบนหน้าเอกสาร |
| `ocr_result[].body_json.body_item[].parameter_name` | string | ✅ | ชื่อพารามิเตอร์การทดสอบ (ห้ามเป็นค่าว่างถ้าส่งมา) |
| `ocr_result[].body_json.body_item[].result` | string | ❌ | ผลลัพธ์ของการทดสอบ |
| `ocr_result[].body_json.body_item[].uom` | string | ❌ | หน่วยวัด (Unit of Measurement) |
| `ocr_result[].confident` | object | ❌ | ค่า confidence จาก OCR เก็บทั้งก้อนใน `confident_json` |

#### 📝 Request Body Example
```json
{
    "job_task_id": "JOB-TEST-001",
    "ocr_result": [
        {
            "file_id": "6a8283ddb3b7d074da97e8f1",
            "tracking_id": "TRACK-001",
            "tracking_status": "ReadyToCheck",
            "body_json": {
                "product_name": "Sample Product",
                "document_type": "COA",
                "Supplier_name": "Sample Supplier",
                "lot_number": null,
                "origin_supplier_name": "Origin Supplier",
                "origin_product_name": "Origin Product",
                "expiry_date": "31/12/2026",
                "mfg_date": "01/01/2026",
                "Internal_lot": "LOT-001",
                "quantity": "10.50 kg",
                "body_item": [
                    {
                        "parameter_name": "Moisture",
                        "result": "5.20",
                        "uom": "%"
                    }
                ]
            }
        }
    ]
}
```

### Response
#### ✅ Success — `200 OK`
ส่งข้อมูล callback สำเร็จและถูกบันทึกลงตารางฐานข้อมูล `t_interface_lims_ocr_callback`, `t_interface_lims_ocr_result`, และ `t_interface_lims_ocr_result_item` หลังจับคู่ inbound ระบบจะ copy ไฟล์ไปยัง Host และเพิ่ม `t_lims_documents` เมื่อกำหนด `document_host_directory` แล้ว
```json
{
    "status": "success",
    "message": "รับข้อมูล callback สำเร็จ",
    "data": {
        "callback_id": "e9a0dbcf-8efb-4cc8-8d02-e25df600b3e5",
        "job_task_id": "JOB-TEST-001",
        "result_count": 1,
        "item_count": 1,
        "interface_status": "completed",
        "updated_item_count": 1,
        "skipped_item_count": 0,
        "pages": [
            {
                "page_id": 1,
                "receipt_detail_id": "f835b76a-60c6-4ab3-abdb-e7355e2aa66a",
                "status": "completed",
                "document_status": "stored",
                "document_id": 12345,
                "document_path": "../_Documents/QC_COA/example.pdf",
                "updated_item_count": 1,
                "skipped_item_count": 0,
                "items": []
            }
        ]
    },
    "errors": null
}
```

#### ❌ Bad Request — `400 Bad Request`
เกิดขึ้นเมื่อโครงสร้างข้อมูลไม่ถูกต้องตาม Validation Rules
* **กรณีลืมส่ง `job_task_id` หรือส่งค่าว่าง:**
  ```json
  {
      "status": "error",
      "message": "ข้อมูลไม่ถูกต้อง",
      "data": {},
      "errors": [
          {
              "field": "job_task_id",
              "message": "ห้ามเว้นว่าง"
          }
      ]
  }
  ```
* **กรณีไม่ส่งทั้ง `page_id` และ `file_id`:**
  ```json
  {
      "status": "error",
      "message": "ข้อมูลไม่ถูกต้อง",
      "data": {},
      "errors": [
          {
              "field": "page_id/file_id",
              "message": "ต้องส่ง page_id ที่มากกว่า 0 หรือ file_id อย่างน้อยหนึ่งค่า"
          }
      ]
  }
  ```
* **กรณีมี `body_item` แต่ส่ง `parameter_name` เป็นค่าว่าง:**
  ```json
  {
      "status": "error",
      "message": "ข้อมูลไม่ถูกต้อง",
      "data": {},
      "errors": [
          {
              "field": "parameter_name",
              "message": "ห้ามเว้นว่าง"
          }
      ]
  }
  ```
