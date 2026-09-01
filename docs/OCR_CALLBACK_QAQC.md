# OCR Callback QAQC Interface

Endpoint `POST /api/call_back` จะบันทึก raw callback ก่อน แล้วประมวลผลเฉพาะหน้า
ที่มี `tracking_status = ReadyToCheck`

## การจับคู่ inbound

ระบบต้องพบ inbound เพียงหนึ่งรายการจากค่าทั้งสี่ต่อไปนี้ โดยตัดช่องว่างหัวท้าย
และไม่สนตัวพิมพ์เล็ก/ใหญ่:

- `product_name` ตรงกับ `t_wms_item.description`
- `Supplier_name` ตรงกับ `t_lims_inbound_receive.supplier_name`
- `lot_number` ตรงกับ `t_lims_inbound_receive.lot_number`; ถ้า OCR ส่งเป็นค่าว่าง
  ระบบจะใช้ `Internal_lot` แทน
- `mfg_date` ตรงกับ `t_lims_inbound_receive.mfg_date`

หลังจากนั้นต้องพบ `t_lims_qaqc_scan` หนึ่งรายการ และ parameter ต้องผ่าน active
mapping ใน `t_lims_coa_parameter_mapping` ก่อนอัปเดต
`t_lims_qaqc_coa_parameter_test` พร้อมสร้าง audit ใน
`t_lims_qaqc_coa_parameter_transaction`

## Idempotency ของ callback

- ระบบ normalize `job_task_id` ด้วยการ trim แล้วใช้เป็น `idempotency_key` โดยถือว่า
  ตัวพิมพ์เล็ก/ใหญ่เป็นคนละ key
- hash คำนวณจากค่าที่ normalize/parse แล้วและมีผลต่อการบันทึกหรือประมวลผล
- key เดิมและ hash เดิม: ตอบ HTTP 200 ด้วย `callback_id` และ summary เดิม,
  `is_duplicate = true` โดยไม่อัปเดต parameter, audit หรือ copy document ซ้ำ
- key เดิมแต่ hash ต่าง: ตอบ HTTP 409 และไม่สร้าง callback graph หรือ QAQC side effect
- ผล `completed`, `partial` และ `not_matched` เป็นผลปลายทางและจะถูก replay;
  หากต้องการส่งข้อมูลที่แก้ไขแล้วต้องใช้ `job_task_id` ใหม่
- ถ้า interface ล้มเหลวและตอบ HTTP 500 callback จะคงสถานะ `pending`; การส่ง payload
  เดิมซ้ำจะ retry callback เดิมหลังจาก transaction ก่อนหน้าถูก rollback

`idempotency_status` มีค่า `accepted` สำหรับ callback ใหม่, `replayed` เมื่อคืนผล
ปลายทางเดิมโดยไม่มี side effect และ `processed_existing` เมื่อ request พบ callback
สถานะ pending แล้วเป็นผู้ประมวลผลรายการเดิมจนเสร็จ

การรับประกันนี้เริ่มกับ callback ที่สร้างหลัง deploy migration รุ่น idempotency เท่านั้น
ข้อมูลเก่าที่ `idempotency_key` เป็น NULL จะไม่ถูก backfill เพื่อหลีกเลี่ยงการรวม callback
ซ้ำที่อาจมี payload ต่างกันโดยอัตโนมัติ ดังนั้นการ retry `job_task_id` เก่าครั้งแรกหลัง
deploy อาจสร้าง canonical callback ใหม่หนึ่งรายการ

Audit ของ parameter ใช้ `item_id` ของ OCR เป็น `tran_id` โดยตรง ดังนั้น primary key
ของ `t_lims_qaqc_coa_parameter_transaction` เป็นตัวป้องกัน audit ซ้ำระดับปลายทาง

## การจัดเก็บไฟล์ COA

เมื่อจับคู่ inbound ได้ ระบบจะใช้ `job_task_id` หา source PDF จาก log ของ
`input_ocr` แล้วทำงานดังนี้:

1. Copy PDF จาก processing path ไปที่
   `{document_host_directory}/{document_group}`
2. เพิ่มข้อมูลใน `t_lims_documents` โดยใช้ `pk_id = receipt_detail_id`
3. เก็บ `job_task_id`, `callback_id` และ `file_id/tracking_id` ใน `udf1-udf3`
4. ใช้ `pk_id + document_group + job_task_id` ตรวจ callback ซ้ำ

หากยังไม่กำหนด `document_host_directory` การ interface parameter ยังทำงานตามปกติ
และ response จะแสดง `document_status = not_configured` หากกำหนด directory แล้ว
แต่หา/copy source file ไม่ได้ ระบบจะ rollback การ interface และตอบ HTTP 500

## Response example

```json
{
  "status": "success",
  "message": "Callback received and processed",
  "data": {
    "callback_id": "e9a0dbcf-8efb-4cc8-8d02-e25df600b3e5",
    "job_task_id": "JOB-TEST-001",
    "is_duplicate": false,
    "idempotency_status": "accepted",
    "result_count": 1,
    "item_count": 2,
    "interface_status": "partial",
    "updated_item_count": 1,
    "skipped_item_count": 1,
    "already_processed_item_count": 0,
    "pages": [
      {
        "page_id": 1,
        "receipt_detail_id": "f835b76a-60c6-4ab3-abdb-e7355e2aa66a",
        "status": "partial",
        "document_status": "stored",
        "document_id": 12345,
        "document_path": "../_Documents/QC_COA/f835b76a60c64ab3abdb7355e2aa66a_20260818103000123_aabbccdd.pdf",
        "updated_item_count": 1,
        "skipped_item_count": 1,
        "already_processed_item_count": 0,
        "items": [
          {
            "seq": 1,
            "parameter_name": "Moisture",
            "status": "updated"
          },
          {
            "seq": 2,
            "parameter_name": "Unknown",
            "status": "skipped",
            "reason": "mapping_not_found"
          }
        ]
      }
    ]
  },
  "errors": null
}
```

`interface_status` มีค่า:

- `completed`: ทุก parameter ของหน้า ReadyToCheck ถูกอัปเดต
- `partial`: อัปเดตได้บาง parameter
- `not_matched`: ไม่มี parameter ถูกอัปเดต

กรณี business matching ไม่สำเร็จ API จะเก็บ raw callback และตอบ HTTP 200 พร้อม
เหตุผลรายหน้า/parameter หากเกิด database error ระหว่างเขียน QAQC จะ rollback
เฉพาะส่วน QAQC และตอบ HTTP 500 โดย raw callback ยังคงอยู่

Payload รองรับทั้ง `page_id` แบบเดิมและ `file_id` จาก Aji OCR หากมีเฉพาะ
`file_id` ระบบจะกำหนด `page_id` ตามลำดับรายการเพื่อใช้เป็น key ภายใน และเก็บ
`file_id`, quantity/UOM, `confident`, result `status` และ
`document_classification` แยกไว้ใน callback result ส่วน `summary` จากต้นทางจะ
เก็บเป็น JSON ที่ callback header เพื่อรักษา payload ตามไฟล์ผล OCR
