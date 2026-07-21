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
