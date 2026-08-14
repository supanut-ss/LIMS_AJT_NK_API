# OCR Callback QAQC Interface

Endpoint `POST /api/call_back` จะบันทึก raw callback ก่อน แล้วประมวลผลเฉพาะหน้า
ที่มี `tracking_status = ReadyToCheck`

## การจับคู่ inbound

ระบบต้องพบ inbound เพียงหนึ่งรายการจากค่าทั้งสี่ต่อไปนี้ โดยตัดช่องว่างหัวท้าย
และไม่สนตัวพิมพ์เล็ก/ใหญ่:

- `product_name` ตรงกับ `t_wms_item.description`
- `Supplier_name` ตรงกับ `t_lims_inbound_receive.supplier_name`
- `lot_number` ตรงกับ `t_lims_inbound_receive.lot_number`
- `mfg_date` ตรงกับ `t_lims_inbound_receive.mfg_date`

หลังจากนั้นต้องพบ `t_lims_qaqc_scan` หนึ่งรายการ และ parameter ต้องผ่าน active
mapping ใน `t_lims_coa_parameter_mapping` ก่อนอัปเดต
`t_lims_qaqc_coa_parameter_test` พร้อมสร้าง audit ใน
`t_lims_qaqc_coa_parameter_transaction`

## Response example

```json
{
  "status": "success",
  "message": "Callback received and processed",
  "data": {
    "callback_id": "e9a0dbcf-8efb-4cc8-8d02-e25df600b3e5",
    "job_task_id": "JOB-TEST-001",
    "result_count": 1,
    "item_count": 2,
    "interface_status": "partial",
    "updated_item_count": 1,
    "skipped_item_count": 1,
    "pages": [
      {
        "page_id": 1,
        "receipt_detail_id": "f835b76a-60c6-4ab3-abdb-e7355e2aa66a",
        "status": "partial",
        "updated_item_count": 1,
        "skipped_item_count": 1,
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
