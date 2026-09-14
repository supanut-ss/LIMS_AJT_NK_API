USE [LIMS_NK];
GO

/*
    Adds the columns required by the current OCR callback flow for legacy
    databases that still have the older callback/result schemas.
*/

IF OBJECT_ID(N'dbo.t_interface_lims_ocr_callback', N'U') IS NOT NULL AND COL_LENGTH('dbo.t_interface_lims_ocr_callback', 'source_summary_json') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_callback
    ADD source_summary_json NVARCHAR(MAX) NULL;
END
GO

IF OBJECT_ID(N'dbo.t_interface_lims_ocr_result', N'U') IS NOT NULL AND COL_LENGTH('dbo.t_interface_lims_ocr_result', 'file_id') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result
    ADD file_id VARCHAR(100) NULL;
END
GO

IF OBJECT_ID(N'dbo.t_interface_lims_ocr_result', N'U') IS NOT NULL AND COL_LENGTH('dbo.t_interface_lims_ocr_result', 'status') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result
    ADD status NVARCHAR(50) NULL;
END
GO

IF OBJECT_ID(N'dbo.t_interface_lims_ocr_result', N'U') IS NOT NULL AND COL_LENGTH('dbo.t_interface_lims_ocr_result', 'document_classification') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result
    ADD document_classification NVARCHAR(100) NULL;
END
GO

IF OBJECT_ID(N'dbo.t_interface_lims_ocr_result', N'U') IS NOT NULL AND COL_LENGTH('dbo.t_interface_lims_ocr_result', 'quantity_uom') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result
    ADD quantity_uom VARCHAR(25) NULL;
END
GO

IF OBJECT_ID(N'dbo.t_interface_lims_ocr_result', N'U') IS NOT NULL AND COL_LENGTH('dbo.t_interface_lims_ocr_result', 'confident_json') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result
    ADD confident_json NVARCHAR(MAX) NULL;
END
GO