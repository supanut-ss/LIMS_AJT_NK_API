# LIMS_AJT_NK_API

## OCR Callback API

- Endpoint: `POST /api/call_back`
- ใช้สำหรับรับข้อมูล OCR callback ตามโครงสร้างจาก `get_result_ocr`
- ระบบจะบันทึกข้อมูลลง SQL Server อัตโนมัติ

### Database (MSSQL)

ตั้งค่า connection string ที่ `appsettings.json`:

```json
"ConnectionStrings": {
	"DefaultConnection": "Server=localhost;Database=LIMS_AJT_NK_API;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

### ตัวอย่าง Request

```json
{
  "job_task_id": "bbad9149-5ff2-45fc-855b-a12a5fc8f810",
  "ocr_result": [
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
          { "parameter_name": "Carminic Acid", "result": "49.2", "uom": "%" }
        ]
      }
    }
  ]
}
```

### ตารางที่ถูกสร้าง

- `OCR_Callback_Requests`
- `OCR_Callback_Pages`
- `OCR_Callback_Items`
