SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'input_ocr_file_url') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD input_ocr_file_url NVARCHAR(500) NULL;
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'submission_mode') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD submission_mode NVARCHAR(20) NOT NULL
            CONSTRAINT df_t_interface_lims_ocr_config_api_submission_mode DEFAULT(N'path');
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'input_ocr_file_field_name') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD input_ocr_file_field_name NVARCHAR(100) NOT NULL
            CONSTRAINT df_t_interface_lims_ocr_config_api_file_field DEFAULT(N'file');
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE name = 'ck_t_interface_lims_ocr_config_api_submission_mode'
      AND parent_object_id = OBJECT_ID('dbo.t_interface_lims_ocr_config_api')
)
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_config_api WITH CHECK
        ADD CONSTRAINT ck_t_interface_lims_ocr_config_api_submission_mode
        CHECK (submission_mode IN (N'path', N'file'));
END;

COMMIT TRANSACTION;
