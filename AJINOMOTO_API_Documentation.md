# AJINOMOTO — API Documentation

Base URL: `{{host}}`

---

## 1. POST `/input_ocr`

ส่งไฟล์ภาพเข้าระบบ OCR เพื่อประมวลผล

### Request Body

| Field | Type | Required | Description |
|---|---|---|---|
| `flow_id` | string | ✅ | ID ของ flow ที่ใช้ประมวลผล |
| `job_task_id` | string | ✅ | ID ของ task งานนี้ ใช้อ้างอิงในขั้นตอนถัดไป |
| `s3_path_image` | string | ✅ | path ของไฟล์ภาพใน S3 รองรับเฉพาะ `.pdf` |
| `callback_url` | string | ✅ | URL ที่ระบบจะเรียกกลับเมื่อประมวลผลเสร็จ |

```json
{
    "flow_id": "69e5a35737ce961ba7f49c65",
    "job_task_id": "bbad9149-5ff2-45fc-855b-a12a5fc8f810",
    "s3_path_image": "folder/image/test_img.pdf",
    "callback_url": "https://myserver.com/api/callback/upload"
}
```

### Response

#### ✅ Success — `200 OK`

```json
{
    "status": "success",
    "message": "ส่งข้อมูลสำเร็จ",
    "data": {},
    "errors": null
}
```

#### ❌ Error — ชนิดไฟล์ไม่ถูกต้อง `400`

เกิดเมื่อ `s3_path_image` เป็นไฟล์ที่ไม่รองรับ เช่น `.jpg`

```json
{
    "status": "error",
    "message": "ข้อมูลไม่ถูกต้อง",
    "data": {},
    "errors": [
        {
            "field": "s3_path_image",
            "message": "ชนิดไฟล์ภาพไม่ถูกต้อง"
        }
    ]
}
```

#### ❌ Error — ข้อมูลไม่ครบ `400`

เกิดเมื่อ field บังคับเว้นว่าง หรือรูปแบบข้อมูลผิด

```json
{
    "status": "error",
    "message": "ข้อมูลไม่ถูกต้อง",
    "data": {},
    "errors": [
        { "field": "flow_id", "message": "ห้ามเว้นว่าง" },
        { "field": "job_task_id", "message": "ห้ามเว้นว่าง" },
        { "field": "ocr", "message": "รูปแบบ ocr ไม่ถูกต้อง" },
        { "field": "callback_url", "message": "ห้ามเว้นว่าง" }
    ]
}
```

#### ❌ Error — Server Error `500`

```json
{
    "status": "error",
    "message": "เกิดข้อผิดพลาดภายในระบบ กรุณาลองใหม่อีกครั้ง",
    "data": {},
    "errors": null
}
```

---

## 2. POST `/get_result_ocr`

ดึงผลลัพธ์ OCR ของแต่ละหน้าเอกสาร

### Request Body

| Field | Type | Required | Description |
|---|---|---|---|
| `job_task_id` | string | ✅ | ID ของ task ที่ส่งเข้ามาจาก `/input_ocr` |

```json
{
    "job_task_id": "bbad9149-5ff2-45fc-855b-a12a5fc8f810"
}
```

### Response

#### ✅ Success — `200 OK`

| Field | Type | Description |
|---|---|---|
| `job_task_id` | string | ID ของ task |
| `ocr_result` | array | ผลลัพธ์แต่ละหน้าของเอกสาร |
| `ocr_result[].page_id` | int | หมายเลขหน้า |
| `ocr_result[].tracking_id` | string | ID สำหรับ tracking หน้านั้น |
| `ocr_result[].tracking_status` | string | สถานะ: `processing`, `fail`, `ReadyToCheck` |
| `ocr_result[].body_json` | object | ข้อมูล OCR ที่อ่านได้ (ว่างถ้ายังไม่เสร็จ) |

**tracking_status ที่เป็นไปได้**

| Status | ความหมาย |
|---|---|
| `processing` | กำลังประมวลผลอยู่ |
| `fail` | ประมวลผลล้มเหลว |
| `ReadyToCheck` | ประมวลผลเสร็จ พร้อมให้ตรวจสอบ |

**โครงสร้าง `body_json` (เมื่อ status เป็น `ReadyToCheck`)**

| Field | Type | Description |
|---|---|---|
| `product_name` | string | ชื่อสินค้า |
| `document_type` | string | ประเภทเอกสาร เช่น `Certificate of analysis` |
| `Supplier_name` | string | รหัส Supplier หรือชื่อ Supplier หลังจากการ Mapping |
| `lot_number` | string | หมายเลข Lot |
| `origin_supplier_name` | string | ชื่อ Supplier ที่อยู่บนหน้าเอกสาร |
| `origin_product_name` | string | ชื่อสินค้าบนหน้าเอกสาร |
| `expiry_date` | string | วันหมดอายุ (format: `DD/MM/YYYY`) |
| `mfg_date` | string | วันที่ผลิต (format: `DD/MM/YYYY`) |
| `Internal_lot` | string | หมายเลข Internal Lot |
| `quantity` | string / null | จำนวน (null ถ้าไม่มีข้อมูล) |
| `body_item` | array | รายการผลการทดสอบ |
| `body_item[].parameter_name` | string | ชื่อพารามิเตอร์ที่ทดสอบ |
| `body_item[].result` | string | ผลการทดสอบ |
| `body_item[].uom` | string / null | หน่วยวัด (null ถ้าไม่มี) |

```json
{
    "job_task_id": "bbad9149-5ff2-45fc-855b-a12a5fc8f810",
    "ocr_result": [
        {
            "page_id": 1,
            "tracking_id": "test_call_coa_1",
            "tracking_status": "processing",
            "body_json": {}
        },
        {
            "page_id": 2,
            "tracking_id": "test_call_coa_2",
            "tracking_status": "fail",
            "body_json": {}
        },
        {
            "page_id": 3,
            "tracking_id": "test_call_coa_3",
            "tracking_status": "ReadyToCheck",
            "body_json": {
                "product_name": "CC-5000-WS-P 5KG",
                "document_type": "Certificate of analysis",
                "Supplier_name": "37",
                "lot_number": "301289",
                "origin_supplier_name": "Brenntag Ingredients (Thailand)",
                "origin_product_name": "CC-5000-WS-P 5KG",
                "expiry_date": "07/12/2026",
                "mfg_date": "07/12/2024",
                "Internal_lot": "2537110301",
                "quantity": null,
                "body_item": [
                    { "parameter_name": "Carminic Acid", "result": "49.2", "uom": "%" },
                    { "parameter_name": "pH", "result": "10.8", "uom": null },
                    { "parameter_name": "Moisture Content", "result": "6", "uom": "%" },
                    { "parameter_name": "Salmonella", "result": "Conforms", "uom": "Absent in 25g" }
                ]
            }
        }
    ]
}
```

---

## 3. POST `/feedback`

ส่งข้อมูลที่ผู้ใช้แก้ไขแล้วกลับเข้าระบบ (หลังจาก review ผล OCR)

### Request Body

| Field | Type | Required | Description |
|---|---|---|---|
| `job_task_id` | string | ✅ | ID ของ task |
| `ocr_result` | array | ✅ | รายการหน้าที่ต้องการ feedback (โครงสร้างเดียวกับ `/get_result_ocr` แต่ไม่มี `tracking_id` และ `tracking_status`) |
| `ocr_result[].page_id` | int | ✅ | หมายเลขหน้าที่ส่ง feedback |
| `ocr_result[].body_json` | object | ✅ | ข้อมูลที่แก้ไขแล้ว โครงสร้างเดียวกับ `body_json` ใน `/get_result_ocr` |

### Response

#### ✅ Success — `200 OK`

```json
{
    "status": "success",
    "message": "ส่งข้อมูลสำเร็จ",
    "data": {},
    "errors": null
}
```

#### ❌ Error — field ไม่ถูกต้อง `400`

เกิดเมื่อส่ง field ที่ไม่รู้จักหรือข้อมูลไม่ครบ

```json
{
    "status": "error",
    "message": "ข้อมูลไม่ถูกต้อง",
    "data": {},
    "errors": [
        { "field": "product_name", "message": "ไม่พบ field นี้" },
        { "field": "body_item", "message": "จำนวน item ไม่ครบ" }
    ]
}
```

---

## Flow การทำงาน

```
1. POST /input_ocr       → ส่งไฟล์เข้าระบบ ได้ job_task_id กลับมา
2. POST /get_result_ocr  → ดึงผลลัพธ์ด้วย job_task_id (polling จนกว่า status = ReadyToCheck)
3. POST /feedback        → ส่งข้อมูลที่ตรวจสอบแล้วกลับเข้าระบบ
```
