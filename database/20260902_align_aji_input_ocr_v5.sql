SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'input_ocr_bearer_token') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD input_ocr_bearer_token NVARCHAR(2000) NULL;
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'update_master_url') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD update_master_url NVARCHAR(500) NULL;
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'get_result_ocr_url') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD get_result_ocr_url NVARCHAR(500) NULL;
END;

IF COL_LENGTH('dbo.t_interface_lims_ocr_config_api', 'feedback_url') IS NULL
BEGIN
    ALTER TABLE dbo.t_interface_lims_ocr_config_api
        ADD feedback_url NVARCHAR(500) NULL;
END;

DECLARE @default_constraint SYSNAME;
SELECT @default_constraint = dc.name
FROM sys.default_constraints AS dc
INNER JOIN sys.columns AS c
    ON c.object_id = dc.parent_object_id
   AND c.column_id = dc.parent_column_id
WHERE dc.parent_object_id = OBJECT_ID('dbo.t_interface_lims_ocr_config_api')
  AND c.name = 'input_ocr_file_field_name';

IF @default_constraint IS NOT NULL
BEGIN
    EXEC(N'ALTER TABLE dbo.t_interface_lims_ocr_config_api DROP CONSTRAINT '
        + QUOTENAME(@default_constraint));
END;

UPDATE dbo.t_interface_lims_ocr_config_api
SET input_ocr_file_field_name = N'files'
WHERE NULLIF(LTRIM(RTRIM(input_ocr_file_field_name)), N'') IS NULL
   OR input_ocr_file_field_name = N'file';

ALTER TABLE dbo.t_interface_lims_ocr_config_api
    ADD CONSTRAINT df_t_interface_lims_ocr_config_api_file_field
        DEFAULT(N'files') FOR input_ocr_file_field_name;

COMMIT TRANSACTION;

/*
Set the active row with deployment-specific values before starting the worker.
Do not commit a real bearer token to source control.

UPDATE dbo.t_interface_lims_ocr_config_api
SET submission_mode = N'file',
    input_ocr_file_url = N'http://dev-hippo.ztrus.net:6206/aji/input_ocr',
    input_ocr_file_field_name = N'files',
    input_ocr_bearer_token = N'<REAL_BEARER_TOKEN>',
    update_master_url = N'http://dev-hippo.ztrus.net:6206/aji/update_master_data',
    get_result_ocr_url = N'http://dev-hippo.ztrus.net:6206/aji/get_result_ocr',
    feedback_url = N'http://dev-hippo.ztrus.net:6206/aji/feedback',
    flow_id = N'6a5efa97abaf97614454562a',
    callback_url = N'https://<LIMS-HOST>/api/call_back'
WHERE config_id = '<ACTIVE_CONFIG_ID>';
*/
