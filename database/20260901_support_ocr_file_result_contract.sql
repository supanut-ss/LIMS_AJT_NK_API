SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.t_interface_lims_ocr_callback', 'source_summary_json') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_callback
        ADD source_summary_json NVARCHAR(MAX) NULL;
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_result', 'status') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result
        ADD status NVARCHAR(50) NULL;
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_result', 'document_classification') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_result
        ADD document_classification NVARCHAR(100) NULL;
END;

COMMIT TRANSACTION;
